using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Agent.Tests.Excel
{
    /// <summary>
    /// D8 回归网：excel_get_range_style 曾经只回读命名样式，模型拿它"验证"标红却什么也验不到。
    /// 这里锁住两件事：颜色能被真正读出来，以及区域内不一致时必须显式说 mixed 而不是当成未设置。
    /// </summary>
    [TestClass]
    public class ExcelStyleReadbackTests
    {
        [TestMethod]
        public void FromOleColor_ConvertsBgrOleValueToRgbHex()
        {
            // Excel 的 OLE 色值是 BGR：红色 #FF0000 存成 0x0000FF = 255。
            Assert.AreEqual("#FF0000", ExcelStyleReadback.FromOleColor(255, 1));
        }

        [TestMethod]
        public void FromOleColor_TreatsXlNoneColorIndexAsNoFill()
        {
            // 无填充时 Interior.Color 仍返回白色 16777215，只有 ColorIndex 能区分。
            Assert.AreEqual("none", ExcelStyleReadback.FromOleColor(16777215, -4142));
        }

        [TestMethod]
        public void FromOleColor_ReportsMixedWhenExcelReturnsNull()
        {
            Assert.AreEqual("mixed", ExcelStyleReadback.FromOleColor(null, null));
        }

        [TestMethod]
        public void FromBoolean_ReportsMixedInsteadOfFalseWhenExcelReturnsNull()
        {
            // 关键区分：把 mixed 折叠成 false 会让模型断言"没有加粗"，而实际只是区域内不一致。
            Assert.AreEqual("mixed", ExcelStyleReadback.FromBoolean(null));
            Assert.AreEqual("true", ExcelStyleReadback.FromBoolean(true));
            Assert.AreEqual("false", ExcelStyleReadback.FromBoolean(false));
        }

        [TestMethod]
        public void FromUnderline_MapsXlUnderlineStyleNoneToFalse()
        {
            Assert.AreEqual("false", ExcelStyleReadback.FromUnderline(-4142));
            Assert.AreEqual("true", ExcelStyleReadback.FromUnderline(2));
            Assert.AreEqual("mixed", ExcelStyleReadback.FromUnderline(null));
        }

        [TestMethod]
        public void DescribeAlignment_MapsExcelEnumCodesToNames()
        {
            Assert.AreEqual("center", ExcelStyleReadback.DescribeHorizontalAlignment(-4108));
            Assert.AreEqual("right", ExcelStyleReadback.DescribeHorizontalAlignment(-4152));
            Assert.AreEqual("mixed", ExcelStyleReadback.DescribeHorizontalAlignment(null));
            Assert.AreEqual("top", ExcelStyleReadback.DescribeVerticalAlignment(-4160));
        }

        [TestMethod]
        public void SummarizeBorders_ListsOnlyEdgesThatActuallyHaveALine()
        {
            var edges = new List<object> { 1, -4142, 1, -4142 };
            Assert.AreEqual("left,bottom", ExcelStyleReadback.SummarizeBorders(edges));
        }

        [TestMethod]
        public void SummarizeBorders_ReportsNoneAndMixed()
        {
            Assert.AreEqual(
                "none",
                ExcelStyleReadback.SummarizeBorders(new List<object> { -4142, -4142, -4142, -4142 }));
            Assert.AreEqual(
                "mixed",
                ExcelStyleReadback.SummarizeBorders(new List<object> { null, null, null, null }));
            Assert.AreEqual(
                "left（top 不一致）",
                ExcelStyleReadback.SummarizeBorders(new List<object> { 1, null, -4142, -4142 }));
        }

        [TestMethod]
        public void CollectInconclusiveProperties_NamesEveryFieldTheModelMustNotConcludeFrom()
        {
            var values = new Dictionary<string, string>
            {
                { "fillColor", "#FF0000" },
                { "bold", "mixed" },
                { "numberFormat", "unknown" }
            };

            Assert.AreEqual("bold,numberFormat", ExcelStyleReadback.CollectInconclusiveProperties(values));
        }
    }
}
