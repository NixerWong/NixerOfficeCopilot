using System;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.Host.Excel.Com;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public abstract class ExcelToolBase : IAgentTool
    {
        public abstract ToolDefinition Definition { get; }

        public abstract Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request);

        protected static IExcelToolHost GetExcelToolHost(ToolExecutionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var host = context.GetService<IExcelToolHost>();
            if (host == null)
            {
                throw new InvalidOperationException("未注册 Excel 工具宿主服务。");
            }

            return host;
        }

        protected ToolExecutionResult CreateSuccess(
            ToolExecutionRequest request,
            string content,
            string structuredContentJson)
        {
            return ToolExecutionResult.CreateSuccess(
                Definition.Name,
                request == null ? string.Empty : request.ToolCallId,
                content,
                structuredContentJson);
        }

        protected ToolExecutionResult CreateFailure(
            ToolExecutionRequest request,
            string errorMessage,
            string errorCode = null,
            string content = null,
            string structuredContentJson = null)
        {
            return ToolExecutionResult.CreateFailure(
                Definition.Name,
                request == null ? string.Empty : request.ToolCallId,
                errorMessage,
                errorCode,
                content,
                structuredContentJson);
        }

        protected ToolExecutionResult CreateFailure(
            ToolExecutionRequest request,
            Exception exception,
            string errorCode = null,
            string content = null,
            string structuredContentJson = null)
        {
            if (ExcelComErrorClassifier.IsBusyException(exception))
            {
                var busyException = new ExcelHostBusyException(exception);
                return ToolExecutionResult.CreateFailure(
                    Definition.Name,
                    request == null ? string.Empty : request.ToolCallId,
                    busyException.Message,
                    ExcelHostBusyException.ErrorCode,
                    content,
                    structuredContentJson);
            }

            var incompleteCoverageException = exception as ExcelCoverageIncompleteException;
            if (incompleteCoverageException != null)
            {
                return ToolExecutionResult.CreateFailure(
                    Definition.Name,
                    request == null ? string.Empty : request.ToolCallId,
                    incompleteCoverageException.Message,
                    ExcelCoverageIncompleteException.ErrorCode,
                    content,
                    structuredContentJson);
            }

            var sourceTooLargeException = exception as ExcelSourceTooLargeException;
            if (sourceTooLargeException != null)
            {
                return ToolExecutionResult.CreateFailure(
                    Definition.Name,
                    request == null ? string.Empty : request.ToolCallId,
                    sourceTooLargeException.Message,
                    ExcelSourceTooLargeException.ErrorCode,
                    content,
                    structuredContentJson);
            }

            return CreateFailure(
                request,
                exception == null ? string.Empty : exception.Message,
                errorCode,
                content,
                structuredContentJson);
        }
    }
}