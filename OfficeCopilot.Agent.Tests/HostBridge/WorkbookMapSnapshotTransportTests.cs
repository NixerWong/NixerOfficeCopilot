using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.HostBridge.Contracts;
using OfficeCopilot.HostBridge.Server;

namespace OfficeCopilot.Agent.Tests.HostBridge
{
    [TestClass]
    public sealed class WorkbookMapSnapshotTransportTests
    {
        [TestMethod]
        public async Task GetContextSnapshotTransportsStructuredWorkbookMap()
        {
            var worksheetMap = new ExcelWorksheetListResult
            {
                WorkbookName = "sales.xlsx",
                TotalWorksheetCount = 2
            };
            worksheetMap.Worksheets.Add(new ExcelWorksheetInfo
            {
                Name = "January",
                CodeName = "Sheet1",
                Index = 1,
                IsVisible = true,
                UsedRangeAddress = "A1:C20",
                UsedRangeRows = 20,
                UsedRangeColumns = 3,
                HeaderSignature = "Order ID | Date | Revenue",
                HeaderColumns = new System.Collections.Generic.List<string>
                {
                    "Order ID",
                    "Date",
                    "Revenue"
                },
                RoleGuess = "data"
            });
            worksheetMap.WorksheetGroups.Add(new ExcelWorksheetHeaderGroup
            {
                GroupId = "header-group-1",
                MinimumHeaderSimilarity = 0.8m,
                WorksheetNames = new System.Collections.Generic.List<string>
                {
                    "January",
                    "February"
                }
            });
            worksheetMap.SharedKeyCandidates.Add(new ExcelSharedKeyCandidate
            {
                ColumnName = "Order ID",
                WorksheetNames = new System.Collections.Generic.List<string>
                {
                    "January",
                    "Customers"
                }
            });

            var adapter = new SnapshotHostAdapter(new HostContextSnapshot
            {
                WorkbookMap = worksheetMap,
                WorkbookMapSummary = "工作簿地图摘要"
            });
            var handler = new DefaultHostBridgeRequestHandler(
                adapter,
                new InMemoryToolRegistry(new IAgentTool[0]));

            var response = await handler.HandleAsync(
                new HostBridgeRequest
                {
                    Operation = HostBridgeOperationNames.GetContextSnapshot,
                    PayloadJson = "{}"
                },
                CancellationToken.None);

            Assert.IsTrue(response.Success, response.ErrorMessage);
            var snapshot = JsonConvert.DeserializeObject<HostContextSnapshotDto>(response.PayloadJson);
            Assert.AreEqual("工作簿地图摘要", snapshot.WorkbookMapSummary);
            Assert.AreEqual("sales.xlsx", snapshot.WorkbookMap.WorkbookName);
            Assert.AreEqual("Sheet1", snapshot.WorkbookMap.Worksheets[0].CodeName);
            Assert.AreEqual("Order ID", snapshot.WorkbookMap.Worksheets[0].HeaderColumns[0]);
            Assert.AreEqual("header-group-1", snapshot.WorkbookMap.WorksheetGroups[0].GroupId);
            Assert.AreEqual(
                "Order ID",
                snapshot.WorkbookMap.SharedColumnCandidates[0].ColumnName);
            Assert.IsFalse(snapshot.WorkbookMap.SharedColumnCandidates[0].UniquenessVerified);
        }

        private sealed class SnapshotHostAdapter : ICopilotHostAdapter
        {
            private readonly HostContextSnapshot _snapshot;

            public SnapshotHostAdapter(HostContextSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public string HostApplicationName
            {
                get { return "Excel"; }
            }

            public HostContextSnapshot GetContextSnapshot()
            {
                return _snapshot;
            }

            public void InsertText(string text)
            {
                throw new System.NotSupportedException();
            }
        }
    }
}
