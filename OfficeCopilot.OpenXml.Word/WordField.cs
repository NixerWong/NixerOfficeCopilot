using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace OfficeCopilot.OpenXml.Word
{
    /// <summary>Options for inserting a Word field (PAGE, DATE, MERGEFIELD, IF, ...).</summary>
    public sealed class FieldOptions
    {
        /// <summary>Field type: page|numpages|section|sectionpages|date|createdate|savedate|printdate|time|edittime|author|lastsavedby|title|subject|filename|numwords|numchars|revnum|template|comments|keywords|mergefield|ref|pageref|noteref|seq|styleref|docproperty|if. Omit when supplying <see cref="Instruction"/>.</summary>
        public string FieldType { get; set; }

        /// <summary>Required name for MERGEFIELD/REF/PAGEREF/NOTEREF/SEQ/STYLEREF/DOCPROPERTY.</summary>
        public string Name { get; set; }

        /// <summary>DATE-family \@ format switch (e.g. "M/d/yyyy", "yyyy-MM-dd").</summary>
        public string Format { get; set; }

        /// <summary>Raw instruction override (wins over FieldType). E.g. "PAGE \* ROMAN".</summary>
        public string Instruction { get; set; }

        /// <summary>Explicit cached result text. Omit to auto-seed a placeholder.</summary>
        public string Text { get; set; }

        /// <summary>IF field expression, e.g. "2 &gt; 1" or "\"A\" = \"A\"".</summary>
        public string Expression { get; set; }

        /// <summary>IF field true branch.</summary>
        public string TrueText { get; set; }

        /// <summary>IF field false branch.</summary>
        public string FalseText { get; set; }

        /// <summary>Extra trailing field switches, e.g. "\* MERGEFORMAT \* Upper".</summary>
        public string Switches { get; set; }
    }

    /// <summary>
    /// Inserts complex Word fields (fldChar begin/instr/separate/result/end runs).
    /// Ported from officecli WordHandler.AddField (C# 7.3 / netstandard2.0
    /// adaptation; dump/batch round-trip and run-formatting extras omitted).
    /// </summary>
    internal static class WordField
    {
        public static void AddFieldToParagraph(WordprocessingDocument doc, int paragraphIndex, FieldOptions options)
        {
            var body = GetBody(doc);
            var para = body.Elements<Paragraph>().ElementAtOrDefault(paragraphIndex);
            if (para == null)
                throw new ArgumentException(string.Format("Paragraph index {0} not found.", paragraphIndex));
            InsertField(para, options, doc);
        }

        public static void AppendFieldParagraph(WordprocessingDocument doc, FieldOptions options)
        {
            var body = GetBody(doc);
            var para = new Paragraph();
            InsertField(para, options, doc);
            body.AppendChild(para);
        }

        private static Body GetBody(WordprocessingDocument doc)
        {
            var mainPart = doc.MainDocumentPart;
            if (mainPart == null || mainPart.Document == null || mainPart.Document.Body == null)
                throw new InvalidOperationException("Main document body is missing.");
            return mainPart.Document.Body;
        }

        private static void InsertField(Paragraph para, FieldOptions options, WordprocessingDocument doc)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var type = (options.FieldType ?? string.Empty).Trim().ToLowerInvariant();
            if (type.Length == 0 && string.IsNullOrWhiteSpace(options.Instruction))
                throw new ArgumentException("FieldOptions requires a FieldType or an Instruction.");

            var instr = BuildInstruction(options);
            var placeholder = BuildPlaceholder(options, type, doc);

            var begin = new Run(new FieldChar { FieldCharType = FieldCharValues.Begin });
            var instrRun = new Run(new FieldCode(instr) { Space = SpaceProcessingModeValues.Preserve });
            var sep = new Run(new FieldChar { FieldCharType = FieldCharValues.Separate });
            var result = new Run(new Text(placeholder) { Space = SpaceProcessingModeValues.Preserve });
            var end = new Run(new FieldChar { FieldCharType = FieldCharValues.End });

            foreach (var run in new[] { begin, instrRun, sep, result, end })
                para.AppendChild(run);
        }

        private static string BuildInstruction(FieldOptions o)
        {
            if (!string.IsNullOrWhiteSpace(o.Instruction))
                return NormalizeInstruction(o.Instruction);

            var type = (o.FieldType ?? string.Empty).Trim().ToLowerInvariant();
            var sw = string.IsNullOrEmpty(o.Switches) ? string.Empty : " " + o.Switches.Trim();
            var dateFmt = string.IsNullOrEmpty(o.Format) ? string.Empty : "\\@ \"" + o.Format + "\" ";
            var name = o.Name ?? string.Empty;

            switch (type)
            {
                case "pagenum":
                case "pagenumber":
                case "page": return " PAGE" + sw + " ";
                case "numpages": return " NUMPAGES" + sw + " ";
                case "sectionpages": return " SECTIONPAGES" + sw + " ";
                case "section": return " SECTION" + sw + " ";
                case "edittime": return " EDITTIME" + sw + " ";
                case "author": return " AUTHOR" + sw + " ";
                case "lastsavedby": return " LASTSAVEDBY" + sw + " ";
                case "title": return " TITLE" + sw + " ";
                case "subject": return " SUBJECT" + sw + " ";
                case "filename": return " FILENAME" + sw + " ";
                case "numwords": return " NUMWORDS" + sw + " ";
                case "numchars": return " NUMCHARS" + sw + " ";
                case "revnum": return " REVNUM" + sw + " ";
                case "template": return " TEMPLATE" + sw + " ";
                case "comments":
                case "doccomments": return " COMMENTS" + sw + " ";
                case "keywords": return " KEYWORDS" + sw + " ";
                case "date":
                case "createdate":
                case "savedate":
                case "printdate":
                case "time":
                    return (" " + type.ToUpperInvariant() + " " + dateFmt + sw.Trim()).TrimEnd() + " ";
                case "mergefield":
                    RequireName(type, name);
                    return " MERGEFIELD " + QuoteIfNeeded(name) + sw + " ";
                case "ref":
                case "pageref":
                case "noteref":
                    RequireName(type, name);
                    return " " + type.ToUpperInvariant() + " " + name + sw + " ";
                case "seq":
                    RequireName(type, name);
                    return " SEQ " + name + sw + " ";
                case "styleref":
                    RequireName(type, name);
                    return " STYLEREF \"" + name + "\"" + sw + " ";
                case "docproperty":
                    RequireName(type, name);
                    return " DOCPROPERTY \"" + name + "\"" + sw + " ";
                case "if":
                    return BuildIfInstruction(o);
                default:
                    throw new ArgumentException(string.Format(
                        "Unknown field type '{0}'. Provide a known FieldType or an Instruction.", o.FieldType));
            }
        }

        private static string BuildIfInstruction(FieldOptions o)
        {
            var expr = (o.Expression ?? string.Empty).Trim();
            if (expr.Length == 0)
                throw new ArgumentException("IF requires an Expression.");
            return " IF " + expr + " \"" + (o.TrueText ?? string.Empty) + "\" \"" + (o.FalseText ?? string.Empty) + "\" ";
        }

        private static string BuildPlaceholder(FieldOptions o, string type, WordprocessingDocument doc)
        {
            if (!string.IsNullOrEmpty(o.Text))
                return o.Text;

            switch (type)
            {
                case "mergefield":
                case "ref":
                case "noteref":
                case "styleref":
                case "docproperty":
                    return "\u00AB" + (o.Name ?? string.Empty) + "\u00BB";
                case "if":
                    return EvaluateIf(o);
                case "seq":
                    return FormatSeqPlaceholder(doc, o.Name ?? string.Empty);
                case "date":
                case "createdate":
                case "savedate":
                case "printdate":
                    return FormatDate(o.Format, "M/d/yyyy");
                case "time":
                    return FormatDate(o.Format, "h:mm tt");
                case "pagenum":
                case "pagenumber":
                case "page":
                case "numpages":
                case "section":
                case "sectionpages":
                case "pageref":
                    return "1";
                default:
                    return string.Empty;
            }
        }

        private static string EvaluateIf(FieldOptions o)
        {
            var expr = (o.Expression ?? string.Empty).Trim();
            var trueText = o.TrueText ?? string.Empty;
            var falseText = o.FalseText ?? string.Empty;

            var m = Regex.Match(expr,
                @"^(?:""(?<ls>[^""]*)""|(?<ln>-?\d+(?:\.\d+)?))\s*(?<op><>|<=|>=|=|<|>)\s*(?:""(?<rs>[^""]*)""|(?<rn>-?\d+(?:\.\d+)?))$");
            if (!m.Success)
                return string.Empty;

            var op = m.Groups["op"].Value;
            bool result;
            if (m.Groups["ln"].Success && m.Groups["rn"].Success)
            {
                double l = double.Parse(m.Groups["ln"].Value, CultureInfo.InvariantCulture);
                double r = double.Parse(m.Groups["rn"].Value, CultureInfo.InvariantCulture);
                result = op == "=" ? l == r : op == "<>" ? l != r : op == "<" ? l < r : op == ">" ? l > r : op == "<=" ? l <= r : l >= r;
            }
            else
            {
                var l = m.Groups["ls"].Success ? m.Groups["ls"].Value : m.Groups["ln"].Value;
                var r = m.Groups["rs"].Success ? m.Groups["rs"].Value : m.Groups["rn"].Value;
                var cmp = string.Compare(l, r, StringComparison.OrdinalIgnoreCase);
                result = op == "=" ? cmp == 0 : op == "<>" ? cmp != 0 : op == "<" ? cmp < 0 : op == ">" ? cmp > 0 : op == "<=" ? cmp <= 0 : cmp >= 0;
            }
            return result ? trueText : falseText;
        }

        private static string FormatSeqPlaceholder(WordprocessingDocument doc, string identifier)
        {
            int count = 0;
            var body = doc.MainDocumentPart != null && doc.MainDocumentPart.Document != null
                ? doc.MainDocumentPart.Document.Body
                : null;
            if (body != null && !string.IsNullOrEmpty(identifier))
            {
                foreach (var instr in body.Descendants<FieldCode>())
                {
                    var text = instr.Text ?? string.Empty;
                    var m = Regex.Match(text, @"^\s*SEQ\s+(\S+)");
                    if (m.Success && m.Groups[1].Value.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                        count++;
                }
            }
            return (count + 1).ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatDate(string format, string defaultFormat)
        {
            var effective = string.IsNullOrEmpty(format) ? defaultFormat : format;
            try
            {
                return DateTime.Now.ToString(effective);
            }
            catch
            {
                return DateTime.Now.ToString(defaultFormat);
            }
        }

        private static string NormalizeInstruction(string instr)
        {
            return " " + instr.Trim() + " ";
        }

        private static void RequireName(string type, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException(string.Format(
                    "{0} requires a Name (e.g. Name=CustomerName).", type.ToUpperInvariant()));
        }

        private static string QuoteIfNeeded(string name)
        {
            if (name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"')
                return name;

            bool needs = false;
            foreach (var ch in name)
            {
                if (char.IsWhiteSpace(ch) || ch == '"' || ch == '\\')
                {
                    needs = true;
                    break;
                }
            }
            if (!needs)
                return name;

            return "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
