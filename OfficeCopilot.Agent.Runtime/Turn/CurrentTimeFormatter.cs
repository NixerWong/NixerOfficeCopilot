using System;
using System.Globalization;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// 当前时间行的格式化，供 Broker 上下文采集（<c>BrokerTurnExecutor</c>）与系统提示词（<c>DefaultContextBuilder</c>）共用。
    /// </summary>
    internal static class CurrentTimeFormatter
    {
        private static readonly string[] WeekdayNames =
        {
            "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"
        };

        public static string FormatLine(DateTimeOffset now)
        {
            return now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + "（" + GetWeekdayName(now) + "），时区 " + FormatUtcOffset(now);
        }

        public static string GetWeekdayName(DateTimeOffset now)
        {
            return WeekdayNames[(int)now.DayOfWeek];
        }

        public static string FormatUtcOffset(DateTimeOffset now)
        {
            var offset = now.Offset;
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var magnitude = offset < TimeSpan.Zero ? offset.Negate() : offset;
            return "UTC" + sign + magnitude.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }
    }
}
