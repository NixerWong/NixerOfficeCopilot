using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel;
using OfficeCopilot.Host.Excel.Analysis;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelExecuteVbaTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_execute_vba",
            DisplayName = "Execute VBA Code",
            Description = "Injects a temporary VBA module into the current workbook and runs it. Use for bulk, structural, multi-step data processing."
                + " Note: VBA writes a static snapshot that does not update when source data changes. If the user needs automatically refreshed results, use formulas or a PivotTable and explain this tradeoff."
                + " Before execution, declare declaredWriteTargets (sheet, optional range, isNewSheet). If targets are undeclared or cannot be statically verified, treat the operation as modify. For complex tasks, split the work into steps: write each intermediate table using outputSheet with a name such as _wip_NN_description, and use expect to declare expected row counts, column names, conservation totals, or assertions. The tool automatically reads back and checks the intermediate table profile; unmet expectations cause failure. After an intermediate table is registered, subsequent range-tool writes to it can be treated as scratch. For large datasets, use probe to test on a sample first. At the end, use excel_cleanup_workspace to remove registered intermediate tables."
                + " Code can call NixerLog(\"...\") to emit structured logs, which are returned with the execution result (maximum 200 entries).",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = false,
            RequiresConfirmation = true,
            RequiresBackup = true,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"code\":{\"type\":\"string\",\"description\":\"Complete VBA source code. Use RGB(r, g, b) for color assignments, for example .Interior.Color = RGB(46, 125, 50); do not use strings such as \\\"2E7D32\\\" or \\\"#FFFFFF\\\". Call NixerLog(\\\"message\\\") to return progress logs.\"},"
                + "\"moduleName\":{\"type\":\"string\"},"
                + "\"entryPoint\":{\"type\":\"string\"},"
                + "\"removeModuleAfterRun\":{\"type\":\"boolean\"},"
                + "\"stageName\":{\"type\":\"string\",\"description\":\"Name of this step, used only in echoed output to track multi-step tasks.\"},"
                + "\"outputSheet\":{\"type\":\"string\",\"description\":\"Worksheet to receive this step's result. When provided, its table profile is read back automatically; a separate excel_profile_range call is unnecessary. Intermediate sheets must use the _wip_NN_description format, for example _wip_01_cleaned or _wip_02_summary. Overall VBA impact is still determined by host preflight; after registration, range-tool writes to an intermediate sheet can be treated as scratch.\"},"
                + "\"declaredWriteTargets\":{\"type\":\"array\",\"minItems\":1,\"items\":{\"type\":\"object\",\"properties\":{\"sheet\":{\"type\":\"string\"},\"range\":{\"type\":\"string\"},\"isNewSheet\":{\"type\":\"boolean\"}},\"required\":[\"sheet\"],\"additionalProperties\":false},\"description\":\"Worksheets and optional ranges this VBA call is allowed to write; if omitted, the operation is conservatively treated as modify.\"},"
                + "\"timeoutSeconds\":{\"type\":\"integer\",\"description\":\"Execution watchdog in seconds (default: 120). On timeout, the macro is interrupted and fails with a suggestion to split the task into smaller steps.\"},"
                + "\"probe\":{\"type\":\"object\",\"description\":\"Canary execution: copy the first rows from the source sheet to a sample sheet, run the same code on the sample, and validate expect. Run on the full data only if the sample passes. Strongly recommended for data sets with more than a few hundred rows. Reference source sheets with a literal such as ThisWorkbook.Worksheets(\\\"SheetName\\\") so the reference can be redirected to the sample sheet.\",\"properties\":{"
                + "\"rows\":{\"type\":\"integer\",\"description\":\"Number of sample rows, excluding the header (recommended: 200). Set to 0 or omit to disable the canary.\"},"
                + "\"sourceWorksheetName\":{\"type\":\"string\",\"description\":\"Name of the source worksheet to sample.\"}"
                + "},\"additionalProperties\":false},"
                + "\"expect\":{\"type\":\"object\",\"description\":\"Expected results for outputSheet. If expectations are not met, this call fails and returns actual values for self-correction.\",\"properties\":{"
                + "\"minRows\":{\"type\":\"integer\"},"
                + "\"maxRows\":{\"type\":\"integer\"},"
                + "\"columns\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                + "\"conserve\":{\"type\":\"array\",\"description\":\"Conservation assertions: matching row counts and column names do not guarantee matching values. Compare an aggregate in the output with the corresponding aggregate in the source detail data, or with a known value. Specify either compareTo or expectedValue.\",\"items\":{"
                + "\"type\":\"object\",\"properties\":{"
                + "\"function\":{\"type\":\"string\",\"enum\":[\"sum\",\"count\",\"countDistinct\",\"average\",\"min\",\"max\"]},"
                + "\"column\":{\"type\":\"string\",\"description\":\"Column name in outputSheet; may be omitted when function is count.\"},"
                + "\"compareTo\":{\"type\":\"object\",\"description\":\"Source to compare against, usually the original detail range used in this calculation.\",\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"},"
                + "\"function\":{\"type\":\"string\",\"description\":\"Leave empty to use the function defined by the assertion.\"},"
                + "\"column\":{\"type\":\"string\"}"
                + "},\"additionalProperties\":false},"
                + "\"expectedValue\":{\"type\":\"number\",\"description\":\"Specify the expected value directly, for example distinctCount from a previous excel_profile_range call.\"},"
                + "\"tolerance\":{\"type\":\"number\",\"description\":\"Allowed absolute error (default: 1e-6). For currency comparisons, 0.01 is recommended.\"}"
                + "},\"required\":[\"function\"],\"additionalProperties\":false}}"
                + ",\"basis\":{\"$ref\":\"#/definitions/verificationBasis\"},"
                + "\"assertions\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/definitions/verificationAssertion\"}}"
                + "},\"additionalProperties\":false}"
                + "},"
                + "\"required\":[\"code\"],"
                + "\"additionalProperties\":false,"
                + "\"definitions\":{"
                + "\"calcFilter\":{\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\"},\"operator\":{\"type\":\"string\"},"
                + "\"value\":{\"type\":\"string\"}},\"required\":[\"column\",\"operator\"],"
                + "\"additionalProperties\":false},"
                + "\"assertionSide\":{\"type\":\"object\",\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},\"address\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"},\"column\":{\"type\":\"string\"},"
                + "\"columns\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                + "\"function\":{\"type\":\"string\"},\"keyColumn\":{\"type\":\"string\"},"
                + "\"valueColumn\":{\"type\":\"string\"},"
                + "\"filters\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/definitions/calcFilter\"}}},"
                + "\"additionalProperties\":false},"
                + "\"verificationAssertion\":{\"type\":\"object\",\"properties\":{"
                + "\"id\":{\"type\":\"string\"},\"kind\":{\"type\":\"string\","
                + "\"enum\":[\"scalar\",\"rowCount\",\"nonBlank\",\"groupMatch\",\"keySet\",\"sorted\"]},"
                + "\"target\":{\"$ref\":\"#/definitions/assertionSide\"},"
                + "\"source\":{\"$ref\":\"#/definitions/assertionSide\"},"
                + "\"expectedValue\":{\"type\":\"number\"},\"tolerance\":{\"type\":\"number\"},"
                + "\"direction\":{\"type\":\"string\",\"enum\":[\"ascending\",\"descending\"]}},"
                + "\"required\":[\"kind\",\"target\"],\"additionalProperties\":false},"
                + "\"verificationBasis\":{\"type\":\"object\",\"properties\":{"
                + "\"source\":{\"type\":\"object\",\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},\"address\":{\"type\":\"string\"},"
                + "\"tableName\":{\"type\":\"string\"}},\"additionalProperties\":false},"
                + "\"sources\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"sheet\":{\"type\":\"string\"},\"range\":{\"type\":\"string\"},"
                + "\"alias\":{\"type\":\"string\"}},\"required\":[\"sheet\",\"range\",\"alias\"],"
                + "\"additionalProperties\":false}},"
                + "\"topology\":{\"type\":\"string\",\"enum\":[\"single\",\"union\",\"lookup\",\"compare\"]},"
                + "\"keys\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                + "\"join\":{\"type\":\"string\",\"enum\":[\"left\",\"inner\"]},"
                + "\"filters\":{\"type\":\"array\",\"items\":{\"$ref\":\"#/definitions/calcFilter\"}},"
                + "\"groupBy\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},"
                + "\"derivedGroupBy\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\"},"
                + "\"transform\":{\"type\":\"string\",\"enum\":[\"firstCharacter\",\"prefix\",\"year\",\"quarter\",\"month\",\"monthName\",\"yearMonth\",\"day\",\"dayOfWeek\",\"numberBucket\"]},"
                + "\"alias\":{\"type\":\"string\"},\"size\":{\"type\":\"number\"}},"
                + "\"required\":[\"column\",\"transform\"],\"additionalProperties\":false}},"
                + "\"measures\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{"
                + "\"column\":{\"type\":\"string\"},\"function\":{\"type\":\"string\"}},"
                + "\"required\":[\"column\",\"function\"],\"additionalProperties\":false}},"
                + "\"note\":{\"type\":\"string\"},"
                + "\"headerMode\":{\"type\":\"string\",\"enum\":[\"auto\",\"yes\",\"no\"]}},"
                + "\"oneOf\":[{\"required\":[\"source\"]},{\"required\":[\"sources\"]}],"
                + "\"additionalProperties\":false}"
                + "}"
                + "}"
        };

        public override ToolDefinition Definition
        {
            get { return _definition; }
        }

        public override Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request)
        {
            try
            {
                var host = GetExcelToolHost(context);
                var argumentsJson = request == null ? null : request.ArgumentsJson;
                var arguments = ExcelToolJsonSerializer.Deserialize<ExecuteVbaToolRequest>(argumentsJson);
                arguments = MergeCamelCaseArguments(argumentsJson, arguments);
                arguments = Normalize(arguments);

                if (string.IsNullOrWhiteSpace(arguments.Code))
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        "VBA code must not be empty.",
                        "excel.execute_vba_missing_code"));
                }

                var targetValidationFailure = ValidateDeclaredWriteTargets(arguments.DeclaredWriteTargets);
                if (!string.IsNullOrWhiteSpace(targetValidationFailure))
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        targetValidationFailure,
                        "excel.execute_vba_invalid_declared_targets"));
                }

                var registryHost = host as IExcelWipRegistryHostAdapter;
                var registrySnapshot = registryHost == null
                    ? null
                    : registryHost.GetRegisteredWipWorksheets(context.ConversationId);

                string stagedOnlyValue;
                var stagedOnly = request != null
                    && request.Metadata != null
                    && request.Metadata.TryGetValue(HostBridgeMetadataKeys.StagedOnly, out stagedOnlyValue)
                    && string.Equals(
                        stagedOnlyValue,
                        HostBridgeMetadataKeys.TrueValue,
                        StringComparison.OrdinalIgnoreCase);
                var protectionAllowedWorksheetNames = new List<string>();
                string protectionTargetsJson;
                if (request != null
                    && request.Metadata != null
                    && request.Metadata.TryGetValue(
                        HostBridgeMetadataKeys.VbaProtectionAllowedWorksheets,
                        out protectionTargetsJson))
                {
                    try
                    {
                        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(protectionTargetsJson ?? "[]")))
                        {
                            protectionAllowedWorksheetNames = new DataContractJsonSerializer(typeof(List<string>))
                                .ReadObject(stream) as List<string>;
                        }

                        if (protectionAllowedWorksheetNames == null
                            || protectionAllowedWorksheetNames.Any(string.IsNullOrWhiteSpace))
                        {
                            return Task.FromResult(CreateFailure(
                                request,
                                "HostBridge 提供的声明目标保护名单无效；已阻止 VBA 执行。",
                                "excel.execute_vba_invalid_protection_targets"));
                        }
                    }
                    catch (SerializationException ex)
                    {
                        return Task.FromResult(CreateFailure(
                            request,
                            "无法读取声明目标保护名单；已阻止 VBA 执行。 " + ex.Message,
                            "excel.execute_vba_invalid_protection_targets"));
                    }
                }

                var result = host.ExecuteVbaStage(new ExcelVbaStageRequest
                {
                    Code = arguments.Code,
                    ModuleName = arguments.ModuleName,
                    EntryPoint = arguments.EntryPoint,
                    RemoveModuleAfterRun = arguments.RemoveModuleAfterRun,
                    StageName = arguments.StageName,
                    OutputSheet = arguments.OutputSheet,
                    DeclaredWriteTargets = arguments.DeclaredWriteTargets,
                    ProtectionAllowedWorksheetNames = protectionAllowedWorksheetNames,
                    Expect = arguments.Expect,
                    Probe = arguments.Probe,
                    TimeoutSeconds = arguments.TimeoutSeconds,
                    StagedOnly = stagedOnly
                });
                var registryWarning = RefreshWipRegistry(
                    registryHost,
                    context.ConversationId,
                    arguments.OutputSheet,
                    registrySnapshot);

                EnsureVerificationEnvelope(result, arguments.Expect);
                var content = BuildContent(result);
                if (!string.IsNullOrWhiteSpace(registryWarning))
                {
                    content += "\n警告：" + registryWarning;
                }

                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                // 期望校验没过就不能报成功：成功会重置连续失败计数并把执行步标记完成，错误被彻底吞掉。
                if (!result.ExpectationsPassed)
                {
                    return Task.FromResult(CreateFailure(
                        request,
                        !string.IsNullOrWhiteSpace(result.ErrorCode)
                            || result.TimedOut
                            || IsPreExecutionRejection(result)
                            ? result.Message
                            : BuildExpectationFailureSummary(result),
                        ResolveStageErrorCode(result),
                        AppendScopeViolationRecoveryGuidance(content, result, request),
                        structuredContentJson));
                }

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    GetErrorCode(ex)));
            }
        }

        private static string RefreshWipRegistry(
            IExcelWipRegistryHostAdapter registryHost,
            string conversationId,
            string outputSheet,
            ExcelWipSheetRegistryResult snapshot)
        {
            var outputIsWip = ExcelStagingConventions.IsWipSheet(outputSheet);
            if (!outputIsWip)
            {
                return string.Empty;
            }

            if (registryHost == null)
            {
                return "本轮中间表登记服务不可用；新建工作表不会按 scratch 处理，后续写入仍受正常闸门保护。";
            }

            if (snapshot == null || !snapshot.Success)
            {
                return "执行前工作表快照不可用，无法登记本次 VBA 新建的中间表；后续写入仍受正常闸门保护。"
                    + (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                        ? string.Empty
                        : " " + snapshot.ErrorMessage);
            }

            var existedBeforeOperation = snapshot.CurrentWorksheetNames != null
                && snapshot.CurrentWorksheetNames.Contains(
                    outputSheet,
                    StringComparer.OrdinalIgnoreCase);
            var result = registryHost.RefreshWipSheetRegistry(
                conversationId,
                !existedBeforeOperation,
                existedBeforeOperation ? string.Empty : outputSheet,
                snapshot.CurrentWorksheetNames);
            if (result == null || !result.Success)
            {
                return "本轮中间表登记失败；新建工作表不会按 scratch 处理，后续写入仍受正常闸门保护。"
                    + (result == null || string.IsNullOrWhiteSpace(result.ErrorMessage) ? string.Empty : " " + result.ErrorMessage);
            }

            var wasRegistered = result.RegisteredWorksheetNames != null
                && result.RegisteredWorksheetNames.Contains(outputSheet, StringComparer.OrdinalIgnoreCase);
            if (outputIsWip && !wasRegistered)
            {
                return "声明的输出表未能登记为本会话中间表；后续写入将按正常级别重新评估。";
            }

            var newlyRegisteredWipSheets = result.RegisteredWorksheetNames
                .Where(name => ExcelStagingConventions.IsWipSheet(name)
                    && snapshot.CurrentWorksheetNames != null
                    && !snapshot.CurrentWorksheetNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            return newlyRegisteredWipSheets.Count == 0
                ? string.Empty
                : "本次 VBA 新建并登记的中间表：" + string.Join("、", newlyRegisteredWipSheets) + "。";
        }

        private static ExecuteVbaToolRequest Normalize(ExecuteVbaToolRequest request)
        {
            request = request ?? new ExecuteVbaToolRequest();
            request.DeclaredWriteTargets = request.DeclaredWriteTargets
                ?? new List<ExcelVbaDeclaredWriteTarget>();
            request.Code = string.IsNullOrWhiteSpace(request.Code) ? string.Empty : request.Code.Trim();
            request.ModuleName = string.IsNullOrWhiteSpace(request.ModuleName) ? string.Empty : request.ModuleName.Trim();
            request.EntryPoint = string.IsNullOrWhiteSpace(request.EntryPoint) ? string.Empty : request.EntryPoint.Trim();
            request.StageName = string.IsNullOrWhiteSpace(request.StageName) ? string.Empty : request.StageName.Trim();
            request.OutputSheet = string.IsNullOrWhiteSpace(request.OutputSheet) ? string.Empty : request.OutputSheet.Trim();
            foreach (var target in request.DeclaredWriteTargets)
            {
                if (target == null)
                {
                    continue;
                }

                target.WorksheetName = string.IsNullOrWhiteSpace(target.WorksheetName)
                    ? string.Empty
                    : target.WorksheetName.Trim();
                target.Range = string.IsNullOrWhiteSpace(target.Range)
                    ? string.Empty
                    : target.Range.Trim();
            }

            return request;
        }

        private static string ValidateDeclaredWriteTargets(
            IList<ExcelVbaDeclaredWriteTarget> targets)
        {
            if (targets == null)
            {
                return string.Empty;
            }

            var uniqueTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
            {
                if (target == null || string.IsNullOrWhiteSpace(target.WorksheetName))
                {
                    return "每个 declaredWriteTargets 项都必须指定非空 sheet。";
                }

                var key = target.WorksheetName.Trim() + "!" + (target.Range ?? string.Empty).Trim();
                if (!uniqueTargets.Add(key))
                {
                    return "declaredWriteTargets 中存在重复目标：" + key;
                }
            }

            return string.Empty;
        }

        internal static void EnsureVerificationEnvelope(
            ExcelVbaStageResult result,
            ExcelVbaExpectation expect)
        {
            if (result == null || expect == null || result.Verification != null)
            {
                return;
            }

            var assertions = (expect.Assertions ?? new System.Collections.Generic.List<ExcelAssertion>())
                .Where(assertion => assertion != null)
                .ToList();
            var conserveRules = (expect.Conserve ?? new System.Collections.Generic.List<ExcelVbaConserveRule>())
                .Where(rule => rule != null)
                .ToList();
            var hasProfileExpectations = expect.MinRows >= 0
                || expect.MaxRows >= 0
                || (expect.Columns != null && expect.Columns.Any(
                    column => !string.IsNullOrWhiteSpace(column)));
            if (assertions.Count == 0 && conserveRules.Count == 0 && !hasProfileExpectations)
            {
                return;
            }

            if (result.OutputProfile == null && result.ExpectationsPassed)
            {
                result.ExpectationsPassed = false;
                result.ExpectationFailures.Add(
                    "指定了 expect，但没有可供验证的输出表画像。");
            }

            if (!result.ExpectationsPassed && result.ExpectationFailures.Count == 0)
            {
                result.ExpectationFailures.Add("VBA 阶段未完成期望校验。");
            }

            var report = new ExcelVerificationReport
            {
                Passed = result.ExpectationsPassed,
                Tier = result.ExpectationsPassed ? "B" : "C"
            };
            foreach (var assertion in assertions)
            {
                report.Items.Add(CreateUnavailableExpectation(
                    assertion.Id,
                    assertion.Kind,
                    result));
            }

            foreach (var rule in conserveRules)
            {
                report.Items.Add(CreateUnavailableExpectation(
                    "conserve",
                    string.IsNullOrWhiteSpace(rule.Function) ? "scalar" : rule.Function,
                    result));
            }

            if (report.Items.Count == 0 && hasProfileExpectations)
            {
                report.Items.Add(CreateUnavailableExpectation(
                    "output-profile",
                    "profileExpectation",
                    result));
            }

            report.DiffCount = result.ExpectationFailures.Count;
            report.Diffs.AddRange(result.ExpectationFailures.Take(20));
            report.Notes.Add("阶段在生成完整验证报告前结束；该结果仅汇总当前可用的期望状态。");
            if (result.ExpectationFailures.Count > 20)
            {
                report.Notes.Add(
                    "差异明细仅展示前 20 条，共 "
                    + result.ExpectationFailures.Count + " 项失败。");
            }

            result.Verification = ExcelVerificationResult.FromReport(
                report,
                OfficeCopilot.HostBridge.Contracts.ToolResultVerificationFields.ProducerVba,
                result.ProbeChecks != null
                    && result.ProbeChecks.Count > 0
                    && !result.ProbePassed,
                expect.Basis);
        }

        private static ExcelAssertionOutcome CreateUnavailableExpectation(
            string id,
            string kind,
            ExcelVbaStageResult result)
        {
            return new ExcelAssertionOutcome
            {
                Id = id ?? string.Empty,
                Kind = kind ?? string.Empty,
                Passed = result.ExpectationsPassed,
                Tier = result.ExpectationsPassed ? "B" : "C",
                Independent = false,
                TargetBackend = result.OutputProfile == null
                    || result.OutputProfile.Coverage == null
                    ? "unavailable"
                    : (result.OutputProfile.Coverage.Backend ?? "profile"),
                TargetCoverage = result.OutputProfile == null
                    ? null
                    : result.OutputProfile.Coverage
            };
        }

        private static ExecuteVbaToolRequest MergeCamelCaseArguments(
            string argumentsJson,
            ExecuteVbaToolRequest request)
        {
            request = request ?? new ExecuteVbaToolRequest();
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return request;
            }

            CamelCaseExecuteVbaToolRequest payload;
            try
            {
                payload = DeserializeCamelCasePayload(argumentsJson);
            }
            catch (SerializationException)
            {
                return request;
            }

            if (payload == null)
            {
                return request;
            }

            if (HasJsonProperty(argumentsJson, "code"))
            {
                request.Code = payload.Code ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "moduleName"))
            {
                request.ModuleName = payload.ModuleName ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "entryPoint"))
            {
                request.EntryPoint = payload.EntryPoint ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "removeModuleAfterRun"))
            {
                request.RemoveModuleAfterRun = payload.RemoveModuleAfterRun;
            }

            if (HasJsonProperty(argumentsJson, "stageName"))
            {
                request.StageName = payload.StageName ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "outputSheet"))
            {
                request.OutputSheet = payload.OutputSheet ?? string.Empty;
            }

            if (HasJsonProperty(argumentsJson, "declaredWriteTargets"))
            {
                request.DeclaredWriteTargets = payload.DeclaredWriteTargets
                    ?? new List<ExcelVbaDeclaredWriteTarget>();
            }

            if (HasJsonProperty(argumentsJson, "expect"))
            {
                request.Expect = payload.Expect;
            }

            if (HasJsonProperty(argumentsJson, "probe"))
            {
                request.Probe = payload.Probe;
            }

            if (HasJsonProperty(argumentsJson, "timeoutSeconds"))
            {
                request.TimeoutSeconds = payload.TimeoutSeconds;
            }

            return request;
        }

        private static CamelCaseExecuteVbaToolRequest DeserializeCamelCasePayload(string argumentsJson)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(argumentsJson)))
            {
                var serializer = new DataContractJsonSerializer(typeof(CamelCaseExecuteVbaToolRequest));
                return serializer.ReadObject(stream) as CamelCaseExecuteVbaToolRequest;
            }
        }

        private static bool HasJsonProperty(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            return json.IndexOf("\"" + propertyName + "\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetErrorCode(Exception ex)
        {
            if (ex is ExcelVbaTouchedFormalSheetException)
            {
                return ExcelVbaTouchedFormalSheetException.ErrorCode;
            }

            if (ex is ExcelVbaStagedProtectionUnavailableException)
            {
                return ExcelVbaStagedProtectionUnavailableException.ErrorCode;
            }

            var message = ex == null || string.IsNullOrWhiteSpace(ex.Message)
                ? string.Empty
                : ex.Message;

            if (message.IndexOf("Trust access to the VBA project object model", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("AccessVBOM", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("Visual Basic Project", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "excel.execute_vba_trust_disabled";
            }

            if (message.IndexOf("entry procedure", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("entry point", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "excel.execute_vba_entry_point_not_found";
            }

            if (message.IndexOf("module", StringComparison.OrdinalIgnoreCase) >= 0
                && (message.IndexOf("exists", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("conflict", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "excel.execute_vba_module_name_conflict";
            }

            if (message.IndexOf("active workbook", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("ActiveWorkbook", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "excel.execute_vba_no_active_workbook";
            }

            return "excel.execute_vba_failed";
        }

        /// <summary>
        /// 区分四种失败：超时 / 命名违规 / 金丝雀拦下 / 真的算错了。
        /// 混成同一个错误码，模型就只会反复重试同一段代码——错误码是它选择下一步的唯一依据。
        /// </summary>
        private static string ResolveStageErrorCode(ExcelVbaStageResult result)
        {
            if (result == null) return "excel.execute_vba_failed";
            if (!string.IsNullOrWhiteSpace(result.ErrorCode)) return result.ErrorCode;
            if (result.TimedOut) return "excel.execute_vba_timeout";
            if (result.ProbeChecks != null && result.ProbeChecks.Count > 0 && !result.ProbePassed)
            {
                return "excel.execute_vba_probe_failed";
            }

            if (IsPreExecutionRejection(result)) return "excel.execute_vba_invalid_stage_name";
            return "excel.execute_vba_expectation_failed";
        }

        private static string AppendScopeViolationRecoveryGuidance(
            string content,
            ExcelVbaStageResult result,
            ToolExecutionRequest request)
        {
            if (result == null
                || !string.Equals(result.ErrorCode, "VBA_SCOPE_VIOLATION", StringComparison.Ordinal))
            {
                return content;
            }

            string backupPath = null;
            if (request != null
                && request.Metadata != null
                && request.Metadata.TryGetValue(ToolMetadataKeys.BackupPath, out backupPath)
                && !string.IsNullOrWhiteSpace(backupPath))
            {
                return content + "\n恢复提示：执行前备份位于 \"" + backupPath
                    + "\"。请检查差异后，必要时使用该备份恢复工作簿。";
            }

            return content
                + "\n恢复提示：本次调用没有可用的执行前备份路径。请立即检查工作簿差异，"
                + "不要继续写入；如需恢复，请使用 Excel 的撤销或已有备份。";
        }

        /// <summary>命名违规是在执行之前拦下的，此时没有入口过程也没有画像。</summary>
        private static bool IsPreExecutionRejection(ExcelVbaStageResult result)
            => result != null
               && !result.Success
               && result.OutputProfile == null
               && string.IsNullOrWhiteSpace(result.EntryPoint)
               && (result.ProbeChecks == null || result.ProbeChecks.Count == 0);

        private static string BuildExpectationFailureSummary(ExcelVbaStageResult result)
        {
            var builder = new StringBuilder();
            builder.Append("VBA 阶段");
            if (result != null && !string.IsNullOrWhiteSpace(result.StageName))
            {
                builder.Append(" \"").Append(result.StageName).Append("\"");
            }

            builder.Append(" 已执行，但 expect 校验未通过，因此本步视为失败：");
            if (result != null && result.ExpectationFailures != null && result.ExpectationFailures.Count > 0)
            {
                builder.Append(string.Join("；", result.ExpectationFailures));
            }
            else
            {
                builder.Append("未提供具体原因。");
            }

            builder.Append(" 输出表可能已被写入，请依据下面的实际画像修正代码或修正 expect 后重试，不要据此认定任务完成。");
            return builder.ToString();
        }

        private static string BuildContent(ExcelVbaStageResult result)
        {
            if (result == null)
            {
                return "执行结果为空。";
            }

            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(result.StageName))
            {
                builder.AppendLine("步骤：" + result.StageName);
            }

            builder.AppendLine("执行成功：" + (result.Success ? "是" : "否"));
            builder.AppendLine("说明：" + (string.IsNullOrWhiteSpace(result.Message) ? "-" : result.Message));
            builder.AppendLine("入口过程：" + (string.IsNullOrWhiteSpace(result.EntryPoint) ? "-" : result.EntryPoint));
            builder.AppendLine("耗时：" + result.ElapsedMilliseconds + " 毫秒");

            if (result.LogEntries != null && result.LogEntries.Count > 0)
            {
                builder.AppendLine("NixerLog 日志：");
                foreach (var entry in result.LogEntries)
                {
                    builder.AppendLine("  " + entry);
                }
            }

            if (result.OutputProfile != null)
            {
                var profile = result.OutputProfile;
                builder.AppendLine("输出表：" + profile.WorksheetName + " " + profile.Address
                    + "（" + profile.RowCount + " 行 × " + profile.ColumnCount + " 列）");
                builder.AppendLine("列结构：");
                foreach (var column in profile.Columns)
                {
                    builder.AppendLine("  " + column.Name + " [" + column.InferredType + "]"
                        + " 非空 " + column.NonEmptyCount
                        + "，去重 " + column.DistinctCount
                        + (column.ErrorCount > 0 ? "，错误值 " + column.ErrorCount : string.Empty));
                }
            }

            if (result.ConserveChecks != null && result.ConserveChecks.Count > 0)
            {
                builder.AppendLine("守恒比对：");
                foreach (var check in result.ConserveChecks)
                {
                    builder.AppendLine("  " + check);
                }
            }

            if (result.ProbeChecks != null && result.ProbeChecks.Count > 0)
            {
                builder.AppendLine("金丝雀：");
                foreach (var check in result.ProbeChecks)
                {
                    builder.AppendLine("  " + check);
                }
            }

            if (result.ScopeChecks != null && result.ScopeChecks.Count > 0)
            {
                builder.AppendLine("VBA 范围核对：");
                foreach (var check in result.ScopeChecks)
                {
                    builder.AppendLine("  " + check);
                }
            }

            if (result.ExpectationFailures != null && result.ExpectationFailures.Count > 0)
            {
                builder.AppendLine("期望校验未通过：");
                foreach (var failure in result.ExpectationFailures)
                {
                    builder.AppendLine("  " + failure);
                }
            }
            else if (result.OutputProfile != null)
            {
                builder.AppendLine("期望校验：通过。");
            }

            var verification = ExcelAggregationService.RenderVerification(result.Verification);
            if (!string.IsNullOrWhiteSpace(verification))
            {
                builder.AppendLine();
                builder.Append(verification);
            }

            return builder.ToString().TrimEnd();
        }

        [DataContract]
        private sealed class ExecuteVbaToolRequest
        {
            public ExecuteVbaToolRequest()
            {
                Code = string.Empty;
                ModuleName = string.Empty;
                EntryPoint = string.Empty;
                RemoveModuleAfterRun = true;
                StageName = string.Empty;
                OutputSheet = string.Empty;
                DeclaredWriteTargets = new List<ExcelVbaDeclaredWriteTarget>();
            }

            [DataMember(Order = 1)]
            public string Code { get; set; }

            [DataMember(Order = 2)]
            public string ModuleName { get; set; }

            [DataMember(Order = 3)]
            public string EntryPoint { get; set; }

            [DataMember(Order = 4)]
            public bool RemoveModuleAfterRun { get; set; }

            [DataMember(Order = 5)]
            public string StageName { get; set; }

            [DataMember(Order = 6)]
            public string OutputSheet { get; set; }

            [DataMember(Order = 7)]
            public List<ExcelVbaDeclaredWriteTarget> DeclaredWriteTargets { get; set; }

            [DataMember(Order = 8)]
            public ExcelVbaExpectation Expect { get; set; }

            [DataMember(Order = 9)]
            public ExcelVbaProbeOptions Probe { get; set; }

            [DataMember(Order = 10)]
            public int TimeoutSeconds { get; set; }
        }

        [DataContract]
        private sealed class CamelCaseExecuteVbaToolRequest
        {
            public CamelCaseExecuteVbaToolRequest()
            {
                Code = string.Empty;
                ModuleName = string.Empty;
                EntryPoint = string.Empty;
                RemoveModuleAfterRun = true;
                DeclaredWriteTargets = new List<ExcelVbaDeclaredWriteTarget>();
            }

            [DataMember(Name = "code", EmitDefaultValue = false)]
            public string Code { get; set; }

            [DataMember(Name = "moduleName", EmitDefaultValue = false)]
            public string ModuleName { get; set; }

            [DataMember(Name = "entryPoint", EmitDefaultValue = false)]
            public string EntryPoint { get; set; }

            [DataMember(Name = "removeModuleAfterRun", EmitDefaultValue = false)]
            public bool RemoveModuleAfterRun { get; set; }

            [DataMember(Name = "stageName", EmitDefaultValue = false)]
            public string StageName { get; set; }

            [DataMember(Name = "outputSheet", EmitDefaultValue = false)]
            public string OutputSheet { get; set; }

            [DataMember(Name = "declaredWriteTargets", EmitDefaultValue = false)]
            public List<ExcelVbaDeclaredWriteTarget> DeclaredWriteTargets { get; set; }

            [DataMember(Name = "expect", EmitDefaultValue = false)]
            public ExcelVbaExpectation Expect { get; set; }

            [DataMember(Name = "probe", EmitDefaultValue = false)]
            public ExcelVbaProbeOptions Probe { get; set; }

            [DataMember(Name = "timeoutSeconds", EmitDefaultValue = false)]
            public int TimeoutSeconds { get; set; }
        }
    }
}