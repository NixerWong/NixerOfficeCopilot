using System.Linq;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Tools;

namespace OfficeCopilot.Agent.Tests.Excel
{
    /// <summary>
    /// DataContractJsonSerializer 不走构造函数，缺省值只能靠 [OnDeserializing]。
    /// 漏掉一次就会让"未提供"和"提供了 0"无法区分，把守恒断言默默变成永假。
    /// </summary>
    [TestClass]
    public class ExcelVbaExpectationContractTests
    {
        [TestMethod]
        public void Expectation_WithoutConserve_DeserializesToEmptyListNotNull()
        {
            var expect = Deserialize<ExcelVbaExpectation>("{\"columns\":[\"部门\"]}");

            Assert.IsNotNull(expect.Conserve);
            Assert.AreEqual(0, expect.Conserve.Count);
            Assert.AreEqual(-1, expect.MinRows, "未提供的 minRows 必须是 -1，不能被当成 0。");
            Assert.AreEqual(-1, expect.MaxRows);
        }

        [TestMethod]
        public void ConserveRule_OmittedExpectedValue_StaysNullSoZeroIsDistinguishable()
        {
            var withoutValue = Deserialize<ExcelVbaConserveRule>("{\"function\":\"sum\",\"column\":\"金额\"}");
            Assert.IsFalse(withoutValue.ExpectedValue.HasValue, "没给 expectedValue 时不能退化成 0。");

            var withZero = Deserialize<ExcelVbaConserveRule>("{\"function\":\"sum\",\"expectedValue\":0}");
            Assert.IsTrue(withZero.ExpectedValue.HasValue);
            Assert.AreEqual(0d, withZero.ExpectedValue.Value);
        }

        [TestMethod]
        public void ConserveRule_ParsesCompareToAndTolerance()
        {
            var rule = Deserialize<ExcelVbaConserveRule>(
                "{\"function\":\"sum\",\"column\":\"金额合计\",\"tolerance\":0.01,"
                + "\"compareTo\":{\"worksheetName\":\"销售明细\",\"column\":\"金额\"}}");

            Assert.AreEqual(0.01d, rule.Tolerance);
            Assert.IsNotNull(rule.CompareTo);
            Assert.AreEqual("销售明细", rule.CompareTo.WorksheetName);
            Assert.AreEqual("金额", rule.CompareTo.Column);
            Assert.AreEqual(string.Empty, rule.CompareTo.Function, "留空表示沿用规则本身的 function。");
        }

        [TestMethod]
        public void Expectation_ParsesVerificationBasisAndAssertions()
        {
            var expect = Deserialize<ExcelVbaExpectation>(
                "{\"basis\":{\"headerMode\":\"no\",\"topology\":\"lookup\","
                + "\"sources\":[{\"sheet\":\"订单\",\"range\":\"A1:C20\",\"alias\":\"orders\"},"
                + "{\"sheet\":\"客户\",\"range\":\"A1:B10\",\"alias\":\"customers\"}],"
                + "\"keys\":[\"客户号\"],\"join\":\"left\","
                + "\"filters\":[{\"column\":\"状态\",\"operator\":\"eq\",\"value\":\"完成\"}]},"
                + "\"assertions\":[{\"id\":\"total\",\"kind\":\"scalar\","
                + "\"target\":{\"column\":\"金额\",\"function\":\"sum\"},\"expectedValue\":10}]}"
            );

            Assert.IsNotNull(expect.Basis);
            Assert.AreEqual("no", expect.Basis.HeaderMode);
            Assert.AreEqual("lookup", expect.Basis.Topology);
            Assert.AreEqual(2, expect.Basis.Sources.Count);
            Assert.AreEqual("订单", expect.Basis.Sources[0].Sheet);
            Assert.AreEqual("A1:C20", expect.Basis.Sources[0].Range);
            Assert.AreEqual("orders", expect.Basis.Sources[0].Alias);
            Assert.AreEqual("客户号", expect.Basis.Keys[0]);
            Assert.AreEqual("left", expect.Basis.Join);
            Assert.AreEqual(1, expect.Basis.Filters.Count);
            Assert.AreEqual(1, expect.Assertions.Count);
            Assert.AreEqual("total", expect.Assertions[0].Id);
            Assert.AreEqual(10d, expect.Assertions[0].ExpectedValue.Value);
        }

