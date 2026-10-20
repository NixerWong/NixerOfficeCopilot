using OfficeCopilot.Diagnostics.Logging;

namespace OfficeCopilot.Skills
{
    public static class SkillLog
    {
        public static void Write(string message)
        {
            OfficeCopilotLog.Write("Skills", message);
        }
    }
}
