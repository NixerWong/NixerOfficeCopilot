using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.Mcp;

namespace OfficeCopilot.Agent.Tests.Mcp
{
    [TestClass]
    public class McpToolRiskInferenceTests
    {
        [TestMethod]
        public void InferMcpToolRiskLevel_CriticalNameFragment_ReturnsCritical()
        {
            Assert.AreEqual(ToolRiskLevels.Critical, DefaultToolDispatcher.InferMcpToolRiskLevel("mcp__srv__run_command"));
        }

        [TestMethod]
        public void InferMcpToolRiskLevel_HighNameFragment_ReturnsHigh()
        {
            Assert.AreEqual(ToolRiskLevels.High, DefaultToolDispatcher.InferMcpToolRiskLevel("mcp__srv__delete_file"));
        }

        [TestMethod]
        public void InferMcpToolRiskLevel_OtherName_ReturnsMedium()
        {
            Assert.AreEqual(ToolRiskLevels.Medium, DefaultToolDispatcher.InferMcpToolRiskLevel("mcp__srv__list_items"));
        }

        [TestMethod]
        public void InferMcpToolRiskLevel_NullOrEmptyName_ReturnsMedium()
        {
            Assert.AreEqual(ToolRiskLevels.Medium, DefaultToolDispatcher.InferMcpToolRiskLevel(null));
            Assert.AreEqual(ToolRiskLevels.Medium, DefaultToolDispatcher.InferMcpToolRiskLevel(string.Empty));
        }

        [TestMethod]
        public void CreateMcpToolDescriptor_ReadOnlyHint_NoConfirmationAndLowRisk()
        {
            var descriptor = DefaultToolDispatcher.CreateMcpToolDescriptor(new McpToolDescriptor
            {
                Name = "mcp__srv__delete_lookup",
                IsReadOnly = true
            });

            Assert.IsTrue(descriptor.IsReadOnly);
            Assert.IsFalse(descriptor.RequiresConfirmation);
            Assert.AreEqual(ToolRiskLevels.Low, descriptor.RiskLevel);
        }

        [TestMethod]
        public void CreateMcpToolDescriptor_NoReadOnlyHint_RequiresConfirmationWithInferredRisk()
        {
            var descriptor = DefaultToolDispatcher.CreateMcpToolDescriptor(new McpToolDescriptor
            {
                Name = "mcp__srv__delete_file",
                IsReadOnly = false
            });

            Assert.IsFalse(descriptor.IsReadOnly);
            Assert.IsTrue(descriptor.RequiresConfirmation);
            Assert.AreEqual(ToolRiskLevels.High, descriptor.RiskLevel);
        }
    }
}
