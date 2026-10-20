using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Mcp;

namespace OfficeCopilot.Agent.Tests.Mcp
{
    [TestClass]
    public class McpServerConfigParsingTests
    {
        [TestMethod]
        public void FromSettingsJson_BareCommandWithWorkingDirectory_KeepsCommandForPathLookup()
        {
            var workingDirectory = Path.Combine(Path.GetTempPath(), "noc-mcp-wd");
            var entry = ParseSingleStdioServer("npx", workingDirectory);

            Assert.AreEqual("npx", entry.Command);
            Assert.AreEqual(workingDirectory, entry.WorkingDirectory);
        }

        [TestMethod]
        public void FromSettingsJson_RelativePathCommand_ResolvesAgainstWorkingDirectory()
        {
            var workingDirectory = Path.Combine(Path.GetTempPath(), "noc-mcp-wd");
            var entry = ParseSingleStdioServer("bin/server.exe", workingDirectory);

            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(workingDirectory, "bin", "server.exe")),
                entry.Command);
            Assert.AreEqual(workingDirectory, entry.WorkingDirectory);
        }

        [TestMethod]
        public void FromSettingsJson_RootedCommand_IsNotChanged()
        {
            var workingDirectory = Path.Combine(Path.GetTempPath(), "noc-mcp-wd");
            var absolute = Path.Combine(Path.GetTempPath(), "tools", "server.exe");
            var entry = ParseSingleStdioServer(absolute, workingDirectory);

            Assert.AreEqual(absolute, entry.Command);
        }

        [TestMethod]
        public void FromSettingsJson_NoWorkingDirectory_LeavesCommandAndWorkingDirectoryEmpty()
        {
            var entry = ParseSingleStdioServer("bin/server.exe", null);

            Assert.AreEqual("bin/server.exe", entry.Command);
            Assert.IsTrue(string.IsNullOrEmpty(entry.WorkingDirectory));
        }

        [TestMethod]
        public void FromSettingsJson_ToolWithIsEnabledFalse_IsListedAsDisabled()
        {
            var json = "{\"McpServers\":[{\"Name\":\"srv\",\"IsEnabled\":true,\"Stdio\":{\"Command\":\"npx\"},"
                + "\"Tools\":[{\"Name\":\"write_file\",\"IsEnabled\":false},"
                + "{\"Name\":\"read_file\",\"IsEnabled\":true},{\"Name\":\"list_dir\"}]}]}";
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-parse-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, json);
                var entry = McpJsonConfig.FromSettingsJson(path).McpServers.Single().Value;

                CollectionAssert.AreEqual(new[] { "write_file" }, entry.DisabledTools);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void FromSettingsJson_InvalidServers_AreReportedAsSkippedWithReason()
        {
            var json = "{\"McpServers\":["
                + "{\"Name\":\"no-flag\",\"Stdio\":{\"Command\":\"npx\"}},"
                + "{\"Name\":\"bad-flag\",\"IsEnabled\":\"yes\",\"Stdio\":{\"Command\":\"npx\"}},"
                + "{\"IsEnabled\":true,\"Stdio\":{\"Command\":\"npx\"}},"
                + "{\"Name\":\"no-transport\",\"IsEnabled\":true},"
                + "{\"Name\":\"Dup\",\"IsEnabled\":true,\"Stdio\":{\"Command\":\"npx\"}},"
                + "{\"Name\":\"dup\",\"IsEnabled\":true,\"Stdio\":{\"Command\":\"npx\"}},"
                + "{\"Name\":\"off\",\"IsEnabled\":false,\"Stdio\":{\"Command\":\"npx\"}}"
                + "]}";
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-parse-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, json);
                var config = McpJsonConfig.FromSettingsJson(path);

                CollectionAssert.AreEqual(new[] { "Dup" }, config.McpServers.Keys.ToArray());
                CollectionAssert.AreEqual(
                    new[] { "no-flag", "bad-flag", "(未命名)", "no-transport", "dup" },
                    config.SkippedServers.Select(s => s.Name).ToArray());
                Assert.IsTrue(config.SkippedServers.All(s => !string.IsNullOrWhiteSpace(s.Reason)));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void FromSettingsJson_MissingFile_ReturnsEmptyConfig()
        {
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-missing-" + Guid.NewGuid().ToString("N") + ".json");

            var config = McpJsonConfig.FromSettingsJson(path);

            Assert.AreEqual(0, config.McpServers.Count);
            Assert.AreEqual(0, config.SkippedServers.Count);
        }

        [TestMethod]
        public void FromSettingsJson_MalformedJson_Throws()
        {
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-bad-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{ not json");

                var threw = false;
                try
                {
                    McpJsonConfig.FromSettingsJson(path);
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
        public void FromSettingsJson_HttpTransportMode_AcceptsNameAndNumericValues()
        {
            var json = "{\"McpServers\":["
                + "{\"Name\":\"sse\",\"IsEnabled\":true,\"Http\":{\"Endpoint\":\"http://x/sse\",\"TransportMode\":\"Sse\"}},"
                + "{\"Name\":\"stream\",\"IsEnabled\":true,\"Http\":{\"Endpoint\":\"http://x/mcp\",\"TransportMode\":\"streamablehttp\"}},"
                + "{\"Name\":\"num\",\"IsEnabled\":true,\"Http\":{\"Endpoint\":\"http://x/mcp\",\"TransportMode\":2}},"
                + "{\"Name\":\"absent\",\"IsEnabled\":true,\"Http\":{\"Endpoint\":\"http://x/mcp\"}}"
                + "]}";
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-parse-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, json);
                var config = McpJsonConfig.FromSettingsJson(path);

                Assert.AreEqual("Sse", config.McpServers["sse"].TransportMode);
                Assert.AreEqual("StreamableHttp", config.McpServers["stream"].TransportMode);
                Assert.AreEqual("StreamableHttp", config.McpServers["num"].TransportMode);
                Assert.AreEqual("AutoDetect", config.McpServers["absent"].TransportMode);
                Assert.AreEqual(0, config.SkippedServers.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void FromSettingsJson_UnknownHttpTransportMode_IsSkippedWithReason()
        {
            var json = "{\"McpServers\":[{\"Name\":\"bad\",\"IsEnabled\":true,"
                + "\"Http\":{\"Endpoint\":\"http://x/mcp\",\"TransportMode\":\"websocket\"}}]}";
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-parse-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, json);
                var config = McpJsonConfig.FromSettingsJson(path);

                Assert.AreEqual(0, config.McpServers.Count);
                CollectionAssert.AreEqual(new[] { "bad" }, config.SkippedServers.Select(s => s.Name).ToArray());
                Assert.IsFalse(string.IsNullOrWhiteSpace(config.SkippedServers[0].Reason));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void FromSettingsJson_LegacyPerToolRiskKeys_AreIgnored()
        {
            var json = "{\"McpServers\":[{\"Name\":\"srv\",\"IsEnabled\":true,\"Stdio\":{\"Command\":\"npx\"},"
                + "\"Tools\":[{\"Name\":\"wipe\",\"IsEnabled\":false,\"TimeoutSeconds\":45,\"RiskLevel\":\"Critical\","
                + "\"RequiresConfirmation\":\"no\",\"RequiresBackup\":true},"
                + "{\"Name\":\"plain\",\"IsEnabled\":true}]}]}";
            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-parse-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, json);
                var config = McpJsonConfig.FromSettingsJson(path);
                var entry = config.McpServers.Single().Value;

                Assert.AreEqual(0, config.SkippedServers.Count);
                CollectionAssert.AreEqual(new[] { "wipe" }, entry.DisabledTools);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static McpServerConfigEntry ParseSingleStdioServer(string command, string workingDirectory)
        {
            var wdJson = workingDirectory == null
                ? string.Empty
                : ",\"WorkingDirectory\":" + System.Text.Json.JsonSerializer.Serialize(workingDirectory);
            var commandJson = System.Text.Json.JsonSerializer.Serialize(command);
            var json = "{\"McpServers\":[{\"Name\":\"srv\",\"IsEnabled\":true,\"Stdio\":{\"Command\":"
                + commandJson + wdJson + "}}]}";

            var path = Path.Combine(Path.GetTempPath(), "noc-mcp-parse-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, json);
                var config = McpJsonConfig.FromSettingsJson(path);
                return config.McpServers.Single().Value;
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
