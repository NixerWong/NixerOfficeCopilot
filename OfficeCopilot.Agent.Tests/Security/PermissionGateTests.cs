using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Turn;

namespace OfficeCopilot.Agent.Tests.Security
{
    [TestClass]
    public class PermissionGateTests
    {
        [TestMethod]
        public void PlanMode_AllowsReadOnlyAndBlocksMutation()
        {
            var gate = new DefaultPermissionGate();
            gate.SetMode(PermissionMode.Plan);

            Assert.IsTrue(gate.CanExecute("excel_read_range", true));
            Assert.IsFalse(gate.CanExecute("excel_write_range", false));
            StringAssert.Contains(gate.GetBlockReason("excel_write_range", false), "Only read-only tools");
        }

        [TestMethod]
        public void PlanMode_AllowsPlanManagementTools()
        {
            var gate = new DefaultPermissionGate();
            gate.SetMode(PermissionMode.Plan);

            Assert.IsTrue(gate.CanExecute(EnterPlanModeTool.ToolName, false));
            Assert.IsTrue(gate.CanExecute(ExitPlanModeTool.ToolName, false));
        }

        [TestMethod]
        public void BypassMode_AllowsMutation()
        {
            var gate = new DefaultPermissionGate();
            gate.SetMode(PermissionMode.BypassPermissions);

            Assert.IsTrue(gate.CanExecute("excel_write_range", false));
            Assert.AreEqual(string.Empty, gate.GetBlockReason("excel_write_range", false));
        }
    }
}
