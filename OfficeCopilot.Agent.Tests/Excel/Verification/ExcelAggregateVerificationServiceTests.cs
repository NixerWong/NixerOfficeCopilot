using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Tools;
using OfficeCopilot.Host.Excel.Verification;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class ExcelAggregateVerificationServiceTests
    {
        [TestMethod]
        public void SupportsTruncatedFirstCharacterAverageAndCountAggregates()
        {
            var request = new ExcelAggregateRequest
            {
                DerivedGroupBy =
                {
                    new ExcelAggregateDerivedGroup
                    {
                        Column = "Name",
                        Transform = "firstCharacter",
                        Alias = "Surname"
                    }
                },
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "average",
                        Alias = "Salary_average"
                    },
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "count",
                        Alias = "Salary_count"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                IsTruncated = true,
                Columns = { "Surname", "Salary_average", "Salary_count" }
            };

            ExcelAggregateVerificationService.GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;
            var supported = ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures);

            Assert.IsTrue(supported);
            Assert.AreEqual("Name", groupDefinition.SourceColumn);
            Assert.AreEqual("firstCharacter", groupDefinition.Transform);
            Assert.AreEqual(2, measures.Count);
        }

        [TestMethod]
        public void SupportsUnfilteredToolRequestWhenOptionalFiltersAreOmitted()
        {
            var request = ExcelToolJsonSerializer.Deserialize<ExcelAggregateRequest>(
                "{\"derivedGroupBy\":[{\"column\":\"Name\",\"transform\":\"firstCharacter\",\"alias\":\"Surname\"}],"
                + "\"measures\":[{\"column\":\"Salary\",\"function\":\"average\"},"
                + "{\"column\":\"Salary\",\"function\":\"count\"}],"
                + "\"orderBy\":[{\"column\":\"Salary_average\",\"descending\":true}],"
                + "\"maxGroups\":5}");
            var result = new ExcelAggregateResult
            {
                IsTruncated = true,
                Columns = { "Surname", "Salary_average", "Salary_count" }
            };

            Assert.IsNull(request.Filters);

            ExcelAggregateVerificationService.GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;
            var supported = ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures);

            Assert.IsTrue(supported);
            Assert.AreEqual("firstCharacter", groupDefinition.Transform);
            Assert.AreEqual(2, measures.Count);
        }

        [TestMethod]
        public void SupportsFilteredGlobalMaxAndCountAggregates()
        {
            var request = new ExcelAggregateRequest
            {
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "max",
                        Alias = "Salary_max"
                    },
                    new ExcelAggregateMeasure
                    {
                        Column = "Name",
                        Function = "count",
                        Alias = "Name_count"
                    }
                },
                Filters =
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Name",
                        Operator = "startsWith",
                        Value = "张"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                GroupCount = 1,
                TotalRowCount = 3,
                Columns = { "Salary_max", "Name_count" },
                Rows = { new[] { "9607", "27" } }
            };

            IList<ExcelAggregateMeasure> measures;
            IList<ExcelAggregateFilter> filters;
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedScalarShape(
                request,
                result,
                out measures,
                out filters));
            Assert.AreEqual(2, measures.Count);
            Assert.AreEqual(1, filters.Count);

            var sink = new ExcelAggregateVerificationService.ScalarProfileSink(
                measures,
                filters);
            sink.Begin(new ExcelColumnReadLayout
            {
                Columns = new[] { "Salary", "Name" },
                ColumnIsDateFormatted = new[] { false, false }
            });
            sink.Accept(0, new[]
            {
                new object[] { 9607d, 8500d, 4000d },
                new object[] { "张三", "张四", "李四" }
            });
            sink.End();

            Assert.IsNull(sink.UnsupportedFilterReason);
            Assert.IsNull(sink.UnsupportedMeasureReason);
            var query = ExcelAggregateVerificationService.CreateScalarMeasureQuery(
                new ExcelVerificationBasis(),
                measures[0],
                sink.CompiledFilters);
            Assert.AreEqual("max", query.Function);
            Assert.AreEqual(1, query.Filters.Count);
            Assert.AreEqual("textStartsWith", query.Filters[0].Operator);
            Assert.AreEqual("张", query.Filters[0].Value);

            var report = ExcelAggregateVerificationService.CompareScalarAggregateOutput(
                result,
                new ExcelVerificationBasis(),
                null,
                measures,
                sink.CompiledFilters,
                new[]
                {
                    new CalcResult { Scalar = 9607d },
                    new CalcResult { Scalar = 27d }
                },
                new ExcelCoverageReport { TotalRows = 3, ScannedRows = 3 },
                3);
            Assert.IsTrue(report.Passed);
            Assert.AreEqual("A", report.Tier);
            Assert.AreEqual(3, report.Items.Count);
        }

        [TestMethod]
        public void SupportsOnlyCompleteUnfilteredSingleSumUnionForIndependentVerification()
        {
            var fixture = CreateUnionSumFixture();

            var supported = ExcelAggregateVerificationService.TryResolveSupportedUnionScalarShape(
                fixture.Request,
                fixture.Result,
                fixture.Basis,
                out var measure,
                out var sourceRowCount,
                out var reason);

            Assert.IsTrue(supported, reason);
            Assert.AreEqual("Revenue", measure.Column);
            Assert.AreEqual(9, sourceRowCount);
            Assert.AreEqual(string.Empty, reason);
        }

        [TestMethod]
        public void UnionScalarSumQueryUsesResolvedSourcesAndHeaderMode()
        {
            var fixture = CreateUnionSumFixture();
            var query = ExcelAggregateVerificationService.CreateUnionScalarMeasureQuery(
                fixture.Basis,
                fixture.Request.Measures[0]);

            Assert.IsTrue(query.Source == null
                || (string.IsNullOrWhiteSpace(query.Source.WorksheetName)
                    && string.IsNullOrWhiteSpace(query.Source.Address)
                    && string.IsNullOrWhiteSpace(query.Source.TableName)));
            Assert.AreEqual("sum", query.Function);
            Assert.AreEqual("Revenue", query.Columns[0]);
            Assert.AreEqual("yes", query.HeaderMode);
            Assert.AreEqual(3, query.Sources.Count);
            Assert.AreEqual("Jan", query.Sources[0].Alias);
            Assert.AreEqual("A1:B4", query.Sources[0].Range);
            Assert.AreEqual("Mar", query.Sources[2].Alias);
        }

        [TestMethod]
        public void UnionVerificationRequiresCompleteComCoverageForEverySource()
        {
            var completeComResult = new CalcResult
            {
                Scalar = 790d,
                Coverage = new ExcelCoverageReport
                {
                    TotalRows = 9,
                    ScannedRows = 9,
                    Backend = "com"
                }
            };
            Assert.IsTrue(
                ExcelAggregateVerificationService.HasCompleteUnionComResult(
                    completeComResult,
                    9));

            completeComResult.Coverage.ScannedRows = 8;
            Assert.IsFalse(
                ExcelAggregateVerificationService.HasCompleteUnionComResult(
                    completeComResult,
                    9));

            completeComResult.Coverage.ScannedRows = 9;
            completeComResult.Coverage.Backend = "managed";
            Assert.IsFalse(
                ExcelAggregateVerificationService.HasCompleteUnionComResult(
                    completeComResult,
                    9));
        }

        [TestMethod]
        public void UnionScalarAggregateOutputPassesWhenComResultMatches()
        {
            var fixture = CreateUnionSumFixture();
            var coverage = new ExcelCoverageReport
            {
                TotalRows = 9,
                ScannedRows = 9,
                Backend = "com",
                CellCountsAvailable = false
            };
            var report = ExcelAggregateVerificationService.CompareScalarAggregateOutput(
                fixture.Result,
                fixture.Basis,
                null,
                fixture.Request.Measures,
                new List<CalcFilter>(),
                new[]
                {
                    new CalcResult
                    {
                        Scalar = 790d,
                        Coverage = coverage
                    }
                },
                coverage,
                9);

            Assert.IsTrue(report.Passed);
            Assert.AreEqual("A", report.Tier);
            Assert.AreEqual(0, report.DiffCount);
            Assert.AreEqual(2, report.Items.Count);
            Assert.IsTrue(report.Items[0].Independent);
            Assert.AreEqual("com", report.Items[0].TargetBackend);
            Assert.AreEqual(9L, report.Items[0].TargetCoverage.ScannedRows);
        }

        [TestMethod]
        public void UnionScalarAggregateOutputMismatchFailsRatherThanPassing()
        {
            var fixture = CreateUnionSumFixture();
            fixture.Result.Rows[0][0] = "791";
            var coverage = new ExcelCoverageReport
            {
                TotalRows = 9,
                ScannedRows = 9,
                Backend = "com",
                CellCountsAvailable = false
            };
            var report = ExcelAggregateVerificationService.CompareScalarAggregateOutput(
                fixture.Result,
                fixture.Basis,
                null,
                fixture.Request.Measures,
                new List<CalcFilter>(),
                new[]
                {
                    new CalcResult
                    {
                        Scalar = 790d,
                        Coverage = coverage
                    }
                },
                coverage,
                9);

            Assert.IsFalse(report.Passed);
            Assert.AreEqual("A", report.Tier);
            Assert.AreEqual(1, report.DiffCount);
            Assert.IsFalse(report.Items[0].Passed);
        }

        [TestMethod]
        public void UnionIndependentVerificationRejectsUnsupportedOrIncompleteShapes()
        {
            var filtered = CreateUnionSumFixture();
            filtered.Request.Filters.Add(new ExcelAggregateFilter
            {
                Column = "Department",
                Operator = "eq",
                Value = "North"
            });
            AssertUnionShapeRejected(filtered);

            var lookup = CreateUnionSumFixture();
            lookup.Request.Lookups.Add(new ExcelAggregateLookup());
            AssertUnionShapeRejected(lookup);

            var grouped = CreateUnionSumFixture();
            grouped.Request.GroupBy.Add("Department");
            AssertUnionShapeRejected(grouped);

            var multipleMeasures = CreateUnionSumFixture();
            multipleMeasures.Request.Measures.Add(new ExcelAggregateMeasure
            {
                Column = "Revenue",
                Function = "sum",
                Alias = "Revenue_sum_2"
            });
            AssertUnionShapeRejected(multipleMeasures);

            var autoHeaders = CreateUnionSumFixture();
            autoHeaders.Request.HeaderMode = "auto";
            autoHeaders.Basis.HeaderMode = "auto";
            AssertUnionShapeRejected(autoHeaders);

            var truncated = CreateUnionSumFixture();
            truncated.Result.IsTruncated = true;
            AssertUnionShapeRejected(truncated);

            var incompleteCoverage = CreateUnionSumFixture();
            incompleteCoverage.Result.Coverage.ScannedRows = 8;
            AssertUnionShapeRejected(incompleteCoverage);

            var mismatchedRange = CreateUnionSumFixture();
            mismatchedRange.Basis.Sources[1].Range = "A1:C4";
            AssertUnionShapeRejected(mismatchedRange);

            var mismatchedMeasure = CreateUnionSumFixture();
            mismatchedMeasure.Basis.Measures[0].Column = "Department";
            AssertUnionShapeRejected(mismatchedMeasure);
        }

        [TestMethod]
        public void ScalarMaxFailsClosedWhenMeasureContainsTextNumbers()
        {
            var measure = new ExcelAggregateMeasure
            {
                Column = "Amount",
                Function = "max"
            };
            var sink = new ExcelAggregateVerificationService.ScalarProfileSink(
                new[] { measure },
                new List<ExcelAggregateFilter>());
            sink.Begin(new ExcelColumnReadLayout
            {
                Columns = new[] { "Amount" },
                ColumnIsDateFormatted = new[] { false }
            });
            sink.Accept(0, new[]
            {
                new object[] { 10d, "12" }
            });
            sink.End();

            Assert.IsNotNull(sink.UnsupportedMeasureReason);
        }

        [TestMethod]
        public void SupportsMonthGroupingWithAnExactDateMonthContainsFilter()
        {
            var request = ExcelToolJsonSerializer.Deserialize<ExcelAggregateRequest>(
                "{\"derivedGroupBy\":[{\"column\":\"Birthday\",\"transform\":\"month\",\"alias\":\"BirthMonth\"}],"
                + "\"measures\":[{\"column\":\"Salary\",\"function\":\"average\",\"alias\":\"Salary_average\"},"
                + "{\"column\":\"Salary\",\"function\":\"count\",\"alias\":\"Salary_count\"}],"
                + "\"filters\":[{\"column\":\"Birthday\",\"operator\":\"contains\",\"value\":\"-06-\"}]}");
            var result = CreateMonthResult();

            ExcelAggregateVerificationService.GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
            Assert.IsTrue(groupDefinition.FilteredMonth.HasValue);
            Assert.AreEqual(6, groupDefinition.FilteredMonth.Value);

            var groupFilter = ExcelAggregateVerificationService.CreateGroupFilter(
                groupDefinition,
                "06月");
            Assert.AreEqual("Birthday", groupFilter.Column);
            Assert.AreEqual("monthEquals", groupFilter.Operator);
            Assert.AreEqual("6", groupFilter.Value);
        }

        [TestMethod]
        public void SupportsMonthGroupingWithGenericFiltersForRuntimeTypeValidation()
        {
            var request = CreateMonthRequest();
            var result = CreateMonthResult();
            ExcelAggregateVerificationService.GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;

            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
            Assert.IsNull(groupDefinition.FilteredMonth);

            request.Filters.Add(new ExcelAggregateFilter
            {
                Column = "Birthday",
                Operator = "contains",
                Value = "06"
            });
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
            Assert.AreEqual(1, groupDefinition.VerificationFilters.Count);

            request.Filters[0].Value = "-06-";
            request.Filters[0].Column = "OtherDate";
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
            Assert.AreEqual(1, groupDefinition.VerificationFilters.Count);
        }

        [TestMethod]
        public void SupportsUnfilteredYearGroupingWithNativeDateValues()
        {
            var request = new ExcelAggregateRequest
            {
                DerivedGroupBy =
                {
                    new ExcelAggregateDerivedGroup
                    {
                        Column = "Birthday",
                        Transform = "year",
                        Alias = "BirthYear"
                    }
                },
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "average",
                        Alias = "Salary_average"
                    },
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "count",
                        Alias = "Salary_count"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                Columns = { "BirthYear", "Salary_average", "Salary_count" }
            };

            ExcelAggregateVerificationService.GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
            Assert.AreEqual("year", groupDefinition.Transform);
            Assert.AreEqual(2, measures.Count);

            var groupFilter = ExcelAggregateVerificationService.CreateGroupFilter(
                groupDefinition,
                "1989");
            Assert.AreEqual("Birthday", groupFilter.Column);
            Assert.AreEqual("yearEquals", groupFilter.Operator);
            Assert.AreEqual("1989", groupFilter.Value);

            request.Filters.Add(new ExcelAggregateFilter
            {
                Column = "Birthday",
                Operator = "contains",
                Value = "1989"
            });
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
        }

        [TestMethod]
        public void SupportsComposableTextFiltersWithDerivedGroupsAndRejectsUnknownOperators()
        {
            var request = new ExcelAggregateRequest
            {
                Filters =
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Gender",
                        Operator = "eq",
                        Value = "女"
                    }
                },
                DerivedGroupBy =
                {
                    new ExcelAggregateDerivedGroup
                    {
                        Column = "Name",
                        Transform = "firstCharacter",
                        Alias = "Surname"
                    }
                },
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Name",
                        Function = "count",
                        Alias = "Name_count"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                Columns = { "Surname", "Name_count" }
            };

            ExcelAggregateVerificationService.GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
            Assert.AreEqual(1, groupDefinition.VerificationFilters.Count);
            Assert.AreEqual("Gender", groupDefinition.VerificationFilters[0].Column);
            Assert.AreEqual("eq", groupDefinition.VerificationFilters[0].Operator);
            Assert.AreEqual("女", groupDefinition.VerificationFilters[0].Value);

            request.Filters[0].Operator = "contains";
            request.Filters[0].Value = "虎";
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));

            request.Filters[0].Operator = "matchesRegex";
            Assert.IsFalse(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
        }

        [TestMethod]
        public void GroupKeyDiscoveryAppliesTextEqualityFiltersBeforeAddingGroups()
        {
            var definition = new ExcelAggregateVerificationService.GroupDefinition(
                "Name",
                "firstCharacter",
                1d,
                null,
                new List<ExcelAggregateFilter>
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Gender",
                        Operator = "eq",
                        Value = "女"
                    }
                });
            var sink = new ExcelAggregateVerificationService.GroupKeySink(definition);
            sink.Begin(new ExcelColumnReadLayout
            {
                Columns = new[] { "Name", "Gender" },
                ColumnIsDateFormatted = new[] { false, false }
            });
            sink.Accept(0, new[]
            {
                new object[] { "李小红", "王强", "陈静", "李丽" },
                new object[] { "女", "男", "女", "男" }
            });
            sink.End();

            Assert.AreEqual(2, sink.GroupKeys.Count);
            Assert.AreEqual("李", sink.GroupKeys[0]);
            Assert.AreEqual("陈", sink.GroupKeys[1]);
            Assert.IsFalse(sink.HasUnsupportedValue);
            Assert.IsNull(sink.UnsupportedFilterReason);
        }

        [TestMethod]
        public void GroupKeyDiscoveryAndComQueryShareContainsFilterSemantics()
        {
            var definition = new ExcelAggregateVerificationService.GroupDefinition(
                "Gender",
                string.Empty,
                0d,
                null,
                new List<ExcelAggregateFilter>
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Name",
                        Operator = "contains",
                        Value = "虎"
                    }
                });
            var sink = new ExcelAggregateVerificationService.GroupKeySink(definition);
            sink.Begin(new ExcelColumnReadLayout
            {
                Columns = new[] { "Gender", "Name" },
                ColumnIsDateFormatted = new[] { false, false }
            });
            sink.Accept(0, new[]
            {
                new object[] { "女", "男", "女", "男", "女", "男", "女" },
                new object[] { "朱虎", "王虎", "李花", "赵虎", "陈虎", "周明", "孙虎" }
            });
            sink.End();

            Assert.IsNull(sink.UnsupportedFilterReason);
            Assert.AreEqual(2, sink.GroupKeys.Count);
            Assert.AreEqual("女", sink.GroupKeys[0]);
            Assert.AreEqual("男", sink.GroupKeys[1]);

            var query = ExcelAggregateVerificationService.CreateMeasureQuery(
                new ExcelVerificationBasis(),
                definition,
                "女",
                new ExcelAggregateMeasure
                {
                    Column = "Name",
                    Function = "count"
                });
            Assert.AreEqual(2, query.Filters.Count);
            Assert.AreEqual("textContains", query.Filters[0].Operator);
            Assert.AreEqual("虎", query.Filters[0].Value);
            Assert.AreEqual("textEquals", query.Filters[1].Operator);
            Assert.AreEqual("女", query.Filters[1].Value);
        }

        [TestMethod]
        public void NumericFiltersAreCompiledOnceAndMixedTextColumnsFailClosed()
        {
            var numericDefinition = new ExcelAggregateVerificationService.GroupDefinition(
                "Gender",
                string.Empty,
                0d,
                null,
                new List<ExcelAggregateFilter>
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Age",
                        Operator = "ge",
                        Value = "18"
                    }
                });
            var numericSink = new ExcelAggregateVerificationService.GroupKeySink(numericDefinition);
            numericSink.Begin(new ExcelColumnReadLayout
            {
                Columns = new[] { "Gender", "Age" },
                ColumnIsDateFormatted = new[] { false, false }
            });
            numericSink.Accept(0, new[]
            {
                new object[] { "女", "男" },
                new object[] { 18d, 17d }
            });
            numericSink.End();

            Assert.IsNull(numericSink.UnsupportedFilterReason);
            var numericQuery = ExcelAggregateVerificationService.CreateMeasureQuery(
                new ExcelVerificationBasis(),
                numericDefinition,
                "女",
                new ExcelAggregateMeasure { Column = "Name", Function = "count" });
            Assert.AreEqual("ge", numericQuery.Filters[0].Operator);

            var mixedDefinition = new ExcelAggregateVerificationService.GroupDefinition(
                "Gender",
                string.Empty,
                0d,
                null,
                new List<ExcelAggregateFilter>
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Name",
                        Operator = "contains",
                        Value = "虎"
                    }
                });
            var mixedSink = new ExcelAggregateVerificationService.GroupKeySink(mixedDefinition);
            mixedSink.Begin(new ExcelColumnReadLayout
            {
                Columns = new[] { "Gender", "Name" },
                ColumnIsDateFormatted = new[] { false, false }
            });
            mixedSink.Accept(0, new[]
            {
                new object[] { "女", "男" },
                new object[] { "朱虎", 123d }
            });
            mixedSink.End();

            StringAssert.Contains(mixedSink.UnsupportedFilterReason, "值类型");
        }

        [TestMethod]
        public void DateTextFiltersComposeWithExistingDateGroupTransforms()
        {
            var definition = new ExcelAggregateVerificationService.GroupDefinition(
                "Birthday",
                "year",
                1d,
                null,
                new List<ExcelAggregateFilter>
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Birthday",
                        Operator = "contains",
                        Value = "1989"
                    }
                });
            var sink = new ExcelAggregateVerificationService.GroupKeySink(definition);
            sink.Begin(new ExcelColumnReadLayout
            {
                Columns = new[] { "Birthday" },
                ColumnIsDateFormatted = new[] { true }
            });
            sink.Accept(0, new[]
            {
                new object[]
                {
                    new DateTime(1989, 1, 15).ToOADate(),
                    new DateTime(1990, 1, 15).ToOADate(),
                    new DateTime(1989, 12, 31).ToOADate()
                }
            });
            sink.End();

            Assert.IsNull(sink.UnsupportedFilterReason);
            Assert.AreEqual(1, sink.GroupKeys.Count);
            Assert.AreEqual("1989", sink.GroupKeys[0]);

            var query = ExcelAggregateVerificationService.CreateMeasureQuery(
                new ExcelVerificationBasis(),
                definition,
                "1989",
                new ExcelAggregateMeasure { Column = "Salary", Function = "average" });
            Assert.AreEqual("dateTextContains", query.Filters[0].Operator);
            Assert.AreEqual("yearEquals", query.Filters[1].Operator);
        }

        [TestMethod]
        public void NumberBucketGroupingUsesTheObservedBucketBoundsForComRecalculation()
        {
            var request = new ExcelAggregateRequest
            {
                DerivedGroupBy =
                {
                    new ExcelAggregateDerivedGroup
                    {
                        Column = "Amount",
                        Transform = "numberBucket",
                        Size = 10d,
                        Alias = "Bucket"
                    }
                },
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Amount",
                        Function = "sum",
                        Alias = "Amount_sum"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                Columns = { "Bucket", "Amount_sum" }
            };
            ExcelAggregateVerificationService.GroupDefinition definition;
            IList<ExcelAggregateMeasure> measures;
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out definition,
                out measures));
            Assert.AreEqual(10d, definition.Size);

            var sink = new ExcelAggregateVerificationService.GroupKeySink(definition);
            sink.Begin(new ExcelColumnReadLayout
            {
                Columns = new[] { "Amount" },
                ColumnIsDateFormatted = new[] { false }
            });
            sink.Accept(0, new[]
            {
                new object[] { 3d, 12d, 24d }
            });
            sink.End();

            Assert.IsFalse(sink.HasUnsupportedValue);
            Assert.AreEqual(3, sink.GroupKeys.Count);
            var query = ExcelAggregateVerificationService.CreateMeasureQuery(
                new ExcelVerificationBasis(),
                definition,
                "10~20",
                measures[0]);
            Assert.AreEqual(2, query.Filters.Count);
            Assert.AreEqual("ge", query.Filters[0].Operator);
            Assert.AreEqual("10", query.Filters[0].Value);
            Assert.AreEqual("lt", query.Filters[1].Operator);
            Assert.AreEqual("20", query.Filters[1].Value);
        }

        [TestMethod]
        public void MeasureQueriesReapplyValidatedSourceFiltersAlongsideGroupFilters()
        {
            var definition = new ExcelAggregateVerificationService.GroupDefinition(
                "Name",
                "firstCharacter",
                1d,
                null,
                new List<ExcelAggregateFilter>
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Gender",
                        Operator = "eq",
                        Value = "女"
                    }
                });
            var query = ExcelAggregateVerificationService.CreateMeasureQuery(
                new ExcelVerificationBasis(),
                definition,
                "李",
                new ExcelAggregateMeasure
                {
                    Column = "Name",
                    Function = "count"
                });

            Assert.AreEqual(2, query.Filters.Count);
            Assert.AreEqual("Gender", query.Filters[0].Column);
            Assert.AreEqual("textEquals", query.Filters[0].Operator);
            Assert.AreEqual("女", query.Filters[0].Value);
            Assert.AreEqual("textStartsWith", query.Filters[1].Operator);
            Assert.AreEqual("李", query.Filters[1].Value);
        }

        [TestMethod]
        public void MonthGroupKeyMatchesNativeExcelDateValuesAndRejectsUnsafeValues()
        {
            string groupKey;
            Assert.IsTrue(ExcelAggregateVerificationService.TryGetMonthGroupKey(
                new DateTime(2024, 6, 15).ToOADate(),
                true,
                out groupKey));
            Assert.AreEqual("06月", groupKey);

            Assert.IsFalse(ExcelAggregateVerificationService.TryGetMonthGroupKey(
                "2024-06-15",
                true,
                out groupKey));
            Assert.IsFalse(ExcelAggregateVerificationService.TryGetMonthGroupKey(
                new DateTime(2024, 6, 15).ToOADate(),
                false,
                out groupKey));
            Assert.IsFalse(ExcelAggregateVerificationService.TryGetMonthGroupKey(
                0d,
                true,
                out groupKey));
        }

        [TestMethod]
        public void YearGroupKeyMatchesNativeExcelDateValuesAndRejectsUnsafeValues()
        {
            string groupKey;
            Assert.IsTrue(ExcelAggregateVerificationService.TryGetYearGroupKey(
                new DateTime(1989, 6, 15).ToOADate(),
                true,
                out groupKey));
            Assert.AreEqual("1989", groupKey);

            Assert.IsFalse(ExcelAggregateVerificationService.TryGetYearGroupKey(
                "1989-06-15",
                true,
                out groupKey));
            Assert.IsFalse(ExcelAggregateVerificationService.TryGetYearGroupKey(
                new DateTime(1989, 6, 15).ToOADate(),
                false,
                out groupKey));
            Assert.IsFalse(ExcelAggregateVerificationService.TryGetYearGroupKey(
                0d,
                true,
                out groupKey));
        }

        [TestMethod]
        public void SupportedDateTransformsShareTheSameIndependentGroupingPipeline()
        {
            var date = new DateTime(2024, 6, 15);
            AssertDateGroupTransform(date, "year", "2024", "yearEquals", "2024");
            AssertDateGroupTransform(date, "quarter", "2024-Q2", "quarterEquals", "2024-Q2");
            AssertDateGroupTransform(date, "month", "06月", "monthEquals", "6");
            AssertDateGroupTransform(
                date,
                "monthName",
                date.ToString("MMMM", System.Globalization.CultureInfo.CurrentCulture),
                "monthEquals",
                "6");
            AssertDateGroupTransform(date, "yearMonth", "2024-06", "yearMonthEquals", "2024-06");
            AssertDateGroupTransform(date, "day", "15", "dayEquals", "15");
            AssertDateGroupTransform(
                date,
                "dayOfWeek",
                "6-" + date.ToString("dddd", System.Globalization.CultureInfo.CurrentCulture),
                "dayOfWeekEquals",
                "6");
        }

        [TestMethod]
        public void PrefixGroupingUsesExactFilterForShortKeysAndPrefixForFullKeys()
        {
            var definition = new ExcelAggregateVerificationService.GroupDefinition(
                "Name",
                "prefix",
                2d);

            Assert.AreEqual(
                "eq",
                ExcelAggregateVerificationService.CreateGroupFilter(definition, "A").Operator);
            Assert.AreEqual(
                "startsWith",
                ExcelAggregateVerificationService.CreateGroupFilter(definition, "AB").Operator);
            Assert.AreEqual(
                "isEmpty",
                ExcelAggregateVerificationService.CreateGroupFilter(definition, string.Empty).Operator);
        }

        [TestMethod]
        public void GroupComparerUsesRequestedMeasureOrderAndOrdinalTieBreak()
        {
            var request = new ExcelAggregateRequest
            {
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "average",
                        Alias = "Salary_average"
                    }
                },
                OrderBy =
                {
                    new ExcelAggregateSort
                    {
                        Column = "Salary_average",
                        Descending = true
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                Columns = { "Surname", "Salary_average" }
            };
            var measures = (IList<ExcelAggregateMeasure>)request.Measures;
            var groups = new List<ExcelAggregateVerificationService.CalculatedGroup>
            {
                CreateGroup("韩", 5896.07d),
                CreateGroup("陈", 5891d),
                CreateGroup("孔", 5896.07d)
            };

            groups.Sort(new ExcelAggregateVerificationService.AggregateGroupComparer(
                request,
                result,
                measures));

            CollectionAssert.AreEqual(
                new[] { "孔", "韩", "陈" },
                groups.ConvertAll(group => group.Key));
        }

        [TestMethod]
        public void TruncatedAggregateMustReturnTheIndependentlyCalculatedTopGroups()
        {
            var sortedGroups = new List<ExcelAggregateVerificationService.CalculatedGroup>
            {
                CreateGroup("韩", 5896.07d),
                CreateGroup("陈", 5891d),
                CreateGroup("孔", 5859.47d)
            };

            Assert.IsTrue(ExcelAggregateVerificationService.MatchesExpectedGroupKeys(
                new[] { "韩", "陈" },
                sortedGroups,
                2));
            Assert.IsFalse(ExcelAggregateVerificationService.MatchesExpectedGroupKeys(
                new[] { "陈", "韩" },
                sortedGroups,
                2));
        }

        [TestMethod]
        public void RejectsGroupKeysThatExcelCriteriaWouldMergeByCase()
        {
            Assert.IsTrue(ExcelAggregateVerificationService.HasCaseInsensitiveCollision(
                new[] { "A", "a" }));
            Assert.IsFalse(ExcelAggregateVerificationService.HasCaseInsensitiveCollision(
                new[] { "韩", "陈" }));
        }

        [TestMethod]
        public void DoesNotClaimUnsupportedFilterOperatorsForYearAggregates()
        {
            var request = new ExcelAggregateRequest
            {
                Filters =
                {
                    new ExcelAggregateFilter
                    {
                        Column = "Region",
                        Operator = "matchesRegex",
                        Value = "North"
                    }
                },
                DerivedGroupBy =
                {
                    new ExcelAggregateDerivedGroup
                    {
                        Column = "Date",
                        Transform = "year",
                        Alias = "Year"
                    }
                },
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "average"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                Columns = { "Year", "Salary_average" }
            };

            ExcelAggregateVerificationService.GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;
            Assert.IsFalse(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures));
        }

        private static ExcelAggregateVerificationService.CalculatedGroup CreateGroup(
            string key,
            double average)
        {
            var group = new ExcelAggregateVerificationService.CalculatedGroup(key, 1);
            group.Values[0] = average;
            return group;
        }

        private static void AssertUnionShapeRejected(UnionSumFixture fixture)
        {
            Assert.IsFalse(ExcelAggregateVerificationService.TryResolveSupportedUnionScalarShape(
                fixture.Request,
                fixture.Result,
                fixture.Basis,
                out var measure,
                out _,
                out _));
            Assert.IsNull(measure);
        }

        private static UnionSumFixture CreateUnionSumFixture()
        {
            var request = new ExcelAggregateRequest
            {
                HeaderMode = "yes",
                Sources =
                {
                    new ExcelAggregateSource
                    {
                        WorksheetName = "Jan",
                        Address = "A1:B4",
                        Alias = "Jan"
                    },
                    new ExcelAggregateSource
                    {
                        WorksheetName = "Feb",
                        Address = "A1:B4",
                        Alias = "Feb"
                    },
                    new ExcelAggregateSource
                    {
                        WorksheetName = "Mar",
                        Address = "A1:B4",
                        Alias = "Mar"
                    }
                },
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Revenue",
                        Function = "sum"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                SourceKind = "union",
                TotalRowCount = 9,
                FilteredRowCount = 9,
                GroupCount = 1,
                Columns = { "Revenue_sum" },
                Rows = { new[] { "790" } },
                Coverage = new ExcelCoverageReport
                {
                    TotalRows = 9,
                    ScannedRows = 9,
                    Backend = "managed",
                    CellCountsAvailable = false
                },
                SourceSummaries =
                {
                    new ExcelAggregateSourceSummary
                    {
                        Alias = "Jan",
                        WorksheetName = "Jan",
                        Address = "A1:B4",
                        TotalRowCount = 3
                    },
                    new ExcelAggregateSourceSummary
                    {
                        Alias = "Feb",
                        WorksheetName = "Feb",
                        Address = "A1:B4",
                        TotalRowCount = 3
                    },
                    new ExcelAggregateSourceSummary
                    {
                        Alias = "Mar",
                        WorksheetName = "Mar",
                        Address = "A1:B4",
                        TotalRowCount = 3
                    }
                }
            };
            var basis = new ExcelVerificationBasis
            {
                HeaderMode = "yes",
                Topology = "union",
                Measures =
                {
                    new ExcelVerificationMeasure
                    {
                        Column = "Revenue",
                        Function = "sum"
                    }
                },
                Sources =
                {
                    new ExcelVerificationSource
                    {
                        Sheet = "Jan",
                        Range = "A1:B4",
                        Alias = "Jan"
                    },
                    new ExcelVerificationSource
                    {
                        Sheet = "Feb",
                        Range = "A1:B4",
                        Alias = "Feb"
                    },
                    new ExcelVerificationSource
                    {
                        Sheet = "Mar",
                        Range = "A1:B4",
                        Alias = "Mar"
                    }
                }
            };
            return new UnionSumFixture
            {
                Request = request,
                Result = result,
                Basis = basis
            };
        }

        private sealed class UnionSumFixture
        {
            public ExcelAggregateRequest Request { get; set; }
            public ExcelAggregateResult Result { get; set; }
            public ExcelVerificationBasis Basis { get; set; }
        }

        private static ExcelAggregateRequest CreateMonthRequest()
        {
            return new ExcelAggregateRequest
            {
                DerivedGroupBy =
                {
                    new ExcelAggregateDerivedGroup
                    {
                        Column = "Birthday",
                        Transform = "month",
                        Alias = "BirthMonth"
                    }
                },
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "average",
                        Alias = "Salary_average"
                    },
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "count",
                        Alias = "Salary_count"
                    }
                }
            };
        }

        private static ExcelAggregateResult CreateMonthResult()
        {
            return new ExcelAggregateResult
            {
                Columns = { "BirthMonth", "Salary_average", "Salary_count" }
            };
        }

        private static void AssertDateGroupTransform(
            DateTime date,
            string transform,
            string expectedGroupKey,
            string expectedFilterOperator,
            string expectedFilterValue)
        {
            string groupKey;
            Assert.IsTrue(ExcelAggregateVerificationService.TryGetDateGroupKey(
                transform,
                date.ToOADate(),
                true,
                out groupKey));
            Assert.AreEqual(expectedGroupKey, groupKey, transform);

            var request = new ExcelAggregateRequest
            {
                DerivedGroupBy =
                {
                    new ExcelAggregateDerivedGroup
                    {
                        Column = "Birthday",
                        Transform = transform,
                        Alias = "Group"
                    }
                },
                Measures =
                {
                    new ExcelAggregateMeasure
                    {
                        Column = "Salary",
                        Function = "average",
                        Alias = "Salary_average"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                Columns = { "Group", "Salary_average" }
            };
            ExcelAggregateVerificationService.GroupDefinition groupDefinition;
            IList<ExcelAggregateMeasure> measures;
            Assert.IsTrue(ExcelAggregateVerificationService.TryResolveSupportedShape(
                request,
                result,
                out groupDefinition,
                out measures),
                transform);

            var filter = ExcelAggregateVerificationService.CreateGroupFilter(
                groupDefinition,
                groupKey);
            Assert.AreEqual(expectedFilterOperator, filter.Operator, transform);
            Assert.AreEqual(expectedFilterValue, filter.Value, transform);
        }
    }
}
