using System;
using System.Diagnostics;
using System.Globalization;

namespace OfficeCopilot.Diagnostics.Logging
{
    /// <summary>
    /// 记录 Office 启动各阶段的时间点，用于确认加载项 Startup 与 splash、窗口出现的先后关系。
    /// sinceProcessStartMs 为距宿主进程启动的毫秒数，近似等于 splash 已显示的时长。
    /// </summary>
    public static class StartupTimingLog
    {
        public static void Mark(string stage)
        {
            var now = DateTime.Now;
            string sinceProcessStart;
            try
            {
                sinceProcessStart = (now - Process.GetCurrentProcess().StartTime).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                sinceProcessStart = "?";
            }

            OfficeCopilotLog.Write(
                "Startup",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "[startup-timing] {0} at={1:HH:mm:ss.fff} sinceProcessStartMs={2}",
                    stage,
                    now,
                    sinceProcessStart));
        }
    }
}
