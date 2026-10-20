using System;

namespace OfficeCopilot.Agent.Planning
{
    /// <summary>技能宿主匹配：统一处理 powerpoint/ppt 等别名，供各规划器共用。</summary>
    public static class SkillHostMatcher
    {
        /// <summary>把宿主名归一为 excel / word / powerpoint；无法识别时返回小写原值。</summary>
        public static string Normalize(string host)
        {
            var value = (host ?? string.Empty).Trim().ToLowerInvariant();
            if (value.Length == 0) return string.Empty;
            if (value.Contains("powerpoint") || value == "ppt" || value.StartsWith("ppt.", StringComparison.Ordinal)) return "powerpoint";
            if (value.Contains("excel") || value == "xl") return "excel";
            if (value.Contains("word")) return "word";
            return value;
        }

        /// <summary>实际宿主为空时视为匹配。</summary>
        public static bool Matches(string expectedHost, string actualHost)
        {
            var expected = Normalize(expectedHost);
            var actual = Normalize(actualHost);
            return expected.Length == 0 || actual.Length == 0 || string.Equals(expected, actual, StringComparison.Ordinal);
        }

        public static string ResolveActual(SkillPlannerRequest request)
        {
            if (request == null) return string.Empty;
            return string.IsNullOrWhiteSpace(request.HostKind) ? request.HostApplicationName ?? string.Empty : request.HostKind;
        }
    }
}
