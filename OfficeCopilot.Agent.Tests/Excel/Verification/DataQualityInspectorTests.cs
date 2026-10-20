using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Excel.Verification;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class DataQualityInspectorTests
    {
        [TestMethod]
        public void DetectsSubtotalLabelRowsAndCapsRowSamples()
        {
            var snapshot = CreateSnapshot();
            snapshot.SubtotalLabelCount = 12;
            snapshot.SubtotalLabelRows = Enumerable.Range(2, 12).ToList();

            var flag = DataQualityInspector.InspectManaged(snapshot)
                .Single(item => item.Name == "subtotalLabelRows");

            Assert.AreEqual(12, flag.Count);
            CollectionAssert.AreEqual(
                Enumerable.Range(2, 10).ToArray(),
                flag.SampleRows.ToArray());
        }

        [TestMethod]
        public void FlagsTextNumbersOnlyInNumericallyDominantColumns()
        {
            var snapshot = CreateSnapshot();
            snapshot.Columns = new List<DataQualityColumnStatistics>
            {
                new DataQualityColumnStatistics
                {
                    Name = "Amount",
                    NumericCount = 8,
                    NonEmptyCount = 10,
                    TextNumberCount = 3,
                    TextNumberSampleRows = new[] { 4, 7, 9 }
                },
                new DataQualityColumnStatistics
                {
                    Name = "Description",
                    NumericCount = 4,
                    NonEmptyCount = 10,
                    TextNumberCount = 2,
                    TextNumberSampleRows = new[] { 6, 8 }
                }
            };

            var flag = DataQualityInspector.InspectManaged(snapshot)
                .Single(item => item.Name == "textNumbers");

            Assert.AreEqual(3, flag.Count);
            CollectionAssert.AreEqual(new[] { 4, 7, 9 }, flag.SampleRows.ToArray());
        }

        [TestMethod]
        public void DetectsEmptyDuplicateAndDataLikeHeaderRows()
        {
            var snapshot = CreateSnapshot();
            snapshot.HasHeader = true;
            snapshot.RawHeaderValues = new object[] { "Region", "Region", " " };
            snapshot.HeaderRow = 5;
            snapshot.DataRowCount = 3;
            snapshot.FirstDataRowNonEmptyCount = 3;
            snapshot.FirstDataRowTextCount = 2;
            snapshot.LaterDataRowsNonEmptyCount = 6;
            snapshot.LaterDataRowsNumericCount = 4;

            var flag = DataQualityInspector.InspectManaged(snapshot)
                .Single(item => item.Name == "suspiciousHeader");

            Assert.AreEqual(3, flag.Count);
            CollectionAssert.AreEqual(new[] { 5, 6 }, flag.SampleRows.ToArray());
        }

        [TestMethod]
        public void DoesNotFlagDataLikeHeaderWhenLaterRowsAreNotMostlyNumeric()
        {
            var snapshot = CreateSnapshot();
            snapshot.DataRowCount = 3;
            snapshot.FirstDataRowNonEmptyCount = 2;
            snapshot.FirstDataRowTextCount = 2;
            snapshot.LaterDataRowsNonEmptyCount = 6;
            snapshot.LaterDataRowsNumericCount = 2;

            Assert.IsFalse(DataQualityInspector.InspectManaged(snapshot)
                .Any(item => item.Name == "suspiciousHeader"));
        }

        [TestMethod]
        public void DoesNotFlagAnOrdinaryHalfTextHalfNumericFirstRow()
        {
            var snapshot = CreateSnapshot();
            snapshot.DataRowCount = 3;
            snapshot.FirstDataRowNonEmptyCount = 2;
            snapshot.FirstDataRowTextCount = 1;
            snapshot.LaterDataRowsNonEmptyCount = 4;
            snapshot.LaterDataRowsNumericCount = 2;

            Assert.IsFalse(DataQualityInspector.InspectManaged(snapshot)
                .Any(item => item.Name == "suspiciousHeader"));
        }

        [TestMethod]
        public void SubtotalLabelMatchingAvoidsOrdinaryTotalSubstrings()
        {
            Assert.IsTrue(DataQualityInspector.IsSubtotalLabel("本月合计"));
            Assert.IsTrue(DataQualityInspector.IsSubtotalLabel("Grand Total"));
            Assert.IsFalse(DataQualityInspector.IsSubtotalLabel("Total Revenue"));
            Assert.IsFalse(DataQualityInspector.IsSubtotalLabel("subtotal estimate"));
        }

        private static DataQualityProfileSnapshot CreateSnapshot()
        {
            return new DataQualityProfileSnapshot
            {
                RawHeaderValues = new object[0],
                Columns = new List<DataQualityColumnStatistics>(),
                SubtotalLabelRows = new List<int>(),
                HeaderRow = 1,
                DataRowCount = 0
            };
        }
    }
}
