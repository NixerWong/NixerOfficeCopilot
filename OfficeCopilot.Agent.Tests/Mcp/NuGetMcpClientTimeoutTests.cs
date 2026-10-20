using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Mcp;

namespace OfficeCopilot.Agent.Tests.Mcp
{
    [TestClass]
    public class NuGetMcpClientTimeoutTests
    {
        [TestMethod]
        public async Task ConnectAsync_ServerExitsImmediately_FailsAndStaysDisconnected()
        {
            var config = new McpServerConfigEntry
            {
                Transport = "stdio",
                Command = "cmd",
                Args = new List<string> { "/c", "exit 1" }
            };
            var client = new NuGetMcpClient("exited", config);

            Exception thrown = null;
            try
            {
                await client.ConnectAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }

            Assert.IsNotNull(thrown, "Connect should fail when the server process exits immediately.");
            Assert.IsInstanceOfType(thrown, typeof(ModelContextProtocol.Client.ClientTransportClosedException),
                "Failure should come from the closed transport, not the timeout: " + thrown);
            Assert.IsFalse(client.IsConnected);
        }
    }
}
