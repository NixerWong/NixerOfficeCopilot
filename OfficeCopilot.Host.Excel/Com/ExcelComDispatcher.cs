using OfficeCopilot.Agent.Abstractions;
using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Host.Excel.Com
{
    public sealed class ExcelComDispatcher : IHostCallPolicy
    {
        private static readonly int[] _retryDelaysMilliseconds =
        {
            50,
            100,
            200,
            400,
            800,
            1600
        };

        private readonly SynchronizationContext _context;
        private readonly int _ownerThreadId;
        private readonly Func<int, CancellationToken, Task> _delayAsync;

        public ExcelComDispatcher()
            : this(
                SynchronizationContext.Current,
                Thread.CurrentThread.ManagedThreadId,
                (milliseconds, cancellationToken) => Task.Delay(milliseconds, cancellationToken))
        {
        }

        internal ExcelComDispatcher(
            SynchronizationContext context,
            int ownerThreadId,
            Func<int, CancellationToken, Task> delayAsync)
        {
            _context = context ?? throw new InvalidOperationException(
                "ExcelComDispatcher must be created on the Excel UI thread with a SynchronizationContext.");
            if (ownerThreadId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownerThreadId));
            }

            _ownerThreadId = ownerThreadId;
            _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
        }

        public async Task<ToolExecutionResult> ExecuteAsync(
            ToolDefinition definition,
            ToolExecutionContext context,
            ToolExecutionRequest request,
            Func<Task<ToolExecutionResult>> execute)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            var operationName = string.IsNullOrWhiteSpace(definition.Name)
                ? "unknown"
                : definition.Name;
            var attempt = 0;
            while (true)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                attempt++;
                var stopwatch = Stopwatch.StartNew();
                ToolExecutionResult result;
                try
                {
                    result = await InvokeOnOwnerThread(execute).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    TraceCallDuration(operationName, attempt, stopwatch.ElapsedMilliseconds);
                    int hresult;
                    if (!ExcelComErrorClassifier.TryGetBusyHResult(ex, out hresult))
                    {
                        throw;
                    }

                    result = CreateBusyResult(definition, request, ex);
                    if (!definition.IsReadOnly || attempt > _retryDelaysMilliseconds.Length)
                    {
                        return result;
                    }

                    await DelayBeforeRetry(
                        operationName,
                        attempt,
                        hresult,
                        context.CancellationToken).ConfigureAwait(false);
                    continue;
                }

                stopwatch.Stop();
                TraceCallDuration(operationName, attempt, stopwatch.ElapsedMilliseconds);
                if (!ExcelComErrorClassifier.IsBusyErrorCode(result == null ? null : result.ErrorCode)
                    || !definition.IsReadOnly
                    || attempt > _retryDelaysMilliseconds.Length)
                {
                    return result;
                }

                await DelayBeforeRetry(
                    operationName,
                    attempt,
                    0,
                    context.CancellationToken).ConfigureAwait(false);
            }
        }

        private Task<ToolExecutionResult> InvokeOnOwnerThread(
            Func<Task<ToolExecutionResult>> execute)
        {
            if (Thread.CurrentThread.ManagedThreadId == _ownerThreadId)
            {
                return execute();
            }

            Task<ToolExecutionResult> result = null;
            ExceptionDispatchInfo failure = null;
            _context.Send(
                _ =>
                {
                    try
                    {
                        result = execute();
                    }
                    catch (Exception ex)
                    {
                        failure = ExceptionDispatchInfo.Capture(ex);
                    }
                },
                null);

            if (failure != null)
            {
                failure.Throw();
            }

            if (result == null)
            {
                throw new InvalidOperationException("Excel tool returned no execution task.");
            }

            return result;
        }

        private async Task DelayBeforeRetry(
            string operationName,
            int attempt,
            int hresult,
            CancellationToken cancellationToken)
        {
            var delay = _retryDelaysMilliseconds[attempt - 1];
            Trace.TraceInformation(
                "Excel COM call was rejected as busy. Operation={0}, Attempt={1}, HResult=0x{2:X8}, RetryDelayMs={3}",
                operationName,
                attempt,
                hresult,
                delay);
            await _delayAsync(delay, cancellationToken).ConfigureAwait(false);
        }

        private static ToolExecutionResult CreateBusyResult(
            ToolDefinition definition,
            ToolExecutionRequest request,
            Exception exception)
        {
            var busyException = new ExcelHostBusyException(exception);
            return ToolExecutionResult.CreateFailure(
                definition.Name,
                request == null ? string.Empty : request.ToolCallId,
                busyException.Message,
                ExcelHostBusyException.ErrorCode);
        }

        private static void TraceCallDuration(
            string operationName,
            int attempt,
            long elapsedMilliseconds)
        {
            Trace.TraceInformation(
                "Excel host call completed. Operation={0}, Attempt={1}, ElapsedMs={2}",
                operationName,
                attempt,
                elapsedMilliseconds);
            if (elapsedMilliseconds > 200)
            {
                Trace.TraceWarning(
                    "Excel host call exceeded 200 ms. Operation={0}, Attempt={1}, ElapsedMs={2}",
                    operationName,
                    attempt,
                    elapsedMilliseconds);
            }
        }
    }
}
