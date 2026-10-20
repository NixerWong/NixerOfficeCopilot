using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.PowerPoint;
using OfficeCopilot.Host.PowerPoint.Tools;

namespace OfficeCopilot.Agent.Tests.PowerPoint
{
    [TestClass]
    public sealed class PowerPointBackupStoreTests
    {
        private string _root;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(Path.GetTempPath(), "nixer-ppt-backup-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(_root), true);
            }
            catch (IOException)
            {
            }
        }

        [TestMethod]
        public void Folder_SavedAndUnsaved_AreStableAndDistinct()
        {
            var store = new PowerPointBackupStore(Path.Combine(_root, "Backups"));
            var saved = store.GetFolder(@"C:\docs\汇报.pptx", "汇报.pptx");
            var savedAgain = store.GetFolder(@"C:\docs\汇报.pptx", "汇报.pptx");
            var unsaved = store.GetFolder(string.Empty, "演示文稿1");

            Assert.AreEqual(saved, savedAgain);
            Assert.AreNotEqual(saved, unsaved);
            StringAssert.StartsWith(Path.GetFileName(saved), "汇报_");
            StringAssert.StartsWith(Path.GetFileName(unsaved), "演示文稿1_");
        }

        [TestMethod]
        public void NewBackupPath_UnsavedUsesPptxAndNeverCollides()
        {
            var store = new PowerPointBackupStore(Path.Combine(_root, "Backups"));
            var now = new DateTime(2025, 1, 2, 10, 11, 12);
            var first = store.GetNewBackupPath(string.Empty, "演示文稿1", now);
            Assert.AreEqual("101112.pptx", Path.GetFileName(first));

            Directory.CreateDirectory(Path.GetDirectoryName(first));
            File.WriteAllText(first, "x");
            var second = store.GetNewBackupPath(string.Empty, "演示文稿1", now);
            Assert.AreEqual("101112_1.pptx", Path.GetFileName(second));
        }

        [TestMethod]
        public void RestoredPath_SavedGoesBesideOriginal_UnsavedGoesToBackupFolder()
        {
            var store = new PowerPointBackupStore(Path.Combine(_root, "Backups"));
            var now = new DateTime(2025, 1, 2, 10, 11, 12);
            var original = Path.Combine(_root, "deck.pptm");
            var backup = store.GetNewBackupPath(original, "deck.pptm", now);

            var restored = store.GetRestoredPath(original, "deck.pptm", backup, now);
            Assert.AreEqual(Path.Combine(_root, "deck_还原_101112.pptm"), restored);

            var unsavedBackup = store.GetNewBackupPath(string.Empty, "演示文稿1", now);
            var unsavedRestored = store.GetRestoredPath(string.Empty, "演示文稿1", unsavedBackup, now);
            Assert.AreEqual(Path.GetDirectoryName(unsavedBackup), Path.GetDirectoryName(unsavedRestored));
            Assert.AreEqual("演示文稿1_还原_101112.pptx", Path.GetFileName(unsavedRestored));
        }

