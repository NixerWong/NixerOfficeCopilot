using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using OfficeCopilot.OpenXml.Core;
using OfficeCopilot.OpenXml.Excel;
using OfficeCopilot.OpenXml.PowerPoint;
using OfficeCopilot.OpenXml.Tools;
using OfficeCopilot.OpenXml.Word;
using OfficeCopilot.Documents.Markdown;
using OfficeCopilot.OpenXml.Word.SmokeTest.ExcelEvalFixtures;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using P = DocumentFormat.OpenXml.Presentation;
using Dgm = DocumentFormat.OpenXml.Drawing.Diagrams;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace OfficeCopilot.OpenXml.Word.SmokeTest
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;
        private static string _dir;

        private static readonly byte[] OnePixelPng = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82
        };

        private static int Main(string[] args)
        {
            if (args != null && args.Length == 2
                && string.Equals(args[0], "--export-excel-eval-fixtures", StringComparison.OrdinalIgnoreCase))
            {
                return ExcelEvalFixtureExporter.Export(args[1]);
            }

            if (args != null && args.Length == 2
                && string.Equals(args[0], "--export-attachment-fixtures", StringComparison.OrdinalIgnoreCase))
            {
                return ExportAttachmentFixtures(args[1]);
            }

            _dir = Path.Combine(Path.GetTempPath(), "openxml-smoke-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_dir);
            Console.WriteLine("Smoke test dir: " + _dir);

            try
            {
                TestMergeData();
                TestCreateBlank();
                TestFields();
                TestNumbering();
                TestWatermark();
                TestRawRoundTrip();
                TestMerge();
                TestToc();
                TestExcel();
                TestPowerPoint();
                TestExcelRange();
                TestPowerPointSlides();
                TestExcelFormula();
                TestExcelMarkdown();
                TestOfficeMarkdown();
                TestLegacyOfficeRouting();
                TestDocumentMarkdownCache();
                TestOpenXmlPackageExpansionLimit();
                TestWordContentControlsAndLocalizedHeadings();
                TestWordAlternateContentAndTableContentControls();
                TestWorkbookTruncationKeepsSheetSections();
                TestExcelTable();
                TestPowerPointShapes();
                TestPowerPointTable();
                TestExcelAdvanced();
                TestPowerPointMedia();
                TestExcelChart();
                TestPowerPointChart();
                TestRegistryRouting();
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("  FATAL: " + ex);
            }

            Console.WriteLine();
            Console.WriteLine("RESULT  PASS=" + _passed + "  FAIL=" + _failed);
            return _failed == 0 ? 0 : 1;
        }

        private static int ExportAttachmentFixtures(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                Console.Error.WriteLine("An output directory is required.");
                return 2;
            }

            outputDirectory = Path.GetFullPath(outputDirectory);
            var workbookPath = Path.Combine(outputDirectory, "sales.xlsx");
            var wordPath = Path.Combine(outputDirectory, "quarterly.docx");
            var presentationPath = Path.Combine(outputDirectory, "quarterly.pptx");
            var manifestPath = Path.Combine(outputDirectory, "manifest.json");
            if (new[] { workbookPath, wordPath, presentationPath, manifestPath }.Any(File.Exists))
            {
                Console.Error.WriteLine("Refusing to overwrite existing attachment evaluation files.");
                return 2;
            }

            Directory.CreateDirectory(outputDirectory);
            ExcelDocumentEngine.CreateBlank(workbookPath);
            using (var workbook = new ExcelDocumentEngine(workbookPath, true))
            {
                workbook.WriteCell("Sheet1", 1, 1, "Region");
                workbook.WriteCell("Sheet1", 1, 2, "Revenue");
                workbook.WriteCell("Sheet1", 2, 1, "North");
                workbook.WriteCell("Sheet1", 2, 2, "1250");
                workbook.WriteCell("Sheet1", 3, 1, "South");
                workbook.WriteCell("Sheet1", 3, 2, "850");
                workbook.Save();
            }

            WordDocumentEngine.CreateBlank(wordPath);
            using (var word = new WordDocumentEngine(wordPath, true))
            {
                word.WritePartXml("/word/document.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
                    + "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Quarterly Sales</w:t></w:r></w:p>"
                    + "<w:p><w:r><w:t>North revenue was 1250 USD for Q1 2025.</w:t></w:r></w:p>"
                    + "<w:sectPr/></w:body></w:document>");
                word.Save();
            }

            PowerPointDocumentEngine.CreateBlank(presentationPath);
            using (var presentation = new PowerPointDocumentEngine(presentationPath, true))
            {
                presentation.AddSlide("Quarterly Sales", "North revenue reached 1250 USD in Q1 2025.");
                presentation.AddTable(1, 2, 2, new List<IReadOnlyList<string>>
                {
                    new List<string> { "Region", "Revenue" },
                    new List<string> { "North", "1250" }
                });
                presentation.Save();
            }

            const string manifest = "{\n"
                + "  \"cases\": [\n"
                + "    {\"id\": \"sales-workbook\", \"file\": \"sales.xlsx\", \"questions\": ["
                + "{\"id\": \"north-revenue\", \"question\": \"What was North revenue?\", \"requiredTerms\": [\"North\", \"1250\"]}]},\n"
                + "    {\"id\": \"quarterly-report\", \"file\": \"quarterly.docx\", \"questions\": ["
                + "{\"id\": \"period\", \"question\": \"Which period is covered?\", \"requiredTerms\": [\"Q1 2025\", \"1250\"]}]},\n"
                + "    {\"id\": \"quarterly-presentation\", \"file\": \"quarterly.pptx\", \"questions\": ["
                + "{\"id\": \"north-revenue\", \"question\": \"What was North revenue?\", \"requiredTerms\": [\"North\", \"1250\"]}]}\n"
                + "  ]\n"
                + "}\n";
            File.WriteAllText(manifestPath, manifest, new System.Text.UTF8Encoding(false));
            Console.WriteLine("Attachment evaluation fixtures written to: " + outputDirectory);
            return 0;
        }

        private static void Check(string name, bool ok)
        {
            if (ok)
            {
                _passed++;
                Console.WriteLine("  PASS  " + name);
            }
            else
            {
                _failed++;
                Console.WriteLine("  FAIL  " + name);
            }
        }

        private static void TestMergeData()
        {
            Console.WriteLine("[0] MergeData flattening");
            var md = MergeData.FromJson("{\"a\":{\"b\":\"X\"},\"items\":[\"y\",\"z\"]}");
            string v;
            Check("a.b flattened", md.TryGet("a.b", out v) && v == "X");
            Check("items[0] flattened", md.TryGet("items[0]", out v) && v == "y");
            Check("items[1] flattened", md.TryGet("items[1]", out v) && v == "z");
        }

        private static void TestCreateBlank()
        {
            Console.WriteLine("[1] CreateBlank + Validate");
            var path = Path.Combine(_dir, "blank.docx");
            WordDocumentEngine.CreateBlank(path);
            Check("file exists", File.Exists(path));

            using (var engine = new WordDocumentEngine(path, false))
            {
                Check("validate clean", engine.Validate().Count == 0);
                var uris = engine.GetPartUris();
                Check("has /word/document.xml", uris.Contains("/word/document.xml"));
                Check("read document.xml", engine.ReadPartXml("/word/document.xml") != null);
            }
        }

        private static void TestFields()
        {
            Console.WriteLine("[2] Fields");
            var path = Path.Combine(_dir, "fields.docx");
            WordDocumentEngine.CreateBlank(path);

            using (var engine = new WordDocumentEngine(path, true))
            {
                engine.AppendFieldParagraph(new FieldOptions { FieldType = "page" });
                engine.AddFieldToParagraph(0, new FieldOptions { FieldType = "date", Format = "yyyy-MM-dd" });
                engine.Save();
            }

            using (var engine = new WordDocumentEngine(path, false))
            {
                var xml = engine.ReadPartXml("/word/document.xml");
                Check("PAGE field present", xml != null && xml.Contains(" PAGE "));
                Check("DATE field present", xml != null && xml.Contains(" DATE "));
                Check("validate clean", engine.Validate().Count == 0);
            }
        }

        private static void TestNumbering()
        {
            Console.WriteLine("[3] Numbering");
            var path = Path.Combine(_dir, "numbering.docx");
            WordDocumentEngine.CreateBlank(path);
            int numId = -1;

            using (var engine = new WordDocumentEngine(path, true))
            {
                numId = engine.AddNumberingList(new NumberingListOptions { Format = "bullet" });
                engine.ApplyNumberingToBodyParagraph(0, numId);
                engine.Save();
            }

            using (var engine = new WordDocumentEngine(path, false))
            {
                Check("numId assigned", numId >= 1);
                Check("numbering part present", engine.GetPartUris().Contains("/word/numbering.xml"));
                Check("validate clean", engine.Validate().Count == 0);
            }
        }

        private static void TestWatermark()
        {
            Console.WriteLine("[4] Watermark");
            var path = Path.Combine(_dir, "watermark.docx");
            WordDocumentEngine.CreateBlank(path);

            using (var engine = new WordDocumentEngine(path, true))
            {
                engine.AddWatermark(new WatermarkOptions { Text = "CONFIDENTIAL" });
                engine.Save();
            }

            using (var engine = new WordDocumentEngine(path, false))
            {
                Check("watermark found", engine.FindWatermark() == "CONFIDENTIAL");
                Check("header part present", engine.GetPartUris().Any(u => u.IndexOf("header", StringComparison.OrdinalIgnoreCase) >= 0));
                Check("validate clean", engine.Validate().Count == 0);
            }

            using (var engine = new WordDocumentEngine(path, true))
            {
                engine.RemoveWatermark();
                engine.Save();
            }

            using (var engine = new WordDocumentEngine(path, false))
            {
                Check("watermark removed", engine.FindWatermark() == null);
            }
        }

        private static void TestRawRoundTrip()
        {
            Console.WriteLine("[5] Raw write persistence");
            var path = Path.Combine(_dir, "raw.docx");
            WordDocumentEngine.CreateBlank(path);

            using (var engine = new WordDocumentEngine(path, true))
            {
                var xml = engine.ReadPartXml("/word/document.xml");
                var modified = xml.Replace("<w:t />", "<w:t>RAW_SENTINEL</w:t>");
                Check("injection matched", modified != xml);
                engine.WritePartXml("/word/document.xml", modified);
                engine.Save();
            }

            using (var engine = new WordDocumentEngine(path, false))
            {
                var after = engine.ReadPartXml("/word/document.xml");
                Check("raw write persisted", after != null && after.IndexOf("RAW_SENTINEL", StringComparison.Ordinal) >= 0);
            }
        }

        private static void TestMerge()
        {
            Console.WriteLine("[6] Merge template");
            var template = Path.Combine(_dir, "template.docx");
            WordDocumentEngine.CreateBlank(template);

            // Inject a {{name}} placeholder through the raw part write.
            using (var engine = new WordDocumentEngine(template, true))
            {
                var xml = engine.ReadPartXml("/word/document.xml");
                xml = xml.Replace("<w:t />", "<w:t>Hello {{name}}</w:t>");
                xml = xml.Replace("<w:t/>", "<w:t>Hello {{name}}</w:t>");
                xml = xml.Replace("<w:t></w:t>", "<w:t>Hello {{name}}</w:t>");
                engine.WritePartXml("/word/document.xml", xml);
                engine.Save();
            }

            var output = Path.Combine(_dir, "merged.docx");
            var result = WordDocumentEngine.MergeTemplate(
                template,
                output,
                MergeData.FromJson("{\"name\":\"World\"}"));

            Check("replaced count > 0", result.ReplacedCount > 0);
            Check("no unresolved placeholders", result.UnresolvedPlaceholders.Count == 0);

            using (var engine = new WordDocumentEngine(output, false))
            {
                var xml = engine.ReadPartXml("/word/document.xml");
                Check("placeholder replaced", xml != null && xml.IndexOf("World", StringComparison.Ordinal) >= 0 && xml.IndexOf("{{name}}", StringComparison.Ordinal) < 0);
            }
        }

        private static void TestToc()
        {
            Console.WriteLine("[7] TOC regeneration");
            var path = Path.Combine(_dir, "toc.docx");
            WordDocumentEngine.CreateBlank(path);

            const string tocDocXml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>
    <w:p><w:pPr><w:pStyle w:val=""Heading1""/></w:pPr><w:r><w:t>Chapter 1</w:t></w:r></w:p>
    <w:p><w:pPr><w:pStyle w:val=""Heading2""/></w:pPr><w:r><w:t>Section 1.1</w:t></w:r></w:p>
    <w:p>
      <w:r><w:fldChar w:fldCharType=""begin""/></w:r>
      <w:r><w:instrText xml:space=""preserve""> TOC \o ""1-3"" \h </w:instrText></w:r>
      <w:r><w:fldChar w:fldCharType=""separate""/></w:r>
      <w:r><w:t>OLD_TOC</w:t></w:r>
      <w:r><w:fldChar w:fldCharType=""end""/></w:r>
    </w:p>
    <w:sectPr><w:pgSz w:w=""11906"" w:h=""16838""/></w:sectPr>
  </w:body>
</w:document>";

            using (var engine = new WordDocumentEngine(path, true))
            {
                engine.WritePartXml("/word/document.xml", tocDocXml);
                engine.Save();
            }

            using (var engine = new WordDocumentEngine(path, true))
            {
                engine.RegenerateAllTocs();
                engine.Save();
            }

            using (var engine = new WordDocumentEngine(path, false))
            {
                var xml = engine.ReadPartXml("/word/document.xml");
                Check("heading preserved", xml != null && xml.IndexOf("Chapter 1", StringComparison.Ordinal) >= 0);
                Check("TOC1 entry generated", xml != null && xml.IndexOf("TOC1", StringComparison.Ordinal) >= 0);
                Check("PAGEREF present", xml != null && xml.IndexOf("PAGEREF", StringComparison.Ordinal) >= 0);
                Check("old TOC content removed", xml == null || xml.IndexOf("OLD_TOC", StringComparison.Ordinal) < 0);
            }
        }

        private static void TestExcel()
        {
            Console.WriteLine("[8] Excel create + merge");
            var path = Path.Combine(_dir, "excel.xlsx");
            ExcelDocumentEngine.CreateBlank(path);
            Check("xlsx exists", File.Exists(path));

            using (var engine = new ExcelDocumentEngine(path, false))
            {
                Check("xlsx validate clean", engine.Validate().Count == 0);
            }

            // Inject a placeholder cell through the raw part write.
            using (var engine = new ExcelDocumentEngine(path, true))
            {
                var xml = engine.ReadPartXml("/xl/worksheets/sheet1.xml");
                const string cell = "<x:row r=\"1\"><x:c r=\"A1\" t=\"inlineStr\"><x:is><x:t>Hello {{name}}</x:t></x:is></x:c></x:row>";
                xml = xml.Replace("<x:sheetData/>", "<x:sheetData>" + cell + "</x:sheetData>");
                xml = xml.Replace("<x:sheetData />", "<x:sheetData>" + cell + "</x:sheetData>");
                xml = xml.Replace("<x:sheetData></x:sheetData>", "<x:sheetData>" + cell + "</x:sheetData>");
                Check("cell injection matched", xml.IndexOf("{{name}}", StringComparison.Ordinal) >= 0);
                engine.WritePartXml("/xl/worksheets/sheet1.xml", xml);
                engine.Save();
            }

            var output = Path.Combine(_dir, "excel-merged.xlsx");
            var result = ExcelDocumentEngine.MergeTemplate(
                path, output, MergeData.FromJson("{\"name\":\"World\"}"));
            Check("excel replaced > 0", result.ReplacedCount > 0);

            using (var engine = new ExcelDocumentEngine(output, false))
            {
                var xml = engine.ReadPartXml("/xl/worksheets/sheet1.xml");
                Check("excel cell replaced", xml != null && xml.IndexOf("World", StringComparison.Ordinal) >= 0 && xml.IndexOf("{{name}}", StringComparison.Ordinal) < 0);
            }
        }

        private static void TestPowerPoint()
        {
            Console.WriteLine("[9] PowerPoint create + merge");
            var path = Path.Combine(_dir, "slides.pptx");
            PowerPointDocumentEngine.CreateBlank(path);
            Check("pptx exists", File.Exists(path));

            using (var engine = new PowerPointDocumentEngine(path, false))
            {
                Check("pptx has slide part", engine.GetPartUris().Any(u => u.IndexOf("slide", StringComparison.OrdinalIgnoreCase) >= 0));
                var errors = engine.Validate();
                foreach (var error in errors.Take(8))
                    Console.WriteLine("      validate: " + error);
                Check("pptx validate clean", errors.Count == 0);
            }

            // Inject a placeholder shape by replacing the whole slide with a
            // self-contained, namespace-declared slide XML.
            const string slideXml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<p:sld xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"" xmlns:p=""http://schemas.openxmlformats.org/presentationml/2006/main"">
  <p:cSld>
    <p:spTree>
      <p:nvGrpSpPr><p:cNvPr id=""1"" name=""""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
      <p:grpSpPr/>
      <p:sp>
        <p:nvSpPr><p:cNvPr id=""2"" name=""Title""/><p:cNvSpPr/><p:nvPr/></p:nvSpPr>
        <p:spPr/>
        <p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Hello {{name}}</a:t></a:r></a:p></p:txBody>
      </p:sp>
    </p:spTree>
  </p:cSld>
  <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
</p:sld>";

            using (var engine = new PowerPointDocumentEngine(path, true))
            {
                engine.WritePartXml("/ppt/slides/slide1.xml", slideXml);
                engine.Save();
            }

            var output = Path.Combine(_dir, "slides-merged.pptx");
            var result = PowerPointDocumentEngine.MergeTemplate(
                path, output, MergeData.FromJson("{\"name\":\"World\"}"));
            Check("pptx replaced > 0", result.ReplacedCount > 0);

            using (var engine = new PowerPointDocumentEngine(output, false))
            {
                var xml = engine.ReadPartXml("/ppt/slides/slide1.xml");
                Check("pptx shape replaced", xml != null && xml.IndexOf("World", StringComparison.Ordinal) >= 0 && xml.IndexOf("{{name}}", StringComparison.Ordinal) < 0);
            }
        }

        private static void TestExcelRange()
        {
            Console.WriteLine("[11] Excel cell/range read + write");
            var path = Path.Combine(_dir, "range.xlsx");
            ExcelDocumentEngine.CreateBlank(path);

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                engine.WriteCell("Sheet1", 1, 1, "Name");
                engine.WriteCell("Sheet1", 1, 2, "Qty");
                engine.WriteCell("Sheet1", 2, 1, "Apples");
                engine.WriteCell("Sheet1", 2, 2, "42");
                engine.WriteCell("Sheet1", 3, 1, "Pears");
                engine.WriteCell("Sheet1", 3, 2, "7.5");
                engine.Save();
            }

            using (var engine = new ExcelDocumentEngine(path, false))
            {
                Check("read cell B2", engine.ReadCell("Sheet1", 2, 2) == "42");
                Check("read cell A3", engine.ReadCell("Sheet1", 3, 1) == "Pears");
                Check("read empty cell", engine.ReadCell("Sheet1", 10, 10) == string.Empty);
                var range = engine.ReadRange("Sheet1", 1, 1, 3, 2);
                Check("range dims", range.RowCount == 3 && range.ColumnCount == 2);
                Check("range value", range.Cells[1][1] == "42");
                Check("validate clean", engine.Validate().Count == 0);
            }

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                engine.WriteRange("Sheet1", 5, 1, new List<IReadOnlyList<string>>
                {
                    new List<string> { "X", "Y" },
                    new List<string> { "1", "2" }
                });
                engine.Save();
            }

            using (var engine = new ExcelDocumentEngine(path, false))
            {
                Check("range write persisted", engine.ReadCell("Sheet1", 6, 2) == "2");
                Check("validate clean after range write", engine.Validate().Count == 0);
            }
        }

        private static void TestPowerPointSlides()
        {
            Console.WriteLine("[12] PowerPoint add/read slides");
            var path = Path.Combine(_dir, "slides2.pptx");
            PowerPointDocumentEngine.CreateBlank(path);

            using (var engine = new PowerPointDocumentEngine(path, true))
            {
                var idx = engine.AddSlide("Quarterly Report", "Revenue up 20%");
                Check("add slide index", idx == 2);
                engine.Save();
            }

            using (var engine = new PowerPointDocumentEngine(path, false))
            {
                var slides = engine.ListSlides();
                Check("slide count", slides.Count == 2);
                var text = engine.ReadSlideText(2);
                Check("slide text has title", text.IndexOf("Quarterly Report", StringComparison.Ordinal) >= 0);
                Check("slide text has body", text.IndexOf("Revenue up 20%", StringComparison.Ordinal) >= 0);
                Check("validate clean", engine.Validate().Count == 0);
            }
        }

        private static void TestExcelFormula()
        {
            Console.WriteLine("[13] Excel formula set/read");
            var path = Path.Combine(_dir, "formula.xlsx");
            ExcelDocumentEngine.CreateBlank(path);

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                engine.WriteCell("Sheet1", 1, 1, "2");
                engine.WriteCell("Sheet1", 1, 2, "3");
                engine.SetCellFormula("Sheet1", 1, 3, "A1+B1");
                engine.Save();
            }

            using (var engine = new ExcelDocumentEngine(path, false))
            {
                Check("formula readback", engine.ReadCellFormula("Sheet1", 1, 3) == "=A1+B1");
                Check("no formula on value cell", engine.ReadCellFormula("Sheet1", 1, 1) == null);
                Check("validate clean", engine.Validate().Count == 0);
            }
        }

        private static void TestExcelTable()
        {
            Console.WriteLine("[14] Excel table add/read/write");
            var path = Path.Combine(_dir, "table.xlsx");
            ExcelDocumentEngine.CreateBlank(path);

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                engine.AddTable("Sheet1", "A1",
                    new List<string> { "Name", "Qty" },
                    new List<IReadOnlyList<string>>
                    {
                        new List<string> { "Apples", "5" },
                        new List<string> { "Pears", "2" }
                    },
                    null);
                engine.Save();
            }

            using (var engine = new ExcelDocumentEngine(path, false))
            {
                var table = engine.ReadTable("Sheet1", "1");
                Check("table name", table.Name == "Table1");
                Check("table headers", table.Headers.Count == 2 && table.Headers[0] == "Name" && table.Headers[1] == "Qty");
                Check("table rows", table.Rows.Count == 2 && table.Rows[0][0] == "Apples" && table.Rows[1][1] == "2");
                Check("validate clean", engine.Validate().Count == 0);
            }

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                engine.WriteTable("Sheet1", "Table1", new List<IReadOnlyList<string>>
                {
                    new List<string> { "Grapes", "9" },
                    new List<string> { "Melon", "4" },
                    new List<string> { "Kiwi", "7" }
                });
                engine.Save();
            }

            using (var engine = new ExcelDocumentEngine(path, false))
            {
                var table = engine.ReadTable("Sheet1", "Table1");
                Check("table expanded", table.Rows.Count == 3 && table.Rows[0][0] == "Grapes" && table.Rows[2][1] == "7");
                Check("validate clean after write", engine.Validate().Count == 0);
            }
        }

        private static void TestExcelMarkdown()
        {
            Console.WriteLine("[14] Excel Markdown conversion");
            var path = Path.Combine(_dir, "markdown.xlsx");
            ExcelDocumentEngine.CreateBlank(path);

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                engine.WriteCell("Sheet1", 1, 1, "Region");
                engine.WriteCell("Sheet1", 1, 2, "Total");
                engine.WriteCell("Sheet1", 2, 1, "North|East");
                engine.WriteCell("Sheet1", 2, 2, "5");
                engine.SetCellFormula("Sheet1", 3, 2, "SUM(B2:B2)");
                engine.AddChart("Sheet1", new List<string> { "Jan", "Feb" }, new List<double> { 4, 7 }, "Units", "Units by month");
                engine.Save();
            }

            var markdown = new DocumentMarkdownService().Convert(path).ToMarkdown();
            Check("worksheet heading", markdown.Contains("## Sheet: Sheet1"));
            Check("table delimiters escaped", markdown.Contains("North\\|East"));
            Check("formula-only row retained", markdown.Contains("=SUM(B2:B2)"));

            using (var document = SpreadsheetDocument.Open(path, true))
            {
                var workbookPart = document.WorkbookPart;
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                var stylesheet = new Stylesheet();
                stylesheet.AppendChild(new NumberingFormats(
                    new NumberingFormat { NumberFormatId = 164, FormatCode = "0.0%" },
                    new NumberingFormat { NumberFormatId = 165, FormatCode = "\"$\"#,##0.00" }));
                stylesheet.AppendChild(new Fonts(new Font()));
                stylesheet.AppendChild(new Fills(
                    new Fill(new PatternFill { PatternType = PatternValues.None }),
                    new Fill(new PatternFill { PatternType = PatternValues.Gray125 })));
                stylesheet.AppendChild(new Borders(new Border()));
                stylesheet.AppendChild(new CellStyleFormats(new CellFormat()));
                stylesheet.AppendChild(new CellFormats(
                    new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },
                    new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 14, ApplyNumberFormat = true },
                    new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 164, ApplyNumberFormat = true },
                    new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 165, ApplyNumberFormat = true }));
                stylesheet.AppendChild(new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
                stylesPart.Stylesheet = stylesheet;
                stylesheet.Save();

                var worksheet = workbookPart.WorksheetParts.First().Worksheet;
                var sheetData = worksheet.GetFirstChild<SheetData>();
                worksheet.InsertAfter(new MergeCells(new MergeCell { Reference = "A8:B8" }), sheetData);
                var row4 = new Row { RowIndex = 4 };
                row4.Append(
                    new Cell { CellReference = "A4", StyleIndex = 2, CellValue = new CellValue("0.125") },
                    new Cell { CellReference = "B4", StyleIndex = 1, CellValue = new CellValue("45292") });
                sheetData.Append(row4);
                var row5 = new Row { RowIndex = 5 };
                row5.Append(new Cell { CellReference = "A5", StyleIndex = 3, CellValue = new CellValue("1234.5") });
                sheetData.Append(row5);
                var row6 = new Row { RowIndex = 6 };
                row6.Append(
                    new Cell { CellReference = "A6", CellValue = new CellValue("3.5") },
                    new Cell { CellReference = "B6", CellValue = new CellValue("100") });
                sheetData.Append(row6);
                var row7 = new Row { RowIndex = 7 };
                row7.Append(
                    new Cell { CellReference = "A7", CellValue = new CellValue("9.5") },
                    new Cell { CellReference = "B7", CellValue = new CellValue("200") });
                sheetData.Append(row7);
                var row10 = new Row { RowIndex = 10 };
                row10.Append(
                    new Cell { CellReference = "D10", DataType = CellValues.String, CellValue = new CellValue("Metric") },
                    new Cell { CellReference = "E10", DataType = CellValues.String, CellValue = new CellValue("Value") });
                sheetData.Append(row10);
                var row11 = new Row { RowIndex = 11 };
                row11.Append(
                    new Cell { CellReference = "D11", DataType = CellValues.String, CellValue = new CellValue("Count") },
                    new Cell { CellReference = "E11", CellValue = new CellValue("12") });
                sheetData.Append(row11);
                if (worksheet.SheetDimension == null)
                {
                    worksheet.SheetDimension = new SheetDimension { Reference = "A1:E11" };
                }
                else
                {
                    worksheet.SheetDimension.Reference = "A1:E11";
                }
                worksheet.Save();
            }

            var formattedMarkdown = new DocumentMarkdownService().Convert(path).ToMarkdown();
            Check("date style rendered as ISO date", formattedMarkdown.Contains("2024-01-01"));
            Check("percentage style scales value", formattedMarkdown.Contains("12.5%"));
            Check("currency style applies separators and decimals", formattedMarkdown.Contains("$1,234.50"));
            Check("merged ranges are described", formattedMarkdown.Contains("Merged ranges: A8:B8"));
            Check("disconnected populated blocks are identified", formattedMarkdown.Contains("Detected populated regions: A1:B7; D10:E11"));
            Check("chart title, type, series, and cached points are extracted",
                formattedMarkdown.Contains("Chart 1: Units by month (column)")
                && formattedMarkdown.Contains("Series 1: Units")
                && formattedMarkdown.Contains("| Feb | 7 |"));
            var chartLimited = new SpreadsheetMarkdownConverter().Convert(path, new MarkdownConversionOptions
            {
                WorksheetName = "Sheet1",
                MaxSheetRows = 1
            }).ToMarkdown();
            Check("chart series respects the configured point limit",
                chartLimited.Contains("Chart series truncated to the first 1 points.")
                && !chartLimited.Contains("| Feb | 7 |"));

            var selectedRange = new SpreadsheetMarkdownConverter().Convert(path, new MarkdownConversionOptions
            {
                WorksheetName = "Sheet1",
                CellRange = "B2:B4"
            }).ToMarkdown();
            Check("selected sheet and range are shown", selectedRange.Contains("## Sheet: Sheet1 (B2:B4)"));
            Check("range table starts at requested column", selectedRange.Contains("| Row | B |"));
            Check("range includes formulas and formatted dates", selectedRange.Contains("=SUM(B2:B2)") && selectedRange.Contains("2024-01-01"));
            Check("range omits values outside requested cells", !selectedRange.Contains("North\\|East") && !selectedRange.Contains("$1,234.50"));
            Check("range fetch omits whole-sheet charts", !selectedRange.Contains("**Charts**"));

            var truncatedService = new DocumentMarkdownService(
                new IDocumentMarkdownConverter[] { new SpreadsheetMarkdownConverter() },
                new MarkdownConversionOptions { MaxSheetRows = 2 });
            var truncatedMarkdown = truncatedService.Convert(path).ToMarkdown();
            Check("truncated sheet reports full numeric column statistics",
                truncatedMarkdown.Contains("| A | Mixed | 6 | 0.125 | 1234.5 | 1247.625 |"));
        }

        private static void TestOfficeMarkdown()
        {
            Console.WriteLine("[15] Word and PowerPoint Markdown conversion");
            var wordPath = Path.Combine(_dir, "markdown.docx");
            WordDocumentEngine.CreateBlank(wordPath);
            const string documentXml = @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main""><w:body>
  <w:p><w:pPr><w:pStyle w:val=""Heading1""/></w:pPr><w:r><w:t>Quarterly Summary</w:t></w:r></w:p>
    <w:p><w:pPr><w:pStyle w:val=""Heading2""/></w:pPr><w:r><w:t>Details</w:t></w:r></w:p>
  <w:p><w:r><w:t>Revenue increased.</w:t></w:r></w:p>
    <w:p><w:pPr><w:numPr><w:ilvl w:val=""0""/><w:numId w:val=""5""/></w:numPr></w:pPr><w:r><w:t>Bullet item</w:t></w:r></w:p>
    <w:p><w:pPr><w:numPr><w:ilvl w:val=""1""/><w:numId w:val=""5""/></w:numPr></w:pPr><w:r><w:t>Nested item</w:t></w:r></w:p>
    <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Region</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Total</w:t></w:r></w:p></w:tc></w:tr>
    <w:tr><w:tc><w:tcPr><w:gridSpan w:val=""2""/></w:tcPr><w:p><w:r><w:t>Combined row</w:t></w:r></w:p></w:tc></w:tr>
    <w:tr><w:tc><w:tcPr><w:vMerge w:val=""restart""/></w:tcPr><w:p><w:r><w:t>Merged label</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>10</w:t></w:r></w:p></w:tc></w:tr>
    <w:tr><w:tc><w:tcPr><w:vMerge/></w:tcPr><w:p/></w:tc><w:tc><w:p><w:r><w:t>20</w:t></w:r></w:p></w:tc></w:tr>
    <w:tr><w:tc><w:p><w:r><w:t>North|East</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>42</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
  <w:sectPr/></w:body></w:document>";
            using (var engine = new WordDocumentEngine(wordPath, true))
            {
                engine.WritePartXml("/word/document.xml", documentXml);
                engine.Save();
            }

            using (var document = WordprocessingDocument.Open(wordPath, true))
            {
                var mainPart = document.MainDocumentPart;
                var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
                numberingPart.Numbering = new W.Numbering(
                    new W.AbstractNum(
                        new W.MultiLevelType { Val = W.MultiLevelValues.Multilevel },
                        new W.Level(
                            new W.StartNumberingValue { Val = 1 },
                            new W.NumberingFormat { Val = W.NumberFormatValues.Bullet },
                            new W.LevelText { Val = "*" }) { LevelIndex = 0 },
                        new W.Level(
                            new W.StartNumberingValue { Val = 1 },
                            new W.NumberingFormat { Val = W.NumberFormatValues.Decimal },
                            new W.LevelText { Val = "%2." }) { LevelIndex = 1 }) { AbstractNumberId = 1 },
                    new W.NumberingInstance(new W.AbstractNumId { Val = 1 }) { NumberID = 5 });
                numberingPart.Numbering.Save();
                var noteParagraph = mainPart.Document.Body.Elements<W.Paragraph>()
                    .First(paragraph => paragraph.InnerText.Contains("Revenue increased."));
                var hyperlinkId = mainPart.AddHyperlinkRelationship(new Uri("https://example.com/quarterly-report"), true).Id;
                noteParagraph.Append(new W.Hyperlink(new W.Run(new W.Text("Quarterly report"))) { Id = hyperlinkId });
                var drawingPlaceholder = new W.Drawing(
                    new Wp.Inline(
                        new Wp.Extent { Cx = 914400L, Cy = 914400L },
                        new Wp.DocProperties { Id = 1U, Name = "Picture 1", Description = "Quarterly chart" },
                        new Wp.NonVisualGraphicFrameDrawingProperties(),
                        new Drawing.Graphic(new Drawing.GraphicData { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })));
                noteParagraph.Append(new W.Run(drawingPlaceholder));
                noteParagraph.Append(new W.Run(new W.FootnoteReference { Id = 1 }));
                noteParagraph.Append(new W.Run(new W.EndnoteReference { Id = 2 }));
                var contentRun = noteParagraph.Elements<W.Run>().First();
                noteParagraph.InsertBefore(new W.CommentRangeStart { Id = "3" }, contentRun);
                noteParagraph.InsertAfter(new W.CommentRangeEnd { Id = "3" }, contentRun);
                noteParagraph.Append(new W.Run(new W.CommentReference { Id = "3" }));

                var footnotesPart = mainPart.AddNewPart<FootnotesPart>();
                footnotesPart.Footnotes = new W.Footnotes(
                    new W.Footnote(new W.Paragraph(new W.Run(new W.Text("Footnote detail.")))) { Id = 1 });
                footnotesPart.Footnotes.Save();

                var endnotesPart = mainPart.AddNewPart<EndnotesPart>();
                endnotesPart.Endnotes = new W.Endnotes(
                    new W.Endnote(new W.Paragraph(new W.Run(new W.Text("Endnote detail.")))) { Id = 2 });
                endnotesPart.Endnotes.Save();

                var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
                commentsPart.Comments = new W.Comments(
                    new W.Comment(new W.Paragraph(new W.Run(new W.Text("Review note."))))
                    {
                        Id = "3",
                        Author = "Reviewer"
                    });
                commentsPart.Comments.Save();
                mainPart.Document.Save();
            }

            var wordDocument = new DocumentMarkdownService().Convert(wordPath);
            var wordMarkdown = wordDocument.ToMarkdown();
            Check("Word heading extracted", wordMarkdown.Contains("# Quarterly Summary"));
            Check("Word heading sections support targeted retrieval",
                wordDocument.Sections.Count == 2
                && wordDocument.Sections[0].Title == "Quarterly Summary"
                && wordDocument.Sections[1].Index == 2
                && wordDocument.Sections[1].Title == "Details"
                && wordDocument.Sections[1].Markdown.Contains("Revenue increased."));
            Check("Word numbering format and nested level are preserved",
                wordMarkdown.Contains("- Bullet item") && wordMarkdown.Contains("  1. Nested item"));
            Check("Word paragraph extracted", wordMarkdown.Contains("Revenue increased."));
            Check("Word external hyperlink text and target extracted",
                wordMarkdown.Contains("[Quarterly report](https://example.com/quarterly-report)"));
            Check("Word drawing alternative text extracted",
                wordMarkdown.Contains("![Image: Quarterly chart]()"));
            Check("Word table escaped", wordMarkdown.Contains("North\\|East"));
            Check("Word horizontal merge preserves covered columns", wordMarkdown.Contains("| Combined row |  |"));
            Check("Word vertical merge emits content only in the leading row",
                wordMarkdown.Contains("| Merged label | 10 |") && wordMarkdown.Contains("|  | 20 |"));
            Check("Word footnote reference and definition extracted",
                wordMarkdown.Contains("[^1]") && wordMarkdown.Contains("[^1]: Footnote detail."));
            Check("Word endnote reference and definition extracted",
                wordMarkdown.Contains("[^2]") && wordMarkdown.Contains("[^2]: Endnote detail."));
            Check("Word comment anchor, author, and definition extracted",
                wordMarkdown.Contains("[Comment 3]") && wordMarkdown.Contains("(Reviewer): Review note."));

            var presentationPath = Path.Combine(_dir, "markdown.pptx");
            var mixedImagePath = Path.Combine(_dir, "mixed-order.png");
            File.WriteAllBytes(mixedImagePath, OnePixelPng);
            PowerPointDocumentEngine.CreateBlank(presentationPath);
            uint lowerShapeId;
            using (var engine = new PowerPointDocumentEngine(presentationPath, true))
            {
                engine.AddSlide("Overview", "Revenue increased.");
                engine.AddNotes(1, "Presenter detail.\nCall out growth.");
                lowerShapeId = engine.AddTextBox(1, "Lower item");
                engine.AddTextBox(1, "Upper item");
                engine.AddChart(1, new List<string> { "A", "B" }, new List<double> { 3, 7 }, "Revenue", "Slide chart");
                engine.AddTable(1, 2, 1, new List<IReadOnlyList<string>>
                {
                    new List<string> { "Mixed table" },
                    new List<string> { "Row value" }
                });
                engine.AddPicture(1, mixedImagePath);
                engine.Save();
            }

            using (var document = PresentationDocument.Open(presentationPath, true))
            {
                var slidePart = document.PresentationPart.SlideParts.First();
                var lowerShape = slidePart.Slide.Descendants<P.Shape>().First(shape => shape.InnerText.Contains("Lower item"));
                lowerShape.ShapeProperties.Transform2D.Offset.Y = 3000000L;
                var upperShape = slidePart.Slide.Descendants<P.Shape>().First(shape => shape.InnerText.Contains("Upper item"));
                upperShape.ShapeProperties.Transform2D.Offset.Y = 1000000L;
                var tableFrame = slidePart.Slide.Descendants<P.GraphicFrame>()
                    .First(frame => frame.Descendants<Drawing.Table>().Any());
                tableFrame.Transform.Offset.Y = 2200000L;
                var picture = slidePart.Slide.Descendants<P.Picture>().First();
                picture.ShapeProperties.Transform2D.Offset.Y = 2900000L;
                picture.NonVisualPictureProperties.NonVisualDrawingProperties.Description = "Growth chart image";

                var diagramPart = slidePart.AddNewPart<DiagramDataPart>();
                var root = new Dgm.Point
                {
                    ModelId = "root",
                    Type = Dgm.PointValues.Document,
                    TextBody = new Dgm.TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle(),
                        new Drawing.Paragraph(new Drawing.Run(new Drawing.Text("Strategy"))))
                };
                var efficiency = new Dgm.Point
                {
                    ModelId = "efficiency",
                    Type = Dgm.PointValues.Node,
                    TextBody = new Dgm.TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle(),
                        new Drawing.Paragraph(new Drawing.Run(new Drawing.Text("Efficiency"))))
                };
                var growth = new Dgm.Point
                {
                    ModelId = "growth",
                    Type = Dgm.PointValues.Node,
                    TextBody = new Dgm.TextBody(new Drawing.BodyProperties(), new Drawing.ListStyle(),
                        new Drawing.Paragraph(new Drawing.Run(new Drawing.Text("Growth"))))
                };
                diagramPart.DataModelRoot = new Dgm.DataModelRoot(
                    new Dgm.PointList(root, efficiency, growth),
                    new Dgm.ConnectionList(
                        new Dgm.Connection
                        {
                            ModelId = "edge-growth",
                            Type = Dgm.ConnectionValues.ParentOf,
                            SourceId = "root",
                            DestinationId = "growth",
                            DestinationPosition = 0
                        },
                        new Dgm.Connection
                        {
                            ModelId = "edge-efficiency",
                            Type = Dgm.ConnectionValues.ParentOf,
                            SourceId = "root",
                            DestinationId = "efficiency",
                            DestinationPosition = 1
                        }));
                diagramPart.DataModelRoot.Save();
                var diagramFrame = new P.GraphicFrame
                {
                    NonVisualGraphicFrameProperties = new P.NonVisualGraphicFrameProperties(
                        new P.NonVisualDrawingProperties { Id = 20U, Name = "SmartArt" },
                        new P.NonVisualGraphicFrameDrawingProperties(),
                        new P.ApplicationNonVisualDrawingProperties()),
                    Transform = new P.Transform(
                        new Drawing.Offset { X = 914400L, Y = 2700000L },
                        new Drawing.Extents { Cx = 3000000L, Cy = 1000000L })
                };
                var diagramRelationshipId = slidePart.GetIdOfPart(diagramPart);
                diagramFrame.AppendChild(new Drawing.Graphic(
                    new Drawing.GraphicData(new Dgm.RelationshipIds { DataPart = diagramRelationshipId })
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/diagram"
                    }));
                slidePart.Slide.CommonSlideData.ShapeTree.AppendChild(diagramFrame);
                slidePart.Slide.Save();
            }

            var presentationMarkdown = new DocumentMarkdownService().Convert(presentationPath).ToMarkdown();
            var normalizedPresentationMarkdown = presentationMarkdown.Replace("\r\n", "\n");
            Check("PowerPoint slide title extracted", presentationMarkdown.Contains("Overview"));
            Check("PowerPoint body extracted", presentationMarkdown.Contains("Revenue increased."));
            Check("PowerPoint speaker notes extracted", presentationMarkdown.Contains("### Speaker notes")
                && presentationMarkdown.Contains("Presenter detail.") && presentationMarkdown.Contains("Call out growth."));
            Check("PowerPoint text shapes follow visual top-to-bottom order",
                presentationMarkdown.IndexOf("Upper item", StringComparison.Ordinal)
                < presentationMarkdown.IndexOf("Lower item", StringComparison.Ordinal));
            Check("PowerPoint text, chart, table, and SmartArt follow mixed visual order",
                presentationMarkdown.IndexOf("Upper item", StringComparison.Ordinal)
                < presentationMarkdown.IndexOf("Chart 1: Slide chart", StringComparison.Ordinal)
                && presentationMarkdown.IndexOf("Chart 1: Slide chart", StringComparison.Ordinal)
                    < presentationMarkdown.IndexOf("Mixed table", StringComparison.Ordinal)
                && presentationMarkdown.IndexOf("Mixed table", StringComparison.Ordinal)
                    < presentationMarkdown.IndexOf("- Strategy", StringComparison.Ordinal)
                && presentationMarkdown.IndexOf("- Strategy", StringComparison.Ordinal)
                    < presentationMarkdown.IndexOf("![Image: Growth chart image]()", StringComparison.Ordinal)
                && presentationMarkdown.IndexOf("![Image: Growth chart image]()", StringComparison.Ordinal)
                    < presentationMarkdown.IndexOf("Lower item", StringComparison.Ordinal));
            Check("PowerPoint image placeholder preserves alternative text",
                presentationMarkdown.Contains("![Image: Growth chart image]()"));
            Check("PowerPoint chart title, series, and cached points extracted",
                presentationMarkdown.Contains("Chart 1: Slide chart (column)")
                && presentationMarkdown.Contains("Series 1: Revenue")
                && presentationMarkdown.Contains("| B | 7 |"));
            Check("PowerPoint SmartArt hierarchy and connection order extracted",
                normalizedPresentationMarkdown.Contains("- Strategy\n  - Growth\n  - Efficiency"));
        }

        private static void TestLegacyOfficeRouting()
        {
            Console.WriteLine("[16] Legacy Office format routing");
            var normalizer = new LegacyOfficeFormatNormalizer();
            Check("DOC maps to DOCX", normalizer.CanNormalize(".doc") && normalizer.GetTargetExtension(".doc") == ".docx");
            Check("XLS maps to XLSX", normalizer.CanNormalize(".xls") && normalizer.GetTargetExtension(".xls") == ".xlsx");
            Check("PPT maps to PPTX", normalizer.CanNormalize(".ppt") && normalizer.GetTargetExtension(".ppt") == ".pptx");
        }

        private static void TestDocumentMarkdownCache()
        {
            Console.WriteLine("[17] Document Markdown cache");
            var path = Path.Combine(_dir, "cache.cachetest");
            File.WriteAllText(path, "first");

            var converter = new CountingMarkdownConverter();
            var service = new DocumentMarkdownService(new IDocumentMarkdownConverter[] { converter }, new MarkdownConversionOptions());
            var first = service.Convert(path);
            first.Sections[0].Markdown = "caller mutation";
            var second = service.Convert(path);
            Check("reuses cached conversion and isolates results", converter.ConvertCount == 1 && second.Sections[0].Markdown == "first");

            File.WriteAllText(path, "second");
            var third = service.Convert(path);
            Check("source content invalidates cache", converter.ConvertCount == 2 && third.Sections[0].Markdown == "second");

            var changedOptions = new MarkdownConversionOptions { MaxSheetRows = 201 };
            var optionsService = new DocumentMarkdownService(new IDocumentMarkdownConverter[] { converter }, changedOptions);
            optionsService.Convert(path);
            Check("conversion options are part of cache key", converter.ConvertCount == 3);
        }

        private static void TestOpenXmlPackageExpansionLimit()
        {
            Console.WriteLine("[18] OpenXML package expansion limit");
            var path = Path.Combine(_dir, "expanded.xlsx");
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var payload = new byte[24 * 1024];
                for (var index = 0; index < 2; index++)
                {
                    using (var entry = archive.CreateEntry("part-" + index + ".xml", CompressionLevel.Optimal).Open())
                    {
                        entry.Write(payload, 0, payload.Length);
                    }
                }
            }

            var rejected = false;
            try
            {
                new SpreadsheetMarkdownConverter().Convert(path, new MarkdownConversionOptions
                {
                    MaxUncompressedPackageBytes = 32 * 1024
                }, CancellationToken.None);
            }
            catch (InvalidDataException ex)
            {
                rejected = ex.Message.Contains("uncompressed package-size limit");
            }

            Check("rejects aggregate package expansion before OpenXML parsing", rejected);
        }

        private static void TestWordContentControlsAndLocalizedHeadings()
        {
            Console.WriteLine("[18a] Word content controls and localized heading styles");
            var path = Path.Combine(_dir, "markdown-sdt.docx");
            using (var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = document.AddMainDocumentPart();
                var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
                stylesPart.Styles = new W.Styles(
                    new W.Style(new W.StyleName { Val = "heading 1" }) { Type = W.StyleValues.Paragraph, StyleId = "1" },
                    new W.Style(new W.StyleName { Val = "Custom Outline" },
                        new W.StyleParagraphProperties(new W.OutlineLevel { Val = 1 })) { Type = W.StyleValues.Paragraph, StyleId = "a5" });
                stylesPart.Styles.Save();
                mainPart.Document = new W.Document(new W.Body(
                    new W.Paragraph(
                        new W.ParagraphProperties(new W.ParagraphStyleId { Val = "1" }),
                        new W.Run(new W.Text("Localized heading"))),
                    new W.SdtBlock(new W.SdtContentBlock(
                        new W.Paragraph(new W.Run(new W.Text("Inside content control"))),
                        new W.Paragraph(
                            new W.ParagraphProperties(new W.ParagraphStyleId { Val = "a5" }),
                            new W.Run(new W.Text("Outline heading"))))),
                    new W.Paragraph(
                        new W.ParagraphProperties(new W.OutlineLevel { Val = 2 }),
                        new W.Run(new W.Text("Direct outline")))));
                mainPart.Document.Save();
            }

            var markdown = new WordMarkdownConverter().Convert(path, new MarkdownConversionOptions());
            var text = markdown.ToMarkdown();
            Check("content control paragraphs are not dropped", text.Contains("Inside content control"));
            Check("localized heading style resolves through style name", text.Contains("# Localized heading"));
            Check("style outline level becomes a heading", text.Contains("## Outline heading"));
            Check("direct paragraph outline level becomes a heading", text.Contains("### Direct outline"));
            Check("localized headings split sections", markdown.Sections.Count == 2 && markdown.Sections[1].Title == "Outline heading");
        }

        private static void TestWordAlternateContentAndTableContentControls()
        {
            Console.WriteLine("[18c] Word mc:Fallback de-duplication and row/cell content controls");
            var path = Path.Combine(_dir, "markdown-altcontent.docx");
            using (var document = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
            {
                var mainPart = document.AddMainDocumentPart();
                mainPart.Document = new W.Document(new W.Body(
                    new W.Paragraph(
                        new W.Run(new W.Text("Anchor ")),
                        new DocumentFormat.OpenXml.AlternateContent(
                            new DocumentFormat.OpenXml.AlternateContentChoice(new W.Run(new W.Text("Textbox once"))) { Requires = "wps" },
                            new DocumentFormat.OpenXml.AlternateContentFallback(new W.Run(new W.Text("Textbox once"))))),
                    new W.Table(
                        new W.TableGrid(new W.GridColumn(), new W.GridColumn()),
                        new W.TableRow(
                            new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Plain cell A")))),
                            new W.SdtCell(new W.SdtContentCell(
                                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Sdt cell B"))))))),
                        new W.SdtRow(new W.SdtContentRow(
                            new W.TableRow(
                                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Sdt row C")))),
                                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Sdt row D"))))))))));
                mainPart.Document.AddNamespaceDeclaration("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
                mainPart.Document.AddNamespaceDeclaration("wps", "http://schemas.microsoft.com/office/word/2010/wordprocessingShape");
                mainPart.Document.Save();
            }

            var text = new WordMarkdownConverter().Convert(path, new MarkdownConversionOptions()).ToMarkdown();
            var occurrences = text.Split(new[] { "Textbox once" }, StringSplitOptions.None).Length - 1;
            Check("mc:Choice text is emitted once (Fallback skipped)", occurrences == 1);
            Check("cell-level content control text is kept", text.Contains("Sdt cell B"));
            Check("row-level content control rows are kept", text.Contains("Sdt row C") && text.Contains("Sdt row D"));
        }

        private static void TestWorkbookTruncationKeepsSheetSections()
        {
            Console.WriteLine("[18b] Workbook truncation keeps sheet sections");
            var path = Path.Combine(_dir, "markdown-truncated.xlsx");
            using (var document = SpreadsheetDocument.Create(path, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook(new Sheets());
                var sheets = workbookPart.Workbook.Sheets;
                for (var sheetIndex = 1; sheetIndex <= 3; sheetIndex++)
                {
                    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                    var sheetData = new SheetData();
                    for (var rowIndex = 1; rowIndex <= 20; rowIndex++)
                    {
                        sheetData.Append(new Row(new Cell
                        {
                            CellReference = "A" + rowIndex,
                            DataType = CellValues.InlineString,
                            InlineString = new InlineString(new Text("Sheet" + sheetIndex + " value " + rowIndex + " padding text"))
                        }) { RowIndex = (uint)rowIndex });
                    }
                    worksheetPart.Worksheet = new Worksheet(sheetData);
                    sheets.Append(new Sheet
                    {
                        Id = workbookPart.GetIdOfPart(worksheetPart),
                        SheetId = (uint)sheetIndex,
                        Name = "Data" + sheetIndex
                    });
                }
                workbookPart.Workbook.Save();
            }

            var result = new SpreadsheetMarkdownConverter().Convert(path, new MarkdownConversionOptions { MaxTotalChars = 900 });
            Check("truncated workbook is flagged", result.Truncated);
            Check("truncated workbook keeps every sheet section index",
                result.Sections.Count == 3
                && result.Sections.Select(section => section.Index).SequenceEqual(new[] { 1, 2, 3 })
                && result.Sections[2].Title == "Data3");
            Check("omitted sheets point to range fetch",
                result.Sections[2].Markdown.Contains("request an A1 range with sectionIndex 3")
                && !result.Sections[2].Markdown.Contains("Sheet3 value"));
        }

        private static void TestPowerPointShapes()
        {
            Console.WriteLine("[15] PowerPoint textbox/shape");
            var path = Path.Combine(_dir, "shapes.pptx");
            PowerPointDocumentEngine.CreateBlank(path);

            using (var engine = new PowerPointDocumentEngine(path, true))
            {
                var id = engine.AddTextBox(1, "Hello box");
                Check("textbox id assigned", id >= 2);
                engine.SetShapeText(1, id.ToString(), "Updated box");
                engine.Save();
            }

            using (var engine = new PowerPointDocumentEngine(path, false))
            {
                var shapes = engine.ReadShapes(1);
                Check("one textbox", shapes.Count == 1);
                Check("textbox type", shapes.Count > 0 && shapes[0].Type == "textbox");
                Check("textbox text", shapes.Count > 0 && shapes[0].Text == "Updated box");
                Check("validate clean", engine.Validate().Count == 0);
            }
        }

        private static void TestPowerPointTable()
        {
            Console.WriteLine("[16] PowerPoint table add/edit");
            var path = Path.Combine(_dir, "ptable.pptx");
            PowerPointDocumentEngine.CreateBlank(path);

            using (var engine = new PowerPointDocumentEngine(path, true))
            {
                var idx = engine.AddTable(1, 2, 2, new List<IReadOnlyList<string>>
                {
                    new List<string> { "A", "B" },
                    new List<string> { "C", "D" }
                });
                Check("table index", idx == 1);
                engine.SetTableCellText(1, idx, 1, 1, "Z");
                engine.Save();
            }

            using (var engine = new PowerPointDocumentEngine(path, false))
            {
                Check("validate clean", engine.Validate().Count == 0);
                var xml = engine.ReadPartXml("/ppt/slides/slide1.xml");
                Check("table cell text present", xml != null && xml.IndexOf("Z", StringComparison.Ordinal) >= 0);
            }
        }

        private static void TestExcelAdvanced()
        {
            Console.WriteLine("[17] Excel advanced (rows/named/validation/databar)");
            var path = Path.Combine(_dir, "advanced.xlsx");
            ExcelDocumentEngine.CreateBlank(path);

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                engine.WriteCell("Sheet1", 1, 1, "10");
                engine.WriteCell("Sheet1", 2, 1, "20");
                engine.Save();
            }

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                var shifted = engine.InsertRows("Sheet1", 2, 1);
                Check("rows shifted", shifted == 1);
                engine.AddNamedRange("MyRange", "Sheet1", "A1:A3");
                engine.AddDataValidation("Sheet1", "B1:B10", "list", "\"Yes,No\"", null);
                engine.AddDataBarConditionalFormatting("Sheet1", "A1:A10", "638EC6");
                engine.Save();
            }

            using (var engine = new ExcelDocumentEngine(path, false))
            {
                Check("row shifted value", engine.ReadCell("Sheet1", 3, 1) == "20");
                Check("validate clean", engine.Validate().Count == 0);
                var workbookXml = engine.ReadPartXml("/xl/workbook.xml");
                Check("named range present", workbookXml != null && workbookXml.IndexOf("MyRange", StringComparison.Ordinal) >= 0);
                var sheetXml = engine.ReadPartXml("/xl/worksheets/sheet1.xml");
                Check("data validation present", sheetXml != null && sheetXml.IndexOf("dataValidation", StringComparison.Ordinal) >= 0);
                Check("data bar present", sheetXml != null && sheetXml.IndexOf("dataBar", StringComparison.Ordinal) >= 0);
            }
        }

        private static void TestPowerPointMedia()
        {
            Console.WriteLine("[18] PowerPoint picture/notes/background");
            var path = Path.Combine(_dir, "media.pptx");
            PowerPointDocumentEngine.CreateBlank(path);

            var png = Path.Combine(_dir, "dot.png");
            File.WriteAllBytes(png, OnePixelPng);

            using (var engine = new PowerPointDocumentEngine(path, true))
            {
                var pid = engine.AddPicture(1, png);
                Check("picture id assigned", pid >= 2);
                engine.AddNotes(1, "Speaker notes here");
                engine.SetBackground(1, "FFCC00");
                engine.Save();
            }

            using (var engine = new PowerPointDocumentEngine(path, false))
            {
                Check("image part present", engine.GetPartUris().Any(u => u.IndexOf("media", StringComparison.OrdinalIgnoreCase) >= 0));
                Check("notes part present", engine.GetPartUris().Any(u => u.IndexOf("notesSlide", StringComparison.OrdinalIgnoreCase) >= 0));
                var slideXml = engine.ReadPartXml("/ppt/slides/slide1.xml");
                Check("background present", slideXml != null && slideXml.IndexOf("FFCC00", StringComparison.Ordinal) >= 0);
                Check("validate clean", engine.Validate().Count == 0);
            }
        }

        private static void TestExcelChart()
        {
            Console.WriteLine("[19] Excel chart add/read");
            var path = Path.Combine(_dir, "chart.xlsx");
            ExcelDocumentEngine.CreateBlank(path);

            using (var engine = new ExcelDocumentEngine(path, true))
            {
                var idx = engine.AddChart("Sheet1",
                    new List<string> { "Q1", "Q2", "Q3" },
                    new List<double> { 10, 20, 15 },
                    "Revenue", "Quarterly");
                Check("chart index", idx == 1);
                engine.Save();
            }

            using (var engine = new ExcelDocumentEngine(path, false))
            {
                Check("validate clean", engine.Validate().Count == 0);
                var charts = engine.ReadCharts("Sheet1");
                Check("one chart", charts.Count == 1);
                Check("chart type", charts.Count > 0 && charts[0].Type == "column");
                Check("chart title", charts.Count > 0 && charts[0].Title == "Quarterly");
                Check("chart series", charts.Count > 0 && charts[0].SeriesCount == 1);
                Check("chart part present", engine.GetPartUris().Any(u => u.IndexOf("chart", StringComparison.OrdinalIgnoreCase) >= 0));
            }
        }

        private static void TestPowerPointChart()
        {
            Console.WriteLine("[20] PowerPoint chart");
            var path = Path.Combine(_dir, "pchart.pptx");
            PowerPointDocumentEngine.CreateBlank(path);

            using (var engine = new PowerPointDocumentEngine(path, true))
            {
                var idx = engine.AddChart(1,
                    new List<string> { "A", "B" },
                    new List<double> { 3, 7 },
                    "Series 1", "My Chart");
                Check("chart index", idx == 1);
                engine.Save();
            }

            using (var engine = new PowerPointDocumentEngine(path, false))
            {
                Check("validate clean", engine.Validate().Count == 0);
                Check("chart part present", engine.GetPartUris().Any(u => u.IndexOf("chart", StringComparison.OrdinalIgnoreCase) >= 0));
            }
        }
        private static void TestRegistryRouting()
        {
            Console.WriteLine("[10] Registry routing (cross-format)");
            DocumentEngineBootstrapper.EnsureRegistered();

            var docx = Path.Combine(_dir, "reg.docx");
            var xlsx = Path.Combine(_dir, "reg.xlsx");
            var pptx = Path.Combine(_dir, "reg.pptx");

            DocumentEngineRegistry.Create(docx);
            DocumentEngineRegistry.Create(xlsx);
            DocumentEngineRegistry.Create(pptx);
            Check("registry created all three", File.Exists(docx) && File.Exists(xlsx) && File.Exists(pptx));

            using (var d = DocumentEngineRegistry.Open(docx, false))
            {
                Check("docx routes to Word engine", d.Format == "docx");
            }

            using (var x = DocumentEngineRegistry.Open(xlsx, false))
            {
                Check("xlsx routes to Excel engine", x.Format == "xlsx");
            }

            using (var p = DocumentEngineRegistry.Open(pptx, false))
            {
                Check("pptx routes to PowerPoint engine", p.Format == "pptx");
            }

            // Merge through the registry dispatches to the Word engine for .docx.
            var template = Path.Combine(_dir, "reg-template.docx");
            WordDocumentEngine.CreateBlank(template);
            using (var engine = new WordDocumentEngine(template, true))
            {
                var xml = engine.ReadPartXml("/word/document.xml");
                xml = xml.Replace("<w:t />", "<w:t>Hi {{who}}</w:t>");
                engine.WritePartXml("/word/document.xml", xml);
                engine.Save();
            }

            var output = Path.Combine(_dir, "reg-merged.docx");
            var result = DocumentEngineRegistry.Merge(template, output, MergeData.FromJson("{\"who\":\"Registry\"}"));
            Check("registry merge replaced > 0", result.ReplacedCount > 0);
        }

        private sealed class CountingMarkdownConverter : IDocumentMarkdownConverter
        {
            public int ConvertCount { get; private set; }

            public bool CanConvert(string extension)
            {
                return string.Equals(extension, ".cachetest", StringComparison.OrdinalIgnoreCase);
            }

            public MarkdownDocument Convert(string path, MarkdownConversionOptions options)
            {
                ConvertCount++;
                var document = new MarkdownDocument { SourceFormat = "cachetest" };
                document.Sections.Add(new MarkdownSection { Index = 1, Markdown = File.ReadAllText(path) });
                return document;
            }
        }
    }
}
