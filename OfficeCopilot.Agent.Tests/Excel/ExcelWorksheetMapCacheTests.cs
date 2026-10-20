using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelWorksheetMapCacheTests
    {
        [TestMethod]
        public void CacheReturnsIndependentCopiesAndInvalidatesWorkbook()
        {
            var cache = new ExcelWorksheetMapCache();
            var original = CreateMap("Data");
            original.WorksheetGroups.Add(new ExcelWorksheetHeaderGroup
            {
                GroupId = "header-group-1",
                MinimumHeaderSimilarity = 1m,
                WorksheetNames = new System.Collections.Generic.List<string> { "Data" }
            });
            original.SharedKeyCandidates.Add(new ExcelSharedKeyCandidate
            {
                ColumnName = "id",
                WorksheetNames = new System.Collections.Generic.List<string> { "Data" }
            });
            cache.Store(@"C:\Books\Sales.xlsx", original);
            original.Worksheets[0].Name = "Changed after cache";
            original.Worksheets[0].HeaderColumns[0] = "changed";

            ExcelWorksheetListResult cached;
            Assert.IsTrue(cache.TryGet(@"c:\books\sales.xlsx", out cached));
            Assert.AreEqual("Data", cached.Worksheets[0].Name);
            Assert.AreEqual("DataCode", cached.Worksheets[0].CodeName);
            Assert.AreEqual("id", cached.Worksheets[0].HeaderColumns[0]);
            cached.Worksheets[0].Name = "Changed by caller";
            cached.WorksheetGroups[0].WorksheetNames[0] = "Changed by caller";
            cached.SharedKeyCandidates[0].WorksheetNames[0] = "Changed by caller";

            Assert.IsTrue(cache.TryGet(@"C:\Books\Sales.xlsx", out cached));
            Assert.AreEqual("Data", cached.Worksheets[0].Name);
            Assert.AreEqual("DataCode", cached.Worksheets[0].CodeName);
            Assert.AreEqual("Data", cached.WorksheetGroups[0].WorksheetNames[0]);
            Assert.AreEqual("Data", cached.SharedKeyCandidates[0].WorksheetNames[0]);

            cache.Invalidate(@"C:\Books\Sales.xlsx");

            Assert.IsFalse(cache.TryGet(@"C:\Books\Sales.xlsx", out cached));
        }

        [TestMethod]
        public void InspectionFailureIsNotCached()
        {
            var cache = new ExcelWorksheetMapCache();
            var map = CreateMap("Data");
            map.Worksheets[0].InspectionError = "Inspection failed";

            cache.Store(@"C:\Books\Sales.xlsx", map);

            ExcelWorksheetListResult cached;
            Assert.IsFalse(cache.TryGet(@"C:\Books\Sales.xlsx", out cached));
        }

        private static ExcelWorksheetListResult CreateMap(string worksheetName)
        {
            var result = new ExcelWorksheetListResult
            {
                WorkbookName = "Sales.xlsx",
                TotalWorksheetCount = 1
            };
            result.Worksheets.Add(new ExcelWorksheetInfo
            {
                Name = worksheetName,
                CodeName = worksheetName + "Code",
                Index = 1,
                IsVisible = true,
                UsedRangeAddress = "A1:B10",
                UsedRangeRows = 10,
                UsedRangeColumns = 2,
                HeaderSignature = "id|amount",
                HeaderColumns = new System.Collections.Generic.List<string> { "id", "amount" },
                RoleGuess = "data"
            });
            return result;
        }
    }
}
