using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Runtime.Verification
{
    internal sealed class VerificationLedger
    {
        private static readonly HashSet<string> ArtifactToolNames = new HashSet<string>(
            new[]
            {
                "excel_aggregate",
                "excel_clear_range",
                "excel_compare_key_sets",
                "excel_execute_vba",
                "excel_set_range_formulas",
                "excel_write_range"
            },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> VerificationToolNames = new HashSet<string>(
            new[]
            {
                "excel_aggregate",
                "excel_assert",
                "excel_compare_key_sets",
                "excel_execute_vba",
                "excel_profile_range",
                "excel_reconcile",
                "excel_validate_range_formulas"
            },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> NonDataToolNames = new HashSet<string>(
            new[]
            {
                "excel_capture_worksheet_screenshot",
                "excel_cleanup_workspace",
                "excel_create_worksheet",
                "excel_get_active_workbook",
                "excel_get_range_lock_info",
                "excel_get_range_merge_info",
                "excel_get_range_number_formats",
                "excel_get_range_style",
                "excel_get_selection",
                "excel_list_backups",
                "excel_list_worksheets",
                "excel_restore_backup",
                "excel_set_range_format"
            },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> DirectReadResultToolNames = new HashSet<string>(
            new[]
            {
                "excel_get_range_info",
                "excel_list_backups",
                "excel_list_tables",
                "excel_list_worksheets",
                "excel_read_range"
            },
            StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> FormatReadbackProperties =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "fillColor", "FillColor" },
                { "fontColor", "FontColor" },
                { "fontName", "FontName" },
                { "fontSize", "FontSize" },
                { "bold", "Bold" },
                { "italic", "Italic" },
                { "underline", "Underline" },
                { "numberFormat", "NumberFormat" },
                { "horizontalAlignment", "HorizontalAlignment" },
                { "verticalAlignment", "VerticalAlignment" },
                { "styleName", "StyleName" }
            };

        private readonly List<VerificationRecord> _verifications = new List<VerificationRecord>();
        private readonly List<OutputArtifact> _artifacts = new List<OutputArtifact>();
        private readonly List<WorksheetCreationArtifact> _worksheetCreationArtifacts =
            new List<WorksheetCreationArtifact>();
        private readonly List<WorksheetCleanupArtifact> _worksheetCleanupArtifacts =
            new List<WorksheetCleanupArtifact>();
        private readonly List<DirectReadResult> _directReadResults =
            new List<DirectReadResult>();
        private readonly List<string> _parseErrors = new List<string>();
        private int _sequence;
        private int _completeReadOnlyResultSequence;
        private string _completeReadOnlyResultToolName = string.Empty;

        public bool HasExcelDataToolCalls { get; private set; }

        public void RecordToolCall(
            string toolName,
            string argumentsJson,
            bool succeeded,
            string structuredContentJson,
            bool isWriteTool = false,
            string executionStepId = null,
            string verificationTargetStepId = null,
            string errorCode = null)
        {
            var normalizedToolName = (toolName ?? string.Empty).Trim();
            var isWorksheetCreation = string.Equals(
                normalizedToolName,
                "excel_create_worksheet",
                StringComparison.OrdinalIgnoreCase);
            var isCleanupWorkspaceDryRun = IsCleanupWorkspaceDryRunRequest(
                normalizedToolName,
                argumentsJson);
            var isCleanupWorkspaceMutation = IsCleanupWorkspaceRequest(normalizedToolName)
                && !isCleanupWorkspaceDryRun;
            var cleanupVerificationTargetStepId = ResolveVerificationTargetStepId(
                executionStepId,
                verificationTargetStepId);
            var isCleanupWorkspaceReadback = succeeded
                && string.Equals(
                    normalizedToolName,
                    "excel_list_worksheets",
                    StringComparison.OrdinalIgnoreCase)
                && HasWipCleanupArtifactForStep(cleanupVerificationTargetStepId);
            var isDirectReadResult = DirectReadResultToolNames.Contains(normalizedToolName)
                && !isCleanupWorkspaceReadback;
            var isFormattingMutation = isWriteTool
                && string.Equals(
                    normalizedToolName,
                    "excel_set_range_format",
                    StringComparison.OrdinalIgnoreCase);
            var matchingFormatArtifact = string.Equals(
                    normalizedToolName,
                    "excel_get_range_style",
                    StringComparison.OrdinalIgnoreCase)
                ? FindMatchingFormatArtifact(argumentsJson)
                : null;
            var isFormattingReadback = matchingFormatArtifact != null;
            var isDataWriteTool = isWriteTool
                && normalizedToolName.StartsWith("excel_", StringComparison.OrdinalIgnoreCase)
                && !NonDataToolNames.Contains(normalizedToolName);
            var matchingSingleCellWriteArtifact = string.Equals(
                    normalizedToolName,
                    "excel_read_range",
                    StringComparison.OrdinalIgnoreCase)
                ? FindMatchingSingleCellWriteArtifact(argumentsJson)
                : null;
            var isSingleCellWriteReadback = matchingSingleCellWriteArtifact != null;
            if (!IsExcelDataTool(normalizedToolName)
                && !isDataWriteTool
                && !isFormattingMutation
                && !isFormattingReadback
                && !isSingleCellWriteReadback
                && !isWorksheetCreation
                && !isCleanupWorkspaceDryRun
                && !isCleanupWorkspaceMutation
                && !isCleanupWorkspaceReadback
                && !isDirectReadResult)
            {
                return;
            }

            if (!isWorksheetCreation
                && !isCleanupWorkspaceDryRun
                && !isCleanupWorkspaceMutation
                && !isCleanupWorkspaceReadback)
            {
                HasExcelDataToolCalls = true;
            }

            var sequence = ++_sequence;
            _completeReadOnlyResultSequence = 0;
            _completeReadOnlyResultToolName = string.Empty;
            if (isDirectReadResult)
            {
                _directReadResults.Add(new DirectReadResult
                {
                    Sequence = sequence,
                    ToolName = normalizedToolName,
                    Scope = BuildDirectReadScope(normalizedToolName, argumentsJson),
                    ExecutionStepId = executionStepId ?? string.Empty,
                    Succeeded = succeeded,
                    IsTruncated = IsStructuredResultTruncated(structuredContentJson)
                });
            }

            if (isWorksheetCreation)
            {
                if (succeeded)
                {
                    RecordWorksheetCreation(argumentsJson, structuredContentJson, executionStepId);
                }

                return;
            }

            if (isCleanupWorkspaceMutation)
            {
                if (succeeded)
                {
                    RecordWipCleanupMutation(
                        argumentsJson,
                        structuredContentJson,
                        executionStepId,
                        verificationTargetStepId);
                }

                return;
            }

            if (isCleanupWorkspaceDryRun)
            {
                if (succeeded)
                {
                    RecordWipCleanupDryRunVerification(
                        structuredContentJson,
                        sequence,
                        executionStepId,
                        verificationTargetStepId);
                }

                return;
            }

            if (isCleanupWorkspaceReadback)
            {
                RecordWipCleanupReadbackVerification(
                    structuredContentJson,
                    sequence,
                    cleanupVerificationTargetStepId);
                return;
            }

            if (isSingleCellWriteReadback)
            {
                _verifications.Add(ReadSingleCellWriteReadback(
                    argumentsJson,
                    structuredContentJson,
                    sequence,
                    matchingSingleCellWriteArtifact,
                    verificationTargetStepId,
                    succeeded));
                return;
            }

            var isAggregateResult = string.Equals(
                normalizedToolName,
                "excel_aggregate",
                StringComparison.OrdinalIgnoreCase);
            var isProfileResult = string.Equals(
                normalizedToolName,
                "excel_profile_range",
                StringComparison.OrdinalIgnoreCase);
            var isHiddenRowsScan = IsHiddenRowsScanRequest(
                normalizedToolName,
                argumentsJson);
            var hasInlineVerification = isAggregateResult
                || isProfileResult
                || string.Equals(
                    normalizedToolName,
                    "excel_compare_key_sets",
                    StringComparison.OrdinalIgnoreCase)
                || isHiddenRowsScan;
            var isArtifact = isDataWriteTool
                || isFormattingMutation
                || (ArtifactToolNames.Contains(normalizedToolName)
                    && (!isAggregateResult || succeeded))
                || (isHiddenRowsScan && succeeded);
            var isVerification = VerificationToolNames.Contains(normalizedToolName)
                || (isHiddenRowsScan && succeeded);
            var relatedWorksheets = new List<string>();

            if (isArtifact || isVerification)
            {
                relatedWorksheets = ReadRelatedWorksheets(
                    normalizedToolName,
                    argumentsJson,
                    isDataWriteTool || isFormattingMutation,
                    out var argumentError);
                if (!string.IsNullOrWhiteSpace(argumentError))
                {
                    _parseErrors.Add(argumentError);
                }
            }

            if (isArtifact)
            {
                var worksheet = relatedWorksheets.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(worksheet))
                {
                    worksheet = ReadResultWorksheet(normalizedToolName, structuredContentJson);
                }

                _artifacts.Add(new OutputArtifact
                {
                    ToolName = normalizedToolName,
                    WorksheetName = NormalizeWorksheetName(worksheet),
                    WorkbookName = ReadResultWorkbookName(structuredContentJson),
                    Sequence = sequence,
                    Succeeded = succeeded,
                    IsAggregateResult = isAggregateResult,
                    HasInlineVerification = hasInlineVerification,
                    IsCompleteUnverifiedAggregateResult = isAggregateResult
                        && succeeded
                        && HasCompleteUnverifiedAggregateResult(structuredContentJson),
                    AggregateArgumentsJson = isAggregateResult
                        ? argumentsJson ?? string.Empty
                        : string.Empty,
                    IsFormattingArtifact = isFormattingMutation,
                    FormatArgumentsJson = isFormattingMutation ? argumentsJson : null,
                    WriteArgumentsJson = isDataWriteTool ? argumentsJson ?? string.Empty : string.Empty,
                    WriteResultJson = isDataWriteTool ? structuredContentJson ?? string.Empty : string.Empty,
                    ErrorCode = errorCode ?? string.Empty,
                    TargetWorkbookFullName = ReadWriteEffectTargetWorkbookFullName(structuredContentJson),
                    Address = isDataWriteTool || isFormattingMutation
                        ? ReadArtifactAddress(normalizedToolName, argumentsJson)
                        : string.Empty,
                    ExecutionStepId = isDataWriteTool || isFormattingMutation
                        ? executionStepId ?? string.Empty
                        : string.Empty
                });
            }

            if (!isVerification && !isFormattingReadback)
            {
                return;
            }

            if (isVerification)
            {
                var verification = isProfileResult
                    ? ReadProfileVerification(
                        argumentsJson,
                        structuredContentJson,
                        sequence,
                        relatedWorksheets,
                        succeeded)
                    : string.Equals(
                        normalizedToolName,
                        "excel_validate_range_formulas",
                        StringComparison.OrdinalIgnoreCase)
                    ? ReadFormulaValidation(
                        structuredContentJson,
                        sequence,
                        succeeded)
                    : ReadVerification(
                        normalizedToolName,
                        structuredContentJson,
                        sequence,
                        relatedWorksheets,
                        isHiddenRowsScan);
                if (verification.Record != null)
                {
                    verification.Record.ToolName = normalizedToolName;
                    verification.Record.ArgumentsJson = argumentsJson ?? string.Empty;
                    verification.Record.VerifiesStepId = verificationTargetStepId ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(verification.Record.Address))
                    {
                        verification.Record.Address = ReadAddress(argumentsJson);
                    }
                    verification.Record.Addresses = ReadVerificationAddresses(
                        normalizedToolName,
                        argumentsJson);
                    if (!string.IsNullOrWhiteSpace(verification.Record.Address))
                    {
                        verification.Record.Addresses.Add(verification.Record.Address);
                    }
                    _verifications.Add(verification.Record);
                }
                if (!string.IsNullOrWhiteSpace(verification.Error))
                {
                    _parseErrors.Add(verification.Error);
                }
            }

            if (isAggregateResult
                && succeeded
                && HasCompleteUnverifiedAggregateResult(structuredContentJson))
            {
                _completeReadOnlyResultSequence = sequence;
                _completeReadOnlyResultToolName = normalizedToolName;
            }

            if (isFormattingReadback)
            {
                var formatVerification = ReadFormatReadback(
                    argumentsJson,
                    structuredContentJson,
                    sequence,
                    matchingFormatArtifact,
                    succeeded,
                    verificationTargetStepId);
                if (formatVerification != null)
                {
                    _verifications.Add(formatVerification);
                }
            }
        }

        private void RecordWorksheetCreation(
            string argumentsJson,
            string structuredContentJson,
            string executionStepId)
        {
            if (string.IsNullOrWhiteSpace(executionStepId))
            {
                return;
            }

            var worksheetName = ReadResultWorksheet(
                "excel_create_worksheet",
                structuredContentJson);
            if (string.IsNullOrWhiteSpace(worksheetName))
            {
                worksheetName = ReadWorksheetNameArgument(argumentsJson);
            }

            if (IsWipWorksheetName(worksheetName))
            {
                _worksheetCreationArtifacts.Add(new WorksheetCreationArtifact
                {
                    WorksheetName = NormalizeWorksheetName(worksheetName),
                    ExecutionStepId = executionStepId.Trim()
                });
            }
        }

        private void RecordWipCleanupDryRunVerification(
            string structuredContentJson,
            int sequence,
            string executionStepId,
            string verificationTargetStepId)
        {
            var targetStepId = !string.IsNullOrWhiteSpace(verificationTargetStepId)
                ? verificationTargetStepId.Trim()
                : (executionStepId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(targetStepId)
                || _artifacts.Any(artifact => string.Equals(
                    artifact.ExecutionStepId,
                    targetStepId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var createdWorksheets = _worksheetCreationArtifacts
                .Where(artifact => string.Equals(
                    artifact.ExecutionStepId,
                    targetStepId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(artifact => artifact.WorksheetName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (createdWorksheets.Count == 0
                || !TryReadWipCleanupDryRunResult(
                    structuredContentJson,
                    out var matchedWorksheets))
            {
                return;
            }

            if (!createdWorksheets.All(matchedWorksheets.Contains))
            {
                return;
            }

            _verifications.Add(new VerificationRecord
            {
                Sequence = sequence,
                ToolName = "excel_cleanup_workspace",
                Tier = "A",
                Passed = true,
                Independent = true,
                Producer = "excel_cleanup_workspace dryRun",
                IsValid = true,
                IsWorksheetStructureVerification = true,
                WorksheetNames = new HashSet<string>(
                    createdWorksheets,
                    StringComparer.OrdinalIgnoreCase),
                VerifiesStepId = targetStepId,
                Addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            });
        }

        private void RecordWipCleanupMutation(
            string argumentsJson,
            string structuredContentJson,
            string executionStepId,
            string verificationTargetStepId)
        {
            var stepId = ResolveVerificationTargetStepId(
                executionStepId,
                verificationTargetStepId);
            if (string.IsNullOrWhiteSpace(stepId)
                || !TryReadWipCleanupMutationResult(
                    argumentsJson,
                    structuredContentJson,
                    out var removedWorksheets))
            {
                return;
            }

            _worksheetCleanupArtifacts.Add(new WorksheetCleanupArtifact
            {
                WorksheetNames = removedWorksheets,
                ExecutionStepId = stepId
            });
        }

        private void RecordWipCleanupReadbackVerification(
            string structuredContentJson,
            int sequence,
            string targetStepId)
        {
            if (string.IsNullOrWhiteSpace(targetStepId)
                || !TryReadCompleteWorksheetList(
                    structuredContentJson,
                    out var currentWorksheets))
            {
                return;
            }

            var removedWorksheets = GetWipCleanupWorksheetsForStep(targetStepId);
            if (removedWorksheets.Count == 0
                || removedWorksheets.Any(currentWorksheets.Contains))
            {
                return;
            }

            _verifications.Add(new VerificationRecord
            {
                Sequence = sequence,
                ToolName = "excel_list_worksheets",
                Tier = "A",
                Passed = true,
                Independent = true,
                Producer = "excel_list_worksheets post-cleanup readback",
                IsValid = true,
                IsWorksheetStructureVerification = true,
                WorksheetNames = currentWorksheets,
                VerifiesStepId = targetStepId,
                Addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            });
        }

        private bool HasWipCleanupArtifactForStep(string stepId)
        {
            return GetWipCleanupWorksheetsForStep(stepId).Count > 0;
        }

        private HashSet<string> GetWipCleanupWorksheetsForStep(string stepId)
        {
            return new HashSet<string>(
                _worksheetCleanupArtifacts
                    .Where(artifact => string.Equals(
                        artifact.ExecutionStepId,
                        stepId,
                        StringComparison.OrdinalIgnoreCase))
                    .SelectMany(artifact => artifact.WorksheetNames),
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryReadWipCleanupMutationResult(
            string argumentsJson,
            string structuredContentJson,
            out HashSet<string> removedWorksheets)
        {
            removedWorksheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(argumentsJson)
                || string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return false;
            }

            try
            {
                string prefix = "_wip_";
                using (var arguments = JsonDocument.Parse(argumentsJson))
                {
                    if (TryGetPropertyIgnoreCase(
                            arguments.RootElement,
                            "prefix",
                            out var requestedPrefix)
                        && requestedPrefix.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(requestedPrefix.GetString()))
                    {
                        prefix = requestedPrefix.GetString().Trim();
                    }
                }

                using (var result = JsonDocument.Parse(structuredContentJson))
                {
                    var root = result.RootElement;
                    if (!TryGetPropertyIgnoreCase(root, "dryRun", out var dryRun)
                        || dryRun.ValueKind != JsonValueKind.False
                        || !TryReadWorksheetNameArray(root, "matchedSheets", out var matchedWorksheets)
                        || !TryReadWorksheetNameArray(root, "removedSheets", out removedWorksheets)
                        || removedWorksheets.Count == 0
                        || !matchedWorksheets.SetEquals(removedWorksheets)
                        || removedWorksheets.Any(name =>
                            !IsWipWorksheetName(name)
                            || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        removedWorksheets.Clear();
                        return false;
                    }

                    return true;
                }
            }
            catch (JsonException)
            {
                removedWorksheets.Clear();
                return false;
            }
        }

        private static bool TryReadCompleteWorksheetList(
            string structuredContentJson,
            out HashSet<string> worksheetNames)
        {
            worksheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return false;
            }

            try
            {
                using (var result = JsonDocument.Parse(structuredContentJson))
                {
                    var root = result.RootElement;
                    if (!TryGetPropertyIgnoreCase(root, "isTruncated", out var isTruncated)
                        || isTruncated.ValueKind != JsonValueKind.False
                        || !TryGetPropertyIgnoreCase(root, "worksheets", out var worksheets)
                        || worksheets.ValueKind != JsonValueKind.Array
                        || !TryGetPropertyIgnoreCase(root, "totalWorksheetCount", out var totalCount)
                        || totalCount.ValueKind != JsonValueKind.Number
                        || !totalCount.TryGetInt32(out var count)
                        || count != worksheets.GetArrayLength())
                    {
                        return false;
                    }

                    foreach (var worksheet in worksheets.EnumerateArray())
                    {
                        if (!TryGetPropertyIgnoreCase(worksheet, "name", out var name)
                            || name.ValueKind != JsonValueKind.String)
                        {
                            worksheetNames.Clear();
                            return false;
                        }

                        var normalizedName = NormalizeWorksheetName(name.GetString());
                        if (string.IsNullOrWhiteSpace(normalizedName)
                            || !worksheetNames.Add(normalizedName))
                        {
                            worksheetNames.Clear();
                            return false;
                        }
                    }

                    return true;
                }
            }
            catch (JsonException)
            {
                worksheetNames.Clear();
                return false;
            }
        }

        private static bool TryReadWorksheetNameArray(
            JsonElement root,
            string propertyName,
            out HashSet<string> worksheetNames)
        {
            worksheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TryGetPropertyIgnoreCase(root, propertyName, out var values)
                || values.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String)
                {
                    worksheetNames.Clear();
                    return false;
                }

                var name = NormalizeWorksheetName(value.GetString());
                if (string.IsNullOrWhiteSpace(name) || !worksheetNames.Add(name))
                {
                    worksheetNames.Clear();
                    return false;
                }
            }

            return true;
        }

        private static string ResolveVerificationTargetStepId(
            string executionStepId,
            string verificationTargetStepId)
        {
            return !string.IsNullOrWhiteSpace(verificationTargetStepId)
                ? verificationTargetStepId.Trim()
                : (executionStepId ?? string.Empty).Trim();
        }

        private static bool IsCleanupWorkspaceRequest(string toolName)
        {
            return string.Equals(
                toolName,
                "excel_cleanup_workspace",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCleanupWorkspaceDryRunRequest(
            string toolName,
            string argumentsJson)
        {
            if (!string.Equals(
                    toolName,
                    "excel_cleanup_workspace",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(argumentsJson))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(argumentsJson))
                {
                    return TryGetPropertyIgnoreCase(
                            document.RootElement,
                            "dryRun",
                            out var dryRun)
                        && dryRun.ValueKind == JsonValueKind.True;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryReadWipCleanupDryRunResult(
            string structuredContentJson,
            out HashSet<string> matchedWorksheets)
        {
            matchedWorksheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    var root = document.RootElement;
                    if (!TryGetPropertyIgnoreCase(root, "dryRun", out var dryRun)
                        || dryRun.ValueKind != JsonValueKind.True
                        || !TryGetPropertyIgnoreCase(root, "matchedSheets", out var matched)
                        || matched.ValueKind != JsonValueKind.Array
                        || !TryGetPropertyIgnoreCase(root, "removedSheets", out var removed)
                        || removed.ValueKind != JsonValueKind.Array
                        || removed.GetArrayLength() != 0)
                    {
                        return false;
                    }

                    foreach (var worksheet in matched.EnumerateArray())
                    {
                        if (worksheet.ValueKind != JsonValueKind.String)
                        {
                            return false;
                        }

                        var name = NormalizeWorksheetName(worksheet.GetString());
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            matchedWorksheets.Add(name);
                        }
                    }

                    return matchedWorksheets.Count > 0;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string ReadWorksheetNameArgument(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return string.Empty;
            }

            try
            {
                using (var document = JsonDocument.Parse(argumentsJson))
                {
                    return TryGetPropertyIgnoreCase(
                            document.RootElement,
                            "worksheetName",
                            out var name)
                        && name.ValueKind == JsonValueKind.String
                        ? NormalizeWorksheetName(name.GetString())
                        : string.Empty;
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static bool IsWipWorksheetName(string worksheetName)
        {
            return !string.IsNullOrWhiteSpace(worksheetName)
                && worksheetName.Trim().StartsWith(
                    "_wip_",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetPropertyIgnoreCase(
            JsonElement element,
            string propertyName,
            out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default(JsonElement);
            return false;
        }

        public bool HasPassingVerificationForCurrentCall(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            return _verifications.Any(verification =>
                verification.Sequence == _sequence
                && string.Equals(
                    verification.ToolName,
                    toolName,
                    StringComparison.OrdinalIgnoreCase)
                && IsPassingVerification(verification));
        }

        public bool HasCompleteReadOnlyResultForCurrentCall(string toolName)
        {
            return !string.IsNullOrWhiteSpace(toolName)
                && _completeReadOnlyResultSequence == _sequence
                && string.Equals(
                    _completeReadOnlyResultToolName,
                    toolName,
                    StringComparison.OrdinalIgnoreCase);
        }

        public bool HasPassingVerificationForStep(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId))
            {
                return false;
            }

            var createdWorksheets = _worksheetCreationArtifacts
                .Where(artifact => string.Equals(
                    artifact.ExecutionStepId,
                    stepId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(artifact => artifact.WorksheetName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var removedWorksheets = GetWipCleanupWorksheetsForStep(stepId);
            var hasWorksheetStructureChanges = createdWorksheets.Count > 0
                || removedWorksheets.Count > 0;
            var hasWorksheetStructureVerification = !hasWorksheetStructureChanges
                || _verifications.Any(verification =>
                    string.Equals(
                        verification.VerifiesStepId,
                        stepId,
                        StringComparison.OrdinalIgnoreCase)
                    && IsPassingVerification(verification)
                    && verification.IsWorksheetStructureVerification
                    && createdWorksheets.All(worksheet =>
                        removedWorksheets.Contains(worksheet)
                        || (verification.WorksheetNames != null
                            && verification.WorksheetNames.Contains(worksheet)))
                    && removedWorksheets.All(worksheet =>
                        verification.WorksheetNames == null
                        || !verification.WorksheetNames.Contains(worksheet)));
            if (!hasWorksheetStructureVerification)
            {
                return false;
            }

            var stageArtifacts = _artifacts
                .Where(artifact => string.Equals(
                    artifact.ExecutionStepId,
                    stepId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (stageArtifacts.Count == 0)
            {
                return hasWorksheetStructureChanges;
            }

            if (!_verifications.Any(verification =>
                    string.Equals(
                        verification.VerifiesStepId,
                        stepId,
                        StringComparison.OrdinalIgnoreCase)
                    && IsPassingVerification(verification)))
            {
                return false;
            }

            return stageArtifacts.All(artifact =>
            {
                var verification = FindLatestApplicableVerification(
                    artifact,
                    requireLinkedStepId: true);
                return verification != null
                    && string.Equals(
                        verification.VerifiesStepId,
                        stepId,
                        StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(artifact.Address)
                        || string.IsNullOrWhiteSpace(verification.Address)
                        || VerificationCoversAddress(artifact, verification))
                    && IsPassingVerification(verification);
            });
        }

        public VerificationLedgerSummary GetSummary()
        {
            var summaryArtifacts = _artifacts
                .Where(artifact => !IsSupersededEscalatedWrite(artifact))
                .ToList();
            var summary = new VerificationLedgerSummary
            {
                HasExcelDataToolCalls = HasExcelDataToolCalls,
                OutputItemCount = summaryArtifacts.Count,
                VerificationCount = _verifications.Count,
                Tier = string.Empty,
                BasisJson = new List<string>(),
                ScopeDescriptions = new List<string>()
            };
            if (!HasExcelDataToolCalls)
            {
                return summary;
            }

            foreach (var directRead in _directReadResults.OrderBy(item => item.Sequence))
            {
                AddScopeDescription(
                    summary.ScopeDescriptions,
                    AddExecutionStepDescription(
                        directRead.Scope,
                        directRead.ExecutionStepId));
            }

            var appliedVerifications = new Dictionary<int, VerificationRecord>();
            foreach (var artifact in summaryArtifacts)
            {
                AddScopeDescription(
                    summary.ScopeDescriptions,
                    BuildArtifactScope(artifact));
                var verification = FindLatestApplicableVerification(
                    artifact,
                    requireLinkedStepId: false);
                if (verification != null && verification.IsValid)
                {
                    summary.CoveredOutputItemCount++;
                }
                if (verification != null)
                {
                    AddScopeDescription(
                        summary.ScopeDescriptions,
                        BuildVerificationScope(artifact, verification));
                }

                if (!artifact.Succeeded)
                {
                    SetFirstReason(summary, "写入工具未成功完成：" + artifact.ToolName + "。");
                    summary.Tier = LowestTier(summary.Tier, "C");
                    continue;
                }

                if (verification == null)
                {
                    SetFirstReason(
                        summary,
                        _parseErrors.FirstOrDefault() ?? BuildUnverifiedReason(artifact));
                    summary.Tier = LowestTier(summary.Tier, "C");
                    continue;
                }

                appliedVerifications[verification.Sequence] = verification;
                if (!verification.IsValid)
                {
                    SetFirstReason(summary, verification.Error ?? "验证结果不完整，无法判定可信度。");
                    summary.Tier = LowestTier(summary.Tier, "C");
                    continue;
                }
                if (!verification.Passed)
                {
                    SetFirstReason(
                        summary,
                        verification.Error ?? "最近一次覆盖产出项的验证未通过：" + artifact.ToolName + "。");
                    summary.Tier = LowestTier(summary.Tier, "C");
                    continue;
                }

                var artifactTier = NormalizeTier(verification.Tier);
                if (artifactTier == "A" && (!verification.Independent || verification.Sampled))
                {
                    artifactTier = "B";
                }

                summary.Tier = LowestTier(summary.Tier, artifactTier);
                if (artifactTier == "B")
                {
                    SetFirstReason(
                        summary,
                        verification.Sampled
                            ? "至少一个产出项只进行了抽样复核，未覆盖全部数据。"
                            : !verification.Independent
                                ? "至少一个产出项由与原计算相同的路径复核，缺少独立复核。"
                                : "至少一个产出项的验证范围或独立性有限。");
                }
            }

            var standaloneProfileVerification = summaryArtifacts.Count == 0
                ? _verifications
                    .Where(verification =>
                        string.Equals(
                            verification.ToolName,
                            "excel_profile_range",
                            StringComparison.OrdinalIgnoreCase)
                        && IsPassingVerification(verification))
                    .OrderByDescending(verification => verification.Sequence)
                    .FirstOrDefault()
                : null;
            var standaloneAssertionVerification = summaryArtifacts.Count == 0
                ? _verifications
                    .Where(verification =>
                        string.Equals(
                            verification.ToolName,
                            "excel_assert",
                            StringComparison.OrdinalIgnoreCase)
                    )
                    .OrderByDescending(verification => verification.Sequence)
                    .FirstOrDefault()
                : null;
            if (standaloneProfileVerification != null)
            {
                appliedVerifications[standaloneProfileVerification.Sequence] = standaloneProfileVerification;
            }
            if (IsPassingVerification(standaloneAssertionVerification))
            {
                appliedVerifications[standaloneAssertionVerification.Sequence] = standaloneAssertionVerification;
            }

            foreach (var verification in appliedVerifications.Values.OrderBy(item => item.Sequence))
            {
                summary.AssertionCount += verification.AssertionCount;
                summary.CoveredRows += verification.CoveredRows;
                summary.HasKeySetDifferenceCalculation |= verification.IsKeySetDifference;
                summary.HasHiddenRowsScan |= verification.IsHiddenRowsScan;
                if (!string.IsNullOrWhiteSpace(verification.BasisJson)
                    && !summary.BasisJson.Contains(verification.BasisJson))
                {
                    summary.BasisJson.Add(verification.BasisJson);
                }
            }

            if (_artifacts.Count == 0)
            {
                if (standaloneAssertionVerification != null)
                {
                    if (!IsPassingVerification(standaloneAssertionVerification))
                    {
                        summary.Tier = "C";
                        SetFirstReason(
                            summary,
                            standaloneAssertionVerification.Error ?? "最近一次独立断言未通过。");
                    }
                    else
                    {
                        summary.Tier = NormalizeTier(standaloneAssertionVerification.Tier);
                        if (summary.Tier == "A"
                            && (!standaloneAssertionVerification.Independent || standaloneAssertionVerification.Sampled))
                        {
                            summary.Tier = "B";
                        }
                        if (summary.Tier == "B")
                        {
                            SetFirstReason(
                                summary,
                                standaloneAssertionVerification.Sampled
                                    ? "断言只进行了抽样复核，未覆盖全部数据。"
                                    : !standaloneAssertionVerification.Independent
                                        ? "断言由与原计算相同的路径复核，缺少独立复核。"
                                        : "断言的验证范围或独立性有限。");
                        }
                    }
                }
                else if (standaloneProfileVerification != null)
                {
                    summary.Tier = NormalizeTier(standaloneProfileVerification.Tier);
                    SetFirstReason(
                        summary,
                        "画像完整覆盖请求区域，但未使用独立计算后端复核。");
                }
                else
                {
                    if (_directReadResults.Count > 0
                        && _verifications.Count == 0
                        && _directReadResults.All(result => result.Succeeded && !result.IsTruncated))
                    {
                        summary.Tier = "B";
                        SetFirstReason(
                            summary,
                            "只读数据由 Excel 工具直接返回；未进行独立复核，派生计算不在此状态内。");
                    }
                    else
                    {
                        summary.Tier = "C";
                        SetFirstReason(
                            summary,
                            _directReadResults.Any(result => !result.Succeeded)
                                ? "至少一项只读数据读取未成功，不能确认完整结果。"
                                : _directReadResults.Any(result => result.IsTruncated)
                                    ? "至少一项只读数据读取被截断，不能确认完整结果。"
                                : _parseErrors.Count > 0
                                    ? _parseErrors[0]
                                    : _verifications.FirstOrDefault(verification =>
                                        !verification.IsValid || !verification.Passed)?.Error
                                        ?? "本回合调用了 Excel 数据工具，但没有登记到明确的产出项。");
                    }
                }
            }
            else if (string.IsNullOrWhiteSpace(summary.Tier))
            {
                summary.Tier = "C";
            }
            else if (string.IsNullOrWhiteSpace(summary.PrimaryReason) && summary.Tier == "C")
            {
                SetFirstReason(summary, "至少一个产出项没有可用的验证证据。");
            }
            summary.UncoveredOutputItemCount = Math.Max(
                0,
                summary.OutputItemCount - summary.CoveredOutputItemCount);
            return summary;
        }

        private bool IsSupersededEscalatedWrite(OutputArtifact artifact)
        {
            if (artifact == null
                || artifact.Succeeded
                || !string.Equals(
                    artifact.ErrorCode,
                    "WRITE_EFFECT_ESCALATED",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(artifact.TargetWorkbookFullName)
                || string.IsNullOrWhiteSpace(artifact.WorksheetName)
                || string.IsNullOrWhiteSpace(artifact.Address))
            {
                return false;
            }

            return _artifacts.Any(candidate =>
                candidate.Succeeded
                && candidate.Sequence > artifact.Sequence
                && string.Equals(candidate.ToolName, artifact.ToolName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.TargetWorkbookFullName, artifact.TargetWorkbookFullName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.WorksheetName, artifact.WorksheetName, StringComparison.OrdinalIgnoreCase)
                && AddressesMatch(candidate.Address, artifact.Address));
        }

        private VerificationRecord FindLatestApplicableVerification(
            OutputArtifact artifact,
            bool requireLinkedStepId)
        {
            return _verifications
                .Where(verification => verification.Sequence >= artifact.Sequence
                    && (!verification.VerifiesArtifactSequence.HasValue
                        || verification.VerifiesArtifactSequence.Value == artifact.Sequence)
                    && !verification.IsWorksheetStructureVerification
                    && verification.IsFormattingReadback == artifact.IsFormattingArtifact
                    && (artifact.HasInlineVerification
                        ? verification.Sequence == artifact.Sequence
                            || IsApplicableAggregateFollowUpVerification(artifact, verification)
                        : !string.IsNullOrWhiteSpace(artifact.WorksheetName)
                            && verification.WorksheetNames.Contains(artifact.WorksheetName)
                        && (!requireLinkedStepId
                            || string.IsNullOrWhiteSpace(artifact.ExecutionStepId)
                            || string.Equals(
                                verification.VerifiesStepId,
                                artifact.ExecutionStepId,
                                StringComparison.OrdinalIgnoreCase))
                            && (string.IsNullOrWhiteSpace(artifact.ExecutionStepId)
                                || string.IsNullOrWhiteSpace(artifact.Address)
                                || VerificationCoversAddress(artifact, verification)))
                    && (!artifact.IsFormattingArtifact
                        || AddressesMatch(artifact.Address, verification.Address)))
                .OrderByDescending(verification => verification.Sequence)
                .FirstOrDefault();
        }

        private bool IsApplicableAggregateFollowUpVerification(
            OutputArtifact artifact,
            VerificationRecord verification)
        {
            if (artifact == null
                || !artifact.IsAggregateResult
                || !artifact.IsCompleteUnverifiedAggregateResult
                || verification == null
                || verification.Sequence <= artifact.Sequence
                || !string.Equals(
                    verification.ToolName,
                    "excel_assert",
                    StringComparison.OrdinalIgnoreCase)
                || !verification.IsValid
                || !verification.Independent
                || verification.Sampled
                || (verification.VerifiesArtifactSequence.HasValue
                    && verification.VerifiesArtifactSequence.Value != artifact.Sequence))
            {
                return false;
            }

            var inlineVerification = _verifications.FirstOrDefault(
                item => item.Sequence == artifact.Sequence);
            if (IsPassingVerification(inlineVerification))
            {
                return false;
            }

            return MatchesAggregateUnionAssertion(artifact, verification);
        }

        private static bool MatchesAggregateUnionAssertion(
            OutputArtifact artifact,
            VerificationRecord verification)
        {
            if (artifact == null
                || verification == null
                || string.IsNullOrWhiteSpace(artifact.AggregateArgumentsJson)
                || string.IsNullOrWhiteSpace(verification.ArgumentsJson)
                || string.IsNullOrWhiteSpace(verification.BasisJson))
            {
                return false;
            }

            try
            {
                using (var aggregateDocument = JsonDocument.Parse(artifact.AggregateArgumentsJson))
                using (var assertionDocument = JsonDocument.Parse(verification.ArgumentsJson))
                using (var resultBasisDocument = JsonDocument.Parse(verification.BasisJson))
                {
                    var aggregate = aggregateDocument.RootElement;
                    var assertionPlan = assertionDocument.RootElement;
                    var resultBasis = resultBasisDocument.RootElement;
                    if (!TryGetProperty(assertionPlan, "basis", out var requestedBasis)
                        || !IsUnfilteredUnionBasis(requestedBasis)
                        || !IsUnfilteredUnionBasis(resultBasis)
                        || !TryReadHeaderMode(aggregate, out var aggregateHeaderMode)
                        || !TryReadHeaderMode(requestedBasis, out var requestedHeaderMode)
                        || !TryReadHeaderMode(resultBasis, out var resultHeaderMode)
                        || !string.Equals(
                            aggregateHeaderMode,
                            requestedHeaderMode,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(
                            aggregateHeaderMode,
                            resultHeaderMode,
                            StringComparison.OrdinalIgnoreCase)
                        || !TryReadAggregateUnionSources(aggregate, out var aggregateSources)
                        || !TryReadBasisUnionSources(requestedBasis, out var requestedSources)
                        || !TryReadBasisUnionSources(resultBasis, out var resultSources)
                        || !aggregateSources.SetEquals(requestedSources)
                        || !aggregateSources.SetEquals(resultSources)
                        || !TryReadSingleAggregateMeasure(aggregate, out var measureColumn, out var measureFunction)
                        || !HasMatchingUnionScalarAssertion(
                            assertionPlan,
                            aggregateSources,
                            measureColumn,
                            measureFunction))
                    {
                        return false;
                    }

                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsUnfilteredUnionBasis(JsonElement basis)
        {
            return TryReadString(basis, "topology", out var topology)
                && string.Equals(topology, "union", StringComparison.OrdinalIgnoreCase)
                && HasNoItems(basis, "filters")
                && HasNoItems(basis, "groupBy")
                && HasNoItems(basis, "derivedGroupBy")
                && HasNoItems(basis, "lookups")
                && HasNoItems(basis, "keys");
        }

        private static bool HasNoItems(JsonElement owner, string propertyName)
        {
            if (!TryGetProperty(owner, propertyName, out var value)
                || value.ValueKind == JsonValueKind.Null
                || value.ValueKind == JsonValueKind.Undefined)
            {
                return true;
            }

            return value.ValueKind == JsonValueKind.Array
                && value.GetArrayLength() == 0;
        }

        private static bool TryReadHeaderMode(JsonElement owner, out string headerMode)
        {
            if (TryReadString(owner, "headerMode", out headerMode)
                && !string.IsNullOrWhiteSpace(headerMode))
            {
                headerMode = headerMode.Trim();
                return true;
            }

            headerMode = string.Empty;
            return false;
        }

        private static bool TryReadAggregateUnionSources(
            JsonElement aggregate,
            out HashSet<string> sources)
        {
            return TryReadUnionSources(
                aggregate,
                "sources",
                "worksheetName",
                "address",
                out sources);
        }

        private static bool TryReadBasisUnionSources(
            JsonElement basis,
            out HashSet<string> sources)
        {
            return TryReadUnionSources(
                basis,
                "sources",
                "sheet",
                "range",
                out sources);
        }

        private static bool TryReadUnionSources(
            JsonElement owner,
            string propertyName,
            string sheetPropertyName,
            string rangePropertyName,
            out HashSet<string> sources)
        {
            sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TryGetProperty(owner, propertyName, out var sourceArray)
                || sourceArray.ValueKind != JsonValueKind.Array
                || sourceArray.GetArrayLength() < 2)
            {
                return false;
            }

            foreach (var source in sourceArray.EnumerateArray())
            {
                var sheet = ReadString(source, sheetPropertyName).Trim();
                var range = ReadString(source, rangePropertyName).Trim();
                var alias = ReadString(source, "alias").Trim();
                if (string.IsNullOrWhiteSpace(sheet)
                    || string.IsNullOrWhiteSpace(range)
                    || string.IsNullOrWhiteSpace(alias))
                {
                    return false;
                }

                var signature = NormalizeWorksheetName(sheet)
                    + "\u001f" + NormalizeAddress(range)
                    + "\u001f" + alias;
                if (!sources.Add(signature))
                {
                    return false;
                }
            }

            return sources.Count == sourceArray.GetArrayLength();
        }

        private static bool TryReadSingleAggregateMeasure(
            JsonElement aggregate,
            out string column,
            out string function)
        {
            column = string.Empty;
            function = string.Empty;
            if (!HasNoItems(aggregate, "filters")
                || !HasNoItems(aggregate, "groupBy")
                || !HasNoItems(aggregate, "derivedGroupBy")
                || !HasNoItems(aggregate, "lookups")
                || !TryGetProperty(aggregate, "measures", out var measures)
                || measures.ValueKind != JsonValueKind.Array
                || measures.GetArrayLength() != 1)
            {
                return false;
            }

            var measure = measures[0];
            column = ReadString(measure, "column").Trim();
            function = ReadString(measure, "function").Trim();
            return !string.IsNullOrWhiteSpace(column)
                && !string.IsNullOrWhiteSpace(function);
        }

        private static bool HasMatchingUnionScalarAssertion(
            JsonElement assertionPlan,
            ISet<string> sourceSignatures,
            string measureColumn,
            string measureFunction)
        {
            if (!TryGetProperty(assertionPlan, "assertions", out var assertions)
                || assertions.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var sourceWorksheets = new HashSet<string>(
                sourceSignatures
                    .Select(signature => signature.Split('\u001f')[0]),
                StringComparer.OrdinalIgnoreCase);
            foreach (var assertion in assertions.EnumerateArray())
            {
                if (!string.Equals(
                        ReadString(assertion, "kind"),
                        "scalar",
                        StringComparison.OrdinalIgnoreCase)
                    || TryGetProperty(assertion, "expectedValue", out _)
                    || !TryGetProperty(assertion, "source", out var source)
                    || !TryGetProperty(assertion, "target", out var target))
                {
                    continue;
                }

                var sourceColumn = ReadString(source, "column").Trim();
                var sourceFunction = ReadString(source, "function").Trim();
                var targetColumn = ReadString(target, "column").Trim();
                var targetFunction = ReadString(target, "function").Trim();
                var targetWorksheet = ReadString(target, "worksheetName").Trim();
                var targetAddress = ReadString(target, "address").Trim();
                if (!string.Equals(sourceColumn, measureColumn, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(sourceFunction, measureFunction, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(targetColumn, measureColumn, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(targetFunction, measureFunction, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(targetWorksheet)
                    || string.IsNullOrWhiteSpace(targetAddress)
                    || sourceWorksheets.Contains(targetWorksheet)
                    || !string.IsNullOrWhiteSpace(ReadString(source, "worksheetName"))
                    || !string.IsNullOrWhiteSpace(ReadString(source, "address"))
                    || !string.IsNullOrWhiteSpace(ReadString(source, "tableName")))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool IsPassingVerification(VerificationRecord verification)
        {
            return verification != null
                && verification.IsValid
                && verification.Passed
                && (string.Equals(verification.Tier, "A", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(verification.Tier, "B", StringComparison.OrdinalIgnoreCase));
        }

        private static bool VerificationCoversAddress(
            OutputArtifact artifact,
            VerificationRecord verification)
        {
            if (artifact == null || verification == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(artifact.Address))
            {
                return true;
            }

            if (verification.Addresses != null && verification.Addresses.Count > 0)
            {
                return verification.Addresses.Any(address => AddressesMatch(artifact.Address, address));
            }

            return string.IsNullOrWhiteSpace(verification.Address)
                || AddressesMatch(artifact.Address, verification.Address);
        }

        private static HashSet<string> ReadVerificationAddresses(
            string toolName,
            string argumentsJson)
        {
            var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return addresses;
            }

            try
            {
                using (var document = JsonDocument.Parse(argumentsJson))
                {
                    var root = document.RootElement;
                    if (string.Equals(toolName, "excel_assert", StringComparison.OrdinalIgnoreCase)
                        && TryGetProperty(root, "assertions", out var assertions)
                        && assertions.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var assertion in assertions.EnumerateArray())
                        {
                            if (TryGetProperty(assertion, "target", out var target))
                            {
                                AddAddress(addresses, ReadString(target, "address"));
                            }
                        }
                    }
                    else if (string.Equals(toolName, "excel_reconcile", StringComparison.OrdinalIgnoreCase))
                    {
                        AddAddress(addresses, ReadString(root, "summaryAddress"));
                    }
                    else if (string.Equals(toolName, "excel_compare_key_sets", StringComparison.OrdinalIgnoreCase)
                        && TryGetProperty(root, "target", out var target))
                    {
                        AddAddress(addresses, ReadString(target, "address"));
                    }
                    else
                    {
                        AddAddress(addresses, ReadString(root, "address"));
                    }
                }
            }
            catch (JsonException)
            {
                return addresses;
            }

            return addresses;
        }

        private static void AddAddress(ISet<string> addresses, string address)
        {
            if (addresses != null && !string.IsNullOrWhiteSpace(address))
            {
                addresses.Add(address.Trim());
            }
        }

        private static VerificationReadResult ReadFormulaValidation(
            string structuredContentJson,
            int sequence,
            bool succeeded)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return new VerificationReadResult
                {
                    Error = "Formula validation returned no structured result."
                };
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    var root = document.RootElement;
                    var hasWorksheet = TryReadString(root, "WorksheetName", out var worksheet)
                        || TryReadString(root, "worksheetName", out worksheet);
                    var hasAddress = TryReadString(root, "Address", out var address)
                        || TryReadString(root, "address", out address);
                    var hasIsValid = TryReadBoolean(root, "IsValid", out var isValid)
                        || TryReadBoolean(root, "isValid", out isValid);
                    var hasFormulaCount = TryReadInt32(root, "FormulaCount", out var formulaCount)
                        || TryReadInt32(root, "formulaCount", out formulaCount);
                    var hasValidCount = TryReadInt32(root, "ValidCount", out var validCount)
                        || TryReadInt32(root, "validCount", out validCount);
                    var hasErrorCount = TryReadInt32(root, "ErrorCount", out var errorCount)
                        || TryReadInt32(root, "errorCount", out errorCount);
                    var hasTruncated = TryReadBoolean(root, "IsTruncated", out var isTruncated)
                        || TryReadBoolean(root, "isTruncated", out isTruncated);
                    var payloadValid = hasWorksheet
                        && !string.IsNullOrWhiteSpace(worksheet)
                        && hasAddress
                        && !string.IsNullOrWhiteSpace(address)
                        && hasIsValid
                        && hasFormulaCount
                        && formulaCount >= 0
                        && hasValidCount
                        && validCount >= 0
                        && hasErrorCount
                        && errorCount >= 0
                        && hasTruncated;
                    var passed = succeeded
                        && payloadValid
                        && isValid
                        && !isTruncated
                        && formulaCount > 0
                        && validCount == formulaCount
                        && errorCount == 0;
                    var record = new VerificationRecord
                    {
                        Sequence = sequence,
                        Tier = "B",
                        Passed = passed,
                        AssertionCount = payloadValid ? 1 : 0,
                        CoveredRows = payloadValid ? CountAddressRows(address) : 0,
                        Independent = false,
                        Sampled = false,
                        Producer = "excel_validate_range_formulas",
                        WorksheetNames = new HashSet<string>(
                            payloadValid
                                ? new[] { NormalizeWorksheetName(worksheet) }
                                : Enumerable.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase),
                        IsValid = payloadValid,
                        Error = payloadValid
                            ? passed ? null : "公式验证未完整通过：可能没有公式、存在公式错误或扫描被截断。"
                            : "Formula validation returned an incomplete result.",
                        Address = payloadValid ? address : string.Empty,
                        Addresses = new HashSet<string>(
                            payloadValid ? new[] { address } : Enumerable.Empty<string>(),
                            StringComparer.OrdinalIgnoreCase)
                    };
                    return new VerificationReadResult { Record = record };
                }
            }
            catch (JsonException exception)
            {
                return new VerificationReadResult
                {
                    Error = "Unable to parse formula validation result: " + exception.Message
                };
            }
        }

        private OutputArtifact FindMatchingFormatArtifact(string argumentsJson)
        {
            var worksheet = ReadStringProperty(argumentsJson, "worksheetName");
            var address = ReadAddress(argumentsJson);
            if (string.IsNullOrWhiteSpace(worksheet) || string.IsNullOrWhiteSpace(address))
            {
                return null;
            }

            return _artifacts
                .Where(artifact => artifact.IsFormattingArtifact
                    && artifact.Succeeded
                    && artifact.Sequence <= _sequence
                    && string.Equals(
                        artifact.WorksheetName,
                        NormalizeWorksheetName(worksheet),
                        StringComparison.OrdinalIgnoreCase)
                    && AddressesMatch(artifact.Address, address))
                .OrderByDescending(artifact => artifact.Sequence)
                .FirstOrDefault();
        }

        private OutputArtifact FindMatchingSingleCellWriteArtifact(string argumentsJson)
        {
            var worksheet = ReadStringProperty(argumentsJson, "worksheetName");
            var address = ReadAddress(argumentsJson);
            if (string.IsNullOrWhiteSpace(worksheet)
                || !IsSingleCellAddress(address))
            {
                return null;
            }

            return _artifacts
                .Where(artifact => artifact.Succeeded
                    && !artifact.IsFormattingArtifact
                    && string.Equals(
                        artifact.ToolName,
                        "excel_write_range",
                        StringComparison.OrdinalIgnoreCase)
                    && artifact.Sequence <= _sequence
                    && string.Equals(
                        artifact.WorksheetName,
                        NormalizeWorksheetName(worksheet),
                        StringComparison.OrdinalIgnoreCase)
                    && AddressesMatch(artifact.Address, address)
                    && IsSingleTextWrite(artifact.WriteArgumentsJson))
                .OrderByDescending(artifact => artifact.Sequence)
                .FirstOrDefault();
        }

        private static VerificationRecord ReadSingleCellWriteReadback(
            string argumentsJson,
            string structuredContentJson,
            int sequence,
            OutputArtifact artifact,
            string verificationTargetStepId,
            bool succeeded)
        {
            var record = new VerificationRecord
            {
                Sequence = sequence,
                ToolName = "excel_read_range",
                Tier = "B",
                Independent = false,
                Sampled = false,
                Producer = "excel_read_range",
                WorksheetNames = new HashSet<string>(
                    new[] { artifact.WorksheetName },
                    StringComparer.OrdinalIgnoreCase),
                Address = ReadAddress(argumentsJson),
                Addresses = new HashSet<string>(
                    new[] { ReadAddress(argumentsJson) },
                    StringComparer.OrdinalIgnoreCase),
                VerifiesStepId = verificationTargetStepId ?? string.Empty,
                VerifiesArtifactSequence = artifact.Sequence,
                AssertionCount = 1,
                CoveredRows = 1
            };

            if (!succeeded)
            {
                record.Error = "单格回读工具未成功，无法验证写入值。";
                return record;
            }

            try
            {
                using (var writeDocument = JsonDocument.Parse(artifact.WriteArgumentsJson))
                using (var readDocument = JsonDocument.Parse(structuredContentJson ?? string.Empty))
                {
                    var writeRoot = writeDocument.RootElement;
                    var readRoot = readDocument.RootElement;
                    var expectedWorksheet = ReadString(writeRoot, "worksheetName");
                    var expectedAddress = ReadAddress(artifact.WriteArgumentsJson);
                    var requestedWorksheet = ReadStringProperty(argumentsJson, "worksheetName");
                    var requestedAddress = ReadAddress(argumentsJson);
                    var writeResultWorksheet = ReadResultWorksheet(
                        artifact.ToolName,
                        artifact.WriteResultJson);
                    var writeResultAddress = ReadResultAddress(artifact.WriteResultJson);
                    var resultWorksheet = ReadString(readRoot, "WorksheetName");
                    var resultAddress = ReadString(readRoot, "Address");
                    var resultWorkbook = ReadString(readRoot, "WorkbookName");
                    var hasExpectedValue = TryGetProperty(writeRoot, "value", out var expectedValue)
                        && expectedValue.ValueKind == JsonValueKind.String;
                    var hasRows = TryGetProperty(readRoot, "Rows", out var rows)
                        && rows.ValueKind == JsonValueKind.Array
                        && rows.GetArrayLength() == 1
                        && rows[0].ValueKind == JsonValueKind.Array
                        && rows[0].GetArrayLength() == 1;
                    var hasReadShape = TryReadInt32(readRoot, "RowCount", out var rowCount)
                            && rowCount == 1
                        && TryReadInt32(readRoot, "ColumnCount", out var columnCount)
                            && columnCount == 1
                        && TryReadBoolean(readRoot, "IsTruncated", out var isTruncated)
                            && !isTruncated;
                    var rangeMatches = string.Equals(
                            NormalizeWorksheetName(expectedWorksheet),
                            NormalizeWorksheetName(requestedWorksheet),
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            NormalizeWorksheetName(expectedWorksheet),
                            NormalizeWorksheetName(resultWorksheet),
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            NormalizeWorksheetName(expectedWorksheet),
                            NormalizeWorksheetName(writeResultWorksheet),
                            StringComparison.OrdinalIgnoreCase)
                        && AddressesMatch(expectedAddress, requestedAddress)
                        && AddressesMatch(expectedAddress, writeResultAddress)
                        && AddressesMatch(expectedAddress, resultAddress);
                    var sameWorkbook = !string.IsNullOrWhiteSpace(artifact.WorkbookName)
                        && string.Equals(
                            artifact.WorkbookName,
                            resultWorkbook,
                            StringComparison.OrdinalIgnoreCase);
                    var payloadValid = hasExpectedValue
                        && hasRows
                        && hasReadShape
                        && rangeMatches
                        && sameWorkbook;
                    var valueMatches = payloadValid
                        && string.Equals(
                            expectedValue.GetString(),
                            rows[0][0].ValueKind == JsonValueKind.String
                                ? rows[0][0].GetString()
                                : null,
                            StringComparison.Ordinal);

                    record.IsValid = payloadValid;
                    record.Passed = valueMatches;
                    record.Error = valueMatches
                        ? null
                        : payloadValid
                            ? "单格回读值与写入值不一致。"
                            : "单格回读结果缺少完整范围或值证据。";
                }
            }
            catch (JsonException)
            {
                record.IsValid = false;
                record.Passed = false;
                record.Error = "无法解析单格写入或回读结果。";
            }

            return record;
        }

        private static bool IsSingleTextWrite(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson)
                || !IsSingleCellAddress(ReadAddress(argumentsJson)))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(argumentsJson))
                {
                    return TryGetProperty(document.RootElement, "value", out var value)
                        && value.ValueKind == JsonValueKind.String;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsSingleCellAddress(string address)
        {
            return Regex.IsMatch(
                NormalizeAddress(address),
                @"^[A-Z]{1,3}\d+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static VerificationRecord ReadFormatReadback(
            string argumentsJson,
            string structuredContentJson,
            int sequence,
            OutputArtifact artifact,
            bool succeeded,
            string verificationTargetStepId)
        {
            if (artifact == null || string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return null;
            }

            try
            {
                var expected = ReadFormatExpectations(
                    artifact.FormatArgumentsJson,
                    out var completeExpectation);
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    var root = document.RootElement;
                    var worksheet = ReadString(root, "WorksheetName");
                    var address = ReadString(root, "Address");
                    var requestedWorksheet = ReadStringProperty(argumentsJson, "worksheetName");
                    var requestedAddress = ReadAddress(argumentsJson);
                    var hasCellCount = TryReadInt32(root, "CellCount", out var cellCount);
                    var passed = succeeded
                        && completeExpectation
                        && expected.Count > 0
                        && string.Equals(
                            NormalizeWorksheetName(worksheet),
                            NormalizeWorksheetName(requestedWorksheet),
                            StringComparison.OrdinalIgnoreCase)
                        && AddressesMatch(address, requestedAddress)
                        && AddressesMatch(address, artifact.Address)
                        && hasCellCount
                        && cellCount > 0;

                    foreach (var property in expected)
                    {
                        if (!TryReadString(root, property.Key, out var actual)
                            || string.Equals(actual, "mixed", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(actual, "unknown", StringComparison.OrdinalIgnoreCase)
                            || !ValuesMatch(property.Value, actual))
                        {
                            passed = false;
                            break;
                        }
                    }

                    return new VerificationRecord
                    {
                        Sequence = sequence,
                        ToolName = "excel_get_range_style",
                        Tier = "B",
                        Passed = passed,
                        AssertionCount = expected.Count,
                        CoveredRows = CountAddressRows(address),
                        Independent = false,
                        Sampled = false,
                        Producer = "excel_get_range_style",
                        WorksheetNames = new HashSet<string>(
                            new[] { NormalizeWorksheetName(worksheet) },
                            StringComparer.OrdinalIgnoreCase),
                        IsValid = passed,
                        Error = passed ? null : "格式回读未能完整匹配写入请求的属性或区域。",
                        IsFormattingReadback = true,
                        Address = address,
                        Addresses = new HashSet<string>(
                            new[] { address },
                            StringComparer.OrdinalIgnoreCase),
                        VerifiesStepId = verificationTargetStepId ?? string.Empty
                    };
                }
            }
            catch (JsonException)
            {
                return new VerificationRecord
                {
                    Sequence = sequence,
                    ToolName = "excel_get_range_style",
                    Tier = "C",
                    Passed = false,
                    Independent = false,
                    Sampled = false,
                    Producer = "excel_get_range_style",
                    WorksheetNames = new HashSet<string>(
                        new[] { artifact.WorksheetName },
                        StringComparer.OrdinalIgnoreCase),
                    IsValid = false,
                    Error = "无法解析格式回读结果。",
                    IsFormattingReadback = true,
                    Address = artifact.Address,
                    Addresses = new HashSet<string>(
                        new[] { artifact.Address },
                        StringComparer.OrdinalIgnoreCase),
                    VerifiesStepId = verificationTargetStepId ?? string.Empty
                };
            }
        }

        private static Dictionary<string, string> ReadFormatExpectations(
            string argumentsJson,
            out bool complete)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            complete = false;
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return values;
            }

            using (var document = JsonDocument.Parse(argumentsJson))
            {
                var root = document.RootElement;
                complete = root.ValueKind == JsonValueKind.Object;
                if (!complete)
                {
                    return values;
                }

                if (TryGetProperty(root, "wrapText", out _)
                    || TryGetProperty(root, "shrinkToFit", out _))
                {
                    complete = false;
                }

                foreach (var property in FormatReadbackProperties)
                {
                    if (TryGetProperty(root, property.Key, out var value)
                        && TryReadScalarText(value, out var text))
                    {
                        values.Add(property.Value, text);
                    }
                }
            }

            return values;
        }

        private static bool ValuesMatch(string expected, string actual)
        {
            return string.Equals(
                (expected ?? string.Empty).Trim(),
                (actual ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadScalarText(JsonElement value, out string text)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    text = value.GetString() ?? string.Empty;
                    return true;
                case JsonValueKind.True:
                    text = "true";
                    return true;
                case JsonValueKind.False:
                    text = "false";
                    return true;
                case JsonValueKind.Number:
                    text = value.GetRawText();
                    return true;
                default:
                    text = string.Empty;
                    return false;
            }
        }

        private static bool AddressesMatch(string left, string right)
        {
            return string.Equals(
                NormalizeAddress(left),
                NormalizeAddress(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAddress(string address)
        {
            return string.IsNullOrWhiteSpace(address)
                ? string.Empty
                : address.Replace("$", string.Empty).Replace(" ", string.Empty).Trim();
        }

        private static long CountAddressRows(string address)
        {
            var match = Regex.Match(
                NormalizeAddress(address),
                @"^[A-Z]{1,3}(?<start>\d+)(?::[A-Z]{1,3}(?<end>\d+))?$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success
                || !long.TryParse(
                    match.Groups["start"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var startRow))
            {
                return 0;
            }

            if (!match.Groups["end"].Success)
            {
                return 1;
            }

            if (!long.TryParse(
                    match.Groups["end"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var endRow))
            {
                return 0;
            }

            return Math.Abs(endRow - startRow) + 1;
        }

        private static string ReadAddress(string argumentsJson)
        {
            return ReadStringProperty(argumentsJson, "address");
        }

        private static string ReadArtifactAddress(string toolName, string argumentsJson)
        {
            if (!string.Equals(toolName, "excel_execute_vba", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(argumentsJson))
            {
                return ReadAddress(argumentsJson);
            }

            try
            {
                using (var document = JsonDocument.Parse(argumentsJson))
                {
                    var root = document.RootElement;
                    var outputSheet = ReadString(root, "outputSheet");
                    if (string.IsNullOrWhiteSpace(outputSheet)
                        || !TryGetProperty(root, "declaredWriteTargets", out var targets)
                        || targets.ValueKind != JsonValueKind.Array
                        || targets.GetArrayLength() != 1)
                    {
                        return string.Empty;
                    }

                    var target = targets[0];
                    return string.Equals(
                            ReadString(target, "sheet"),
                            outputSheet,
                            StringComparison.OrdinalIgnoreCase)
                        ? ReadString(target, "range")
                        : string.Empty;
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string ReadStringProperty(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            try
            {
                using (var document = JsonDocument.Parse(json))
                {
                    return ReadString(document.RootElement, propertyName);
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static bool IsExcelDataTool(string toolName)
        {
            return toolName.StartsWith("excel_", StringComparison.OrdinalIgnoreCase)
                && !NonDataToolNames.Contains(toolName);
        }

        private static List<string> ReadRelatedWorksheets(
            string toolName,
            string argumentsJson,
            bool isWriteTool,
            out string error)
        {
            error = null;
            var worksheets = new List<string>();
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return worksheets;
            }

            try
            {
                using (var document = JsonDocument.Parse(argumentsJson))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        error = "Excel tool arguments were not a JSON object for " + toolName + ".";
                        return worksheets;
                    }

                    if (string.Equals(toolName, "excel_assert", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetProperty(root, "assertions", out var assertions)
                            && assertions.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var assertion in assertions.EnumerateArray())
                            {
                                if (TryGetProperty(assertion, "target", out var target))
                                {
                                    AddWorksheet(worksheets, ReadString(target, "worksheetName"));
                                }
                            }
                        }
                    }
                    else if (string.Equals(toolName, "excel_reconcile", StringComparison.OrdinalIgnoreCase))
                    {
                        AddWorksheet(worksheets, ReadString(root, "summaryWorksheetName"));
                    }
                    else if (string.Equals(toolName, "excel_compare_key_sets", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetProperty(root, "target", out var target))
                        {
                            AddWorksheet(worksheets, ReadString(target, "worksheetName"));
                        }
                        if (TryGetProperty(root, "source", out var source))
                        {
                            AddWorksheet(worksheets, ReadString(source, "worksheetName"));
                        }
                    }
                    else if (string.Equals(toolName, "excel_execute_vba", StringComparison.OrdinalIgnoreCase))
                    {
                        AddWorksheet(worksheets, ReadString(root, "outputSheet"));
                    }
                    else if ((isWriteTool
                            || ArtifactToolNames.Contains(toolName)
                            || string.Equals(
                                toolName,
                                "excel_get_range_info",
                                StringComparison.OrdinalIgnoreCase))
                        && !string.Equals(toolName, "excel_aggregate", StringComparison.OrdinalIgnoreCase))
                    {
                        var worksheet = ReadString(root, "worksheetName");
                        if (string.IsNullOrWhiteSpace(worksheet))
                        {
                            worksheet = ReadString(root, "outputSheet");
                        }
                        AddWorksheet(worksheets, worksheet);
                    }
                }
            }
            catch (JsonException exception)
            {
                error = "Unable to parse Excel tool arguments for "
                    + toolName + ": " + exception.Message;
            }

            return worksheets;
        }

        private static string ReadResultWorksheet(string toolName, string structuredContentJson)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return string.Empty;
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    var root = document.RootElement;
                    var worksheetName = string.Equals(
                        toolName,
                        "excel_execute_vba",
                        StringComparison.OrdinalIgnoreCase)
                        ? ReadString(root, "outputSheet")
                        : ReadString(root, "WorksheetName");
                    if (string.IsNullOrWhiteSpace(worksheetName))
                    {
                        worksheetName = string.Equals(
                            toolName,
                            "excel_execute_vba",
                            StringComparison.OrdinalIgnoreCase)
                            ? ReadString(root, "OutputSheet")
                            : ReadString(root, "worksheetName");
                    }
                    if (string.IsNullOrWhiteSpace(worksheetName))
                    {
                        worksheetName = ReadString(root, "outputSheet");
                    }
                    return NormalizeWorksheetName(worksheetName);
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string ReadResultWorkbookName(string structuredContentJson)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return string.Empty;
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    return ReadString(document.RootElement, "WorkbookName");
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string ReadWriteEffectTargetWorkbookFullName(string structuredContentJson)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return string.Empty;
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    return TryGetProperty(document.RootElement, "writeEffect", out var writeEffect)
                        ? ReadString(writeEffect, "targetWorkbookFullName")
                        : string.Empty;
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string ReadResultAddress(string structuredContentJson)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return string.Empty;
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    return ReadString(document.RootElement, "Address");
                }
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static VerificationReadResult ReadProfileVerification(
            string argumentsJson,
            string structuredContentJson,
            int sequence,
            IEnumerable<string> targetWorksheets,
            bool succeeded)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson)
                || string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return new VerificationReadResult
                {
                    Error = "excel_profile_range returned no structured profile result."
                };
            }

            try
            {
                using (var argumentsDocument = JsonDocument.Parse(argumentsJson))
                using (var resultDocument = JsonDocument.Parse(structuredContentJson))
                {
                    var arguments = argumentsDocument.RootElement;
                    var root = resultDocument.RootElement;
                    var requestedSourceCount = 0;
                    var hasSourceList = TryGetProperty(arguments, "sources", out var requestedSources)
                        && requestedSources.ValueKind == JsonValueKind.Array;
                    if (hasSourceList)
                    {
                        requestedSourceCount = requestedSources.GetArrayLength();
                    }

                    var sourceProfilesValid = true;
                    var coveredRows = 0L;
                    var hasRootCoverage = TryReadCompleteCoverage(
                        root,
                        out var totalRows,
                        out coveredRows);
                    var hasTruncationState = TryReadBoolean(root, "isTruncated", out var isTruncated);
                    if (hasSourceList && requestedSourceCount > 0)
                    {
                        sourceProfilesValid = TryGetProperty(root, "sourceProfiles", out var sourceProfiles)
                            && sourceProfiles.ValueKind == JsonValueKind.Array
                            && sourceProfiles.GetArrayLength() == requestedSourceCount;
                        if (sourceProfilesValid)
                        {
                            foreach (var profile in sourceProfiles.EnumerateArray())
                            {
                                var profileIsTruncated = !TryReadBoolean(
                                    profile,
                                    "isTruncated",
                                    out var sourceIsTruncated)
                                    || sourceIsTruncated;
                                sourceProfilesValid &= !profileIsTruncated
                                    && TryReadCompleteCoverage(
                                        profile,
                                        out _,
                                        out _);
                            }
                        }

                        if (requestedSourceCount > 1)
                        {
                            sourceProfilesValid &= TryGetProperty(
                                    root,
                                    "headerConsistency",
                                    out var headerConsistency)
                                && headerConsistency.ValueKind == JsonValueKind.Object
                                && TryReadBoolean(
                                    headerConsistency,
                                    "namesConsistent",
                                    out _)
                                && TryReadBoolean(
                                    headerConsistency,
                                    "orderConsistent",
                                    out _);
                        }
                    }

                    var isValid = root.ValueKind == JsonValueKind.Object
                        && hasRootCoverage
                        && hasTruncationState
                        && sourceProfilesValid;
                    var passed = succeeded
                        && isValid
                        && !isTruncated;
                    var record = new VerificationRecord
                    {
                        Sequence = sequence,
                        Tier = passed ? "B" : "C",
                        Passed = passed,
                        AssertionCount = hasSourceList && requestedSourceCount > 0
                            ? requestedSourceCount
                            : 1,
                        CoveredRows = coveredRows,
                        Independent = false,
                        Sampled = false,
                        Producer = "excel_profile_range",
                        WorksheetNames = new HashSet<string>(
                            (targetWorksheets ?? Enumerable.Empty<string>())
                                .Select(NormalizeWorksheetName)
                                .Where(name => !string.IsNullOrWhiteSpace(name)),
                            StringComparer.OrdinalIgnoreCase),
                        IsValid = isValid,
                        Error = isValid
                            ? (passed ? null : "excel_profile_range did not scan all requested rows.")
                            : "excel_profile_range returned an incomplete profile structure."
                    };

                    return new VerificationReadResult { Record = record };
                }
            }
            catch (JsonException exception)
            {
                return new VerificationReadResult
                {
                    Error = "Unable to parse excel_profile_range result: " + exception.Message
                };
            }
        }

        private static bool HasCompleteUnverifiedAggregateResult(string structuredContentJson)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    var root = document.RootElement;
                    if (!TryReadBoolean(root, "isTruncated", out var isTruncated)
                        || isTruncated
                        || !TryReadCompleteCoverage(root, out var totalRows, out var scannedRows)
                        || totalRows != scannedRows
                        || !TryGetProperty(root, "rows", out var rows)
                        || rows.ValueKind != JsonValueKind.Array
                        || !TryGetProperty(root, ToolResultVerificationFields.Verification, out var verification)
                        || !TryReadString(verification, ToolResultVerificationFields.Tier, out var tier)
                        || !string.Equals(tier, "C", StringComparison.OrdinalIgnoreCase)
                        || !TryReadBoolean(verification, ToolResultVerificationFields.Passed, out var passed)
                        || passed
                        || !TryReadInt32(
                            verification,
                            ToolResultVerificationFields.AssertionCount,
                            out var assertionCount)
                        || assertionCount != 0
                        || !TryReadInt64(
                            verification,
                            ToolResultVerificationFields.CoveredRows,
                            out var verificationCoveredRows)
                        || verificationCoveredRows != 0
                        || !TryGetProperty(verification, ToolResultVerificationFields.Details, out _))
                    {
                        return false;
                    }

                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryReadCompleteCoverage(
            JsonElement owner,
            out long totalRows,
            out long scannedRows)
        {
            totalRows = 0;
            scannedRows = 0;
            return TryGetProperty(owner, "coverage", out var coverage)
                && TryReadInt64(coverage, "totalRows", out totalRows)
                && TryReadInt64(coverage, "scannedRows", out scannedRows)
                && totalRows >= 0
                && scannedRows == totalRows;
        }

        private static VerificationReadResult ReadVerification(
            string toolName,
            string structuredContentJson,
            int sequence,
            IEnumerable<string> targetWorksheets,
            bool expectedHiddenRowsScan)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return new VerificationReadResult
                {
                    Error = "Tool " + toolName + " returned no structured verification result."
                };
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    if (!TryGetProperty(
                            document.RootElement,
                            ToolResultVerificationFields.Verification,
                            out var envelope)
                        || envelope.ValueKind != JsonValueKind.Object)
                    {
                        return new VerificationReadResult
                        {
                            Error = "Tool " + toolName + " did not return a canonical verification envelope."
                        };
                    }

                    var isKeySetDifference = string.Equals(
                        toolName,
                        "excel_compare_key_sets",
                        StringComparison.OrdinalIgnoreCase);
                    var isHiddenRowsScan = false;
                    if (TryReadString(
                            envelope,
                            ToolResultVerificationFields.Operation,
                            out var operation))
                    {
                        isKeySetDifference |= string.Equals(
                            operation,
                            "keySetDifference",
                            StringComparison.OrdinalIgnoreCase);
                        isHiddenRowsScan = string.Equals(
                                toolName,
                                "excel_get_range_info",
                                StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                operation,
                                "hiddenRowsScan",
                                StringComparison.OrdinalIgnoreCase);
                    }

                    var record = new VerificationRecord
                    {
                        Sequence = sequence,
                        IsKeySetDifference = isKeySetDifference,
                        IsHiddenRowsScan = isHiddenRowsScan,
                        WorksheetNames = new HashSet<string>(
                            (targetWorksheets ?? Enumerable.Empty<string>())
                                .Select(NormalizeWorksheetName)
                                .Where(name => !string.IsNullOrWhiteSpace(name)),
                            StringComparer.OrdinalIgnoreCase)
                    };
                    var hasTier = TryReadString(envelope, ToolResultVerificationFields.Tier, out var tier);
                    var hasPassed = TryReadBoolean(envelope, ToolResultVerificationFields.Passed, out var passed);
                    var hasAssertionCount = TryReadInt32(
                        envelope,
                        ToolResultVerificationFields.AssertionCount,
                        out var assertionCount);
                    var hasCoveredRows = TryReadInt64(
                        envelope,
                        ToolResultVerificationFields.CoveredRows,
                        out var coveredRows);
                    var hasIndependent = TryReadBoolean(
                        envelope,
                        ToolResultVerificationFields.Independent,
                        out var independent);
                    var hasSampled = TryReadBoolean(envelope, ToolResultVerificationFields.Sampled, out var sampled);
                    var hasProducer = TryReadString(envelope, ToolResultVerificationFields.Producer, out var producer);
                    var hasNotes = TryGetProperty(
                            envelope,
                            ToolResultVerificationFields.Notes,
                            out var notes)
                        && notes.ValueKind == JsonValueKind.Array;
                    var hasDetails = TryGetProperty(
                            envelope,
                            ToolResultVerificationFields.Details,
                            out var details)
                        && details.ValueKind == JsonValueKind.Object;
                    record.Tier = tier;
                    record.Passed = passed;
                    record.AssertionCount = assertionCount;
                    record.CoveredRows = coveredRows;
                    record.Independent = independent;
                    record.Sampled = sampled;
                    record.Producer = producer;
                    record.IsValid = hasTier
                        && IsKnownTier(record.Tier)
                        && hasPassed
                        && hasAssertionCount
                        && record.AssertionCount >= 0
                        && hasCoveredRows
                        && record.CoveredRows >= 0
                        && hasIndependent
                        && hasSampled
                        && hasProducer
                        && !string.IsNullOrWhiteSpace(record.Producer)
                        && hasNotes
                        && hasDetails
                        && (!expectedHiddenRowsScan
                            || (record.IsHiddenRowsScan
                                && HasCompleteHiddenRowsScanPayload(
                                    document.RootElement,
                                    record.CoveredRows)));
                    if (!record.IsValid)
                    {
                        record.Error = "Tool " + toolName + " returned an incomplete verification envelope.";
                    }

                    if (TryGetProperty(envelope, ToolResultVerificationFields.Basis, out var basis)
                        && basis.ValueKind == JsonValueKind.Object)
                    {
                        record.BasisJson = basis.GetRawText();
                    }

                    if (TryGetProperty(envelope, ToolResultVerificationFields.Details, out details)
                        && TryGetProperty(details, "items", out var items)
                        && items.ValueKind == JsonValueKind.Array
                        && !record.IsKeySetDifference)
                    {
                        if (!record.IsHiddenRowsScan)
                        {
                            record.AssertionCount = Math.Max(
                                record.AssertionCount,
                                items.GetArrayLength());
                        }
                    }

                    return new VerificationReadResult { Record = record };
                }
            }
            catch (JsonException exception)
            {
                return new VerificationReadResult
                {
                    Error = "Unable to parse verification envelope from "
                        + toolName + ": " + exception.Message
                };
            }
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                return element.TryGetProperty(name, out value);
            }

            value = default(JsonElement);
            return false;
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            return TryReadString(element, propertyName, out var value)
                ? value
                : string.Empty;
        }

        private static bool TryReadString(JsonElement element, string propertyName, out string value)
        {
            if (TryGetProperty(element, propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString() ?? string.Empty;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool TryReadBoolean(JsonElement element, string propertyName, out bool value)
        {
            if (TryGetProperty(element, propertyName, out var property)
                && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False))
            {
                value = property.GetBoolean();
                return true;
            }

            value = false;
            return false;
        }

        private static bool TryReadInt32(JsonElement element, string propertyName, out int value)
        {
            if (TryGetProperty(element, propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        private static bool TryReadInt64(JsonElement element, string propertyName, out long value)
        {
            if (TryGetProperty(element, propertyName, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out value))
            {
                return true;
            }

            value = 0;
            return false;
        }

        private static bool IsKnownTier(string tier)
        {
            return string.Equals(tier, "A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tier, "B", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tier, "C", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTier(string tier)
        {
            return IsKnownTier(tier) ? tier.ToUpperInvariant() : "C";
        }

        private static string LowestTier(string current, string candidate)
        {
            if (string.IsNullOrWhiteSpace(current))
            {
                return NormalizeTier(candidate);
            }

            var currentTier = NormalizeTier(current);
            var candidateTier = NormalizeTier(candidate);
            return TierRank(candidateTier) > TierRank(currentTier)
                ? candidateTier
                : currentTier;
        }

        private static int TierRank(string tier)
        {
            switch (NormalizeTier(tier))
            {
                case "A":
                    return 0;
                case "B":
                    return 1;
                default:
                    return 2;
            }
        }

        private static string BuildUnverifiedReason(OutputArtifact artifact)
        {
            if (artifact.IsFormattingArtifact)
            {
                return "格式产出“" + artifact.WorksheetName + "!" + artifact.Address
                    + "”尚无匹配且通过的样式回读。";
            }
            if (artifact.IsAggregateResult)
            {
                return "excel_aggregate 结果没有可解析且通过的自验证报告。";
            }
            if (string.IsNullOrWhiteSpace(artifact.WorksheetName))
            {
                return "产出项 " + artifact.ToolName + " 的目标工作表未明确，无法绑定后续验证。";
            }

            return "产出表“" + artifact.WorksheetName + "”尚无后续覆盖验证。";
        }

        private static string BuildDirectReadScope(string toolName, string argumentsJson)
        {
            if (string.Equals(
                    toolName,
                    "excel_read_range",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    toolName,
                    "excel_get_range_info",
                    StringComparison.OrdinalIgnoreCase))
            {
                var worksheet = ReadWorksheetNameArgument(argumentsJson);
                var address = ReadAddress(argumentsJson);
                var target = string.IsNullOrWhiteSpace(worksheet)
                    ? address
                    : string.IsNullOrWhiteSpace(address)
                        ? worksheet
                        : worksheet + "!" + address;
                return (string.Equals(
                        toolName,
                        "excel_get_range_info",
                        StringComparison.OrdinalIgnoreCase)
                        ? "范围信息 "
                        : "读取 ")
                    + (string.IsNullOrWhiteSpace(target) ? toolName : target);
            }

            if (string.Equals(
                    toolName,
                    "excel_list_worksheets",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "工作表清单";
            }

            if (string.Equals(
                    toolName,
                    "excel_list_backups",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "备份清单";
            }

            if (string.Equals(
                    toolName,
                    "excel_list_tables",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "表格清单";
            }

            return toolName ?? string.Empty;
        }

        private static bool IsStructuredResultTruncated(string structuredContentJson)
        {
            if (string.IsNullOrWhiteSpace(structuredContentJson))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(structuredContentJson))
                {
                    var root = document.RootElement;
                    return (TryReadBoolean(root, "IsTruncated", out var isTruncated)
                            && isTruncated)
                        || (TryReadBoolean(root, "isTruncated", out isTruncated)
                            && isTruncated);
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string BuildArtifactScope(OutputArtifact artifact)
        {
            if (artifact == null)
            {
                return string.Empty;
            }

            var target = string.IsNullOrWhiteSpace(artifact.WorksheetName)
                ? artifact.ToolName
                : string.IsNullOrWhiteSpace(artifact.Address)
                    ? artifact.WorksheetName
                    : artifact.WorksheetName + "!" + artifact.Address;
            var action = artifact.IsFormattingArtifact
                ? "格式"
                : artifact.IsAggregateResult
                    ? "聚合"
                    : string.Equals(
                        artifact.ToolName,
                        "excel_clear_range",
                        StringComparison.OrdinalIgnoreCase)
                        ? "清除"
                        : "写入/产出";
            return AddExecutionStepDescription(
                action + " " + target,
                artifact.ExecutionStepId);
        }

        private static string BuildVerificationScope(
            OutputArtifact artifact,
            VerificationRecord verification)
        {
            if (artifact == null || verification == null)
            {
                return string.Empty;
            }

            var target = string.IsNullOrWhiteSpace(artifact.WorksheetName)
                ? artifact.ToolName
                : string.IsNullOrWhiteSpace(artifact.Address)
                    ? artifact.WorksheetName
                    : artifact.WorksheetName + "!" + artifact.Address;
            return AddExecutionStepDescription(
                "验证 " + verification.ToolName + " → " + target,
                verification.VerifiesStepId);
        }

        private static string AddExecutionStepDescription(string scope, string executionStepId)
        {
            var normalizedScope = (scope ?? string.Empty).Trim();
            var normalizedStep = (executionStepId ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(normalizedScope)
                ? string.Empty
                : string.IsNullOrWhiteSpace(normalizedStep)
                    ? normalizedScope
                    : normalizedScope + "（步骤：" + normalizedStep + "）";
        }

        private static void AddScopeDescription(ICollection<string> descriptions, string description)
        {
            var normalized = (description ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(normalized)
                && !descriptions.Any(existing => string.Equals(
                    existing,
                    normalized,
                    StringComparison.OrdinalIgnoreCase)))
            {
                descriptions.Add(normalized);
            }
        }

        private static void SetFirstReason(VerificationLedgerSummary summary, string reason)
        {
            if (summary != null && string.IsNullOrWhiteSpace(summary.PrimaryReason))
            {
                summary.PrimaryReason = reason ?? string.Empty;
            }
        }

        private static void AddWorksheet(ICollection<string> worksheets, string worksheet)
        {
            var normalized = NormalizeWorksheetName(worksheet);
            if (!string.IsNullOrWhiteSpace(normalized)
                && !worksheets.Any(existing => string.Equals(
                    existing,
                    normalized,
                    StringComparison.OrdinalIgnoreCase)))
            {
                worksheets.Add(normalized);
            }
        }

        private static string NormalizeWorksheetName(string worksheetName)
        {
            return string.IsNullOrWhiteSpace(worksheetName)
                ? string.Empty
                : worksheetName.Trim();
        }

        private static bool IsHiddenRowsScanRequest(string toolName, string argumentsJson)
        {
            if (!string.Equals(
                    toolName,
                    "excel_get_range_info",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(argumentsJson))
            {
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(argumentsJson))
                {
                    return document.RootElement.ValueKind == JsonValueKind.Object
                        && TryGetProperty(
                            document.RootElement,
                            "includeHiddenRows",
                            out var includeHiddenRows)
                        && includeHiddenRows.ValueKind == JsonValueKind.True;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool HasCompleteHiddenRowsScanPayload(
            JsonElement root,
            long coveredRows)
        {
            if (!TryReadBoolean(root, "hiddenRowsScanComplete", out var scanComplete)
                || !scanComplete
                || !TryReadInt64(root, "hiddenRowsScanned", out var rowsScanned)
                || rowsScanned != coveredRows
                || !TryReadInt32(root, "hiddenRowCount", out var hiddenRowCount)
                || hiddenRowCount < 0
                || hiddenRowCount > rowsScanned
                || !TryGetProperty(root, "hiddenRowNumbers", out var hiddenRows)
                || hiddenRows.ValueKind != JsonValueKind.Array
                || hiddenRows.GetArrayLength() != hiddenRowCount)
            {
                return false;
            }

            var seenRows = new HashSet<int>();
            foreach (var hiddenRow in hiddenRows.EnumerateArray())
            {
                if (hiddenRow.ValueKind != JsonValueKind.Number
                    || !hiddenRow.TryGetInt32(out var worksheetRow)
                    || worksheetRow <= 0
                    || !seenRows.Add(worksheetRow))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class OutputArtifact
        {
            public string ToolName { get; set; }
            public string WorksheetName { get; set; }
            public string WorkbookName { get; set; }
            public string WriteResultJson { get; set; }
            public string ErrorCode { get; set; }
            public string TargetWorkbookFullName { get; set; }
            public int Sequence { get; set; }
            public bool Succeeded { get; set; }
            public bool IsAggregateResult { get; set; }
            public bool HasInlineVerification { get; set; }
            public bool IsCompleteUnverifiedAggregateResult { get; set; }
            public string AggregateArgumentsJson { get; set; }
            public bool IsFormattingArtifact { get; set; }
            public string FormatArgumentsJson { get; set; }
            public string WriteArgumentsJson { get; set; }
            public string Address { get; set; }
            public string ExecutionStepId { get; set; }
        }

        private sealed class DirectReadResult
        {
            public int Sequence { get; set; }
            public string ToolName { get; set; }
            public string Scope { get; set; }
            public string ExecutionStepId { get; set; }
            public bool Succeeded { get; set; }
            public bool IsTruncated { get; set; }
        }

        private sealed class WorksheetCreationArtifact
        {
            public string WorksheetName { get; set; }
            public string ExecutionStepId { get; set; }
        }

        private sealed class WorksheetCleanupArtifact
        {
            public HashSet<string> WorksheetNames { get; set; }
            public string ExecutionStepId { get; set; }
        }

        private sealed class VerificationRecord
        {
            public int Sequence { get; set; }
            public string ToolName { get; set; }
            public string Tier { get; set; }
            public bool Passed { get; set; }
            public int AssertionCount { get; set; }
            public long CoveredRows { get; set; }
            public bool Independent { get; set; }
            public bool Sampled { get; set; }
            public string Producer { get; set; }
            public string BasisJson { get; set; }
            public string ArgumentsJson { get; set; }
            public HashSet<string> WorksheetNames { get; set; }
            public bool IsValid { get; set; }
            public string Error { get; set; }
            public bool IsKeySetDifference { get; set; }
            public bool IsHiddenRowsScan { get; set; }
            public bool IsFormattingReadback { get; set; }
            public bool IsWorksheetStructureVerification { get; set; }
            public string Address { get; set; }
            public string VerifiesStepId { get; set; }
            public int? VerifiesArtifactSequence { get; set; }
            public HashSet<string> Addresses { get; set; }
        }

        private sealed class VerificationReadResult
        {
            public VerificationRecord Record { get; set; }
            public string Error { get; set; }
        }
    }

    internal sealed class VerificationLedgerSummary
    {
        public VerificationLedgerSummary()
        {
            Tier = string.Empty;
            PrimaryReason = string.Empty;
            BasisJson = new List<string>();
            ScopeDescriptions = new List<string>();
        }

        public bool HasExcelDataToolCalls { get; set; }
        public string Tier { get; set; }
        public int OutputItemCount { get; set; }
        public int CoveredOutputItemCount { get; set; }
        public int UncoveredOutputItemCount { get; set; }
        public int VerificationCount { get; set; }
        public int AssertionCount { get; set; }
        public bool HasKeySetDifferenceCalculation { get; set; }
        public bool HasHiddenRowsScan { get; set; }
        public long CoveredRows { get; set; }
        public string PrimaryReason { get; set; }
        public IList<string> BasisJson { get; set; }
        public IList<string> ScopeDescriptions { get; set; }
    }
}
