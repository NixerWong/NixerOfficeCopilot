using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Skills;

namespace OfficeCopilot.Agent.Tests.Skills
{
    [TestClass]
    public class SkillStorageLocationsTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "skill-storage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [TestMethod]
        public void GetHostFolderName_MapsEachHostToItsOwnFolder()
        {
            Assert.AreEqual("excel", SkillStorageLocations.GetHostFolderName("Excel"));
            Assert.AreEqual("word", SkillStorageLocations.GetHostFolderName("Word"));
            Assert.AreEqual("powerpoint", SkillStorageLocations.GetHostFolderName("PowerPoint"));
            Assert.AreEqual("powerpoint", SkillStorageLocations.GetHostFolderName("ppt"));
        }

        [TestMethod]
        public void GetHostFolderName_UnknownOrEmptyHost_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, SkillStorageLocations.GetHostFolderName(null));
            Assert.AreEqual(string.Empty, SkillStorageLocations.GetHostFolderName(string.Empty));
            Assert.AreEqual(string.Empty, SkillStorageLocations.GetHostFolderName("Outlook"));
        }

        [TestMethod]
        public void GetHostDirectory_UnknownHost_ReturnsEmptyToFailClosed()
        {
            Assert.AreEqual(string.Empty, SkillStorageLocations.GetHostDirectory(string.Empty));
            Assert.AreEqual(string.Empty, SkillStorageLocations.GetHostDirectory("Outlook"));
        }

        [TestMethod]
        public void GetHostDirectory_KnownHost_IsSharedRootSubfolderNamedAfterHost()
        {
            var root = SkillStorageLocations.GetSharedRoot();
            var excelDir = SkillStorageLocations.GetHostDirectory("Excel");

            Assert.AreEqual(Path.Combine(root, "excel"), excelDir);
            Assert.AreEqual(Path.Combine(root, "word"), SkillStorageLocations.GetHostDirectory("Word"));
            Assert.AreEqual(Path.Combine(root, "powerpoint"), SkillStorageLocations.GetHostDirectory("PowerPoint"));
        }

        [TestMethod]
        public void Loader_EmptyRootDirectory_LoadsNothing()
        {
            var loaded = new FileSystemSkillLoader(string.Empty).Load();

            Assert.AreEqual(0, loaded.Count);
        }

        [TestMethod]
        public void Loader_HostFolder_StampsHostFromFolderOverridingDeclaredHost()
        {
            var excelDir = Path.Combine(_root, "excel");
            WriteSkill(excelDir, "tool-a", "host: word");

            var loaded = new FileSystemSkillLoader(excelDir, "excel").Load();

            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual("excel", loaded[0].Frontmatter.Metadata[SkillMetadataKeys.HostKind]);
        }

        [TestMethod]
        public void Loader_HostFolders_AreIsolatedFromEachOther()
        {
            WriteSkill(Path.Combine(_root, "excel"), "excel-only", null);
            WriteSkill(Path.Combine(_root, "word"), "word-only", null);

            var excelNames = new FileSystemSkillLoader(Path.Combine(_root, "excel"), "excel").Load()
                .Select(s => s.Frontmatter.Name).ToList();
            var wordNames = new FileSystemSkillLoader(Path.Combine(_root, "word"), "word").Load()
                .Select(s => s.Frontmatter.Name).ToList();

            CollectionAssert.AreEqual(new[] { "excel-only" }, excelNames);
            CollectionAssert.AreEqual(new[] { "word-only" }, wordNames);
        }

        private static void WriteSkill(string hostDir, string name, string extraFrontmatterLine)
        {
            var dir = Path.Combine(hostDir, name);
            Directory.CreateDirectory(dir);
            var lines = "---\nname: " + name + "\ndescription: " + name + "\n";
            if (extraFrontmatterLine != null)
            {
                lines += extraFrontmatterLine + "\n";
            }

            File.WriteAllText(Path.Combine(dir, "SKILL.md"), lines + "---\nBody\n");
        }
    }
}
