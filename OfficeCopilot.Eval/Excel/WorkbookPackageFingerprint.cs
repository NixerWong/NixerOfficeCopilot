using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace OfficeCopilot.Eval.Excel
{
    internal static class WorkbookPackageFingerprint
    {
        private static readonly HashSet<string> _volatileParts = new HashSet<string>(
            new[]
            {
                "docProps/app.xml",
                "docProps/core.xml",
                "xl/calcChain.xml"
            },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _volatileElements = new HashSet<string>(
            new[] { "calcPr", "sheetViews", "bookViews", "revisionPtr" },
            StringComparer.Ordinal);

        public static string Compute(string workbookPath)
        {
            if (string.IsNullOrWhiteSpace(workbookPath))
            {
                throw new ArgumentException("A workbook path is required.", nameof(workbookPath));
            }

            using (var sha256 = SHA256.Create())
            using (var cryptoStream = new CryptoStream(Stream.Null, sha256, CryptoStreamMode.Write, true))
            using (var writer = new BinaryWriter(cryptoStream, new UTF8Encoding(false), true))
            using (var fileStream = File.OpenRead(workbookPath))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, false))
            {
                writer.Write("xlsx-semantic-fingerprint-v1");
                foreach (var entry in archive.Entries
                    .Where(item => !_volatileParts.Contains(item.FullName))
                    .OrderBy(item => item.FullName, StringComparer.Ordinal))
                {
                    writer.Write(entry.FullName);
                    if (IsXmlPart(entry.FullName))
                    {
                        WriteCanonicalXml(entry, writer);
                    }
                    else
                    {
                        WriteBinaryPart(entry, writer);
                    }
                }

                writer.Flush();
                cryptoStream.FlushFinalBlock();
                return BitConverter.ToString(sha256.Hash).Replace("-", string.Empty);
            }
        }

        private static bool IsXmlPart(string partName)
        {
            return partName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                || partName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteCanonicalXml(ZipArchiveEntry entry, BinaryWriter writer)
        {
            var isWorkbookPropertiesPart = string.Equals(
                entry.FullName,
                "xl/workbook.xml",
                StringComparison.OrdinalIgnoreCase);
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                XmlResolver = null
            };

            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, settings))
            {
                var skippedElementDepth = -1;
                while (reader.Read())
                {
                    if (skippedElementDepth >= 0)
                    {
                        if (reader.NodeType == XmlNodeType.EndElement
                            && reader.Depth == skippedElementDepth)
                        {
                            skippedElementDepth = -1;
                        }

                        continue;
                    }

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (_volatileElements.Contains(reader.LocalName))
                        {
                            if (!reader.IsEmptyElement)
                            {
                                skippedElementDepth = reader.Depth;
                            }

                            continue;
                        }

                        var elementName = reader.LocalName;
                        writer.Write((byte)'E');
                        WriteName(writer, reader.NamespaceURI, reader.LocalName);

                        if (reader.HasAttributes)
                        {
                            var attributes = new List<KeyValuePair<string, string>>();
                            while (reader.MoveToNextAttribute())
                            {
                                if (reader.Prefix == "xmlns" || reader.Name == "xmlns")
                                {
                                    continue;
                                }
                                if (isWorkbookPropertiesPart
                                    && elementName == "workbookPr"
                                    && reader.LocalName == "codeName"
                                    && reader.Value == "ThisWorkbook")
                                {
                                    // Excel may add this implicit default when opening an otherwise unchanged workbook.
                                    continue;
                                }

                                attributes.Add(new KeyValuePair<string, string>(
                                    reader.NamespaceURI + "|" + reader.LocalName,
                                    reader.Value));
                            }

                            reader.MoveToElement();
                            foreach (var attribute in attributes.OrderBy(
                                item => item.Key,
                                StringComparer.Ordinal))
                            {
                                writer.Write((byte)'A');
                                writer.Write(attribute.Key);
                                writer.Write(attribute.Value);
                            }
                        }

                        writer.Write(reader.IsEmptyElement ? (byte)'S' : (byte)'B');
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        writer.Write((byte)'e');
                        WriteName(writer, reader.NamespaceURI, reader.LocalName);
                    }
                    else if (reader.NodeType == XmlNodeType.Text
                        || reader.NodeType == XmlNodeType.CDATA
                        || reader.NodeType == XmlNodeType.SignificantWhitespace)
                    {
                        writer.Write((byte)'T');
                        writer.Write(reader.Value);
                    }
                }
            }
        }

        private static void WriteBinaryPart(ZipArchiveEntry entry, BinaryWriter writer)
        {
            writer.Write((byte)'P');
            writer.Write(entry.Length);

            using (var stream = entry.Open())
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    writer.Write(buffer, 0, bytesRead);
                }
            }
        }

        private static void WriteName(BinaryWriter writer, string namespaceUri, string localName)
        {
            writer.Write(namespaceUri ?? string.Empty);
            writer.Write(localName ?? string.Empty);
        }
    }
}
