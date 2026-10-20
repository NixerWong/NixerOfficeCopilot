using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace OfficeCopilot.Host.Excel.Analysis
{
    internal sealed class ExcelWipSheetRegistryStore
    {
        private static readonly object _processSync = new object();
        private readonly string _path;

        public ExcelWipSheetRegistryStore()
            : this(GetDefaultPath())
        {
        }

        internal ExcelWipSheetRegistryStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A registry storage path is required.", nameof(path));
            }

            _path = path;
        }

        public IDictionary<string, string> Load(
            string conversationId,
            string workbookFullName)
        {
            if (string.IsNullOrWhiteSpace(conversationId)
                || string.IsNullOrWhiteSpace(workbookFullName))
            {
                return null;
            }

            lock (_processSync)
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                var document = ReadDocument();
                var entry = document.Entries.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.ConversationId,
                        conversationId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        candidate.WorkbookFullName,
                        workbookFullName,
                        StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    return null;
                }

                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var worksheet in entry.Worksheets)
                {
                    result.Add(worksheet.Name, worksheet.Identity);
                }

                return result;
            }
        }

        public void Save(
            string conversationId,
            string workbookFullName,
            IDictionary<string, string> worksheetIdentities)
        {
            if (string.IsNullOrWhiteSpace(conversationId))
            {
                throw new InvalidOperationException(
                    "A conversation ID is required to persist the WIP worksheet registry.");
            }

            if (string.IsNullOrWhiteSpace(workbookFullName))
            {
                throw new InvalidOperationException(
                    "A workbook path is required to persist the WIP worksheet registry.");
            }

            if (worksheetIdentities == null)
            {
                throw new ArgumentNullException(nameof(worksheetIdentities));
            }

            lock (_processSync)
            {
                if (worksheetIdentities.Count == 0 && !File.Exists(_path))
                {
                    return;
                }

                var directory = Path.GetDirectoryName(_path);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new InvalidOperationException("The WIP registry path has no parent directory.");
                }

                Directory.CreateDirectory(directory);
                using (new FileStream(
                    _path + ".lock",
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None))
                {
                    var document = ReadDocument();
                    var matchingEntries = document.Entries
                        .Where(entry =>
                            string.Equals(
                                entry.ConversationId,
                                conversationId,
                                StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                entry.WorkbookFullName,
                                workbookFullName,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (matchingEntries.Count > 1)
                    {
                        throw new InvalidDataException(
                            "The WIP registry contains duplicate conversation/workbook entries.");
                    }

                    if (worksheetIdentities.Count == 0)
                    {
                        if (matchingEntries.Count == 1)
                        {
                            document.Entries.Remove(matchingEntries[0]);
                        }
                    }
                    else
                    {
                        var entry = matchingEntries.Count == 0
                            ? new ExcelWipRegistryEntry
                            {
                                ConversationId = conversationId,
                                WorkbookFullName = workbookFullName
                            }
                            : matchingEntries[0];
                        entry.Worksheets = worksheetIdentities
                            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(pair => new ExcelWipWorksheetIdentity
                            {
                                Name = pair.Key,
                                Identity = pair.Value
                            })
                            .ToList();
                        if (matchingEntries.Count == 0)
                        {
                            document.Entries.Add(entry);
                        }
                    }

                    WriteDocument(document);
                }
            }
        }

        private RegistryDocument ReadDocument()
        {
            if (!File.Exists(_path))
            {
                return new RegistryDocument();
            }

            RegistryDocument document;
            try
            {
                using (var stream = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete))
                {
                    document = new DataContractJsonSerializer(typeof(RegistryDocument))
                        .ReadObject(stream) as RegistryDocument;
                }
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException("The persisted WIP registry is invalid.", ex);
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException("The persisted WIP registry is invalid.", ex);
            }

            if (document == null
                || document.Version != 1
                || document.Entries == null)
            {
                throw new InvalidDataException("The persisted WIP registry has an unsupported or invalid format.");
            }

            var entryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in document.Entries)
            {
                if (entry == null
                    || string.IsNullOrWhiteSpace(entry.ConversationId)
                    || string.IsNullOrWhiteSpace(entry.WorkbookFullName)
                    || entry.Worksheets == null)
                {
                    throw new InvalidDataException("The persisted WIP registry contains an invalid entry.");
                }

                var entryKey = entry.ConversationId + "\n" + entry.WorkbookFullName;
                if (!entryKeys.Add(entryKey))
                {
                    throw new InvalidDataException(
                        "The persisted WIP registry contains duplicate conversation/workbook entries.");
                }

                var worksheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var worksheet in entry.Worksheets)
                {
                    if (worksheet == null
                        || string.IsNullOrWhiteSpace(worksheet.Name)
                        || string.IsNullOrWhiteSpace(worksheet.Identity)
                        || !worksheetNames.Add(worksheet.Name))
                    {
                        throw new InvalidDataException(
                            "The persisted WIP registry contains an invalid worksheet identity.");
                    }
                }
            }

            return document;
        }

        private void WriteDocument(RegistryDocument document)
        {
            var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(RegistryDocument))
                        .WriteObject(stream, document);
                    stream.Flush(true);
                }

                if (File.Exists(_path))
                {
                    File.Replace(temporaryPath, _path, null);
                }
                else
                {
                    File.Move(temporaryPath, _path);
                }
            }
            catch (Exception writeFailure)
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (Exception cleanupFailure)
                {
                    throw new IOException(
                        "The WIP registry could not be saved, and its temporary file could not be removed.",
                        new AggregateException(writeFailure, cleanupFailure));
                }

                throw;
            }
        }

        private static string GetDefaultPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException(
                    "Cannot determine the local application data directory for the WIP registry.");
            }

            return Path.Combine(localAppData, "NixerOfficeCopilot", "excel-wip-registry.json");
        }

        [DataContract]
        private sealed class RegistryDocument
        {
            public RegistryDocument()
            {
                Version = 1;
                Entries = new List<ExcelWipRegistryEntry>();
            }

            [DataMember(Order = 1)]
            public int Version { get; set; }

            [DataMember(Order = 2)]
            public List<ExcelWipRegistryEntry> Entries { get; set; }
        }

        [DataContract]
        private sealed class ExcelWipRegistryEntry
        {
            public ExcelWipRegistryEntry()
            {
                ConversationId = string.Empty;
                WorkbookFullName = string.Empty;
                Worksheets = new List<ExcelWipWorksheetIdentity>();
            }

            [DataMember(Order = 1)]
            public string ConversationId { get; set; }

            [DataMember(Order = 2)]
            public string WorkbookFullName { get; set; }

            [DataMember(Order = 3)]
            public List<ExcelWipWorksheetIdentity> Worksheets { get; set; }
        }

        [DataContract]
        private sealed class ExcelWipWorksheetIdentity
        {
            public ExcelWipWorksheetIdentity()
            {
                Name = string.Empty;
                Identity = string.Empty;
            }

            [DataMember(Order = 1)]
            public string Name { get; set; }

            [DataMember(Name = "CodeName", Order = 2)]
            public string Identity { get; set; }
        }
    }
}
