using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Agent.Tests.Harness;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.Turn
{
    [TestClass]
    public sealed class ToolArgumentValidatorTests
    {
        [TestMethod]
        public void Validate_AcceptsSupportedNestedSchema()
        {
            const string schema = "{\"type\":\"object\",\"required\":[\"category\",\"rows\",\"count\"],\"properties\":{"
                + "\"category\":{\"type\":\"string\",\"enum\":[\"sales\",\"returns\"]},"
                + "\"rows\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"required\":[\"amount\"],"
                + "\"properties\":{\"amount\":{\"type\":\"number\",\"minimum\":0,\"maximum\":100}}}},"
                + "\"count\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10}}}";
            const string arguments = "{\"category\":\"sales\",\"rows\":[{\"amount\":25.5}],\"count\":2}";

            var result = ToolArgumentValidator.Validate(schema, arguments);

            Assert.IsTrue(result.SchemaValid, result.ErrorMessage);
            Assert.IsTrue(result.IsValid, result.ErrorMessage);
            Assert.AreEqual(string.Empty, result.ErrorMessage);
        }

        [TestMethod]
        public void Validate_AcceptsBooleanAndNullTypes()
        {
            var result = ToolArgumentValidator.Validate(
                "{\"type\":\"object\",\"properties\":{\"enabled\":{\"type\":\"boolean\"},\"value\":{\"type\":\"null\"}}}",
                "{\"enabled\":true,\"value\":null}");

            Assert.IsTrue(result.SchemaValid, result.ErrorMessage);
            Assert.IsTrue(result.IsValid, result.ErrorMessage);
        }

        [TestMethod]
        public void Validate_ReportsNestedTypeMismatchPath()
        {
            const string schema = "{\"type\":\"object\",\"properties\":{\"rows\":{\"type\":\"array\",\"items\":{"
                + "\"type\":\"object\",\"properties\":{\"amount\":{\"type\":\"number\"}}}}}}";

            var result = ToolArgumentValidator.Validate(schema, "{\"rows\":[{\"amount\":\"25\"}]}");

            Assert.IsTrue(result.SchemaValid);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains(result.ErrorMessage, "$.rows[0].amount");
            StringAssert.Contains(result.ErrorMessage, "expected type 'number'");
            StringAssert.Contains(result.ErrorMessage, "got 'string'");
        }

        [TestMethod]
        public void Validate_ReportsMissingRequiredProperty()
        {
            var result = ToolArgumentValidator.Validate(
                "{\"type\":\"object\",\"required\":[\"code\"]}",
                "{}");

            Assert.IsTrue(result.SchemaValid);
            Assert.IsFalse(result.IsValid);
            StringAssert.Contains(result.ErrorMessage, "$.code");
            StringAssert.Contains(result.ErrorMessage, "got missing");
        }

        [TestMethod]
        public void Validate_ReportsEnumAndNumericBoundViolations()
        {
            var enumResult = ToolArgumentValidator.Validate(
                "{\"type\":\"object\",\"properties\":{\"mode\":{\"type\":\"string\",\"enum\":[\"sum\",\"count\"]}}}",
                "{\"mode\":\"average\"}");
            var minimumResult = ToolArgumentValidator.Validate(
                "{\"type\":\"object\",\"properties\":{\"limit\":{\"type\":\"number\",\"minimum\":1}}}",
                "{\"limit\":0}");
            var maximumResult = ToolArgumentValidator.Validate(
                "{\"type\":\"object\",\"properties\":{\"limit\":{\"type\":\"number\",\"maximum\":10}}}",
                "{\"limit\":11}");
            var integerResult = ToolArgumentValidator.Validate(
                "{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\"}}}",
                "{\"count\":1.5}");
            var preciseEnumResult = ToolArgumentValidator.Validate(
                "{\"type\":\"object\",\"properties\":{\"value\":{\"enum\":[9007199254740992]}}}",
                "{\"value\":9007199254740993}");

            Assert.IsFalse(enumResult.IsValid);
            StringAssert.Contains(enumResult.ErrorMessage, "$.mode");
            StringAssert.Contains(enumResult.ErrorMessage, "enum");
            Assert.IsFalse(minimumResult.IsValid);
            StringAssert.Contains(minimumResult.ErrorMessage, ">= 1");
            Assert.IsFalse(maximumResult.IsValid);
            StringAssert.Contains(maximumResult.ErrorMessage, "<= 10");
            Assert.IsFalse(integerResult.IsValid);
            Assert.IsFalse(preciseEnumResult.IsValid);
        }

        [TestMethod]
        public void Validate_SeparatesInvalidSchemaFromInvalidArguments()
        {
            var invalidSchema = ToolArgumentValidator.Validate("{", "{}");
            var invalidArguments = ToolArgumentValidator.Validate("{\"type\":\"object\"}", "{");

            Assert.IsFalse(invalidSchema.SchemaValid);
            Assert.IsFalse(invalidSchema.IsValid);
            StringAssert.Contains(invalidSchema.ErrorMessage, "schema JSON");
            Assert.IsTrue(invalidArguments.SchemaValid);
            Assert.IsFalse(invalidArguments.IsValid);
            StringAssert.Contains(invalidArguments.ErrorMessage, "valid JSON arguments");
        }

        [TestMethod]
        public async Task Dispatcher_ContinuesHostInvocationWhenArgumentsDoNotMatchSchema()
        {
            const string toolName = "schema_test_tool";
            var host = new FakeHostToolInvoker().Succeeds(toolName, "executed");
            var dispatcher = new DefaultToolDispatcher(host);
            var tool = new HostBridgeToolDescriptor
            {
                Name = toolName,
                Description = "Test schema validation",
                IsReadOnly = true,
                InputSchemaJson = "{\"type\":\"object\",\"required\":[\"code\"],\"properties\":{\"code\":{\"type\":\"string\"}}}"
            };
            var catalog = new Dictionary<string, HostBridgeToolDescriptor> { { toolName, tool } };

            var outcome = await dispatcher.DispatchToolCallAsync(
                "schema-call",
                toolName,
                "{\"code\":5}",
                new HostContextSnapshotDto(),
                catalog,
                null,
                CancellationToken.None);

            Assert.IsTrue(outcome.Success);
            Assert.AreEqual("executed", outcome.Content);
            Assert.AreEqual(1, host.CountOf(toolName));
        }
    }
}
