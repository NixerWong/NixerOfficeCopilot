using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Tools;

namespace OfficeCopilot.Agent.Tests.Excel
{
    /// <summary>
    /// D9 回归网：派生分组表达不了日期部分，会把 agent 逼到 VBA，再被技能闸门拦死。
    /// </summary>
    [TestClass]
    public class ExcelAggregationEngineTests
    {
        [TestMethod]
        public void DerivedGroupBy_Month_GroupsByBirthMonthWithoutHelperColumn()
        {
            var result = Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "生日", Transform = "month" },
                new ExcelAggregateMeasure { Column = "工资", Function = "average", Alias = "平均工资" });

            CollectionAssert.AreEqual(new[] { "生日月份", "平均工资" }, result.Columns.ToArray());

            var rows = result.Rows.ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
            Assert.AreEqual(3, rows.Count, "三个不同月份应聚成三组。");
            Assert.AreEqual("15000", rows["03月"], "3 月两人 10000/20000，均值 15000。");
            Assert.AreEqual("8000", rows["07月"]);
            Assert.AreEqual("9000", rows["12月"]);
        }

        [TestMethod]
        public void DerivedGroupBy_QuarterAndYearMonth_UseCalendarSemantics()
        {
            var quarter = Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "生日", Transform = "quarter", Alias = "季度" },
                new ExcelAggregateMeasure { Function = "count", Alias = "人数" });

            var quarterRows = quarter.Rows.ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
            Assert.AreEqual("2", quarterRows["1990-Q1"]);
            Assert.AreEqual("1", quarterRows["1985-Q3"]);
            Assert.AreEqual("1", quarterRows["1990-Q4"]);

            var yearMonth = Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "生日", Transform = "yearMonth", Alias = "年月" },
                new ExcelAggregateMeasure { Function = "count", Alias = "人数" });

            CollectionAssert.Contains(yearMonth.Rows.Select(r => r[0]).ToArray(), "1990-03");
        }

        [TestMethod]
        public void DerivedGroupBy_NumberBucket_BucketsByRequestedWidth()
        {
            var result = Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "工资", Transform = "numberBucket", Alias = "工资段", Size = 10000 },
                new ExcelAggregateMeasure { Function = "count", Alias = "人数" });

            var rows = result.Rows.ToDictionary(r => r[0], r => r[1], StringComparer.Ordinal);
            Assert.AreEqual("2", rows["0~10000"], "桶是左闭右开，8000/9000 在第一个桶，10000 已进下一个。");
            Assert.AreEqual("1", rows["10000~20000"]);
            Assert.AreEqual("1", rows["20000~30000"]);
        }

        [TestMethod]
        public void DerivedGroupBy_Prefix_TakesRequestedCharacterCount()
        {
            var result = Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "姓名", Transform = "prefix", Alias = "前两字", Size = 2 },
                new ExcelAggregateMeasure { Function = "count", Alias = "人数" });

            CollectionAssert.Contains(result.Rows.Select(r => r[0]).ToArray(), "张三");
        }

        [TestMethod]
        public void DerivedGroupBy_FirstCharacter_StillDefaultsToSingleCharacter()
        {
            var result = Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "姓名", Transform = "firstCharacter" },
                new ExcelAggregateMeasure { Function = "count", Alias = "人数" });

            Assert.AreEqual("姓名首字符", result.Columns[0]);
            CollectionAssert.Contains(result.Rows.Select(r => r[0]).ToArray(), "张");
        }

        [TestMethod]
        public void DerivedGroupBy_DateTransformOnNonDateColumn_FailsWithActionableMessage()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "姓名", Transform = "month" },
                new ExcelAggregateMeasure { Function = "count", Alias = "人数" }));

            StringAssert.Contains(ex.Message, "日期格式列");
        }

        [TestMethod]
        public void DerivedGroupBy_UnknownTransform_ListsTheSupportedOnes()
        {
            var ex = Assert.ThrowsException<InvalidOperationException>(() => Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "生日", Transform = "weekOfYear" },
                new ExcelAggregateMeasure { Function = "count", Alias = "人数" }));

            StringAssert.Contains(ex.Message, "quarter");
            StringAssert.Contains(ex.Message, "numberBucket");
        }

        [TestMethod]
        public void HeaderInferenceCanUseTheFirstRowWithoutMisreportingTheSourceRowCount()
        {
            bool inferred;
            string reason;
            var hasHeader = ExcelAggregationEngine.ShouldTreatHeaderRow(
                new object[] { "Name", "Amount" },
                100,
                "auto",
                out inferred,
                out reason);

            Assert.IsTrue(hasHeader);
            Assert.IsTrue(inferred);
            StringAssert.Contains(reason, "自动规则");
            Assert.IsFalse(reason.Contains("仅一行"));
        }

        [TestMethod]
        public void DerivedGroupBy_NumberBucketWithoutSize_IsRejected()
        {
            Assert.ThrowsException<InvalidOperationException>(() => Run(
                BuildPeopleSnapshot(),
                new ExcelAggregateDerivedGroup { Column = "工资", Transform = "numberBucket" },
                new ExcelAggregateMeasure { Function = "count", Alias = "人数" }));
        }

        [TestMethod]
        public void AggregateFilters_SeparateNativeNumbersFromParseableTextNumbers()
        {
            var snapshot = new ExcelTabularSnapshot(
                new[] { "Revenue" },
                new List<object[]>
                {
                    new object[] { 10d },
                    new object[] { "20.5" },
                    new object[] { "not a number" },
                    new object[] { -2146826246 },
                    new object[] { null }
                },
                new[] { false });

            var nativeNumbers = AggregateRevenueByType(snapshot, "isNativeNumber");
            var textNumbers = AggregateRevenueByType(snapshot, "isTextNumber");

            Assert.AreEqual("10", nativeNumbers.Rows[0][0]);
            Assert.AreEqual("20.5", textNumbers.Rows[0][0]);
        }

        [TestMethod]
        public void AggregateTypeFilters_RejectUnexpectedValues()
        {
            var snapshot = new ExcelTabularSnapshot(
                new[] { "Revenue" },
                new List<object[]> { new object[] { 10d } },
                new[] { false });
            var request = new ExcelAggregateRequest();
            request.Measures.Add(new ExcelAggregateMeasure
            {
                Column = "Revenue",
                Function = "sum"
            });
            request.Filters.Add(new ExcelAggregateFilter
            {
                Column = "Revenue",
                Operator = "isNativeNumber",
                Value = "true"
            });

            var exception = Assert.ThrowsException<InvalidOperationException>(
                () => ExcelAggregationEngine.Aggregate(
                    snapshot,
                    request,
                    new ExcelAggregateResult()));

            StringAssert.Contains(exception.Message, "不接受 value");
        }

        [TestMethod]
        public void UnionAggregation_ComputesNonAdditiveMeasuresAcrossCombinedRowsAndGroupsBySource()
        {
            var snapshot = new ExcelTabularSnapshot(
                new[] { "Region", "Amount", "__source" },
                new List<object[]>
                {
                    new object[] { "North", 10d, "jan" },
                    new object[] { "South", 20d, "jan" },
                    new object[] { "North", 40d, "feb" },
                    new object[] { null, null, null }
                },
                new[] { false, false, false });
            var combinedRequest = new ExcelAggregateRequest();
            combinedRequest.Measures.Add(new ExcelAggregateMeasure
            {
                Column = "Amount",
                Function = "sum",
                Alias = "合计"
            });
            combinedRequest.Measures.Add(new ExcelAggregateMeasure
            {
                Column = "Amount",
                Function = "average",
                Alias = "平均"
            });
            var combinedResult = new ExcelAggregateResult();

            ExcelAggregationEngine.Aggregate(snapshot, combinedRequest, combinedResult);

            Assert.AreEqual("70", combinedResult.Rows[0][0]);
            Assert.AreEqual(70d / 3d, double.Parse(combinedResult.Rows[0][1], CultureInfo.InvariantCulture), 1e-10);
            Assert.AreEqual(1, combinedResult.SkippedRowCount);

            var bySourceRequest = new ExcelAggregateRequest();
            bySourceRequest.GroupBy.Add("__source");
            bySourceRequest.Measures.Add(new ExcelAggregateMeasure
            {
                Column = "Amount",
                Function = "sum"
            });
            var bySourceResult = new ExcelAggregateResult();

            ExcelAggregationEngine.Aggregate(snapshot, bySourceRequest, bySourceResult);

            var sums = bySourceResult.Rows.ToDictionary(row => row[0], row => row[1]);
            Assert.AreEqual("30", sums["jan"]);
            Assert.AreEqual("40", sums["feb"]);
        }

        [TestMethod]
        public void ExcelToolSchemasExposeQualityAndHiddenRowOperations()
        {
            using (var aggregateSchema = JsonDocument.Parse(
                new ExcelAggregateTool().Definition.InputSchemaJson))
            {
                var properties = aggregateSchema.RootElement.GetProperty("properties");
                var functionValues = properties
                    .GetProperty("measures")
                    .GetProperty("items")
                    .GetProperty("properties")
                    .GetProperty("function")
                    .GetProperty("enum")
                    .EnumerateArray()
                    .Select(value => value.GetString())
                    .ToArray();
                var filterOperators = properties
                    .GetProperty("filters")
                    .GetProperty("items")
                    .GetProperty("properties")
                    .GetProperty("operator")
                    .GetProperty("enum")
                    .EnumerateArray()
                    .Select(value => value.GetString())
                    .ToArray();

                CollectionAssert.Contains(functionValues, "errorCount");
                CollectionAssert.Contains(filterOperators, "isNativeNumber");
                CollectionAssert.Contains(filterOperators, "isTextNumber");
                Assert.AreEqual(
                    2,
                    properties.GetProperty("sources").GetProperty("minItems").GetInt32());
                StringAssert.Contains(
                    properties.GetProperty("sources").GetProperty("description").GetString(),
                    "__source");
                var sourceItems = properties.GetProperty("sources").GetProperty("items");
                Assert.AreEqual(
                    2,
                    sourceItems.GetProperty("oneOf").GetArrayLength());
                var lookupProperties = properties.GetProperty("lookups")
                    .GetProperty("items").GetProperty("properties");
                CollectionAssert.AreEqual(
                    new[] { "left", "inner" },
                    lookupProperties.GetProperty("join").GetProperty("enum")
                        .EnumerateArray().Select(value => value.GetString()).ToArray());
                CollectionAssert.AreEqual(
                    new[] { "error", "first" },
                    lookupProperties.GetProperty("onDuplicateKey").GetProperty("enum")
                        .EnumerateArray().Select(value => value.GetString()).ToArray());
            }

            var multiSourceRequest = ExcelToolJsonSerializer.Deserialize<ExcelAggregateRequest>(
                "{\"sources\":["
                + "{\"worksheetName\":\"Jan\",\"address\":\"A1:B4\",\"alias\":\"jan\"},"
                + "{\"worksheetName\":\"Feb\",\"address\":\"A1:B5\",\"alias\":\"feb\"}],"
                + "\"measures\":[{\"column\":\"Amount\",\"function\":\"sum\"}]}");
            Assert.AreEqual(2, multiSourceRequest.Sources.Count);
            Assert.AreEqual("feb", multiSourceRequest.Sources[1].Alias);
            var lookupRequest = ExcelToolJsonSerializer.Deserialize<ExcelAggregateRequest>(
                "{\"worksheetName\":\"Orders\",\"address\":\"A1:C10\","
                + "\"lookups\":[{\"source\":{\"worksheetName\":\"Customers\",\"address\":\"A1:B5\"},"
                + "\"key\":\"CustomerId\",\"foreignKey\":\"CustomerId\","
                + "\"columns\":[\"Region\"]}]}");
            Assert.AreEqual("left", lookupRequest.Lookups[0].Join);
            Assert.AreEqual("error", lookupRequest.Lookups[0].OnDuplicateKey);
            Assert.AreEqual("Region", lookupRequest.Lookups[0].Columns[0]);

            using (var rangeInfoSchema = JsonDocument.Parse(
                new ExcelGetRangeInfoTool().Definition.InputSchemaJson))
            {
                Assert.AreEqual(
                    "boolean",
                    rangeInfoSchema.RootElement
                        .GetProperty("properties")
                        .GetProperty("includeHiddenRows")
                        .GetProperty("type")
                        .GetString());
            }
        }

        private static ExcelAggregateResult AggregateRevenueByType(
            ExcelTabularSnapshot snapshot,
            string filterOperator)
        {
            var request = new ExcelAggregateRequest();
            request.Measures.Add(new ExcelAggregateMeasure
            {
                Column = "Revenue",
                Function = "sum",
                Alias = "Total"
            });
            request.Filters.Add(new ExcelAggregateFilter
            {
                Column = "Revenue",
                Operator = filterOperator
            });

            var result = new ExcelAggregateResult();
            ExcelAggregationEngine.Aggregate(snapshot, request, result);
            return result;
        }

        private static ExcelAggregateResult Run(
            ExcelTabularSnapshot snapshot,
            ExcelAggregateDerivedGroup derivedGroup,
            ExcelAggregateMeasure measure)
        {
            var request = new ExcelAggregateRequest();
            request.DerivedGroupBy.Add(derivedGroup);
            request.Measures.Add(measure);

            var result = new ExcelAggregateResult();
            ExcelAggregationEngine.Aggregate(snapshot, request, result);
            return result;
        }

        /// <summary>生日列以 OADate 序列号存放，并标记为日期格式列，与 Excel 的 Value2 读法一致。</summary>
        private static ExcelTabularSnapshot BuildPeopleSnapshot()
        {
            var rows = new List<object[]>
            {
                new object[] { "张三丰", new DateTime(1990, 3, 15).ToOADate(), 10000d },
                new object[] { "张三石", new DateTime(1990, 3, 28).ToOADate(), 20000d },
                new object[] { "李四", new DateTime(1985, 7, 1).ToOADate(), 8000d },
                new object[] { "王五", new DateTime(1990, 12, 31).ToOADate(), 9000d }
            };

            return new ExcelTabularSnapshot(
                new[] { "姓名", "生日", "工资" },
                rows,
                new[] { false, true, false });
        }
    }
}
