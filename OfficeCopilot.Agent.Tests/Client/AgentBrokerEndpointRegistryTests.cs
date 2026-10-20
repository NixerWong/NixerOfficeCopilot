using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;
using System;
using System.Diagnostics;
using System.Linq;

namespace OfficeCopilot.Agent.Tests.Client
{
    [TestClass]
    public sealed class AgentBrokerEndpointRegistryTests
    {
        [TestMethod]
        public void RegisterPublishesEndpointAndDisposeRemovesIt()
        {
            var pipeName = "agent-broker-registry-test-" + Guid.NewGuid().ToString("N");
            AgentBrokerEndpointRegistration registration = null;
            try
            {
                registration = AgentBrokerEndpointRegistry.Register(pipeName, ".");
                var endpoints = AgentBrokerEndpointRegistry.GetForProcess(Process.GetCurrentProcess().Id);

                Assert.IsTrue(endpoints.Any(endpoint =>
                    string.Equals(endpoint.PipeName, pipeName, StringComparison.Ordinal)));
            }
            finally
            {
                if (registration != null)
                {
                    registration.Dispose();
                }
            }

            var remainingEndpoints = AgentBrokerEndpointRegistry.GetForProcess(Process.GetCurrentProcess().Id);
            Assert.IsFalse(remainingEndpoints.Any(endpoint =>
                string.Equals(endpoint.PipeName, pipeName, StringComparison.Ordinal)));
        }
    }
}
