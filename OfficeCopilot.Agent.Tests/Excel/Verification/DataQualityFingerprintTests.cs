using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Excel.Verification.Backends.Com;
using System;
using System.Linq;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class DataQualityFingerprintTests
    {
        [TestMethod]
        public void CalculateNonNumericCountExcludesNumericAndErrorCells()
        {
            Assert.AreEqual(3, DataQualityFingerprint.CalculateNonNumericCount(12, 8, 1));
        }

        [TestMethod]
        public void CalculateNonNumericCountRejectsInconsistentNativeCounts()
        {
            Assert.ThrowsException<InvalidOperationException>(
                () => DataQualityFingerprint.CalculateNonNumericCount(7, 8, 0));
            Assert.ThrowsException<InvalidOperationException>(
                () => DataQualityFingerprint.CalculateNonNumericCount(7, 4, 4));
        }

        [TestMethod]
        public void DetectNestedSubtotalIsUnknownWhenInputsAreUnavailableOrFiltered()
        {
            Assert.IsNull(DataQualityFingerprint.DetectNestedSubtotal(null, 10d, false));
            Assert.IsNull(DataQualityFingerprint.DetectNestedSubtotal(12d, 10d, true));
            Assert.IsTrue(DataQualityFingerprint.DetectNestedSubtotal(12d, 10d, false).Value);
            Assert.IsFalse(DataQualityFingerprint.DetectNestedSubtotal(10d, 10d, false).Value);
        }

        [TestMethod]
        public void HasSignificantDifferenceUsesRelativeAndAbsoluteTolerance()
        {
            Assert.IsFalse(DataQualityFingerprint.HasSignificantDifference(100000000d, 100000000.00001d));
            Assert.IsTrue(DataQualityFingerprint.HasSignificantDifference(100d, 99d));
        }

        [TestMethod]
        public void FindSubtotalFormulaRowsReturnsWorksheetRowsAndCapsSamples()
        {
            var formulas = new object[12, 1];
            for (var row = 0; row < formulas.GetLength(0); row++)
            {
                formulas[row, 0] = "=SUM(A1:A2)";
            }

            for (var row = 0; row < formulas.GetLength(0); row++)
            {
                formulas[row, 0] = "=SUBTOTAL(9,A1:A2)";
            }

            formulas[5, 0] = "=SUM(A1:A2)";

            int count;
            var rows = DataQualityFingerprint.FindSubtotalFormulaRows(formulas, 20, out count);

            Assert.AreEqual(11, count);
            CollectionAssert.AreEqual(
                new[] { 20, 21, 22, 23, 24, 26, 27, 28, 29, 30 },
                rows.ToArray());
        }
    }
}
