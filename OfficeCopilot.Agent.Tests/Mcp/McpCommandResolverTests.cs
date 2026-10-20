using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.Agent.Tests.Mcp
{
    [TestClass]
    public class McpCommandResolverTests
    {
        private string _root;
        private string _originalPath;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "mcp-resolver-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _originalPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", _root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable("PATH", _originalPath);
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        [TestMethod]
        public void Resolve_BareNameWithCmdShimOnPath_ReturnsFullPath()
        {
            var shim = Touch("npxfake.cmd");

            Assert.AreEqual(shim, McpCommandResolver.Resolve("npxfake", null));
        }

        [TestMethod]
        public void Resolve_BareNameWithExplicitExtensionOnPath_ReturnsFullPath()
        {
            var shim = Touch("npxfake.cmd");

            Assert.AreEqual(shim, McpCommandResolver.Resolve("npxfake.cmd", null));
        }

        [TestMethod]
        public void Resolve_BareNameWithOnlyExtensionlessFile_ReturnsOriginal()
        {
            Touch("plainfile");

            Assert.AreEqual("plainfile", McpCommandResolver.Resolve("plainfile", null));
        }

        [TestMethod]
        public void Resolve_BareNameNotFound_ReturnsOriginal()
        {
            Assert.AreEqual("missingtool", McpCommandResolver.Resolve("missingtool", null));
        }

        [TestMethod]
        public void Resolve_BareName_DoesNotSearchWorkingDirectory()
        {
            var wd = Path.Combine(_root, "server");
            Directory.CreateDirectory(wd);
            File.WriteAllText(Path.Combine(wd, "localonly.cmd"), string.Empty);

            Assert.AreEqual("localonly", McpCommandResolver.Resolve("localonly", wd));
        }

        [TestMethod]
        public void Resolve_RelativePathWithWorkingDirectory_CombinesWithWorkingDirectory()
        {
            var wd = Path.Combine(_root, "server");
            var exe = Path.Combine(wd, "bin", "srv.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(exe));
            File.WriteAllText(exe, string.Empty);

            Assert.AreEqual(exe, McpCommandResolver.Resolve(Path.Combine("bin", "srv.exe"), wd));
        }

        [TestMethod]
        public void Resolve_EmptyCommand_ReturnsInput()
        {
            Assert.AreEqual(string.Empty, McpCommandResolver.Resolve(string.Empty, null));
        }

        private string Touch(string fileName)
        {
            var path = Path.Combine(_root, fileName);
            File.WriteAllText(path, string.Empty);
            return path;
        }
    }
}
