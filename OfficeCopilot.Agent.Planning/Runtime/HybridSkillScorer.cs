using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Planning
{
    internal static class HybridSkillScorer
    {
        public static HybridSkillScore Score(SkillDefinition skill, SkillPlannerRequest request)
        {
            var score = new HybridSkillScore(skill);
            if (skill == null)
            {
                score.TotalScore = int.MinValue;
                score.Reason = "Skill is not available.";
                return score;
            }

            var inputText = Normalize(request == null ? null : request.UserInput);
            var contextText = BuildContextText(request);
            var skillText = BuildSkillText(skill);
            var toolProfile = BuildToolProfile(request == null ? null : request.AvailableToolNames);

            score.KeywordScore = CountMatches(skill.UseWhen, inputText) * 12;
            score.KeywordScore -= CountMatches(skill.AvoidWhen, inputText) * 14;

            score.IntentScore = ScoreIntentFit(skill, inputText, skillText);
            score.HostScore = ScoreHostFit(skill, request);
            score.ToolScore = ScoreToolFit(skill, request, toolProfile, skillText);
            score.SimilarityScore = ScaleSimilarity(ComputeTextSimilarity(inputText, skillText), 16);
            score.ContextScore = ScaleSimilarity(ComputeTextSimilarity(contextText, skillText), 6);

            score.TotalScore = score.KeywordScore
                + score.IntentScore
                + score.HostScore
                + score.ToolScore
                + score.SimilarityScore
                + score.ContextScore;

            if (string.Equals(skill.Name, "read-analysis", StringComparison.OrdinalIgnoreCase)
                && score.TotalScore == 0)
            {
                score.TotalScore = 1;
            }

            score.Reason = BuildReason(score, toolProfile);
            return score;
        }

        private static int ScoreIntentFit(SkillDefinition skill, string inputText, string skillText)
        {
            if (skill == null || string.IsNullOrWhiteSpace(inputText))
            {
                return 0;
            }

            var score = 0;
            if (LooksLikeReadIntent(inputText))
            {
                score += string.Equals(skill.Category, "read", StringComparison.OrdinalIgnoreCase) ? 8 : -2;
            }

            if (LooksLikeWriteIntent(inputText))
            {
                score += string.Equals(skill.Category, "write", StringComparison.OrdinalIgnoreCase) ? 7 : -1;
            }

            if (LooksLikeFormulaIntent(inputText))
            {
                score += ContainsAny(skillText, "formula", "公式", "函数", "calculate", "sum", "xlookup") ? 9 : -3;
            }

            if (LooksLikeFormattingIntent(inputText))
            {
                score += ContainsAny(skillText, "format", "style", "样式", "格式", "font", "color") ? 8 : -2;
            }

            if (LooksLikeTableIntent(inputText))
            {
                score += ContainsAny(skillText, "table", "tables", "数据表", "power query", "query", "measure") ? 8 : -2;
            }

            if (LooksLikeHighRiskIntent(inputText))
            {
                score += skill.AllowHighRiskTools ? 9 : -4;
            }

            if (LooksLikeCodeIntent(inputText))
            {
                score += skill.AllowCodeExecution ? 10 : -5;
            }

            return score;
        }

        private static int ScoreToolFit(
            SkillDefinition skill,
            SkillPlannerRequest request,
            ToolContextProfile toolProfile,
            string skillText)
        {
            if (skill == null)
            {
                return 0;
            }

            var availableToolNames = request == null ? null : request.AvailableToolNames;
            if (availableToolNames == null || availableToolNames.Count == 0)
            {
                return 0;
            }

            var score = 0;
            var allowedOverlap = CountOverlap(skill.AllowedToolNames, availableToolNames);
            var preferredOverlap = CountOverlap(skill.PreferredToolNames, availableToolNames);
            var verificationOverlap = CountOverlap(skill.VerificationToolNames, availableToolNames);

            score += Math.Min(18, allowedOverlap * 3);
            score += Math.Min(8, preferredOverlap * 2);
            score += Math.Min(4, verificationOverlap);

            if (skill.AllowedToolNames != null && skill.AllowedToolNames.Count > 0 && allowedOverlap == 0)
            {
                score -= 8;
            }

            if (string.Equals(skill.Category, "read", StringComparison.OrdinalIgnoreCase))
            {
                score += toolProfile.ReadToolCount > 0 ? 4 : -2;
            }

            if (string.Equals(skill.Category, "write", StringComparison.OrdinalIgnoreCase))
            {
                score += toolProfile.WriteToolCount > 0 ? 4 : -3;
            }

            if (ContainsAny(skillText, "formula", "公式", "函数"))
            {
                score += toolProfile.FormulaToolCount > 0 ? 5 : -3;
            }

            if (ContainsAny(skillText, "format", "格式", "样式", "style"))
            {
                score += toolProfile.FormatToolCount > 0 ? 5 : -3;
            }

            if (ContainsAny(skillText, "table", "数据表", "power query", "query", "measure"))
            {
                score += toolProfile.TableToolCount > 0 ? 5 : -3;
            }

            if (skill.AllowCodeExecution)
            {
                score += toolProfile.CodeToolCount > 0 ? 4 : -2;
            }

            return score;
        }

        private static int ScoreHostFit(SkillDefinition skill, SkillPlannerRequest request)
        {
            if (skill == null || skill.Metadata == null || request == null)
            {
                return 0;
            }

            string expectedHostKind;
            if (!skill.Metadata.TryGetValue(SkillMetadataKeys.HostKind, out expectedHostKind)
                || string.IsNullOrWhiteSpace(expectedHostKind))
            {
                return 0;
            }

            var actualHost = SkillHostMatcher.ResolveActual(request);
            if (string.IsNullOrWhiteSpace(actualHost))
            {
                return 0;
            }

            return SkillHostMatcher.Matches(expectedHostKind, actualHost)
                ? 10
                : -12;
        }

        private static ToolContextProfile BuildToolProfile(IEnumerable<string> toolNames)
        {
            var profile = new ToolContextProfile();
            foreach (var rawName in toolNames ?? Enumerable.Empty<string>())
            {
                var toolName = Normalize(rawName);
                if (string.IsNullOrWhiteSpace(toolName))
                {
                    continue;
                }

                if (StartsWithAny(toolName, "excel_get_", "excel_read_", "excel_list_", "excel_find_", "excel_check_", "excel_validate_", "runtime get_", "runtime list_",
                    "ppt_get_", "ppt_read_", "ppt_list_", "ppt_extract_", "ppt_check_", "ppt_export_", "ppt_style_", "ppt_deck_get_", "ppt_deck_validate", "ppt_deck_preview"))
                {
                    profile.ReadToolCount++;
                }

                if (StartsWithAny(toolName, "excel_write_", "excel_set_", "excel_clear_", "excel_refresh_", "excel_execute_",
                    "ppt_set_", "ppt_format_", "ppt_arrange_", "ppt_add_", "ppt_insert_", "ppt_delete_", "ppt_move_", "ppt_deck_build", "ppt_deck_put_", "ppt_execute_"))
                {
                    profile.WriteToolCount++;
                }

                if (toolName.IndexOf("formula", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    profile.FormulaToolCount++;
                }

                if (toolName.IndexOf("format", StringComparison.OrdinalIgnoreCase) >= 0
                    || toolName.IndexOf("style", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    profile.FormatToolCount++;
                }

                if (toolName.IndexOf("table", StringComparison.OrdinalIgnoreCase) >= 0
                    || toolName.IndexOf("query", StringComparison.OrdinalIgnoreCase) >= 0
                    || toolName.IndexOf("connection", StringComparison.OrdinalIgnoreCase) >= 0
                    || toolName.IndexOf("data model", StringComparison.OrdinalIgnoreCase) >= 0
                    || toolName.IndexOf("measure", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    profile.TableToolCount++;
                }

                if (toolName.IndexOf("vba", StringComparison.OrdinalIgnoreCase) >= 0
                    || toolName.IndexOf("macro", StringComparison.OrdinalIgnoreCase) >= 0
                    || toolName.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0
                    || toolName.IndexOf("execute code", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    profile.CodeToolCount++;
                }
            }

            return profile;
        }

        private static int CountOverlap(IEnumerable<string> left, IEnumerable<string> right)
        {
            if (left == null || right == null)
            {
                return 0;
            }

            var rightSet = new HashSet<string>(right.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
            var count = 0;
            foreach (var value in left)
            {
                if (!string.IsNullOrWhiteSpace(value) && rightSet.Contains(value))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountMatches(IList<string> phrases, string inputText)
        {
            if (phrases == null || phrases.Count == 0 || string.IsNullOrWhiteSpace(inputText))
            {
                return 0;
            }

            var count = 0;
            foreach (var phrase in phrases)
            {
                var normalizedPhrase = Normalize(phrase);
                if (!string.IsNullOrWhiteSpace(normalizedPhrase)
                    && ContainsIntentTerm(inputText, normalizedPhrase))
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildSkillText(SkillDefinition skill)
        {
            if (skill == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendText(builder, skill.Name);
            AppendText(builder, skill.DisplayName);
            AppendText(builder, skill.Category);
            AppendText(builder, skill.Description);
            AppendTexts(builder, skill.UseWhen);
            AppendTexts(builder, skill.PreferredToolNames);
            return Normalize(builder.ToString());
        }

        private static string BuildContextText(SkillPlannerRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            AppendText(builder, request.HostApplicationName);
            AppendText(builder, request.HostKind);
            AppendText(builder, ReadMetadata(request.Metadata, SkillPlannerRequestMetadataKeys.DocumentName));
            AppendText(builder, ReadMetadata(request.Metadata, SkillPlannerRequestMetadataKeys.WorksheetName));
            AppendText(builder, ReadMetadata(request.Metadata, SkillPlannerRequestMetadataKeys.SelectionAddress));
            AppendText(builder, LimitText(ReadMetadata(request.Metadata, SkillPlannerRequestMetadataKeys.SelectionPreview), 120));
            AppendText(builder, LimitText(ReadMetadata(request.Metadata, SkillPlannerRequestMetadataKeys.Summary), 160));
            return Normalize(builder.ToString());
        }

        private static string ReadMetadata(IDictionary<string, string> metadata, string key)
        {
            if (metadata == null || string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string value;
            return metadata.TryGetValue(key, out value) ? value ?? string.Empty : string.Empty;
        }

        private static string LimitText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || maxLength <= 0 || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength);
        }

        private static void AppendText(StringBuilder builder, string value)
        {
            if (builder == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(value.Trim());
        }

        private static void AppendTexts(StringBuilder builder, IEnumerable<string> values)
        {
            if (builder == null || values == null)
            {
                return;
            }

            foreach (var value in values)
            {
                AppendText(builder, value);
            }
        }

        private static int ScaleSimilarity(double similarity, int maxScore)
        {
            if (similarity <= 0 || maxScore <= 0)
            {
                return 0;
            }

            return (int)Math.Round(similarity * maxScore, MidpointRounding.AwayFromZero);
        }

        private static double ComputeTextSimilarity(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return 0d;
            }

            var tokenSimilarity = ComputeJaccardSimilarity(Tokenize(left), Tokenize(right));
            var trigramSimilarity = ComputeJaccardSimilarity(BuildNGrams(left, 3), BuildNGrams(right, 3));
            var bigramSimilarity = ComputeJaccardSimilarity(BuildNGrams(left, 2), BuildNGrams(right, 2));
            return (tokenSimilarity * 0.45d) + (trigramSimilarity * 0.35d) + (bigramSimilarity * 0.20d);
        }

        private static ISet<string> Tokenize(string value)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalized = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return result;
            }

            var latinToken = new StringBuilder();
            foreach (var ch in normalized)
            {
                if (IsAsciiLetterOrDigit(ch))
                {
                    latinToken.Append(ch);
                    continue;
                }

                if (latinToken.Length > 0)
                {
                    result.Add(latinToken.ToString());
                    latinToken.Clear();
                }

                if (IsCjkCharacter(ch))
                {
                    result.Add(ch.ToString());
                }
            }

            if (latinToken.Length > 0)
            {
                result.Add(latinToken.ToString());
            }

            return result;
        }

        private static ISet<string> BuildNGrams(string value, int n)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value) || n <= 0)
            {
                return result;
            }

            var compact = new string(Normalize(value).Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            if (compact.Length == 0)
            {
                return result;
            }

            if (compact.Length <= n)
            {
                result.Add(compact);
                return result;
            }

            for (var i = 0; i <= compact.Length - n; i++)
            {
                result.Add(compact.Substring(i, n));
            }

            return result;
        }

        private static double ComputeJaccardSimilarity(ISet<string> left, ISet<string> right)
        {
            if (left == null || right == null || left.Count == 0 || right.Count == 0)
            {
                return 0d;
            }

            var intersection = left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();
            var union = left.Union(right, StringComparer.OrdinalIgnoreCase).Count();
            if (union == 0)
            {
                return 0d;
            }

            return (double)intersection / union;
        }

        private static bool LooksLikeReadIntent(string inputText)
        {
            return ContainsAny(inputText, "查看", "读取", "分析", "检查", "read", "inspect", "analyze", "review", "summary", "selection");
        }

        private static bool LooksLikeWriteIntent(string inputText)
        {
            return ContainsAny(inputText, "写入", "填写", "更新", "设置", "修改", "write", "insert", "update", "set", "fill", "edit", "create");
        }

        private static bool LooksLikeFormulaIntent(string inputText)
        {
            return ContainsAny(inputText, "公式", "函数", "计算", "formula", "formulas", "sum", "xlookup", "vlookup", "calculate");
        }

        private static bool LooksLikeFormattingIntent(string inputText)
        {
            return ContainsAny(inputText, "格式", "样式", "字体", "颜色", "边框", "format", "style", "font", "color", "border", "alignment");
        }

        private static bool LooksLikeTableIntent(string inputText)
        {
            return ContainsAny(inputText, "表格", "数据表", "工作表", "查询", "table", "tables", "power query", "query", "measure", "relationship");
        }

        private static bool LooksLikeHighRiskIntent(string inputText)
        {
            return ContainsAny(inputText, "删除", "清空", "覆盖", "批量替换", "delete", "remove", "clear", "overwrite", "replace all", "reset");
        }

        private static bool LooksLikeCodeIntent(string inputText)
        {
            return ContainsAny(inputText, "vba", "macro", "脚本", "自动化", "script", "code", "python", "javascript");
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(text) || values == null || values.Length == 0)
            {
                return false;
            }

            foreach (var value in values)
            {
                var normalizedValue = Normalize(value);
                if (!string.IsNullOrWhiteSpace(normalizedValue)
                    && ContainsIntentTerm(text, normalizedValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsIntentTerm(string text, string normalizedTerm)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(normalizedTerm))
            {
                return false;
            }

            if (normalizedTerm.Any(IsCjkCharacter))
            {
                return text.IndexOf(normalizedTerm, StringComparison.OrdinalIgnoreCase) >= 0;
            }

            var pattern = @"(?<![a-z0-9])" + Regex.Escape(normalizedTerm) + @"(?![a-z0-9])";
            return Regex.IsMatch(
                text,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool StartsWithAny(string value, params string[] prefixes)
        {
            if (string.IsNullOrWhiteSpace(value) || prefixes == null)
            {
                return false;
            }

            foreach (var prefix in prefixes)
            {
                // value 已经过 Normalize（下划线、点号都变成空格），前缀按同样规则比较。
                if (!string.IsNullOrWhiteSpace(prefix)
                    && value.StartsWith(prefix.Replace('_', ' ').Replace('.', ' '), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAsciiLetterOrDigit(char value)
        {
            return value < 128 && char.IsLetterOrDigit(value);
        }

        private static bool IsCjkCharacter(char value)
        {
            return (value >= 0x4E00 && value <= 0x9FFF)
                || (value >= 0x3400 && value <= 0x4DBF);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            var previousWasWhitespace = false;
            foreach (var raw in value.Trim())
            {
                var ch = char.ToLowerInvariant(raw);
                if (char.IsLetterOrDigit(ch) || IsCjkCharacter(ch))
                {
                    builder.Append(ch);
                    previousWasWhitespace = false;
                    continue;
                }

                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }
            }

            return builder.ToString().Trim();
        }

        private static string BuildReason(HybridSkillScore score, ToolContextProfile toolProfile)
        {
            if (score == null)
            {
                return string.Empty;
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Selected skill '{0}' with total score {1} (keyword {2}, intent {3}, host {4}, tool {5}, similarity {6}, context {7}). Available tools: read {8}, write {9}, formula {10}, format {11}, table {12}, code {13}.",
                score.Skill == null ? string.Empty : score.Skill.Name ?? string.Empty,
                score.TotalScore,
                score.KeywordScore,
                score.IntentScore,
                score.HostScore,
                score.ToolScore,
                score.SimilarityScore,
                score.ContextScore,
                toolProfile == null ? 0 : toolProfile.ReadToolCount,
                toolProfile == null ? 0 : toolProfile.WriteToolCount,
                toolProfile == null ? 0 : toolProfile.FormulaToolCount,
                toolProfile == null ? 0 : toolProfile.FormatToolCount,
                toolProfile == null ? 0 : toolProfile.TableToolCount,
                toolProfile == null ? 0 : toolProfile.CodeToolCount);
        }

        internal sealed class HybridSkillScore
        {
            public HybridSkillScore(SkillDefinition skill)
            {
                Skill = skill;
                Reason = string.Empty;
            }

            public SkillDefinition Skill { get; private set; }

            public int TotalScore { get; set; }

            public int KeywordScore { get; set; }

            public int IntentScore { get; set; }

            public int HostScore { get; set; }

            public int ToolScore { get; set; }

            public int SimilarityScore { get; set; }

            public int ContextScore { get; set; }

            public string Reason { get; set; }
        }

        private sealed class ToolContextProfile
        {
            public int ReadToolCount { get; set; }

            public int WriteToolCount { get; set; }

            public int FormulaToolCount { get; set; }

            public int FormatToolCount { get; set; }

            public int TableToolCount { get; set; }

            public int CodeToolCount { get; set; }
        }
    }
}
