using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Verification.Backends.Com;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class ComCalcBackendTests
    {
        [TestMethod]
        public void CountFunctionsPreserveExcelCountAndCountaSemantics()
        {
            Assert.AreEqual("Count", ComCalcBackend.GetWorksheetFunctionName("count"));
            Assert.AreEqual("CountA", ComCalcBackend.GetWorksheetFunctionName("counta"));
            Assert.AreEqual(
                "--ISNUMBER(A2:A5)",
                ComCalcBackend.BuildCountValueExpression("count", "A2:A5"));
            Assert.AreEqual(
                "--(NOT(ISBLANK(A2:A5)))",
                ComCalcBackend.BuildCountValueExpression("counta", "A2:A5"));
            Assert.AreEqual(
                "SUMPRODUCT(--(NOT(ISBLANK(A2:A5))))",
                ComCalcBackend.BuildCountaFormula("A2:A5"));
        }

        [TestMethod]
        public void SumFormulaIncludesParseableTextNumbersAndIgnoresCellErrors()
        {
            Assert.AreEqual(
                "IFERROR(IF(ISNUMBER(A2:A5),A2:A5,IF(ISTEXT(A2:A5),NUMBERVALUE(A2:A5,\".\",\",\"),0)),0)",
                ComCalcBackend.BuildSumValueExpression("A2:A5", ","));
        }

        [TestMethod]
        public void FilteredSumFormulaDoesNotDoubleWrapErrorSafeTextPredicates()
        {
            var genderFilter =
                "IFERROR(ISTEXT($B$2:$B$5)*($B$2:$B$5=\"女\"),FALSE)";
            var surnameFilter =
                "IFERROR(ISTEXT($A$2:$A$5)*(LEFT($A$2:$A$5,LEN(\"孔\"))=\"孔\"),FALSE)";

            var formula = ComCalcBackend.BuildFilteredSumFormula(
                "$C$2:$C$5",
                new[] { genderFilter, surnameFilter },
                ",");

            Assert.AreEqual(
                "SUMPRODUCT(IFERROR(IF(ISNUMBER($C$2:$C$5),$C$2:$C$5,"
                    + "IF(ISTEXT($C$2:$C$5),NUMBERVALUE($C$2:$C$5,\".\",\",\"),0)),0),"
                    + "--IFERROR(ISTEXT($B$2:$B$5)*($B$2:$B$5=\"女\"),FALSE),"
                    + "--IFERROR(ISTEXT($A$2:$A$5)*(LEFT($A$2:$A$5,LEN(\"孔\"))=\"孔\"),FALSE))",
                formula);
        }

        [TestMethod]
        public void SumIfsFormulaQuotesCriteriaAndEscapesEmbeddedQuotes()
        {
            Assert.AreEqual(
                "SUMIFS($C$2:$C$5,$A$2:$A$5,\"<>\"\"x\"\"\",$B$2:$B$5,\"\")",
                ComCalcBackend.BuildSumIfsFormula(
                    "$C$2:$C$5",
                    new[] { "$A$2:$A$5", "$B$2:$B$5" },
                    new[] { "<>\"x\"", string.Empty },
                    ","));
        }

        [TestMethod]
        public void TextNumberCountFormulaSubtractsNativeNumbersFromAllNumericValues()
        {
            Assert.AreEqual(
                "SUMPRODUCT(--((ISNUMBER(A2:A5)+IFERROR(ISTEXT(A2:A5)"
                    + "*ISNUMBER(NUMBERVALUE(A2:A5,\".\",\",\")),FALSE))>0))-COUNT(A2:A5)",
                ComCalcBackend.BuildTextNumberCountFormula("A2:A5", ","));
        }

        [TestMethod]
        public void AverageCountFormulaCountsNativeAndParseableTextNumbersWithFilterMasks()
        {
            var prefixFilter = "IFERROR(LEFT(B2:B5,LEN(\"韩\"))=\"韩\",FALSE)";

            Assert.AreEqual(
                "SUMPRODUCT(--((ISNUMBER(A2:A5)+IFERROR(ISTEXT(A2:A5)*ISNUMBER(NUMBERVALUE(A2:A5,\".\",\",\")),FALSE))>0),--IFERROR(LEFT(B2:B5,LEN(\"韩\"))=\"韩\",FALSE))",
                ComCalcBackend.BuildFilteredNumericCountFormula(
                    "A2:A5",
                    new[] { prefixFilter },
                    ","));
        }

        [TestMethod]
        public void AverageCountFormulaDoesNotDoubleWrapMultipleErrorSafeFilters()
        {
            var genderFilter =
                "IFERROR(ISTEXT(B2:B5)*(B2:B5=\"女\"),FALSE)";
            var surnameFilter =
                "IFERROR(ISTEXT(A2:A5)*(LEFT(A2:A5,LEN(\"孔\"))=\"孔\"),FALSE)";

            var formula = ComCalcBackend.BuildFilteredNumericCountFormula(
                "C2:C5",
                new[] { genderFilter, surnameFilter },
                ",");

            Assert.AreEqual(
                "SUMPRODUCT(--((ISNUMBER(C2:C5)+IFERROR(ISTEXT(C2:C5)"
                    + "*ISNUMBER(NUMBERVALUE(C2:C5,\".\",\",\")),FALSE))>0),"
                    + "--IFERROR(ISTEXT(B2:B5)*(B2:B5=\"女\"),FALSE),"
                    + "--IFERROR(ISTEXT(A2:A5)*(LEFT(A2:A5,LEN(\"孔\"))=\"孔\"),FALSE))",
                formula);
        }

        [TestMethod]
        public void WorksheetFunctionInvocationRethrowsTheUnderlyingException()
        {
            var exception = Assert.ThrowsException<InvalidOperationException>(
                () => ComCalcBackend.InvokeWorksheetFunction(
                    new ThrowingWorksheetFunctions(),
                    "SumIfs",
                    new object[] { 1 }));

            Assert.AreEqual("Root failure.", exception.Message);
        }

        [TestMethod]
        public void CanEvaluateFilteredCountAQueries()
        {
            var query = Query("counta", "OrderID");
            query.Filters.Add(new CalcFilter
            {
                Column = "Year",
                Operator = "eq",
                Value = "2025"
            });

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
        }

        [TestMethod]
        public void CanEvaluateSupportsEmptyFilterAliasesUsedByAggregateVerification()
        {
            var query = Query("sum", "Revenue");
            query.Filters.Add(new CalcFilter
            {
                Column = "OrderID",
                Operator = "notEmpty",
                Value = string.Empty
            });

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Filters[0].Operator = "isEmpty";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
        }

        [TestMethod]
        public void CanEvaluateSumWithTextEqualityFilterUsingFormulaComparison()
        {
            var query = Query("sum", "Revenue");
            query.Filters.Add(new CalcFilter
            {
                Column = "Gender",
                Operator = "textEquals",
                Value = "女"
            });

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
        }

        [TestMethod]
        public void CanEvaluateSupportsNativeScalarAndFilteredQueries()
        {
            var query = Query("sum", "Amount");
            var cost = Cost(100, 1);

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, cost));

            query.Filters.Add(new CalcFilter
            {
                Column = "Region",
                Operator = "eq",
                Value = "West"
            });
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
        }

        [TestMethod]
        public void CanEvaluateSupportsFilteredAverageAndPrefixQueries()
        {
            var query = Query("average", "Salary");
            query.Filters.Add(new CalcFilter
            {
                Column = "Name",
                Operator = "startsWith",
                Value = "韩"
            });

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Function = "sum";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Function = "average";
            query.Filters[0].Operator = "contains";
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
        }

        [TestMethod]
        public void CanEvaluateSupportsFilteredMinAndMaxFormulaQueries()
        {
            var query = Query("max", "Salary");
            query.Filters.Add(new CalcFilter
            {
                Column = "Name",
                Operator = "textStartsWith",
                Value = "张"
            });

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Function = "min";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
        }

        [TestMethod]
        public void FilteredExtremeFormulaUsesNumericMaskAndRespectsExcelFormulaLength()
        {
            string formula;
            Assert.IsTrue(ComCalcBackend.TryBuildFilteredExtremeFormula(
                "$C$2:$C$5",
                new[] { "IFERROR(LEFT($A$2:$A$5;LEN(\"张\"))=\"张\";FALSE)" },
                true,
                ";",
                out formula));
            Assert.AreEqual(
                "AGGREGATE(14;6;IF((ISNUMBER($C$2:$C$5))"
                    + "*(IFERROR(LEFT($A$2:$A$5;LEN(\"张\"))=\"张\";FALSE));"
                    + "$C$2:$C$5;NA());1)",
                formula);

            Assert.IsFalse(ComCalcBackend.TryBuildFilteredExtremeFormula(
                new string('A', 3000),
                new[] { new string('x', 3000) },
                false,
                ",",
                out formula));
        }

        [TestMethod]
        public void CanEvaluateSupportsNativeDatePartFiltersForSumAverageAndCount()
        {
            var query = Query("average", "Salary");
            query.Filters.Add(new CalcFilter
            {
                Column = "Birthday",
                Operator = "monthEquals",
                Value = "6"
            });

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Function = "sum";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Function = "counta";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Filters[0].Operator = "yearEquals";
            query.Filters[0].Value = "1989";
            query.Function = "average";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Function = "sum";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Function = "counta";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            var additionalDateParts = new[]
            {
                new CalcFilter { Column = "Birthday", Operator = "quarterEquals", Value = "1989-Q1" },
                new CalcFilter { Column = "Birthday", Operator = "yearMonthEquals", Value = "1989-06" },
                new CalcFilter { Column = "Birthday", Operator = "dayEquals", Value = "15" },
                new CalcFilter { Column = "Birthday", Operator = "dayOfWeekEquals", Value = "6" }
            };
            foreach (var filter in additionalDateParts)
            {
                query.Filters[0] = filter;
                query.Function = "average";
                Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
                query.Function = "sum";
                Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
                query.Function = "counta";
                Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
            }
        }

        [TestMethod]
        public void CanEvaluateSumWithNativeOrTextNumberFilters()
        {
            var query = Query("sum", "Amount");
            query.Filters.Add(new CalcFilter
            {
                Column = "Amount",
                Operator = "isNativeNumber"
            });

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));

            query.Filters[0].Operator = "isTextNumber";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));
        }

        [TestMethod]
        public void CanEvaluateRejectsUnsupportedShapesAndInconsistentCostEstimates()
        {
            var query = Query("sum", "Amount");

            query.GroupByColumn = "Region";
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));

            query.GroupByColumn = string.Empty;
            query.Function = "countDistinct";
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));

            query.Function = "sum";
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, new CalcCostEstimate
            {
                RowCount = 100,
                ReadColumnCount = 2,
                ProjectedCellCount = 100
            }));
        }

        [TestMethod]
        public void CanEvaluateRejectsRowExclusionsThatRequireManagedFiltering()
        {
            var query = Query("sum", "Amount");
            query.ExcludeRows.Add(10);
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));

            query.ExcludeRows.Clear();
            query.ExcludeLabelRows = true;
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));
        }

        [TestMethod]
        public void CanEvaluateEnforcesConditionalComparisonBudget()
        {
            Assert.IsTrue(ComCalcBackend.IsConditionalWorkWithinLimit(
                1,
                (int)ComCalcBackend.MaximumConditionalComparisons));
            Assert.IsFalse(ComCalcBackend.IsConditionalWorkWithinLimit(
                1,
                (int)ComCalcBackend.MaximumConditionalComparisons + 1));
        }

        [TestMethod]
        public void CanEvaluateAppliesConservativeEstimatedCellBudget()
        {
            var query = Query("sum", "Amount");

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(
                query,
                Cost(ComCalcBackend.MaximumEstimatedCells, 1)));
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(
                query,
                Cost(ComCalcBackend.MaximumEstimatedCells + 1, 1)));
        }

        [TestMethod]
        public void CanEvaluateSupportsTwoHundredThousandRowsAcrossTwoColumns()
        {
            var query = Query("sum", "Amount");

            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(200000, 2)));
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(250001, 2)));
        }

        [TestMethod]
        public void CanEvaluateRejectsUnsupportedFiltersAndOversizedErrorCountAddresses()
        {
            var query = Query("sum", "Amount");
            query.Filters.Add(new CalcFilter
            {
                Column = "Region",
                Operator = "contains",
                Value = "West"
            });
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query = Query("errorCount", "Amount");
            query.Source.Address = "A1:A100";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));

            query.Source.Address = string.Empty;
            query.Source.TableName = "ErrorData";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));

            query.Source.TableName = string.Empty;
            query.Source.Address = new string('A', 256);
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 1)));
        }

        [TestMethod]
        public void CanEvaluateAppliesCriteriaLengthLimitOnlyToSumIfs()
        {
            var value = new string('x', 300);
            var query = Query("sum", "Amount");
            query.Filters.Add(new CalcFilter
            {
                Column = "Region",
                Operator = "eq",
                Value = value
            });
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));

            query.Function = "count";
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(query, Cost(100, 2)));
        }

        [TestMethod]
        public void CanEvaluateSupportsStructuralRowCountAndUnfilteredNonBlankQueries()
        {
            var rowCountQuery = new CalcQuery
            {
                Source = new ExcelSourceRef { WorksheetName = "Data" },
                Function = "rowCount"
            };
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(rowCountQuery, Cost(100, 0)));
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(rowCountQuery, Cost(100, 1)));

            var nonBlankQuery = Query("nonBlank", "Amount");
            nonBlankQuery.Columns.Add("Tax");
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(nonBlankQuery, Cost(100, 2)));

            nonBlankQuery.Filters.Add(new CalcFilter
            {
                Column = "Region",
                Operator = "eq",
                Value = "West"
            });
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(nonBlankQuery, Cost(100, 3)));

            nonBlankQuery = Query("keySet", "Region");
            Assert.IsFalse(ComCalcBackend.CanEvaluateQuery(nonBlankQuery, Cost(100, 1)));
        }

        private static CalcQuery Query(string function, string column)
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef
                {
                    WorksheetName = "Data",
                    Address = "A1:B100"
                },
                Function = function
            };
            query.Columns.Add(column);
            return query;
        }

        private static CalcCostEstimate Cost(long rowCount, int readColumnCount)
        {
            return new CalcCostEstimate
            {
                RowCount = checked((int)rowCount),
                ReadColumnCount = readColumnCount,
                ProjectedCellCount = rowCount * readColumnCount
            };
        }

        public sealed class ThrowingWorksheetFunctions
        {
            public object SumIfs(object value)
            {
                throw new InvalidOperationException("Root failure.");
            }
        }
    }
}
