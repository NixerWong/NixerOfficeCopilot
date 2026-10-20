using System;

namespace OfficeCopilot.Agent.Abstractions
{
    /// <summary>
    /// 工具风险等级常量。取值与 <see cref="ToolDefinition.RiskLevel"/> 对应，
    /// 空字符串表示工具作者未声明，由 <see cref="ToolSafetyPolicy"/> 推导。
    /// </summary>
    public static class ToolRiskLevels
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";
        public const string Critical = "critical";
    }

    public static class ToolWriteEffectLevels
    {
        public const string None = "none";
        public const string Scratch = "scratch";
        public const string Append = "append";
        public const string Modify = "modify";

        public static bool IsValid(string value)
        {
            return string.Equals(value, None, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, Scratch, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, Append, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, Modify, StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return IsValid(value) ? value.Trim().ToLowerInvariant() : Modify;
        }

        public static int GetRank(string value)
        {
            if (string.Equals(value, None, StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(value, Scratch, StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(value, Append, StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(value, Modify, StringComparison.OrdinalIgnoreCase)) return 3;
            return -1;
        }

        public static bool IsWithinLimit(string effect, string limit)
        {
            var effectRank = GetRank(effect);
            var limitRank = GetRank(limit);
            return effectRank >= 0 && limitRank >= 0 && effectRank <= limitRank;
        }
    }

    /// <summary>
    /// 内置工具安全字段的统一补齐策略，供 Excel / Word / PowerPoint 三个宿主的工具目录共用。
    ///
    /// 设计原则：只推导未声明的写入上限；<c>IsReadOnly</c> 始终由该上限派生。
    /// 旧实现在各宿主目录里各写一份 <c>NormalizeSafetyPolicy</c>，并且把所有非只读工具
    /// 无条件置为 <c>RequiresConfirmation = true</c>，连纯导航工具也被强制确认。
    /// </summary>
    public static class ToolSafetyPolicy
    {
        /// <summary>
        /// 明确会执行任意代码的工具关键字 —— 归为 critical。
        /// </summary>
        private static readonly string[] CriticalNameFragments =
        {
            "vba",
            "macro",
            "powershell",
            "run_command"
        };

        /// <summary>
        /// 破坏性或不可逆的批量操作关键字 —— 归为 high。
        /// </summary>
        private static readonly string[] HighRiskNameFragments =
        {
            "clear",
            "delete",
            "remove",
            "replace_all",
            "merge",
            "overwrite",
            "file_create"
        };

        /// <summary>
        /// 补齐写入上限、超时、风险等级与备份要求，并依据写入上限推导只读性。
        /// <c>RequiresConfirmation</c> 仅在只读工具上强制关闭，其余情况沿用工具作者的声明。
        /// </summary>
        public static void Normalize(ToolDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            definition.MaxWriteEffect = ToolWriteEffectLevels.Normalize(
                definition.MaxWriteEffect,
                definition.IsReadOnly ? ToolWriteEffectLevels.None : ToolWriteEffectLevels.Modify);
            definition.IsReadOnly = string.Equals(
                definition.MaxWriteEffect,
                ToolWriteEffectLevels.None,
                StringComparison.OrdinalIgnoreCase);

            if (definition.TimeoutSeconds <= 0)
            {
                definition.TimeoutSeconds = definition.IsReadOnly ? 60 : 120;
            }

            if (string.IsNullOrWhiteSpace(definition.RiskLevel))
            {
                definition.RiskLevel = InferRiskLevel(definition);
            }

            if (definition.IsReadOnly)
            {
                // 只读工具既不改文档也无从回滚，确认与备份都没有意义。
                definition.RequiresConfirmation = false;
                definition.RequiresBackup = false;
                return;
            }

            // 只写工作区、不触碰宿主文档的工具无需备份文档。
            definition.RequiresBackup = definition.AffectsDocument;
        }

        /// <summary>
        /// 依据只读性与工具名语义推导风险等级。仅在作者未声明时调用。
        /// </summary>
        public static string InferRiskLevel(ToolDefinition definition)
        {
            if (definition == null)
            {
                return ToolRiskLevels.Low;
            }

            if (definition.IsReadOnly)
            {
                return ToolRiskLevels.Low;
            }

            var name = definition.Name ?? string.Empty;
            if (ContainsAny(name, CriticalNameFragments))
            {
                return ToolRiskLevels.Critical;
            }

            if (ContainsAny(name, HighRiskNameFragments))
            {
                return ToolRiskLevels.High;
            }

            // 其余写入工具：可通过备份回滚，属于中等风险。
            return ToolRiskLevels.Medium;
        }

        private static bool ContainsAny(string value, string[] fragments)
        {
            if (string.IsNullOrWhiteSpace(value) || fragments == null)
            {
                return false;
            }

            for (var i = 0; i < fragments.Length; i++)
            {
                if (value.IndexOf(fragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
