namespace OfficeCopilot.Agent.Abstractions
{
    /// <summary>
    /// Runtime tool group name constants.
    /// These match the groups shown in fmConfig's tool management tab.
    /// </summary>
    public static class RuntimeToolGroups
    {
        /// <summary>Runtime shell/command tools (PowerShell, etc.)</summary>
        public const string Shell = "runtime.shell";

        /// <summary>Runtime interaction tools (AskUserQuestion, TodoWrite, etc.)</summary>
        public const string Interaction = "runtime.interaction";

        /// <summary>Runtime planning and agent tools.</summary>
        public const string Planning = "runtime.planning";

        /// <summary>Runtime notification and status tools.</summary>
        public const string Notification = "runtime.notification";

        /// <summary>Runtime context management tools (compact, etc.)</summary>
        public const string Context = "runtime.context";
    }
}
