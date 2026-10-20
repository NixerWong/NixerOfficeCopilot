using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Eval.Excel;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class WorkbookChangeScopeAuditorTests
    {
        [TestMethod]
        public void CompareAllowsChangesInsideDeclaredRange()
        {
            WithWorkbooks(
                new[] { new Sheet("Data", "<c r=\"A1\"><v>1</v></c><c r=\"A2\"><v>2</v></c>") },
                new[] { new Sheet("Data", "<c r=\"A1\"><v>1</v></c><c r=\"A2\"><v>3</v></c>") },
                result =>
                {
                    Assert.AreEqual(1, result.ChangedCellCount);
                    Assert.AreEqual(0, result.OutOfScopeCellCount);
                    Assert.IsTrue(result.IsWithinScope);
                },
                Scope("Data", "A1:A3"));
        }

        [TestMethod]
        public void CompareFindsChangesOutsideRangeAndUnapprovedWorksheet()
        {
            WithWorkbooks(
                new[] { new Sheet("Data", "<c r=\"A1\"><v>1</v></c>") },
                new[]
                {
                    new Sheet("Data", "<c r=\"A1\"><v>1</v></c><c r=\"B1\" s=\"1\"><v>2</v></c>"),
                    new Sheet("Unexpected", "<c r=\"A1\"><v>9</v></c>")
                },
                result =>
                {
                    Assert.AreEqual(2, result.ChangedCellCount);
                    Assert.AreEqual(2, result.OutOfScopeCellCount);
                    Assert.AreEqual(1, result.ChangedWorksheetCount);
                    Assert.AreEqual(1, result.OutOfScopeWorksheetCount);
                    Assert.IsFalse(result.IsWithinScope);
                },
                Scope("Data", "A1:A3"));
        }

        [TestMethod]
        public void CompareAllowsADeclaredNewWorksheetAsAWholeSheet()
        {
            WithWorkbooks(
                new[] { new Sheet("Data", "<c r=\"A1\"><v>1</v></c>") },
                new[]
                {
                    new Sheet("Data", "<c r=\"A1\"><v>1</v></c>"),
                    new Sheet("Output", "<c r=\"A1\"><v>9</v></c>")
                },
                result =>
                {
                    Assert.AreEqual(1, result.ChangedWorksheetCount);
                    Assert.AreEqual(0, result.OutOfScopeWorksheetCount);
                    Assert.IsTrue(result.IsWithinScope);
                },
                WholeSheet("Output"));
        }

        [TestMethod]
        public void CompareRequiresExplicitPermissionToAddADeclaredWholeSheet()
        {
            WithWorkbooks(
                new[] { new Sheet("Data", "<c r=\"A1\"><v>1</v></c>") },
                new[]
                {
                    new Sheet("Data", "<c r=\"A1\"><v>1</v></c>"),
                    new Sheet("Output", "<c r=\"A1\"><v>9</v></c>")
                },
                result =>
                {
                    Assert.AreEqual(1, result.OutOfScopeWorksheetCount);
                    Assert.IsFalse(result.IsWithinScope);
                },
                new ArrayList
                {
                    new Dictionary<string, object>
                    {
                        ["sheet"] = "Output",
                        ["wholeSheet"] = true
                    }
                });
        }

        [TestMethod]
        public void CompareDoesNotAllowDeletingAWorksheet()
        {
            WithWorkbooks(
                new[] { new Sheet("Data", "<c r=\"A1\"><v>1</v></c>") },
                new Sheet[0],
                result =>
                {
                    Assert.AreEqual(1, result.ChangedWorksheetCount);
                    Assert.AreEqual(1, result.OutOfScopeWorksheetCount);
                    Assert.IsFalse(result.IsWithinScope);
                },
                WholeSheet("Data"));
        }

        [TestMethod]
        public void CompareIgnoresRecalculatedValuesWhenFormulaIsUnchanged()
        {
            WithWorkbooks(
                new[] { new Sheet("Data", "<c r=\"A1\"><f>SUM(B1:B2)</f><v>3</v></c>") },
                new[] { new Sheet("Data", "<c r=\"A1\"><f>SUM(B1:B2)</f><v>4</v></c>") },
                result =>
                {
                    Assert.AreEqual(0, result.ChangedCellCount);
                    Assert.IsTrue(result.IsWithinScope);
                },
                Scope("Other", "A1"));
        }

        [TestMethod]
        public void CompareIgnoresRecalculatedSharedFormulaValues()
        {
            WithWorkbooks(
                new[] { new Sheet("Data", "<c r=\"A2\"><f t=\"shared\" si=\"0\"/><v>3</v></c>") },
                new[] { new Sheet("Data", "<c r=\"A2\"><f t=\"shared\" si=\"0\"/><v>4</v></c>") },
                result =>
                {
                    Assert.AreEqual(0, result.ChangedCellCount);
                    Assert.IsTrue(result.IsWithinScope);
                },
                Scope("Other", "A1"));
        }

        private static void WithWorkbooks(
            IList<Sheet> baselineSheets,
            IList<Sheet> outputSheets,
            Action<WorkbookChangeScopeAuditResult> assertResult,
            IList expectedChangeScope)
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "WorkbookChangeScopeAuditorTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var baselinePath = Path.Combine(directory, "baseline.xlsx");
            var outputPath = Path.Combine(directory, "output.xlsx");
            try
            {
                WriteWorkbook(baselinePath, baselineSheets);
                WriteWorkbook(outputPath, outputSheets);
                assertResult(WorkbookChangeScopeAuditor.Compare(
                    baselinePath,
                    outputPath,
                    expectedChangeScope));
            }
            finally
            {
                if (File.Exists(baselinePath))
                {
                    File.Delete(baselinePath);
                }

                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                Directory.Delete(directory);
            }
        }

        private static IList Scope(string sheet, string range)
        {
            return new ArrayList
            {
                new Dictionary<string, object>
                {
                    ["sheet"] = sheet,
                    ["range"] = range
                }
            };
        }

        private static IList WholeSheet(string sheet)
        {
            return new ArrayList
            {
                new Dictionary<string, object>
                {
                    ["sheet"] = sheet,
                    ["wholeSheet"] = true,
                    ["allowWorksheetAddition"] = true
                }
            };
        }

        private static void WriteWorkbook(string path, IList<Sheet> sheets)
        {
            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var workbook = new StringBuilder();
                workbook.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ");
                workbook.Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
                for (var index = 0; index < sheets.Count; index++)
                {
                    workbook.Append("<sheet name=\"").Append(sheets[index].Name)
                        .Append("\" sheetId=\"").Append(index + 1)
                        .Append("\" r:id=\"rId").Append(index + 1).Append("\"/>");
                }

                workbook.Append("</sheets></workbook>");
                WriteEntry(archive, "xl/workbook.xml", workbook.ToString());

                var relationships = new StringBuilder();
                relationships.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                for (var index = 0; index < sheets.Count; index++)
                {
                    relationships.Append("<Relationship Id=\"rId").Append(index + 1)
                        .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\"")
                        .Append(" Target=\"worksheets/sheet").Append(index + 1).Append(".xml\"/>");
                }

                relationships.Append("</Relationships>");
                WriteEntry(archive, "xl/_rels/workbook.xml.rels", relationships.ToString());
                for (var index = 0; index < sheets.Count; index++)
                {
                    WriteEntry(
                        archive,
                        "xl/worksheets/sheet" + (index + 1) + ".xml",
                        "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                            + "<sheetData><row r=\"1\">" + sheets[index].Cells
                            + "</row></sheetData></worksheet>");
                }
            }
        }

        private static void WriteEntry(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private sealed class Sheet
        {
            public Sheet(string name, string cells)
            {
                Name = name;
                Cells = cells;
            }

            public string Name { get; private set; }

            public string Cells { get; private set; }
        }
    }
}