        [TestMethod]
        public void ExecuteVbaToolSchema_IncludesAssertionDefinitionsAndParsesAsJson()
        {
            var definition = ExcelBuiltInToolCatalog.CreateDataTools();
            var vba = new ExcelExecuteVbaTool().Definition;

            using (var document = System.Text.Json.JsonDocument.Parse(vba.InputSchemaJson))
            {
                var expect = document.RootElement.GetProperty("properties").GetProperty("expect");
                Assert.IsTrue(expect.GetProperty("properties").TryGetProperty("assertions", out _));
                Assert.IsTrue(expect.GetProperty("properties").TryGetProperty("basis", out _));
                var definitions = document.RootElement.GetProperty("definitions");
                var derivedGroups = definitions.GetProperty("verificationBasis")
                    .GetProperty("properties").GetProperty("derivedGroupBy")
                    .GetProperty("items");
                Assert.IsTrue(derivedGroups.GetProperty("properties")
                    .TryGetProperty("transform", out _));
                var basisProperties = definitions.GetProperty("verificationBasis")
                    .GetProperty("properties");
                Assert.IsTrue(basisProperties.TryGetProperty("sources", out _));
                Assert.IsTrue(basisProperties.TryGetProperty("topology", out _));
                Assert.IsTrue(basisProperties.TryGetProperty("keys", out _));
                Assert.IsTrue(basisProperties.TryGetProperty("join", out _));
            }

            Assert.IsTrue(definition.Any(candidate => candidate.Definition.Name == "excel_assert"));
        }

        [TestMethod]
        public void VbaStageResultUsesCanonicalVerificationField()
        {
            var stage = new ExcelVbaStageResult
            {
                Verification = ExcelVerificationResult.FromReport(
                    new ExcelVerificationReport { Passed = true, Tier = "B" },
                    OfficeCopilot.HostBridge.Contracts.ToolResultVerificationFields.ProducerVba,
                    false,
                    new ExcelVerificationBasis())
            };
            var serializer = new DataContractJsonSerializer(typeof(ExcelVbaStageResult));
            string json;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, stage);
                json = Encoding.UTF8.GetString(stream.ToArray());
            }

            using (var document = System.Text.Json.JsonDocument.Parse(json))
            {
                Assert.IsTrue(document.RootElement.TryGetProperty("verification", out _));
                Assert.IsFalse(document.RootElement.TryGetProperty("verificationReport", out _));
            }
        }

        [TestMethod]
        public void EarlyVbaFailure_CreatesCanonicalFailedVerificationEnvelope()
        {
            var result = new ExcelVbaStageResult
            {
                Success = false,
                ExpectationsPassed = false,
                Message = "VBA execution failed."
            };
            result.ExpectationFailures.Add("VBA execution failed.");
            var expect = new ExcelVbaExpectation
            {
                Assertions =
                {
                    new ExcelAssertion { Id = "total", Kind = "scalar" }
                }
            };

            ExcelExecuteVbaTool.EnsureVerificationEnvelope(result, expect);

            Assert.IsNotNull(result.Verification);
            Assert.IsFalse(result.Verification.Passed);
            Assert.AreEqual("C", result.Verification.Tier);
            Assert.AreEqual("vba", result.Verification.Producer);
            Assert.AreEqual(1, result.Verification.AssertionCount);
            Assert.IsNotNull(result.Verification.Details);
            Assert.AreEqual("total", result.Verification.Details.Items[0].Id);
            Assert.IsFalse(result.Verification.Details.Items[0].Passed);
        }

        [TestMethod]
        public void MissingOutputProfile_CannotPassRequestedProfileExpectation()
        {
            var result = new ExcelVbaStageResult
            {
                Success = true,
                ExpectationsPassed = true
            };
            var expect = new ExcelVbaExpectation { MinRows = 1 };

            ExcelExecuteVbaTool.EnsureVerificationEnvelope(result, expect);

            Assert.IsFalse(result.ExpectationsPassed);
            Assert.IsNotNull(result.Verification);
            Assert.IsFalse(result.Verification.Passed);
            Assert.AreEqual(1, result.Verification.AssertionCount);
            Assert.AreEqual(
                "指定了 expect，但没有可供验证的输出表画像。",
                result.ExpectationFailures[0]);
        }

        [TestMethod]
        public void VbaSyntaxValidator_RejectsReservedKeywordUsedAsVariableName()
        {
            const string code = "Sub FindDiff()\r\n"
                + "    Dim total As Double, sub As Double\r\n"
                + "End Sub";

            var result = VbaSyntaxValidator.Validate(code);

            Assert.IsFalse(result.IsValid, result.ToDiagnosticMessage());
            Assert.IsTrue(
                VbaSyntaxValidator.Validate(
                    "Sub FindDiff()\r\n    Dim subTotal As Double\r\nEnd Sub").IsValid,
                "A non-keyword identifier containing 'sub' must remain valid.");
        }

        private static T Deserialize<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }
    }
}
