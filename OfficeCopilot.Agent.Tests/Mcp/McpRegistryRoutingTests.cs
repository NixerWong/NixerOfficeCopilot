using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Mcp;

namespace OfficeCopilot.Agent.Tests.Mcp
{
    [TestClass]
    public class McpRegistryRoutingTests
    {
        [TestMethod]
        public async Task TryRoute_ServerNameWithDot_RoutesToRawServerAndToolName()
        {
            var client = new FakeMcpClient("my.server", new[] { Tool("read.file") });
            var registry = await LoadAsync(client);

            var catalogName = McpToolNaming.BuildFullToolName("my.server", "read.file");
            var routed = registry.TryRoute(catalogName, out var routedClient, out var realToolName);

            Assert.IsTrue(routed);
            Assert.AreSame(client, routedClient);
            Assert.AreEqual("read.file", realToolName);
        }

        [TestMethod]
        public async Task TryRoute_ToolNameContainingDoubleUnderscore_KeepsFullToolName()
        {
            var client = new FakeMcpClient("fs", new[] { Tool("a__b") });
            var registry = await LoadAsync(client);

            var catalogName = McpToolNaming.BuildFullToolName("fs", "a__b");
            var routed = registry.TryRoute(catalogName, out _, out var realToolName);

            Assert.IsTrue(routed);
            Assert.AreEqual("a__b", realToolName);
        }

        [TestMethod]
        public async Task TryRoute_UnknownToolName_ReturnsFalse()
        {
            var client = new FakeMcpClient("fs", new[] { Tool("read_file") });
            var registry = await LoadAsync(client);

            Assert.IsFalse(registry.TryRoute("mcp__fs__missing", out _, out _));
            Assert.IsFalse(registry.TryRoute("mcp__other__read_file", out _, out _));
            Assert.IsFalse(registry.TryRoute("runtime.powershell", out _, out _));
        }

        [TestMethod]
        public async Task TryRoute_DisconnectedClient_ReturnsFalse()
        {
            var client = new FakeMcpClient("fs", new[] { Tool("read_file") });
            var registry = await LoadAsync(client);
            client.Connected = false;

            Assert.IsFalse(registry.TryRoute("mcp__fs__read_file", out _, out _));
        }

        [TestMethod]
        public async Task TryRoute_EveryCatalogToolName_RoutesBack()
        {
            var client = new FakeMcpClient("my server", new[] { Tool("get.items"), Tool("set-value"), Tool("a__b") });
            var registry = await LoadAsync(client);

            var catalog = await registry.ListAllToolsAsync(CancellationToken.None);

            Assert.AreEqual(3, catalog.Count);
            var realNames = new List<string>();
            foreach (var descriptor in catalog)
            {
                Assert.IsTrue(registry.TryRoute(descriptor.Name, out _, out var realName), descriptor.Name);
                realNames.Add(realName);
            }

            CollectionAssert.AreEqual(new[] { "a__b", "get.items", "set-value" }, realNames.OrderBy(n => n).ToArray());
        }

