using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Agent.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    internal sealed class TurnExecutionContract
    {
        internal const string LastContractMetadataKey = "excel.execution.lastContract";
        internal const string SessionPreferenceMetadataKey = "excel.execution.modePreference";

        public string Mode { get; set; } = string.Empty;

        public string MaxWriteEffect { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public IList<string> Targets { get; set; } = new List<string>();

        public IList<string> Sources { get; set; } = new List<string>();

        public bool IsDeclared { get; set; }

        public bool IsUserSelected { get; set; }

        public bool IsInherited { get; set; }

        public static TurnExecutionContract CreateForTurn(
            IDictionary<string, string> conversationMetadata,
            string userInput)
        {
            if (IsExplicitModeOverride(userInput))
            {
                return new TurnExecutionContract();
            }

            TurnExecutionContract inherited;
            if (ConversationHistoryFollowUpPolicy.IsSkillPlanContinuation(userInput)
                && TryReadMetadataContract(
                    conversationMetadata,
                    LastContractMetadataKey,
                    out inherited))
            {
                inherited.IsInherited = true;
                return inherited;
            }

            if (TryReadMetadataContract(
                conversationMetadata,
                SessionPreferenceMetadataKey,
                out inherited))
            {
                inherited.IsUserSelected = true;
                inherited.IsInherited = true;
                return inherited;
            }

            return new TurnExecutionContract();
        }

        public static bool TryParseCandidates(
            string argumentsJson,
            out IList<ExecutionModeCandidate> candidates,
            out string errorMessage)
        {
            candidates = new List<ExecutionModeCandidate>();
            errorMessage = string.Empty;
            try
            {
                using (var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson)
                    ? "{}"
                    : argumentsJson))
                {
                    JsonElement candidatesElement;
                    if (document.RootElement.ValueKind != JsonValueKind.Object
                        || !TryGetProperty(document.RootElement, "candidates", out candidatesElement)
                        || candidatesElement.ValueKind != JsonValueKind.Array)
                    {
                        errorMessage = "必须提供 candidates 数组。";
                        return false;
                    }

                    foreach (var candidateElement in candidatesElement.EnumerateArray())
                    {
                        if (candidates.Count >= 4)
                        {
                            errorMessage = "执行方式候选最多为 4 个。";
                            return false;
                        }

                        ExecutionModeCandidate candidate;
                        if (!TryParseCandidate(candidateElement, out candidate, out errorMessage))
                        {
                            return false;
                        }

                        candidates.Add(candidate);
                    }

                    if (candidates.Count == 0)
                    {
                        errorMessage = "至少需要提供一个执行方式候选。";
                        return false;
                    }

                    return true;
                }
            }
            catch (JsonException ex)
            {
                errorMessage = "执行方式声明不是有效 JSON：" + ex.Message;
                return false;
            }
        }

        public static bool TryParseClarifications(
            string argumentsJson,
            out IList<ExecutionModeClarification> clarifications,
            out string errorMessage)
        {
            clarifications = new List<ExecutionModeClarification>();
            errorMessage = string.Empty;
            try
            {
                using (var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson)
                    ? "{}"
                    : argumentsJson))
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        errorMessage = "执行方式声明必须是 JSON 对象。";
                        return false;
                    }

                    JsonElement questionsElement;
                    if (!TryGetProperty(document.RootElement, "clarifications", out questionsElement))
                    {
                        return true;
                    }

                    if (questionsElement.ValueKind != JsonValueKind.Array)
                    {
                        errorMessage = "clarifications 必须是数组。";
                        return false;
                    }

                    var questionTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var questionElement in questionsElement.EnumerateArray())
                    {
                        if (clarifications.Count >= 2)
                        {
                            errorMessage = "口径澄清问题最多为 2 个。";
                            return false;
                        }

                        ExecutionModeClarification clarification;
                        if (!TryParseClarification(questionElement, out clarification, out errorMessage))
                        {
                            return false;
                        }

                        if (!questionTexts.Add(clarification.Question))
                        {
                            errorMessage = "澄清问题不能重复。";
                            return false;
                        }

                        clarifications.Add(clarification);
                    }

                    return true;
                }
            }
            catch (JsonException ex)
            {
                errorMessage = "执行方式声明不是有效 JSON：" + ex.Message;
                return false;
            }
        }

        public static string GetEffectForMode(string mode)
        {
            switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "result_only":
                    return ToolWriteEffectLevels.Scratch;
                case "new_sheet_static":
                case "new_sheet_formula":
                    return ToolWriteEffectLevels.Append;
                case "modify_in_place":
                    return ToolWriteEffectLevels.Modify;
                default:
                    return string.Empty;
            }
        }

        public static string Serialize(TurnExecutionContract contract)
        {
            return JsonSerializer.Serialize(contract ?? new TurnExecutionContract());
        }

        public static bool TryDeserialize(string json, out TurnExecutionContract contract)
        {
            contract = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                contract = JsonSerializer.Deserialize<TurnExecutionContract>(json);
                return contract != null
                    && !string.IsNullOrWhiteSpace(contract.Mode)
                    && ToolWriteEffectLevels.IsValid(contract.MaxWriteEffect);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public bool AllowsTarget(string worksheetName, string targetAddress)
        {
            if (Targets == null || Targets.Count == 0)
            {
                return true;
            }

            foreach (var target in Targets)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                var separator = target.IndexOf('!');
                var expectedSheet = separator >= 0 ? target.Substring(0, separator) : target;
                expectedSheet = expectedSheet.Trim().Trim('\'');
                if (!string.Equals(expectedSheet, worksheetName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (separator < 0)
                {
                    return true;
                }

                var expectedAddress = target.Substring(separator + 1).Trim();
                if (string.IsNullOrWhiteSpace(expectedAddress)
                    || IsAddressContainedBy(expectedAddress, targetAddress))
                {
                    return true;
                }
            }

            return false;
        }

        public string ToDisplayText()
        {
            var details = (Summary ?? string.Empty).Trim();
            if (Targets != null && Targets.Count > 0)
            {
                var targets = Targets.Where(target => !string.IsNullOrWhiteSpace(target)).ToList();
                if (targets.Count > 0)
                {
                    details += (details.Length == 0 ? string.Empty : "；")
                        + "目标：" + string.Join("、", targets);
                }
            }

            if (Sources != null && Sources.Count > 0)
            {
                var sources = Sources.Where(source => !string.IsNullOrWhiteSpace(source)).ToList();
                if (sources.Count > 0)
                {
                    details += (details.Length == 0 ? string.Empty : "；")
                        + "数据源：" + string.Join("、", sources);
                }
            }

            return "本轮将：" + ModeLabel(Mode)
                + (details.Length == 0 ? string.Empty : " · " + details)
                + (IsUserSelected ? "（用户选择，本轮写入上限）" : string.Empty);
        }

        public static string ModeLabel(string mode)
        {
            switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "result_only":
                    return "只告诉我结果";
                case "new_sheet_static":
                    return "生成到新表（静态值）";
                case "new_sheet_formula":
                    return "生成到新表（公式）";
                case "modify_in_place":
                    return "直接改原表";
                default:
                    return "未声明执行方式";
            }
        }

        private static bool TryParseCandidate(
            JsonElement element,
            out ExecutionModeCandidate candidate,
            out string errorMessage)
        {
            candidate = null;
            errorMessage = string.Empty;
            if (element.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "每个候选必须是对象。";
                return false;
            }

            JsonElement modeElement;
            if (!TryGetProperty(element, "mode", out modeElement)
                || modeElement.ValueKind != JsonValueKind.String)
            {
                errorMessage = "候选缺少有效的 mode。";
                return false;
            }

            var mode = modeElement.GetString();
            var effect = GetEffectForMode(mode);
            if (string.IsNullOrWhiteSpace(effect))
            {
                errorMessage = "不支持执行方式：" + (mode ?? string.Empty);
                return false;
            }

            JsonElement summaryElement;
            var summary = TryGetProperty(element, "summary", out summaryElement)
                && summaryElement.ValueKind == JsonValueKind.String
                    ? summaryElement.GetString()
                    : string.Empty;
            if (string.IsNullOrWhiteSpace(summary))
            {
                errorMessage = "每个候选都必须提供非空 summary。";
                return false;
            }

            string targetError;
            string sourceError;
            var targets = ReadStringArray(element, "targets", out targetError);
            var sources = ReadStringArray(element, "sources", out sourceError);
            if (!string.IsNullOrWhiteSpace(targetError) || !string.IsNullOrWhiteSpace(sourceError))
            {
                errorMessage = !string.IsNullOrWhiteSpace(targetError) ? targetError : sourceError;
                return false;
            }

            candidate = new ExecutionModeCandidate
            {
                Mode = mode.Trim().ToLowerInvariant(),
                MaxWriteEffect = effect,
                Summary = summary.Trim(),
                Targets = targets,
                Sources = sources
            };
            return true;
        }

        private static bool TryParseClarification(
            JsonElement element,
            out ExecutionModeClarification clarification,
            out string errorMessage)
        {
            clarification = null;
            errorMessage = string.Empty;
            if (element.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "每个澄清问题必须是对象。";
                return false;
            }

            JsonElement questionElement;
            JsonElement headerElement;
            JsonElement optionsElement;
            if (!TryGetProperty(element, "question", out questionElement)
                || questionElement.ValueKind != JsonValueKind.String
                || !TryGetProperty(element, "header", out headerElement)
                || headerElement.ValueKind != JsonValueKind.String
                || !TryGetProperty(element, "options", out optionsElement)
                || optionsElement.ValueKind != JsonValueKind.Array)
            {
                errorMessage = "澄清问题必须包含 question、header 和 options。";
                return false;
            }

            var question = questionElement.GetString().Trim();
            var header = headerElement.GetString().Trim();
            if (question.Length == 0 || question.Length > 240 || header.Length == 0 || header.Length > 24)
            {
                errorMessage = "澄清问题或标题长度无效。";
                return false;
            }

            var options = new List<ExecutionModeClarificationOption>();
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var optionElement in optionsElement.EnumerateArray())
            {
                JsonElement labelElement;
                if (options.Count >= 4)
                {
                    errorMessage = "每个澄清问题最多有 4 个选项。";
                    return false;
                }

                if (optionElement.ValueKind != JsonValueKind.Object
                    || !TryGetProperty(optionElement, "label", out labelElement)
                    || labelElement.ValueKind != JsonValueKind.String)
                {
                    errorMessage = "每个澄清选项都必须包含有效的 label。";
                    return false;
                }

                var label = labelElement.GetString().Trim();
                if (label.Length == 0 || label.Length > 80 || !labels.Add(label))
                {
                    errorMessage = "澄清选项标签不能为空、不能超过 80 个字符且不能重复。";
                    return false;
                }

                JsonElement descriptionElement;
                var description = string.Empty;
                if (TryGetProperty(optionElement, "description", out descriptionElement))
                {
                    if (descriptionElement.ValueKind != JsonValueKind.String)
                    {
                        errorMessage = "澄清选项 description 必须是字符串。";
                        return false;
                    }

                    description = descriptionElement.GetString().Trim();
                }

                if (description.Length > 240)
                {
                    errorMessage = "澄清选项说明不能超过 240 个字符。";
                    return false;
                }

                options.Add(new ExecutionModeClarificationOption
                {
                    Label = label,
                    Description = description
                });
            }

            if (options.Count < 2)
            {
                errorMessage = "每个澄清问题至少需要 2 个选项。";
                return false;
            }

            clarification = new ExecutionModeClarification
            {
                Question = question,
                Header = header,
                Options = options
            };
            return true;
        }

        private static IList<string> ReadStringArray(
            JsonElement parent,
            string name,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            JsonElement value;
            if (!TryGetProperty(parent, name, out value))
            {
                return new List<string>();
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                errorMessage = name + " 必须是字符串数组。";
                return new List<string>();
            }

            var items = value.EnumerateArray().ToList();
            if (items.Any(item => item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString())))
            {
                errorMessage = name + " 只能包含非空字符串。";
                return new List<string>();
            }

            return items
                .Select(item => item.GetString().Trim())
                .ToList();
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default(JsonElement);
            return false;
        }

        private static bool IsAddressContainedBy(string expectedAddress, string actualAddress)
        {
            if (string.Equals(expectedAddress, actualAddress ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int expectedTop;
            int expectedLeft;
            int expectedBottom;
            int expectedRight;
            int actualTop;
            int actualLeft;
            int actualBottom;
            int actualRight;
            return TryParseRange(expectedAddress, out expectedTop, out expectedLeft, out expectedBottom, out expectedRight)
                && TryParseRange(actualAddress, out actualTop, out actualLeft, out actualBottom, out actualRight)
                && expectedTop <= actualTop
                && expectedLeft <= actualLeft
                && expectedBottom >= actualBottom
                && expectedRight >= actualRight;
        }

        private static bool TryParseRange(
            string address,
            out int top,
            out int left,
            out int bottom,
            out int right)
        {
            top = left = bottom = right = 0;
            var parts = (address ?? string.Empty).Replace("$", string.Empty).Split(':');
            if (parts.Length < 1 || parts.Length > 2)
            {
                return false;
            }

            int firstRow;
            int firstColumn;
            if (!TryParseCell(parts[0], out firstRow, out firstColumn))
            {
                return false;
            }

            int lastRow;
            int lastColumn;
            if (parts.Length == 1)
            {
                lastRow = firstRow;
                lastColumn = firstColumn;
            }
            else if (!TryParseCell(parts[1], out lastRow, out lastColumn))
            {
                return false;
            }

            top = Math.Min(firstRow, lastRow);
            left = Math.Min(firstColumn, lastColumn);
            bottom = Math.Max(firstRow, lastRow);
            right = Math.Max(firstColumn, lastColumn);
            return true;
        }

        private static bool TryParseCell(string cell, out int row, out int column)
        {
            row = 0;
            column = 0;
            var match = Regex.Match((cell ?? string.Empty).Trim(), "^([A-Za-z]{1,3})([1-9][0-9]*)$");
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out row))
            {
                return false;
            }

            foreach (var letter in match.Groups[1].Value.ToUpperInvariant())
            {
                column = (column * 26) + (letter - 'A' + 1);
            }

            return row <= 1048576 && column <= 16384;
        }

        private static bool TryReadMetadataContract(
            IDictionary<string, string> metadata,
            string key,
            out TurnExecutionContract contract)
        {
            contract = null;
            string json;
            return metadata != null
                && metadata.TryGetValue(key, out json)
                && TryDeserialize(json, out contract)
                && contract.IsDeclared;
        }

        private static bool IsExplicitModeOverride(string userInput)
        {
            var text = (userInput ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return false;
            }

            var phrases = new[]
            {
                "直接改原表", "改原表", "写回原表", "生成新表", "新建工作表",
                "只告诉我", "只给我结果", "不要修改", "不写入", "写到新表",
                "静态值", "写成公式", "modify in place", "new sheet",
                "result only", "static values", "write formulas"
            };
            return phrases.Any(phrase => text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    internal sealed class ExecutionModeCandidate
    {
        public string Mode { get; set; } = string.Empty;

        public string MaxWriteEffect { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public IList<string> Targets { get; set; } = new List<string>();

        public IList<string> Sources { get; set; } = new List<string>();

        public TurnExecutionContract ToContract(bool userSelected, bool inherited)
        {
            return new TurnExecutionContract
            {
                Mode = Mode ?? string.Empty,
                MaxWriteEffect = MaxWriteEffect ?? string.Empty,
                Summary = Summary ?? string.Empty,
                Targets = Targets == null ? new List<string>() : new List<string>(Targets),
                Sources = Sources == null ? new List<string>() : new List<string>(Sources),
                IsDeclared = true,
                IsUserSelected = userSelected,
                IsInherited = inherited
            };
        }
    }

    internal sealed class ExecutionModeClarification
    {
        public string Question { get; set; } = string.Empty;

        public string Header { get; set; } = string.Empty;

        public IList<ExecutionModeClarificationOption> Options { get; set; } =
            new List<ExecutionModeClarificationOption>();
    }

    internal sealed class ExecutionModeClarificationOption
    {
        public string Label { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
    }
}
