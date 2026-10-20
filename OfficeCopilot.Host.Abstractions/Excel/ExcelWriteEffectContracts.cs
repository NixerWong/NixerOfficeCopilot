using System.Collections.Generic;

namespace OfficeCopilot.Host.Abstractions
{
    public interface IExcelWriteEffectHostAdapter
    {
        ExcelWriteEffectTurnResult BeginWriteEffectTurn(string conversationId, string messageId);

        ExcelWriteEffectAssessment AssessWriteEffect(
            string conversationId,
            string messageId,
            string toolName,
            string argumentsJson);
    }

    public interface IExcelWipRegistryHostAdapter
    {
        ExcelWipSheetRegistryResult GetRegisteredWipWorksheets(string conversationId);

        ExcelWipSheetRegistryResult RefreshWipSheetRegistry(
            string conversationId,
            bool registerNewWorksheets,
            string expectedWorksheetName,
            IList<string> worksheetNamesBeforeOperation);
    }

    public sealed class ExcelWipSheetRegistryResult
    {
        public ExcelWipSheetRegistryResult()
        {
            RegisteredWorksheetNames = new List<string>();
            CurrentWorksheetNames = new List<string>();
        }

        public bool Success { get; set; }

        public IList<string> RegisteredWorksheetNames { get; set; }

        public IList<string> CurrentWorksheetNames { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }

    public sealed class ExcelWriteEffectTurnResult
    {
        public bool Success { get; set; }

        public string WorkbookFullName { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }

    public sealed class ExcelWriteEffectAssessment
    {
        public bool Success { get; set; }

        public string Effect { get; set; }

        public string OperationSummary { get; set; }

        public string TargetWorkbookFullName { get; set; }

        public string TargetWorksheetName { get; set; }

        public string TargetAddress { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }
    }
}
