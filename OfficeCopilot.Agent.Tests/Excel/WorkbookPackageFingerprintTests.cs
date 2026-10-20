using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Eval.Excel;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class WorkbookPackageFingerprintTests
    {
        [TestMethod]
        public void IgnoresRevisionMetadataButDetectsWorksheetValueChanges()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "WorkbookPackageFingerprintTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var baselinePath = Path.Combine(directory, "baseline.xlsx");
            var sameDataPath = Path.Combine(directory, "same-data.xlsx");
            var changedDataPath = Path.Combine(directory, "changed-data.xlsx");

            try
            {
                WriteWorkbookPackage(baselinePath, "doc-a", "100", "2026-10-04T00:00:00Z");
                WriteWorkbookPackage(sameDataPath, "doc-b", "100", "2026-10-05T00:00:00Z");
                WriteWorkbookPackage(changedDataPath, "doc-c", "101", "2026-10-05T00:00:00Z");

                Assert.AreEqual(
                    WorkbookPackageFingerprint.Compute(baselinePath),
                    WorkbookPackageFingerprint.Compute(sameDataPath));
                Assert.AreNotEqual(
                    WorkbookPackageFingerprint.Compute(baselinePath),
                    WorkbookPackageFingerprint.Compute(changedDataPath));
            }
            finally
            {
                DeleteIfExists(baselinePath);
                DeleteIfExists(sameDataPath);
                DeleteIfExists(changedDataPath);
                Directory.Delete(directory);
            }
        }

        [TestMethod]
        public void IgnoresExcelDefaultWorkbookCodeNameButDetectsCustomCodeName()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "WorkbookPackageFingerprintTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var baselinePath = Path.Combine(directory, "baseline.xlsx");
            var defaultCodeNamePath = Path.Combine(directory, "default-code-name.xlsx");
            var customCodeNamePath = Path.Combine(directory, "custom-code-name.xlsx");

            try
            {
                WriteWorkbookPackage(baselinePath, "doc-a", "100", "2026-10-04T00:00:00Z");
                WriteWorkbookPackage(
                    defaultCodeNamePath,
                    "doc-b",
                    "100",
                    "2026-10-05T00:00:00Z",
                    "ThisWorkbook");
                WriteWorkbookPackage(
                    customCodeNamePath,
                    "doc-c",
                    "100",
                    "2026-10-05T00:00:00Z",
                    "CustomWorkbook");

                Assert.AreEqual(
                    WorkbookPackageFingerprint.Compute(baselinePath),
                    WorkbookPackageFingerprint.Compute(defaultCodeNamePath));
                Assert.AreNotEqual(
                    WorkbookPackageFingerprint.Compute(baselinePath),
                    WorkbookPackageFingerprint.Compute(customCodeNamePath));
            }
            finally
            {
                DeleteIfExists(baselinePath);
                DeleteIfExists(defaultCodeNamePath);
                DeleteIfExists(customCodeNamePath);
                Directory.Delete(directory);
            }
        }

        private static void WriteWorkbookPackage(
            string path,
            string documentId,
            string cellValue,
            string modifiedAt,
            string workbookCodeName = null)
        {
            using (var stream = File.Create(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(
                    archive,
                    "xl/workbook.xml",
                    "<workbook xmlns=\"urn:workbook\" xmlns:xr=\"urn:revision\">"
                        + "<workbookPr defaultThemeVersion=\"166925\""
                        + (workbookCodeName == null ? string.Empty : " codeName=\"" + workbookCodeName + "\"")
                        + "/>"
                        + "<xr:revisionPtr documentId=\"" + documentId + "\"/>"
                        + "<sheets><sheet name=\"Sales\"/></sheets>"
                        + "<calcPr calcId=\"191029\"/></workbook>");
                WriteEntry(
                    archive,
                    "xl/worksheets/sheet1.xml",
                    "<worksheet xmlns=\"urn:worksheet\"><sheetData>"
                        + "<row r=\"1\"><c r=\"A1\"><v>" + cellValue + "</v></c></row>"
                        + "</sheetData></worksheet>");
                WriteEntry(
                    archive,
                    "docProps/core.xml",
                    "<coreProperties xmlns=\"urn:core\"><modified>"
                        + modifiedAt + "</modified></coreProperties>");
            }
        }

        private static void WriteEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
