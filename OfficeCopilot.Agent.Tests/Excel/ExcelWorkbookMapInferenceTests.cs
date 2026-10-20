using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelWorkbookMapInferenceTests
    {
        [TestMethod]
        public void FindsSimilarHeaderGroupsAndSharedColumnCandidates()
        {
            var map = new ExcelWorksheetListResult();
            map.Worksheets.Add(CreateWorksheet(
                "January",
                1,
                "Order ID",
                "Date",
                "Revenue"));
            map.Worksheets.Add(CreateWorksheet(
                "February",
                2,
                "Revenue",
                "Date",
                "Order ID"));
            map.Worksheets.Add(CreateWorksheet(
                "Customers",
                3,
                "Customer ID",
                "Date",
                "City"));

            ExcelWorkbookMapInference.Populate(map);

            Assert.AreEqual(1, map.WorksheetGroups.Count);
            Assert.AreEqual("header-group-1", map.WorksheetGroups[0].GroupId);
            Assert.AreEqual(1m, map.WorksheetGroups[0].MinimumHeaderSimilarity);
            CollectionAssert.AreEqual(
                new[] { "January", "February" },
                map.WorksheetGroups[0].WorksheetNames);

            var dateCandidate = map.SharedKeyCandidates.Find(
                candidate => candidate.ColumnName == "Date");
            Assert.IsNotNull(dateCandidate);
            CollectionAssert.AreEqual(
                new[] { "Customers", "February", "January" },
                dateCandidate.WorksheetNames);
            Assert.IsFalse(dateCandidate.UniquenessVerified);
        }

        [TestMethod]
        public void GroupsRequireAtLeastEightyPercentJaccardSimilarity()
        {
            var map = new ExcelWorksheetListResult();
            map.Worksheets.Add(CreateWorksheet("FourColumns", 1, "A", "B", "C", "D"));
            map.Worksheets.Add(CreateWorksheet("FiveColumns", 2, "A", "B", "C", "D", "E"));
            map.Worksheets.Add(CreateWorksheet("LowSimilarity", 3, "A", "B", "C", "X", "Y"));
            map.Worksheets.Add(CreateWorksheet("InspectionFailed", 4, "A", "B", "C", "D"));
            map.Worksheets[3].InspectionError = "COM read failed";

            ExcelWorkbookMapInference.Populate(map);

            Assert.AreEqual(1, map.WorksheetGroups.Count);
            Assert.AreEqual(0.8m, map.WorksheetGroups[0].MinimumHeaderSimilarity);
            CollectionAssert.AreEqual(
                new[] { "FourColumns", "FiveColumns" },
                map.WorksheetGroups[0].WorksheetNames);
            Assert.IsFalse(map.SharedKeyCandidates.Exists(
                candidate => candidate.WorksheetNames.Contains("InspectionFailed")));
        }

        [TestMethod]
        public void InferredMapFieldsSurviveDataContractJsonRoundTrip()
        {
            var map = new ExcelWorksheetListResult();
            map.Worksheets.Add(CreateWorksheet("FourColumns", 1, "A", "B", "C", "D"));
            map.Worksheets.Add(CreateWorksheet("FiveColumns", 2, "A", "B", "C", "D", "E"));
            ExcelWorkbookMapInference.Populate(map);

            var serializer = new DataContractJsonSerializer(typeof(ExcelWorksheetListResult));
            ExcelWorksheetListResult roundTrip;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, map);
                stream.Position = 0;
                roundTrip = (ExcelWorksheetListResult)serializer.ReadObject(stream);
            }

            Assert.AreEqual("A", roundTrip.Worksheets[0].HeaderColumns[0]);
            Assert.AreEqual(0.8m, roundTrip.WorksheetGroups[0].MinimumHeaderSimilarity);
            Assert.IsFalse(roundTrip.SharedKeyCandidates[0].UniquenessVerified);
        }

        private static ExcelWorksheetInfo CreateWorksheet(
            string name,
            int index,
            params string[] headers)
        {
            return new ExcelWorksheetInfo
            {
                Name = name,
                Index = index,
                HeaderColumns = new List<string>(headers),
                HeaderSignature = string.Join(" | ", headers)
            };
        }
    }
}
