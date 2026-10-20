using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Excel.Analysis;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelWipSheetRegistryStoreTests
    {
        [TestMethod]
        public void RegistryRoundTripsAndIsScopedToConversationAndWorkbook()
        {
            var path = CreateTemporaryPath();
            try
            {
                var store = new ExcelWipSheetRegistryStore(path);
                var worksheets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "_wip_01_summary", "Sheet7" }
                };
                store.Save("conversation-a", @"C:\Books\Sales.xlsx", worksheets);
                store.Save(
                    "conversation-b",
                    @"C:\Books\Sales.xlsx",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "_wip_02_summary", "Sheet8" }
                    });

                var loaded = new ExcelWipSheetRegistryStore(path)
                    .Load("CONVERSATION-A", @"c:\books\sales.xlsx");

                Assert.IsNotNull(loaded);
                Assert.AreEqual("Sheet7", loaded["_wip_01_summary"]);
                Assert.IsNull(store.Load("conversation-a", @"C:\Books\Other.xlsx"));

                store.Save(
                    "conversation-a",
                    @"C:\Books\Sales.xlsx",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

                Assert.IsNull(store.Load("conversation-a", @"C:\Books\Sales.xlsx"));
                Assert.AreEqual(
                    "Sheet8",
                    store.Load("conversation-b", @"C:\Books\Sales.xlsx")["_wip_02_summary"]);
            }
            finally
            {
                DeleteTemporaryRegistryFiles(path);
            }
        }

        [TestMethod]
        public void CorruptRegistryFailsExplicitly()
        {
            var path = CreateTemporaryPath();
            try
            {
                File.WriteAllText(path, "{ invalid json");

                Assert.ThrowsException<InvalidDataException>(
                    () => new ExcelWipSheetRegistryStore(path)
                        .Load("conversation-a", @"C:\Books\Sales.xlsx"));
            }
            finally
            {
                DeleteTemporaryRegistryFiles(path);
            }
        }

        [TestMethod]
        public void RegistryRoundTripsPersistentWorksheetIdentity()
        {
            var path = CreateTemporaryPath();
            try
            {
                var marker = ExcelStagingConventions.CreateWipWorksheetIdentityMarker();
                string identity;
                Assert.IsTrue(ExcelStagingConventions.TryFormatWipWorksheetIdentity(
                    marker,
                    out identity));

                var store = new ExcelWipSheetRegistryStore(path);
                store.Save(
                    "conversation-a",
                    @"C:\Books\Sales.xlsx",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "_wip_01_summary", identity }
                    });

                var loaded = store.Load("conversation-a", @"C:\Books\Sales.xlsx");

                Assert.IsNotNull(loaded);
                Assert.AreEqual(identity, loaded["_wip_01_summary"]);
            }
            finally
            {
                DeleteTemporaryRegistryFiles(path);
            }
        }

        private static string CreateTemporaryPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "excel-wip-registry-" + Guid.NewGuid().ToString("N") + ".json");
        }

        private static void DeleteTemporaryRegistryFiles(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var lockPath = path + ".lock";
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
            }

            foreach (var temporaryPath in Directory.GetFiles(
                Path.GetDirectoryName(path),
                Path.GetFileName(path) + ".*.tmp"))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
