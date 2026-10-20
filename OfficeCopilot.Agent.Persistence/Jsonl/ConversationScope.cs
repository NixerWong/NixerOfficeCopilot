using System;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Agent.Persistence.Jsonl
{
    /// <summary>
    /// 会话数据的宿主作用域。作用域名即根目录下的一级子目录名，用于按宿主隔离会话、索引与 WebView2 缓存。
    /// </summary>
    public static class ConversationScope
    {
        /// <summary>宿主名缺失或不合法时使用的兜底作用域。</summary>
        public const string Unscoped = "_unscoped";

        private static readonly Regex ValidScopePattern = new Regex("^[A-Za-z0-9_-]{1,32}\\z", RegexOptions.CultureInvariant);

        /// <summary>
        /// 把宿主应用名映射为作用域名。空值或不符合作用域规则的值统一落入 <see cref="Unscoped"/>。
        /// </summary>
        public static string ResolveHostScope(string hostApplicationName)
        {
            var candidate = (hostApplicationName ?? string.Empty).Trim();
            return IsValid(candidate) ? candidate : Unscoped;
        }

        public static bool IsValid(string scope)
        {
            return scope != null && ValidScopePattern.IsMatch(scope);
        }

        public static void Validate(string scope)
        {
            if (!IsValid(scope))
            {
                throw new ArgumentException(
                    "会话作用域只允许 1-32 位字母、数字、下划线或连字符。",
                    nameof(scope));
            }
        }
    }
}
