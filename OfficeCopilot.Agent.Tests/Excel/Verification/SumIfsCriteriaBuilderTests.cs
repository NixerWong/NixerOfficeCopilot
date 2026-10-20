using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Verification.Backends.Com;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class SumIfsCriteriaBuilderTests
    {
        [TestMethod]
        public void TryBuildEscapesExcelWildcardCharactersInText()
        {
            string criteria;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "eq", Value = "A*?~B" },
                ".",
                out criteria));

            Assert.AreEqual("=A~*~?~~B", criteria);
        }

        [TestMethod]
        public void TryBuildSupportsNonNumericTextEqualityWithoutCoercingNumbers()
        {
            string criteria;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "textEquals", Value = "A*?~B" },
                ".",
                out criteria));
            Assert.AreEqual("=A~*~?~~B", criteria);

            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "textEquals", Value = "2025" },
                ".",
                out criteria));
            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "textEquals", Value = string.Empty },
                ".",
                out criteria));
        }

        [TestMethod]
        public void TryBuildSupportsPrefixCriteriaAndEscapesWildcards()
        {
            string criteria;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "startsWith", Value = "A*?~B" },
                ".",
                out criteria));

            Assert.AreEqual("=A~*~?~~B*", criteria);
            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "startsWith", Value = string.Empty },
                ".",
                out criteria));
        }

        [TestMethod]
        public void TryBuildFormatsNumericCriteriaUsingExcelDecimalSeparator()
        {
            string criteria;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "ge", Value = "1.25" },
                ",",
                out criteria));

            Assert.AreEqual(">=1,25", criteria);
        }

        [TestMethod]
        public void TryBuildSupportsBlankOperatorsAndRejectsUnsupportedOperators()
        {
            string criteria;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "blank" },
                ".",
                out criteria));
            Assert.AreEqual(string.Empty, criteria);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "notBlank" },
                ".",
                out criteria));
            Assert.AreEqual("<>", criteria);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "isEmpty" },
                ".",
                out criteria));
            Assert.AreEqual(string.Empty, criteria);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "notEmpty" },
                ".",
                out criteria));
            Assert.AreEqual("<>", criteria);

            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "contains", Value = "x" },
                ".",
                out criteria));
        }

        [TestMethod]
        public void FormulaComparisonSupportsEmptyFilterAliases()
        {
            string expression;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "isEmpty" },
                "$A$2:$A$5",
                ".",
                out expression));
            Assert.AreEqual("IFERROR($A$2:$A$5=\"\",FALSE)", expression);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "notEmpty" },
                "$A$2:$A$5",
                ".",
                out expression));
            Assert.AreEqual(
                "IF(ISERROR($A$2:$A$5),TRUE,IFERROR($A$2:$A$5<>\"\",TRUE))",
                expression);
        }

        [TestMethod]
        public void TryBuildRejectsCriteriaLongerThanExcelLimit()
        {
            string criteria;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "eq", Value = new string('x', 254) },
                ".",
                out criteria));
            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter { Operator = "eq", Value = new string('x', 255) },
                ".",
                out criteria));
        }

        [TestMethod]
        public void TryBuildFormulaComparisonQuotesTextAndEscapesQuotes()
        {
            string expression;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "eq", Value = "D\"Angelo" },
                "$B$2:$B$5",
                ".",
                out expression));

            Assert.AreEqual(
                "IFERROR($B$2:$B$5=\"D\"\"Angelo\",FALSE)",
                expression);
        }

        [TestMethod]
        public void FormulaComparisonSupportsTextEqualityWithoutCoercingNonTextValues()
        {
            string expression;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "textEquals", Value = "女" },
                "$B$2:$B$5",
                ".",
                out expression));
            Assert.AreEqual(
                "IFERROR(ISTEXT($B$2:$B$5)*($B$2:$B$5=\"女\"),FALSE)",
                expression);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "textEquals", Value = "女" },
                "$B$2:$B$5",
                ".",
                ";",
                out expression));
            Assert.AreEqual(
                "IFERROR(ISTEXT($B$2:$B$5)*($B$2:$B$5=\"女\");FALSE)",
                expression);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "textEquals", Value = string.Empty },
                "$B$2:$B$5",
                ".",
                out expression));
            Assert.AreEqual("IFERROR($B$2:$B$5=\"\",FALSE)", expression);

            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter
                {
                    Operator = "textEquals",
                    Value = new string('x', SumIfsCriteriaBuilder.MaximumCriteriaLength + 1)
                },
                "$B$2:$B$5",
                ".",
                out expression));
        }

        [TestMethod]
        public void FormulaComparisonCompilesTextMembershipAndNumericComparisons()
        {
            string expression;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "textContains", Value = "A*?~B" },
                "$A$2:$A$5",
                ".",
                out expression));
            Assert.AreEqual(
                "IFERROR(ISTEXT($A$2:$A$5)*ISNUMBER(SEARCH(\"A~*~?~~B\",$A$2:$A$5)),FALSE)",
                expression);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "textStartsWith", Value = "女" },
                "$A$2:$A$5",
                ".",
                out expression));
            Assert.AreEqual(
                "IFERROR(ISTEXT($A$2:$A$5)*(LEFT($A$2:$A$5,LEN(\"女\"))=\"女\"),FALSE)",
                expression);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "textIn", Value = "女,男" },
                "$A$2:$A$5",
                ".",
                out expression));
            Assert.AreEqual(
                "IFERROR(((ISTEXT($A$2:$A$5)*($A$2:$A$5=\"女\"))"
                    + "+(ISTEXT($A$2:$A$5)*($A$2:$A$5=\"男\")))>0,FALSE)",
                expression);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "numberIn", Value = "1,2" },
                "$A$2:$A$5",
                ".",
                out expression));
            Assert.AreEqual(
                "IFERROR((($A$2:$A$5=1)+($A$2:$A$5=2))>0,FALSE)",
                expression);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "ge", Value = "10" },
                "$A$2:$A$5",
                ".",
                out expression));
            Assert.AreEqual("IFERROR($A$2:$A$5>=10,FALSE)", expression);
        }

        [TestMethod]
        public void FormulaComparisonFormatsDateColumnsForGenericTextFilters()
        {
            string expression;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
            new CalcFilter { Operator = "dateTextContains", Value = "-06-" },
            "$D$2:$D$5",
            ".",
            ";",
            out expression));
            StringAssert.Contains(expression, "SEARCH(\"-06-\";");
            StringAssert.Contains(expression, "TEXT(IF($D$2:$D$5<60");
            StringAssert.Contains(expression, "\"yyyy-mm-dd\"");

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
            new CalcFilter { Operator = "dateTextEquals", Value = "1989-06-01" },
            "$D$2:$D$5",
            ".",
            out expression));
            StringAssert.Contains(expression, "IFERROR(IF(ISNUMBER($D$2:$D$5)");
            StringAssert.Contains(expression, "=\"1989-06-01\"");
        }

        [TestMethod]
        public void FormulaComparisonSupportsPrefixMatching()
        {
            string expression;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "startsWith", Value = "韩\"姓" },
                "$B$2:$B$5",
                ".",
                out expression));

            Assert.AreEqual(
                "IFERROR(LEFT($B$2:$B$5,LEN(\"韩\"\"姓\"))=\"韩\"\"姓\",FALSE)",
                expression);
            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "startsWith", Value = string.Empty },
                "$B$2:$B$5",
                ".",
                out expression));
        }

        [TestMethod]
        public void FormulaComparisonSupportsNativeDateMonthFilters()
        {
            string expression;
            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "monthEquals", Value = "6" },
                "$D$2:$D$1001",
                ".",
                ";",
                out expression));
            Assert.AreEqual(
                "IFERROR(ISNUMBER($D$2:$D$1001)*(MONTH($D$2:$D$1001)=6);FALSE)",
                expression);

            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "monthEquals", Value = "0" },
                "$D$2:$D$1001",
                ".",
                ";",
                out expression));
            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "monthEquals", Value = "13" },
                "$D$2:$D$1001",
                ".",
                ";",
                out expression));
        }

        [TestMethod]
        public void FormulaComparisonSupportsNativeDateYearFilters()
        {
            string expression;
            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "yearEquals", Value = "1989" },
                "$D$2:$D$1001",
                ".",
                ";",
                out expression));
            Assert.AreEqual(
                "IFERROR(ISNUMBER($D$2:$D$1001)*(YEAR($D$2:$D$1001)=1989);FALSE)",
                expression);

            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "yearEquals", Value = "0" },
                "$D$2:$D$1001",
                ".",
                ";",
                out expression));
            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "yearEquals", Value = "10000" },
                "$D$2:$D$1001",
                ".",
                ";",
                out expression));
        }

        [TestMethod]
        public void FormulaComparisonSupportsAllRemainingDatePartFilters()
        {
            var range = "$D$2:$D$1001";
            var filters = new[]
            {
                new[]
                {
                    "quarterEquals",
                    "2024-Q2",
                    "IFERROR(ISNUMBER(" + range + ")*((YEAR(" + range + ")=2024)"
                        + "*(INT((MONTH(" + range + ")-1)/3)+1=2));FALSE)"
                },
                new[]
                {
                    "yearMonthEquals",
                    "2024-06",
                    "IFERROR(ISNUMBER(" + range + ")*((YEAR(" + range + ")=2024)"
                        + "*(MONTH(" + range + ")=6));FALSE)"
                },
                new[]
                {
                    "dayEquals",
                    "15",
                    "IFERROR(ISNUMBER(" + range + ")*(DAY(" + range + ")=15);FALSE)"
                },
                new[]
                {
                    "dayOfWeekEquals",
                    "6",
                    "IFERROR(ISNUMBER(" + range + ")*(WEEKDAY(" + range + ";2)=6);FALSE)"
                }
            };

            foreach (var testCase in filters)
            {
                string expression;
                Assert.IsTrue(SumIfsCriteriaBuilder.IsDatePartFilter(testCase[0]));
                Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                    new CalcFilter { Operator = testCase[0], Value = testCase[1] },
                    range,
                    ".",
                    ";",
                    out expression),
                    testCase[0]);
                Assert.AreEqual(testCase[2], expression, testCase[0]);
            }

            var invalidFilters = new[]
            {
                new CalcFilter { Operator = "quarterEquals", Value = "2024-Q5" },
                new CalcFilter { Operator = "yearMonthEquals", Value = "2024-13" },
                new CalcFilter { Operator = "dayEquals", Value = "0" },
                new CalcFilter { Operator = "dayOfWeekEquals", Value = "8" }
            };
            foreach (var filter in invalidFilters)
            {
                string expression;
                Assert.IsFalse(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                    filter,
                    range,
                    ".",
                    ";",
                    out expression),
                    filter.Operator);
            }
        }

        [TestMethod]
        public void FormulaDateFiltersUseExcelDatesAndTheLocaleListSeparator()
        {
            var filter = new CalcFilter
            {
                Operator = "ge",
                Value = "2025-01-01"
            };
            string comparison;
            string criteria;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                filter,
                "$B$2:$B$5",
                ".",
                ";",
                out comparison));
            Assert.AreEqual(
                "IFERROR($B$2:$B$5>=DATE(2025;1;1);FALSE)",
                comparison);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaCriteria(
                filter,
                ".",
                ";",
                out criteria));
            Assert.AreEqual("\">=\"&DATE(2025;1;1)", criteria);
        }

        [TestMethod]
        public void TypeFiltersBuildNativeAndParseableTextNumberExpressions()
        {
            string expression;

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "isNativeNumber" },
                "$A$2:$A$5",
                ".",
                ";",
                out expression));
            Assert.AreEqual("ISNUMBER($A$2:$A$5)", expression);

            Assert.IsTrue(SumIfsCriteriaBuilder.TryBuildFormulaComparison(
                new CalcFilter { Operator = "isTextNumber" },
                "$A$2:$A$5",
                ".",
                ";",
                out expression));
            Assert.AreEqual(
                "IFERROR(ISTEXT($A$2:$A$5)*ISNUMBER(NUMBERVALUE($A$2:$A$5;\".\";\",\"));FALSE)",
                expression);

            Assert.IsFalse(SumIfsCriteriaBuilder.TryBuild(
                new CalcFilter
                {
                    Operator = "isTextNumber",
                    Value = "unexpected"
                },
                ".",
                out expression));
        }
    }
}