        [TestMethod]
        public void Index_RoundTripsUtf8AndFindsLatestExisting()
        {
            var store = new PowerPointBackupStore(Path.Combine(_root, "Backups"));
            var folder = store.GetFolder(string.Empty, "演示文稿1");
            Directory.CreateDirectory(folder);

            var older = Path.Combine(folder, "090000.pptx");
            var newer = Path.Combine(folder, "100000.pptx");
            File.WriteAllText(older, "x");

            store.AppendIndex(folder, new DocumentBackupResult { BackupPath = older, ToolName = "ppt_deck_build", UserIntent = "做一份汇报" });
            store.AppendIndex(folder, new DocumentBackupResult { BackupPath = newer, ToolName = "ppt_set_text" });
            File.AppendAllText(Path.Combine(folder, PowerPointBackupStore.IndexFileName), "{broken\n");

            var entries = store.ReadIndex(folder);
            Assert.AreEqual(2, entries.Count);
            Assert.AreEqual("做一份汇报", entries[0].UserIntent);
            Assert.IsTrue(entries[0].Exists);
            Assert.IsFalse(entries[1].Exists);

            // 最新条目的文件已不存在 → 回退到上一条。
            Assert.AreEqual(older, store.FindLatestExistingBackup(folder));

            var bytes = File.ReadAllBytes(Path.Combine(folder, PowerPointBackupStore.IndexFileName));
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "index.jsonl 不得带 BOM");
        }

        [TestMethod]
        public void IsUnderBackupsRoot_RejectsTraversalAndForeignPaths()
        {
            var root = Path.Combine(_root, "Backups");
            var store = new PowerPointBackupStore(root);

            Assert.IsTrue(store.IsUnderBackupsRoot(Path.Combine(root, "a_1234", "100000.pptx")));
            Assert.IsFalse(store.IsUnderBackupsRoot(Path.Combine(root, "..", "evil.pptx")));
            Assert.IsFalse(store.IsUnderBackupsRoot(root + "Evil\\x.pptx"));
            Assert.IsFalse(store.IsUnderBackupsRoot(@"C:\Windows\win.ini"));
            Assert.IsFalse(store.IsUnderBackupsRoot(string.Empty));
        }
    }

    [TestClass]
    public sealed class PowerPointToolCatalogTests
    {
        [TestMethod]
        public void Catalog_UsesPptPrefixAndGroups()
        {
            var definitions = PowerPointBuiltInTools.CreateAllAvailableDefinitions();
            var names = definitions.Select(d => d.Name).ToList();

            CollectionAssert.Contains(names, PowerPointToolNames.GetActivePresentation);
            CollectionAssert.Contains(names, PowerPointToolNames.GetSelection);
            CollectionAssert.Contains(names, PowerPointToolNames.ListSlides);
            CollectionAssert.Contains(names, PowerPointToolNames.ReadSlide);
            CollectionAssert.Contains(names, PowerPointToolNames.ListBackups);
            CollectionAssert.Contains(names, PowerPointToolNames.RestoreBackup);
            Assert.IsFalse(names.Any(n => n.StartsWith("powerpoint_", StringComparison.OrdinalIgnoreCase)));

            foreach (var definition in definitions)
            {
                string group;
                Assert.IsTrue(ToolCatalogBuilder.TryGetGroupName(definition, out group), definition.Name);
                StringAssert.StartsWith(group, "ppt.", definition.Name);
            }
        }

        [TestMethod]
        public void RestoreTool_RequiresConfirmationButNoBackup()
        {
            var restore = PowerPointBuiltInTools.CreateAllAvailableDefinitions()
                .Single(d => d.Name == PowerPointToolNames.RestoreBackup);

            Assert.IsFalse(restore.IsReadOnly);
            Assert.IsTrue(restore.RequiresConfirmation);
            Assert.IsFalse(restore.RequiresBackup);
        }

        [TestMethod]
        public void LegacyToolName_RoutesToNewTool()
        {
            var registry = new InMemoryToolRegistry();
            registry.RegisterPowerPointBuiltInTools();

            IAgentTool tool;
            Assert.IsTrue(registry.TryGetTool("powerpoint_get_selection", out tool));
            Assert.AreEqual(PowerPointToolNames.GetSelection, tool.Definition.Name);

            // 别名不出现在工具列表里。
            Assert.IsFalse(registry.GetDefinitions().Any(d => d.Name.StartsWith("powerpoint_", StringComparison.Ordinal)));
        }

        [TestMethod]
        public void RegistrationOptions_MigrateLegacySettingsAndKeepNewToolsOn()
        {
            Func<string, bool?> groups = g => g == "powerpoint.file" ? false : (bool?)null;
            Func<string, bool?> tools = t => t == "powerpoint_get_selection" ? false : (bool?)null;

            var options = PowerPointBuiltInTools.CreateRegistrationOptions(groups, tools);

            Assert.IsFalse(options.EnabledGroupNames.Contains(PowerPointToolGroups.File));
            Assert.IsTrue(options.EnabledGroupNames.Contains(PowerPointToolGroups.Context));
            Assert.IsTrue(options.EnabledGroupNames.Contains(PowerPointToolGroups.Read));
            Assert.IsFalse(options.EnabledToolNames.Contains(PowerPointToolNames.GetSelection));
            Assert.IsTrue(options.EnabledToolNames.Contains(PowerPointToolNames.ReadSlide));

            var configured = PowerPointBuiltInTools.CreateConfiguredTools(options).Select(t => t.Definition.Name).ToList();
            CollectionAssert.DoesNotContain(configured, PowerPointToolNames.GetSelection);
            CollectionAssert.Contains(configured, PowerPointToolNames.ListSlides);
            Assert.IsFalse(configured.Any(n => n.StartsWith("ppt_file_", StringComparison.Ordinal)));
        }
    }

    [TestClass]
    public sealed class ToolSafetyPolicyAffectsDocumentTests
    {
        [TestMethod]
        public void Normalize_WorkspaceOnlyWriteTool_DoesNotRequireBackup()
        {
            var definition = new ToolDefinition { Name = "ppt_deck_put_slides", IsReadOnly = false, AffectsDocument = false };
            ToolSafetyPolicy.Normalize(definition);
            Assert.IsFalse(definition.RequiresBackup);

            var write = new ToolDefinition { Name = "ppt_set_text", IsReadOnly = false };
            ToolSafetyPolicy.Normalize(write);
            Assert.IsTrue(write.RequiresBackup);
            Assert.IsTrue(write.Clone().AffectsDocument);
        }
    }
}
