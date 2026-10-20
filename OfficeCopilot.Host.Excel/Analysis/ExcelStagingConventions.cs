using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Excel.Analysis
{
    /// <summary>
    /// 中间表命名约定与金丝雀代码重定向。纯字符串处理，不碰 COM，可直接单测。
    /// </summary>
    public static class ExcelStagingConventions
    {
        public const string WipPrefix = StagedWriteConfirmationPolicy.WipPrefix;
        public const string WipIdentityCustomPropertyName = "NixerOfficeCopilot.WipIdentity.v1";

        /// <summary>金丝雀样本表名。固定名字，便于失败后用户按名字找到它。</summary>
        public const string ProbeSourceSheetName = WipPrefix + "probe_src";

        /// <summary>金丝雀阶段的输出表名。</summary>
        public const string ProbeOutputSheetName = WipPrefix + "probe_out";

        // 序号让中间表在工作表标签栏里天然按执行顺序排列，也让"第 3 步出的错"能被直接指认。
        private static readonly Regex StagedNameRegex = new Regex(
            "^" + Regex.Escape(WipPrefix) + @"\d{2}_.+$",
            RegexOptions.Compiled);
        private const string WipIdentityRegistryPrefix = "wip-identity:";

        public static bool IsWipSheet(string worksheetName)
            => !string.IsNullOrWhiteSpace(worksheetName)
               && worksheetName.Trim().StartsWith(WipPrefix, StringComparison.OrdinalIgnoreCase);

        public static bool IsRegisteredWipWorksheetForCleanup(
            string worksheetName,
            string prefix,
            ISet<string> registeredWorksheetNames)
        {
            var name = (worksheetName ?? string.Empty).Trim();
            var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? WipPrefix : prefix.Trim();
            return IsWipSheet(name)
                && name.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)
                && registeredWorksheetNames != null
                && registeredWorksheetNames.Contains(name);
        }

        public static string CreateWipWorksheetIdentityMarker()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static bool TryFormatWipWorksheetIdentity(
            string identityMarker,
            out string registryIdentity)
        {
            Guid identity;
            if (!Guid.TryParseExact(identityMarker ?? string.Empty, "N", out identity))
            {
                registryIdentity = null;
                return false;
            }

            registryIdentity = WipIdentityRegistryPrefix + identity.ToString("N");
            return true;
        }

        public static bool IsWipWorksheetRegistryIdentity(string registryIdentity)
        {
            return !string.IsNullOrWhiteSpace(registryIdentity)
                && registryIdentity.StartsWith(
                    WipIdentityRegistryPrefix,
                    StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryGetCreatedWipWorksheet(
            string expectedWorksheetName,
            IDictionary<string, string> currentWorksheetIdentities,
            ISet<string> worksheetNamesBeforeOperation,
            ISet<string> worksheetNamesAtTurnStart,
            out string failureReason)
        {
            failureReason = null;
            var name = (expectedWorksheetName ?? string.Empty).Trim();
            if (!IsWipSheet(name) || IsProbeSheet(name) || GetStagedNameViolation(name) != null)
            {
                failureReason = "输出表名称不符合中间表登记规范。";
                return false;
            }

            if (currentWorksheetIdentities == null
                || worksheetNamesBeforeOperation == null
                || worksheetNamesAtTurnStart == null)
            {
                failureReason = "工作表快照不完整。";
                return false;
            }

            if (worksheetNamesBeforeOperation.Contains(name))
            {
                failureReason = "该工作表在本次操作前已存在。";
                return false;
            }

            if (worksheetNamesAtTurnStart.Contains(name))
            {
                failureReason = "该工作表在本轮开始前已存在。";
                return false;
            }

            if (!currentWorksheetIdentities.ContainsKey(name))
            {
                failureReason = "执行后未找到该工作表。";
                return false;
            }

            return true;
        }

        /// <summary>金丝雀自己用的两张表不参与 _wip_NN_ 命名校验，否则会被自己的规则拦下。</summary>
        public static bool IsProbeSheet(string worksheetName)
        {
            var name = (worksheetName ?? string.Empty).Trim();
            return string.Equals(name, ProbeSourceSheetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ProbeOutputSheetName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 中间表命名违规时返回可自纠的说明，合规返回 null。
        /// 只约束 <c>_wip_</c> 开头的表：正式表命名是用户的事，不该由我们规定。
        /// </summary>
        public static string GetStagedNameViolation(string worksheetName)
        {
            var name = (worksheetName ?? string.Empty).Trim();
            if (!IsWipSheet(name) || IsProbeSheet(name))
            {
                return null;
            }

            if (StagedNameRegex.IsMatch(name))
            {
                return null;
            }

            return "中间表名 \"" + name + "\" 不符合命名规范。中间表必须命名为 "
                + WipPrefix + "NN_描述"
                + "（NN 为两位递增序号，如 " + WipPrefix + "01_清洗、"
                + WipPrefix + "02_汇总）。"
                + "序号用于按顺序审计与清理，缺了它无法判断哪一步先执行、哪一步出的错。请改名后重试。";
        }

        /// <summary>
        /// 把代码里对源表 / 输出表的引号字面量替换成金丝雀用的样本表名。
        /// 找不到源表字面量说明代码用的是 ActiveSheet 之类的隐式引用，
        /// 这种代码在金丝雀上跑等于在原表上跑，必须拒绝而不是静默跳过金丝雀。
        /// </summary>
        public static bool TryRedirectToProbe(
            string code,
            string sourceWorksheetName,
            string outputSheetName,
            out string probeCode,
            out string failureReason)
        {
            probeCode = null;
            failureReason = null;

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(sourceWorksheetName))
            {
                failureReason = "金丝雀执行需要同时提供代码与 probe.sourceWorksheetName。";
                return false;
            }

            var source = sourceWorksheetName.Trim();
            if (!ContainsQuotedLiteral(code, source))
            {
                failureReason = "代码中没有以引号字面量形式出现的工作表名 \"" + source + "\"，"
                    + "无法把它重定向到金丝雀样本表。请把源表写成 ThisWorkbook.Worksheets(\"" + source + "\") 这样的显式引用"
                    + "（不要用 ActiveSheet / Selection），或去掉 probe 后重试。";
                return false;
            }

            var rewritten = ReplaceQuotedLiteral(code, source, ProbeSourceSheetName);
            if (!string.IsNullOrWhiteSpace(outputSheetName) && ContainsQuotedLiteral(rewritten, outputSheetName.Trim()))
            {
                rewritten = ReplaceQuotedLiteral(rewritten, outputSheetName.Trim(), ProbeOutputSheetName);
            }

            probeCode = rewritten;
            return true;
        }

        private static bool ContainsQuotedLiteral(string code, string value)
            => code.IndexOf("\"" + value + "\"", StringComparison.OrdinalIgnoreCase) >= 0;

        private static string ReplaceQuotedLiteral(string code, string oldValue, string newValue)
            => Regex.Replace(
                code,
                "\"" + Regex.Escape(oldValue) + "\"",
                "\"" + newValue + "\"",
                RegexOptions.IgnoreCase);
    }
}
