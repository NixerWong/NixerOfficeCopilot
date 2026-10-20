namespace OfficeCopilot.Host.Abstractions
{
    public interface IExcelToolHost
    {
        string HostApplicationName { get; }

        ExcelWorkbookInfo GetActiveWorkbookInfo();

        ExcelSelectionInfo GetSelectionInfo();

        ExcelRangeReadResult ReadRange(ExcelRangeReadRequest request);

        ExcelRangeWriteResult WriteRange(ExcelRangeWriteRequest request);

        ExcelRangeFormulaWriteResult SetRangeFormulas(ExcelRangeFormulaWriteRequest request);

        ExcelRangeFormatWriteResult SetRangeFormat(ExcelRangeFormatWriteRequest request);

        VbaExecutionResult ExecuteVba(VbaExecutionRequest request);

        ExcelRangeClearResult ClearRange(ExcelRangeRequest request);

        ExcelWorksheetListResult ListWorksheets();

        ExcelWorksheetCreationResult CreateWorksheet(ExcelWorksheetCreationRequest request);

        ExcelWorksheetDataResult ReadWorksheetData(ExcelWorksheetDataRequest request);

        ExcelNamedRangeListResult ListNamedRanges();

        ExcelNamedRangeValueResult GetNamedRangeValue(ExcelNamedRangeValueRequest request);

        ExcelPowerQueryListResult ListPowerQueries();

        ExcelPowerQueryViewResult ViewPowerQuery(ExcelPowerQueryViewRequest request);

        ExcelPowerQueryRefreshResult RefreshPowerQuery(ExcelPowerQueryRefreshRequest request);

        ExcelPowerQueryErrorCheckResult CheckPowerQueryErrors(ExcelPowerQueryErrorCheckRequest request);

        ExcelPowerQueryValidationResult ValidatePowerQuery(ExcelPowerQueryValidationRequest request);

        ExcelVbaModuleListResult ListVbaModules();

        ExcelVbaModuleViewResult ViewVbaModule(ExcelVbaModuleViewRequest request);

        ExcelConnectionListResult ListConnections();

        ExcelConnectionViewResult ViewConnection(ExcelConnectionViewRequest request);

        ExcelTableListResult ListTables();

        ExcelTableViewResult ViewTable(ExcelTableViewRequest request);

        ExcelTableDataResult ReadTableData(ExcelTableDataRequest request);

        ExcelRangeFormulaResult ReadRangeFormulas(ExcelRangeRequest request);

        ExcelRangeInfoResult GetRangeInfo(ExcelRangeRequest request);

        ExcelWorksheetScreenshotResult CaptureWorksheetScreenshot(ExcelWorksheetScreenshotRequest request);

        ExcelRangeHyperlinkResult GetRangeHyperlinks(ExcelRangeRequest request);

        ExcelRangeValidationResult GetRangeValidation(ExcelRangeRequest request);

        ExcelRangeNumberFormatResult GetRangeNumberFormats(ExcelRangeRequest request);

        ExcelRangeMergeInfoResult GetRangeMergeInfo(ExcelRangeRequest request);

        ExcelRangeLockInfoResult GetRangeLockInfo(ExcelRangeRequest request);

        ExcelRangeStyleResult GetRangeStyle(ExcelRangeRequest request);

        ExcelRangeFindResult FindInRange(ExcelRangeFindRequest request);

        ExcelAnalyzeRangeResult AnalyzeRange(ExcelAnalyzeRangeRequest request);

        ExcelProfileRangeResult ProfileRange(ExcelProfileRangeRequest request);

        ExcelAggregateResult Aggregate(ExcelAggregateRequest request);

        ExcelReconcileResult Reconcile(ExcelReconcileRequest request);

        ExcelVerificationReport VerifyAssertions(ExcelVerificationPlan request);

        ExcelVbaStageResult ExecuteVbaStage(ExcelVbaStageRequest request);

        ExcelCleanupWorkspaceResult CleanupWorkspace(ExcelCleanupWorkspaceRequest request);

        ExcelListBackupsResult ListBackups(ExcelListBackupsRequest request);

        ExcelRestoreBackupResult RestoreBackup(ExcelRestoreBackupRequest request);

        ExcelRangeFormulaValidationResult ValidateRangeFormulas(ExcelRangeFormulaValidationRequest request);

        ExcelDataModelTableListResult ListDataModelTables();

        ExcelDataModelMeasureListResult ListDataModelMeasures();

        ExcelDataModelMeasureViewResult ViewDataModelMeasure(ExcelDataModelMeasureViewRequest request);

        ExcelDataModelRelationshipListResult ListDataModelRelationships();

        ExcelDataModelCalculatedColumnListResult ListDataModelCalculatedColumns();

        ExcelDataModelCalculatedColumnViewResult ViewDataModelCalculatedColumn(ExcelDataModelCalculatedColumnViewRequest request);
    }
}