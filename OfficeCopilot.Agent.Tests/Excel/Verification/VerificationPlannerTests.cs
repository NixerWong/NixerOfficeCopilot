using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Verification;
using OfficeCopilot.Host.Excel.Verification.Backends;
using OfficeCopilot.Host.Excel.Verification.Backends.Com;
using OfficeCopilot.Host.Excel.Verification.Backends.Managed;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class VerificationPlannerTests
    {
        [TestMethod]
        public void PlannerBuildsScalarQueriesAndPrefersComForBothSides()
        {
            var plan = Plan();
            plan.Basis.Source = new ExcelSourceRef
            {
                WorksheetName = "Sales",
                TableName = "SalesData"
            };
            plan.Basis.HeaderMode = "no";
            plan.Basis.Filters.Add(new CalcFilter
            {
                Column = "Year",
                Operator = "eq",
                Value = "2025"
            });
            var assertion = new ExcelAssertion
            {
                Id = "total",
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Summary",
                    Column = "Total",
                    Function = "sum"
                },
                Source = new ExcelAssertionSide
                {
                    Column = "Amount",
                    Function = "sum"
                },
                Tolerance = 0.01d
            };

            var result = CreatePlanner().PlanAssertion(
                plan,
                assertion,
                Cost(10, 1),
                Cost(100, 2),
                false,
                false);

            Assert.AreEqual(2, result.Sides.Count);
            Assert.AreEqual("com", result.Sides[0].Backend.Name);
            Assert.AreEqual("Summary", result.Sides[0].Query.Source.WorksheetName);
            Assert.AreEqual("com", result.Sides[1].Backend.Name);
            Assert.AreEqual("SalesData", result.Sides[1].Query.Source.TableName);
            Assert.AreEqual("no", result.Sides[1].Query.HeaderMode);
            Assert.AreEqual("2025", result.Sides[1].Query.Filters[0].Value);
            Assert.IsTrue(result.Sides[0].Independent);
            Assert.IsTrue(result.Sides[1].Independent);
        }

        [TestMethod]
        public void PlannerUsesTargetHeaderInsteadOfInheritingSourceMeasureColumn()
        {
            var plan = Plan();
            plan.Basis.Source = new ExcelSourceRef
            {
                WorksheetName = "Orders",
                Address = "A1:C3001"
            };
            plan.Basis.HeaderMode = "yes";
            plan.Basis.Measures.Add(new ExcelVerificationMeasure
            {
                Column = "Amount",
                Function = "sum"
            });
            var assertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "S0_FollowUp_Result",
                    Address = "A1:B2",
                    Column = "Value",
                    Function = "sum"
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Orders",
                    Address = "A1:C3001",
                    Column = "Amount",
                    Function = "sum"
                }
            };

            var result = CreatePlanner().PlanAssertion(
                plan,
                assertion,
                Cost(1, 1),
                Cost(3000, 1),
                false,
                false);

            Assert.AreEqual("Value", result.Sides[0].Query.Columns.Single());
            Assert.AreEqual("Amount", result.Sides[1].Query.Columns.Single());
            Assert.AreEqual("yes", result.Sides[0].Query.HeaderMode);
            Assert.AreEqual("yes", result.Sides[1].Query.HeaderMode);
        }

        [TestMethod]
        public void PlannerUsesEveryUnionSourceForAdditiveScalarConservation()
        {
            var plan = Plan();
            plan.Basis.Topology = "union";
            plan.Basis.Sources.Add(new ExcelVerificationSource
            {
                Sheet = "Jan",
                Range = "$A$1:$B$10",
                Alias = "jan"
            });
            plan.Basis.Sources.Add(new ExcelVerificationSource
            {
                Sheet = "Feb",
                Range = "$A$1:$B$12",
                Alias = "feb"
            });
            plan.Basis.Filters.Add(new CalcFilter
            {
                Column = "Status",
                Operator = "eq",
                Value = "Closed"
            });
            var assertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Summary",
                    Address = "A1:B3",
                    Column = "Total",
                    Function = "sum"
                },
                Source = new ExcelAssertionSide
                {
                    Column = "Amount",
                    Function = "sum"
                }
            };

            var result = CreatePlanner().PlanAssertion(
                plan,
                assertion,
                Cost(3, 2),
                Cost(20, 2),
                false,
                false);

            var sourceQuery = result.Sides[1].Query;
            Assert.IsNull(sourceQuery.Source);
            Assert.AreEqual(2, sourceQuery.Sources.Count);
            Assert.AreEqual("Jan", sourceQuery.Sources[0].Sheet);
            Assert.AreEqual("Feb", sourceQuery.Sources[1].Sheet);
            Assert.AreEqual("sum", sourceQuery.Function);
            Assert.AreEqual("Amount", sourceQuery.Columns[0]);
            Assert.AreEqual("Closed", sourceQuery.Filters[0].Value);
            Assert.AreEqual("com", result.Sides[1].Backend.Name);
            Assert.IsTrue(ComCalcBackend.CanEvaluateQuery(sourceQuery, Cost(20, 2)));
            Assert.IsTrue(ManagedCalcBackend.CanEvaluateQuery(sourceQuery, Cost(20, 2)));
        }

        [TestMethod]
        public void PlannerRejectsNonAdditiveUnionSourceMeasures()
        {
            var plan = Plan();
            plan.Basis.Topology = "union";
            plan.Basis.Sources.Add(new ExcelVerificationSource
            {
                Sheet = "Jan",
                Range = "A1:B10",
                Alias = "jan"
            });
            plan.Basis.Measures.Add(new ExcelVerificationMeasure
            {
                Column = "Amount",
                Function = "average"
            });
            var assertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Summary",
                    Address = "A1:B3",
                    Column = "Average",
                    Function = "average"
                },
                Source = new ExcelAssertionSide
                {
                    Column = "Amount",
                    Function = "average"
                }
            };

            Assert.ThrowsException<InvalidOperationException>(
                () => CreatePlanner().PlanAssertion(
                    plan,
                    assertion,
                    Cost(3, 1),
                    Cost(10, 1),
                    false,
                    false));
        }

        [TestMethod]
        public void MultiSourceResultCombinerSumsScalarsAndCoverage()
        {
            var sources = new[]
            {
                new ExcelVerificationSource { Sheet = "Jan", Range = "A1:B2", Alias = "jan" },
                new ExcelVerificationSource { Sheet = "Feb", Range = "A1:B3", Alias = "feb" },
                new ExcelVerificationSource { Sheet = "Mar", Range = "A1:B1", Alias = "mar" }
            };
            var results = new[]
            {
                new CalcResult
                {
                    Scalar = 10d,
                    Coverage = new ExcelCoverageReport
                    {
                        TotalRows = 2,
                        ScannedRows = 2,
                        Backend = "managed",
                        CellCountsAvailable = true,
                        NonNumericCount = 1
                    },
                    Notes = { "第一来源提示" }
                },
                new CalcResult
                {
                    Scalar = 25d,
                    Coverage = new ExcelCoverageReport
                    {
                        TotalRows = 3,
                        ScannedRows = 3,
                        Backend = "managed",
                        CellCountsAvailable = true,
                        NonNumericCount = 2
                    }
                },
                VerificationPlanner.CreateEmptyMultiSourceResult("managed")
            };

            var result = VerificationPlanner.CombineMultiSourceResults(
                sources,
                results,
                "managed");

            Assert.AreEqual(35d, result.Scalar.Value, 0d);
            Assert.AreEqual(5, result.Coverage.TotalRows);
            Assert.AreEqual(5, result.Coverage.ScannedRows);
            Assert.AreEqual(3, result.Coverage.NonNumericCount);
            Assert.AreEqual(true, result.Coverage.CellCountsAvailable);
            Assert.AreEqual("jan：第一来源提示", result.Notes[0]);
        }

        [TestMethod]
        public void PlannerUsesIndependentComForFilteredAverageWithPrefixFilter()
        {
            var plan = Plan();
            plan.Basis.Source = new ExcelSourceRef
            {
                WorksheetName = "Sheet1"
            };
            var assertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    Column = "月薪",
                    Function = "average",
                    Filters =
                    {
                        new CalcFilter
                        {
                            Column = "姓名",
                            Operator = "startsWith",
                            Value = "韩"
                        }
                    }
                },
                ExpectedValue = 5896.071428571428d
            };

            var result = CreatePlanner().PlanAssertion(
                plan,
                assertion,
                Cost(1000, 2),
                null,
                true,
                false);

            Assert.AreEqual("com", result.Sides[0].Backend.Name);
            Assert.IsTrue(result.Sides[0].Independent);
            Assert.AreEqual("average", result.Sides[0].Query.Function);
            Assert.AreEqual("startsWith", result.Sides[0].Query.Filters[0].Operator);
        }

        [TestMethod]
        public void PlannerRoutesGroupAndSetQueriesToManagedBackend()
        {
            var groupAssertion = new ExcelAssertion
            {
                Kind = "groupMatch",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Summary",
                    KeyColumn = "Department",
                    ValueColumn = "Total"
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Sales",
                    KeyColumn = "Department",
                    ValueColumn = "Amount",
                    Function = "sum"
                }
            };

            var groupPlan = CreatePlanner().PlanAssertion(
                Plan(),
                groupAssertion,
                Cost(20, 2),
                Cost(100, 2),
                false,
                false);

            Assert.AreEqual("managed", groupPlan.Sides[0].Backend.Name);
            Assert.AreEqual("Department", groupPlan.Sides[0].Query.GroupByColumn);
            Assert.AreEqual("sum", groupPlan.Sides[0].Query.Function);

            var setAssertion = new ExcelAssertion
            {
                Kind = "keySet",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Output",
                    KeyColumn = "CustomerId"
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Input",
                    KeyColumn = "CustomerId"
                }
            };
            var setPlan = CreatePlanner().PlanAssertion(
                Plan(),
                setAssertion,
                Cost(10, 1),
                Cost(12, 1),
                false,
                false);

            Assert.AreEqual("managed", setPlan.Sides[0].Backend.Name);
            Assert.AreEqual("keySet", setPlan.Sides[0].Query.Function);
            Assert.AreEqual("CustomerId", setPlan.Sides[0].Query.Columns[0]);

            var differenceAssertion = new ExcelAssertion
            {
                Kind = "keySetDifference",
                SampleCount = 2,
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Orders",
                    KeyColumn = "CustomerId"
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Customers",
                    KeyColumn = "CustomerId"
                }
            };
            var differencePlan = CreatePlanner().PlanAssertion(
                Plan(),
                differenceAssertion,
                Cost(20, 1),
                Cost(30, 1),
                false,
                false);

            Assert.AreEqual(2, differencePlan.SampleCount);
            Assert.AreEqual("keySet", differencePlan.Sides[0].Query.Function);
            Assert.AreEqual("keySet", differencePlan.Sides[1].Query.Function);
        }

        [TestMethod]
        public void PlannerRoutesSortedAndNonBlankWithExplicitResultShapes()
        {
            var sortedAssertion = new ExcelAssertion
            {
                Kind = "sorted",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Output",
                    Column = "Amount"
                },
                Direction = "descending"
            };
            var sortedPlan = CreatePlanner().PlanAssertion(
                Plan(),
                sortedAssertion,
                Cost(10, 1),
                null,
                false,
                false);
            Assert.AreEqual("managed", sortedPlan.Sides[0].Backend.Name);
            Assert.AreEqual("sorted", sortedPlan.Sides[0].Query.Function);
            Assert.AreEqual("descending", sortedPlan.Sides[0].Query.SortDirection);

            var nonBlankAssertion = new ExcelAssertion
            {
                Kind = "nonBlank",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Output",
                    Columns = { "Q1", "Q2" }
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Input",
                    Columns = { "Jan", "Feb" }
                }
            };
            var nonBlankPlan = CreatePlanner().PlanAssertion(
                Plan(),
                nonBlankAssertion,
                Cost(10, 2),
                Cost(10, 2),
                false,
                false);
            Assert.AreEqual("managed", nonBlankPlan.Sides[0].Backend.Name);
            Assert.AreEqual("nonBlank", nonBlankPlan.Sides[0].Query.Function);
            Assert.AreEqual(2, nonBlankPlan.Sides[0].Query.Columns.Count);
        }

        [TestMethod]
        public void ManagedProducerUsesIndependentComOrMarksManagedFallback()
        {
            var scalarAssertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Output",
                    Column = "Amount",
                    Function = "sum"
                },
                ExpectedValue = 10d,
                Tolerance = 0.5d
            };
            var independentPlan = CreatePlanner().PlanAssertion(
                Plan(),
                scalarAssertion,
                Cost(10, 1),
                null,
                true,
                false);
            Assert.AreEqual("com", independentPlan.Sides[0].Backend.Name);
            Assert.IsTrue(independentPlan.Sides[0].Independent);
            Assert.AreEqual(10d, independentPlan.ExpectedValue.Value, 0d);
            Assert.AreEqual(0.5d, independentPlan.Tolerance.Value, 0d);

            var oversizedQueryAssertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Output",
                    Columns = { "Amount", "Tax" },
                    Function = "sum"
                },
                ExpectedValue = 10d
            };
            var fallbackPlan = CreatePlanner().PlanAssertion(
                Plan(),
                oversizedQueryAssertion,
                Cost(250001, 2),
                null,
                true,
                false);
            Assert.AreEqual("managed", fallbackPlan.Sides[0].Backend.Name);
            Assert.IsFalse(fallbackPlan.Sides[0].Independent);
        }

        [TestMethod]
        public void PlannerSelectsIndependentComForErrorCount()
        {
            var assertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    TableName = "OutputData",
                    Column = "Amount",
                    Function = "errorCount"
                },
                ExpectedValue = 1d
            };

            var planned = CreatePlanner().PlanAssertion(
                Plan(),
                assertion,
                Cost(10, 1),
                null,
                true,
                false);

            Assert.AreEqual("com", planned.Sides[0].Backend.Name);
            Assert.IsTrue(planned.Sides[0].Independent);
        }

        [TestMethod]
        public void PlannerRejectsAmbiguousAssertionShape()
        {
            var assertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Output",
                    Column = "Amount",
                    Function = "sum"
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Input",
                    Column = "Amount",
                    Function = "sum"
                },
                ExpectedValue = 10d
            };

            Assert.ThrowsException<InvalidOperationException>(
                () => CreatePlanner().PlanAssertion(
                    Plan(),
                    assertion,
                    Cost(10, 1),
                    Cost(10, 1),
                    false,
                    false));
        }

        [TestMethod]
        public void PlannerRejectsUnsupportedAssertionFields()
        {
            var groupMatch = new ExcelAssertion
            {
                Kind = "groupMatch",
                Target = new ExcelAssertionSide
                {
                    KeyColumn = "Department",
                    ValueColumn = "Total"
                },
                Source = new ExcelAssertionSide
                {
                    KeyColumn = "Department",
                    ValueColumn = "Amount"
                },
                ExpectedValue = 1d
            };
            AssertPlanRejected(groupMatch);

            var keySet = new ExcelAssertion
            {
                Kind = "keySet",
                Target = new ExcelAssertionSide { KeyColumn = "Id" },
                Source = new ExcelAssertionSide { KeyColumn = "Id" },
                ExpectedValue = 1d
            };
            AssertPlanRejected(keySet);

            var invalidKeySetDifference = new ExcelAssertion
            {
                Kind = "keySetDifference",
                SampleCount = 11,
                Target = new ExcelAssertionSide { KeyColumn = "Id" },
                Source = new ExcelAssertionSide { KeyColumn = "Id" }
            };
            AssertPlanRejected(invalidKeySetDifference);

            var rowCount = new ExcelAssertion
            {
                Kind = "rowCount",
                Target = new ExcelAssertionSide
                {
                    Filters =
                    {
                        new CalcFilter
                        {
                            Column = "Status",
                            Operator = "eq",
                            Value = "Open"
                        }
                    }
                },
                ExpectedValue = 1d
            };
            AssertPlanRejected(rowCount);

            var nonFiniteValue = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    Column = "Amount",
                    Function = "sum"
                },
                ExpectedValue = double.PositiveInfinity
            };
            AssertPlanRejected(nonFiniteValue);
        }

        [TestMethod]
        public void PlannerScopesExclusionsToEachSideAndRoutesThemToManaged()
        {
            var assertion = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "Output",
                    Column = "Total",
                    Function = "sum",
                    ExcludeRows = { 8 }
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Input",
                    Column = "Amount",
                    Function = "sum",
                    ExcludeLabelRows = true
                }
            };

            var result = CreatePlanner().PlanAssertion(
                Plan(),
                assertion,
                Cost(10, 1),
                Cost(10, 3),
                false,
                false);

            Assert.AreEqual("managed", result.Sides[0].Backend.Name);
            Assert.AreEqual("managed", result.Sides[1].Backend.Name);
            Assert.AreEqual(1, result.Sides[0].Query.ExcludeRows.Count);
            Assert.AreEqual(8, result.Sides[0].Query.ExcludeRows[0]);
            Assert.IsFalse(result.Sides[0].Query.ExcludeLabelRows);
            Assert.AreEqual(0, result.Sides[1].Query.ExcludeRows.Count);
            Assert.IsTrue(result.Sides[1].Query.ExcludeLabelRows);
        }

        [TestMethod]
        public void PlannerRejectsInvalidOrUnsupportedRowExclusions()
        {
            var duplicateRows = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    Column = "Amount",
                    ExcludeRows = { 4, 4 }
                },
                ExpectedValue = 10d
            };
            AssertPlanRejected(duplicateRows);

            var zeroRow = new ExcelAssertion
            {
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    Column = "Amount",
                    ExcludeRows = { 0 }
                },
                ExpectedValue = 10d
            };
            AssertPlanRejected(zeroRow);

            var sortedWithExclusions = new ExcelAssertion
            {
                Kind = "sorted",
                Target = new ExcelAssertionSide
                {
                    Column = "Amount",
                    ExcludeLabelRows = true
                }
            };
            AssertPlanRejected(sortedWithExclusions);
        }

        private static void AssertPlanRejected(ExcelAssertion assertion)
        {
            Assert.ThrowsException<InvalidOperationException>(
                () => CreatePlanner().PlanAssertion(
                    Plan(),
                    assertion,
                    Cost(10, 1),
                    Cost(10, 1),
                    false,
                    false));
        }

        private static VerificationPlanner CreatePlanner()
        {
            return new VerificationPlanner(
                new CapabilityBackend("com", ComCalcBackend.CanEvaluateQuery),
                new CapabilityBackend("managed", ManagedCalcBackend.CanEvaluateQuery));
        }

        private static ExcelVerificationPlan Plan()
        {
            return new ExcelVerificationPlan();
        }

        private static CalcCostEstimate Cost(int rowCount, int readColumnCount)
        {
            return new CalcCostEstimate
            {
                RowCount = rowCount,
                ReadColumnCount = readColumnCount,
                ProjectedCellCount = (long)rowCount * readColumnCount
            };
        }

        private sealed class CapabilityBackend : IExcelCalcBackend
        {
            private readonly Func<CalcQuery, CalcCostEstimate, bool> _canEvaluate;

            public CapabilityBackend(
                string name,
                Func<CalcQuery, CalcCostEstimate, bool> canEvaluate)
            {
                Name = name;
                _canEvaluate = canEvaluate;
            }

            public string Name { get; private set; }

            public bool CanEvaluate(CalcQuery query, CalcCostEstimate cost)
            {
                return _canEvaluate(query, cost);
            }

            public CalcResult Evaluate(CalcQuery query)
            {
                throw new NotSupportedException("Capability-only backend.");
            }
        }
    }
}
