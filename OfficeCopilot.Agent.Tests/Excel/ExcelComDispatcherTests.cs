using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Excel.Tools;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelComDispatcherTests
    {
        [TestMethod]
        public async Task ReadOnlyToolRetriesBusyResultWithBoundedBackoff()
        {
            var delays = new List<int>();
            var dispatcher = CreateDispatcher(delays);
            var definition = CreateDefinition(true);
            var context = new ToolExecutionContext();
            var request = new ToolExecutionRequest { ToolCallId = "read-1" };
            var attempts = 0;

            var result = await dispatcher.ExecuteAsync(
                definition,
                context,
                request,
                () =>
                {
                    attempts++;
                    return Task.FromResult(attempts < 3
                        ? ToolExecutionResult.CreateFailure(
                            definition.Name,
                            request.ToolCallId,
                            "busy",
                            ExcelHostBusyException.ErrorCode)
                        : ToolExecutionResult.CreateSuccess(definition.Name, request.ToolCallId, "ok"));
                });

            Assert.IsTrue(result.Success);
            Assert.AreEqual(3, attempts);
            CollectionAssert.AreEqual(new[] { 50, 100 }, delays);
        }

        [TestMethod]
        public async Task WriteToolReturnsBusyWithoutRetry()
        {
            var delays = new List<int>();
            var dispatcher = CreateDispatcher(delays);
            var definition = CreateDefinition(false);
            var request = new ToolExecutionRequest { ToolCallId = "write-1" };
            var attempts = 0;

            var result = await dispatcher.ExecuteAsync(
                definition,
                new ToolExecutionContext(),
                request,
                () =>
                {
                    attempts++;
                    return Task.FromResult(ToolExecutionResult.CreateFailure(
                        definition.Name,
                        request.ToolCallId,
                        "busy",
                        ExcelHostBusyException.ErrorCode));
                });

            Assert.AreEqual(1, attempts);
            Assert.AreEqual(0, delays.Count);
            Assert.AreEqual(ExcelHostBusyException.ErrorCode, result.ErrorCode);
        }

        [TestMethod]
        public async Task ReadOnlyToolDoesNotRetryUnrelatedFailure()
        {
            var delays = new List<int>();
            var dispatcher = CreateDispatcher(delays);
            var definition = CreateDefinition(true);
            var request = new ToolExecutionRequest { ToolCallId = "read-3" };
            var attempts = 0;

            var result = await dispatcher.ExecuteAsync(
                definition,
                new ToolExecutionContext(),
                request,
                () =>
                {
                    attempts++;
                    return Task.FromResult(ToolExecutionResult.CreateFailure(
                        definition.Name,
                        request.ToolCallId,
                        "Range not found.",
                        "excel.range_not_found"));
                });

            Assert.AreEqual(1, attempts);
            Assert.AreEqual(0, delays.Count);
            Assert.AreEqual("excel.range_not_found", result.ErrorCode);
        }

        [TestMethod]
        public async Task ReadOnlyToolStopsAfterSixRetriesAndReturnsHostBusy()
        {
            var delays = new List<int>();
            var dispatcher = CreateDispatcher(delays);
            var definition = CreateDefinition(true);
            var request = new ToolExecutionRequest { ToolCallId = "read-2" };
            var attempts = 0;

            var result = await dispatcher.ExecuteAsync(
                definition,
                new ToolExecutionContext(),
                request,
                () =>
                {
                    attempts++;
                    throw new COMException("Excel is busy.", ExcelComErrorClassifier.RpcEServerCallRetryLater);
                });

            Assert.AreEqual(7, attempts);
            CollectionAssert.AreEqual(new[] { 50, 100, 200, 400, 800, 1600 }, delays);
            Assert.AreEqual(ExcelHostBusyException.ErrorCode, result.ErrorCode);
            StringAssert.Contains(result.ErrorMessage, "按 Esc 退出编辑");
        }

        [TestMethod]
        public void ClassifierRecognizesExcelBusyHresultsThroughExceptionWrappers()
        {
            var exception = new InvalidOperationException(
                "Tool failed.",
                new TargetInvocationException(
                    new COMException("Excel busy.", ExcelComErrorClassifier.ExcelVbaBusy)));

            int hresult;
            Assert.IsTrue(ExcelComErrorClassifier.TryGetBusyHResult(exception, out hresult));
            Assert.AreEqual(ExcelComErrorClassifier.ExcelVbaBusy, hresult);
            Assert.IsFalse(ExcelComErrorClassifier.IsBusyHResult(unchecked((int)0x80004005)));
        }

        [TestMethod]
        public void ExcelToolFailureMappingPreservesHostBusyErrorCode()
        {
            var tool = new FailureMappingTool();
            var result = tool.MapFailure(
                new COMException("Excel busy.", ExcelComErrorClassifier.RpcECallRejected));

            Assert.AreEqual(ExcelHostBusyException.ErrorCode, result.ErrorCode);
            StringAssert.Contains(result.ErrorMessage, "单元格编辑状态");
        }

        private static ExcelComDispatcher CreateDispatcher(IList<int> delays)
        {
            return new ExcelComDispatcher(
                SynchronizationContext.Current ?? new SynchronizationContext(),
                Thread.CurrentThread.ManagedThreadId,
                (milliseconds, cancellationToken) =>
                {
                    delays.Add(milliseconds);
                    return Task.FromResult(0);
                });
        }

        private static ToolDefinition CreateDefinition(bool isReadOnly)
        {
            return new ToolDefinition
            {
                Name = isReadOnly ? "excel_read_range" : "excel_write_range",
                IsReadOnly = isReadOnly
            };
        }

        private sealed class FailureMappingTool : ExcelToolBase
        {
            private readonly ToolDefinition _definition = CreateDefinition(true);

            public override ToolDefinition Definition
            {
                get { return _definition; }
            }

            public override Task<ToolExecutionResult> ExecuteAsync(
                ToolExecutionContext context,
                ToolExecutionRequest request)
            {
                return Task.FromResult<ToolExecutionResult>(null);
            }

            public ToolExecutionResult MapFailure(Exception exception)
            {
                return CreateFailure(
                    new ToolExecutionRequest { ToolCallId = "mapping-1" },
                    exception,
                    "excel.read_range_failed");
            }
        }
    }
}