        [TestMethod]
        public async Task Refresh_UnchangedConfig_DoesNotReconnectServer()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read_file") });
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "fs.exe"));
            try
            {
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                Assert.AreEqual(1, factory.Created.Count);
                Assert.IsTrue(registry.TryRoute("mcp__fs__read_file", out _, out _));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Refresh_ClientSessionEnded_StopsRoutingAndReconnectsOnNextRefresh()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read_file") });
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "fs.exe"));
            try
            {
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                var first = factory.Created[0];

                first.Connected = false;

                Assert.IsFalse(registry.TryRoute("mcp__fs__read_file", out _, out _));
                var exited = registry.GetServerStates().Single(s => s.ServerName == "fs");
                Assert.AreEqual(McpServerConnectionState.Disconnected, exited.State);

                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                Assert.AreEqual(2, factory.Created.Count);
                Assert.IsTrue(first.Disposed);
                Assert.IsTrue(registry.TryRoute("mcp__fs__read_file", out var routed, out _));
                Assert.AreNotSame(first, routed);
                Assert.AreEqual(McpServerConnectionState.Connected, registry.GetServerStates().Single(s => s.ServerName == "fs").State);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Refresh_FailedConnect_IsNotRetriedWithUnchangedConfig()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read_file") });
            factory.ConnectFailure = new InvalidOperationException("boom");
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "fs.exe"));
            try
            {
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                Assert.AreEqual(1, factory.Created.Count);
                Assert.AreEqual(McpServerConnectionState.Failed, registry.GetServerStates().Single(s => s.ServerName == "fs").State);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Refresh_ChangedCommand_ReplacesClientAndDisposesOld()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read_file") });
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "fs-v1.exe"));
            try
            {
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                var first = factory.Created[0];

                File.WriteAllText(path, SettingsJson(("fs", "fs-v2.exe")));
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                Assert.AreEqual(2, factory.Created.Count);
                Assert.IsTrue(first.Disposed);
                Assert.IsTrue(registry.TryRoute("mcp__fs__read_file", out var routed, out _));
                Assert.AreNotSame(first, routed);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Refresh_RemovedServer_DropsRoutesAndDisposesClient()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read_file") });
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "fs.exe"), ("git", "git.exe"));
            try
            {
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                Assert.IsTrue(registry.TryRoute("mcp__git__read_file", out _, out _));

                File.WriteAllText(path, SettingsJson(("fs", "fs.exe")));
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                Assert.IsFalse(registry.TryRoute("mcp__git__read_file", out _, out _));
                Assert.IsTrue(registry.TryRoute("mcp__fs__read_file", out _, out _));
                var removed = factory.Created.Single(c => c.ServerName == "git");
                Assert.IsTrue(removed.Disposed);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Refresh_MalformedJson_KeepsExistingConnections()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read_file") });
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "fs.exe"));
            try
            {
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                File.WriteAllText(path, "{ \"McpServers\": [ ");
                Exception caught = null;
                try
                {
                    await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }

                Assert.IsInstanceOfType(caught, typeof(JsonException));
                Assert.IsTrue(registry.TryRoute("mcp__fs__read_file", out _, out _));
                Assert.IsFalse(factory.Created[0].Disposed);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Refresh_MissingFile_KeepsExistingConnections()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read_file") });
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "fs.exe"));

            await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
            File.Delete(path);
            await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

            Assert.IsTrue(registry.TryRoute("mcp__fs__read_file", out _, out _));
            Assert.IsFalse(factory.Created[0].Disposed);
        }

        [TestMethod]
        public async Task DisabledTool_IsHiddenFromCatalogAndNotRoutable()
        {
            var client = new FakeMcpClient("fs", new[] { Tool("read_file"), Tool("write_file") });
            var registry = new McpRegistry(new FakeMcpClientFactory(new[] { client }));

            var config = new McpJsonConfig();
            config.McpServers["fs"] = new McpServerConfigEntry { DisabledTools = new List<string> { "write_file" } };
            await registry.LoadAsync(config, CancellationToken.None);

            var catalog = await registry.ListAllToolsAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "mcp__fs__read_file" }, catalog.Select(t => t.Name).ToArray());
            Assert.IsFalse(registry.TryRoute("mcp__fs__write_file", out _, out _));
            Assert.IsTrue(registry.TryRoute("mcp__fs__read_file", out _, out _));
        }

        [TestMethod]
        public async Task Refresh_ToolDisabledInSettings_DropsOnlyThatRoute()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read_file"), Tool("write_file") });
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "fs.exe"));
            try
            {
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                Assert.IsTrue(registry.TryRoute("mcp__fs__write_file", out _, out _));

                File.WriteAllText(path,
                    "{\"McpServers\":[{\"Name\":\"fs\",\"IsEnabled\":true,\"Stdio\":{\"Command\":\"fs.exe\"},"
                    + "\"Tools\":[{\"Name\":\"write_file\",\"IsEnabled\":false}]}]}");
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                Assert.IsFalse(registry.TryRoute("mcp__fs__write_file", out _, out _));
                Assert.IsTrue(registry.TryRoute("mcp__fs__read_file", out _, out _));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Refresh_LegacyToolTimeoutKey_IsIgnoredAndToolStillListed()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("plain") });
            var registry = new McpRegistry(factory);
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-policy-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, LegacyToolTimeoutSettingsJson(30));
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                var descriptor = (await registry.ListAllToolsAsync(CancellationToken.None)).Single();
                Assert.AreEqual(McpToolNaming.BuildFullToolName("srv", "plain"), descriptor.Name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task NormalizedNameCollision_KeepsFirstRouteAndReportsConflict()
        {
            // "my.server" 与 "my_server" 归一化后相同，工具全名均为 mcp__my_server__read。
            var first = new FakeMcpClient("my.server", new[] { Tool("read") });
            var second = new FakeMcpClient("my_server", new[] { Tool("read") });
            var registry = await LoadAsync(first, second);

            Assert.IsTrue(registry.TryRoute("mcp__my_server__read", out var routed, out _));
            Assert.AreSame(first, routed);

            var catalog = await registry.ListAllToolsAsync(CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "mcp__my_server__read" }, catalog.Select(t => t.Name).ToArray());

            var secondState = registry.GetServerStates().Single(s => s.ServerName == "my_server");
            StringAssert.Contains(secondState.ErrorMessage, "read");
            Assert.IsNull(registry.GetServerStates().Single(s => s.ServerName == "my.server").ErrorMessage);
        }

        [TestMethod]
        public async Task ServerNamesDifferingOnlyInCase_ConnectOnlyFirst()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read") });
            var registry = new McpRegistry(factory);

            var config = new McpJsonConfig();
            config.McpServers["fs"] = new McpServerConfigEntry();
            config.McpServers["FS"] = new McpServerConfigEntry();
            await registry.LoadAsync(config, CancellationToken.None);

            Assert.AreEqual(1, factory.Created.Count);
            Assert.AreEqual("fs", factory.Created[0].ServerName);
            Assert.AreEqual(1, registry.GetServerStates().Count);
        }

        [TestMethod]
        public async Task ConnectFailure_MarksServerFailedWithErrorMessageAndDisposesClient()
        {
            var client = new FakeMcpClient("broken", new[] { Tool("read") })
            {
                ConnectFailure = new InvalidOperationException("子进程提前退出")
            };
            var registry = await LoadAsync(client);

            var state = registry.GetServerStates().Single(s => s.ServerName == "broken");
            Assert.AreEqual(McpServerConnectionState.Failed, state.State);
            StringAssert.Contains(state.ErrorMessage, "子进程提前退出");
            Assert.IsTrue(client.Disposed);
            Assert.AreEqual(0, (await registry.ListAllToolsAsync(CancellationToken.None)).Count);
        }

        [TestMethod]
        public async Task LoadAsync_SettingsPath_ConnectsServersAndRoutesTools()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read") });
            var registry = new McpRegistry(factory);
            var path = WriteSettings(("fs", "npx"));
            try
            {
                await registry.LoadAsync(path, CancellationToken.None);

                Assert.AreEqual(1, factory.Created.Count);
                Assert.IsTrue(registry.TryRoute("mcp__fs__read", out _, out var realTool));
                Assert.AreEqual("read", realTool);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task LoadAsync_MalformedSettingsPath_ThrowsInsteadOfSilentlyEmpty()
        {
            var registry = new McpRegistry(new CountingMcpClientFactory(new[] { Tool("read") }));
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-bad-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{ not json");

                var threw = false;
                try
                {
                    await registry.LoadAsync(path, CancellationToken.None);
                }
                catch (System.Text.Json.JsonException)
                {
                    threw = true;
                }
                Assert.IsTrue(threw, "坏 JSON 应当抛出，而不是静默返回空配置。");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task InvalidServerEntry_ShowsAsFailedStateWithReasonAndIsNotRoutable()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read") });
            var registry = new McpRegistry(factory);
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-invalid-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path,
                    "{\"McpServers\":[{\"Name\":\"broken\",\"Stdio\":{\"Command\":\"npx\"}}]}");

                await registry.LoadAsync(path, CancellationToken.None);

                var state = registry.GetServerStates().Single(s => s.ServerName == "broken");
                Assert.AreEqual(McpServerConnectionState.Failed, state.State);
                StringAssert.Contains(state.ErrorMessage, "IsEnabled");
                Assert.AreEqual(0, factory.Created.Count);
                Assert.AreEqual(0, (await registry.ListAllToolsAsync(CancellationToken.None)).Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public async Task Refresh_InvalidEntryAppears_AndIsClearedWhenFixed()
        {
            var factory = new CountingMcpClientFactory(new[] { Tool("read") });
            var registry = new McpRegistry(factory);
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-refresh-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{\"McpServers\":[{\"Name\":\"srv\",\"Stdio\":{\"Command\":\"npx\"}}]}");
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);
                Assert.AreEqual(McpServerConnectionState.Failed,
                    registry.GetServerStates().Single(s => s.ServerName == "srv").State);

                File.WriteAllText(path, SettingsJson(("srv", "npx")));
                await registry.RefreshFromSettingsAsync(path, CancellationToken.None);

                Assert.AreEqual(1, registry.GetServerStates().Count);
                Assert.AreEqual(McpServerConnectionState.Connected, registry.GetServerStates()[0].State);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string WriteSettings(params (string Name, string Command)[] servers)
        {
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, SettingsJson(servers));
            return path;
        }

        private static string SettingsJson(params (string Name, string Command)[] servers)
        {
            var items = servers.Select(s =>
                "{\"Name\":\"" + s.Name + "\",\"IsEnabled\":true,\"Stdio\":{\"Command\":\"" + s.Command + "\"}}");
            return "{\"McpServers\":[" + string.Join(",", items) + "]}";
        }

        private static string LegacyToolTimeoutSettingsJson(int seconds)
        {
            return "{\"McpServers\":[{\"Name\":\"srv\",\"IsEnabled\":true,\"Stdio\":{\"Command\":\"srv.exe\"},"
                + "\"Tools\":[{\"Name\":\"slow\",\"IsEnabled\":true,\"TimeoutSeconds\":" + seconds + "}]}]}";
        }

        private static async Task<McpRegistry> LoadAsync(params FakeMcpClient[] clients)
        {
            var registry = new McpRegistry(new FakeMcpClientFactory(clients));

            var config = new McpJsonConfig();
            foreach (var client in clients)
            {
                config.McpServers[client.ServerName] = new McpServerConfigEntry();
            }

            await registry.LoadAsync(config, CancellationToken.None);
            return registry;
        }

        private static McpToolDescriptor Tool(string name)
        {
            return new McpToolDescriptor { Name = name, Description = name, InputSchemaJson = "{}" };
        }

        private sealed class FakeMcpClientFactory : IMcpClientFactory
        {
            private readonly Dictionary<string, FakeMcpClient> _clients;

            public FakeMcpClientFactory(IEnumerable<FakeMcpClient> clients)
            {
                _clients = clients.ToDictionary(c => c.ServerName, StringComparer.OrdinalIgnoreCase);
            }

            public bool SupportsTransport(string transport) => true;

            public IMcpClient CreateClient(string serverName, McpServerConfigEntry config) => _clients[serverName];
        }

        private sealed class FakeMcpClient : IMcpClient
        {
            private readonly IReadOnlyList<McpToolDescriptor> _tools;

            public FakeMcpClient(string serverName, IReadOnlyList<McpToolDescriptor> tools)
            {
                ServerName = serverName;
                _tools = tools;
            }

            public string ServerName { get; }

            public bool Connected { get; set; } = true;

            public bool Disposed { get; private set; }

            public McpServerConfigEntry Config { get; set; }

            public bool IsConnected => Connected;

            public Exception ConnectFailure { get; set; }

            public Task ConnectAsync(CancellationToken cancellationToken)
                => ConnectFailure != null ? Task.FromException(ConnectFailure) : Task.CompletedTask;

            public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken)
                => Task.FromResult(_tools);

            public Task<McpToolCallResult> CallToolAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                return default;
            }
        }

        private sealed class CountingMcpClientFactory : IMcpClientFactory
        {
            private readonly IReadOnlyList<McpToolDescriptor> _tools;

            public CountingMcpClientFactory(IReadOnlyList<McpToolDescriptor> tools)
            {
                _tools = tools;
            }

            public List<FakeMcpClient> Created { get; } = new List<FakeMcpClient>();

            public Exception ConnectFailure { get; set; }

            public bool SupportsTransport(string transport) => true;

            public IMcpClient CreateClient(string serverName, McpServerConfigEntry config)
            {
                var client = new FakeMcpClient(serverName, _tools) { Config = config, ConnectFailure = ConnectFailure };
                Created.Add(client);
                return client;
            }
        }
    }
}
