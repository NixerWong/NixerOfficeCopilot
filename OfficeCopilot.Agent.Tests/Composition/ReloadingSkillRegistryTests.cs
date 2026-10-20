using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Runtime.Composition;
using OfficeCopilot.Skills;
using OfficeCopilot.Agent.Planning;

namespace OfficeCopilot.Agent.Tests.Composition
{
    [TestClass]
    public class ReloadingSkillRegistryTests
    {
        private string _root;
        private int _loadCount;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "reloading-skills-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _loadCount = 0;
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private void WriteSkill(string name, bool enabled)
        {
            var dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "SKILL.md"),
                "---\nname: " + name + "\ndescription: " + name + "\nenabled: " + (enabled ? "true" : "false") + "\n---\nBody\n");
        }

        private ReloadingSkillRegistry CreateRegistry()
        {
            return new ReloadingSkillRegistry(
                () =>
                {
                    _loadCount++;
                    var parsed = new FileSystemSkillLoader(_root).Load();
                    return new InMemorySkillRegistry(parsed.Select(p => p.ToSkillDefinition()));
                },
                _root);
        }

        [TestMethod]
        public void Lookup_WithoutDiskChange_DoesNotReload()
        {
            WriteSkill("demo", enabled: true);
            var registry = CreateRegistry();

            registry.FindByName("demo");
            registry.GetSkills();
            registry.FindByName("demo");

            Assert.AreEqual(1, _loadCount);
        }

        [TestMethod]
        public void Lookup_AfterSkillDisabledOnDisk_ReturnsDisabledWithoutRestart()
        {
            WriteSkill("demo", enabled: true);
            var registry = CreateRegistry();
            Assert.IsTrue(registry.FindByName("demo").IsEnabled);

            WriteSkill("demo", enabled: false);

            Assert.IsFalse(registry.FindByName("demo").IsEnabled);
            Assert.AreEqual(2, _loadCount);
        }

        [TestMethod]
        public void Lookup_AfterSkillAddedOnDisk_ReturnsNewSkill()
        {
            WriteSkill("first", enabled: true);
            var registry = CreateRegistry();
            Assert.IsNull(registry.FindByName("second"));

            WriteSkill("second", enabled: true);

            Assert.IsNotNull(registry.FindByName("second"));
        }
    }
}
