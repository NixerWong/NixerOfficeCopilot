using System;

namespace OfficeCopilot.Settings
{
    /// <summary>
    /// 宿主标识常量。取值与各 <c>*HostAdapter.HostApplicationName</c> 保持一致。
    /// </summary>
    public static class HostApplicationNames
    {
        public const string Excel = "Excel";
        public const string Word = "Word";
        public const string PowerPoint = "PowerPoint";

        /// <summary>
        /// 归一化宿主名；无法识别时回落到 Excel。
        /// </summary>
        public static string Normalize(string hostApplicationName)
        {
            if (string.Equals(hostApplicationName, Word, StringComparison.OrdinalIgnoreCase))
            {
                return Word;
            }

            if (string.Equals(hostApplicationName, PowerPoint, StringComparison.OrdinalIgnoreCase))
            {
                return PowerPoint;
            }

            return Excel;
        }

        public static string GetDisplayName(string hostApplicationName)
        {
            switch (Normalize(hostApplicationName))
            {
                case Word:
                    return "Word";
                case PowerPoint:
                    return "PowerPoint";
                default:
                    return "Excel";
            }
        }
    }
}
