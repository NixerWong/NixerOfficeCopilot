using System.Text;

namespace OfficeCopilot.Agent.Runtime.PowerShell
{
    /// <summary>
    /// Generates the system prompt for the PowerShell tool,
    /// dynamically adapting to the detected PowerShell edition.
    /// Ported from cc-haha <c>tools/PowerShellTool/prompt.ts</c>.
    /// </summary>
    public static class PowerShellPromptBuilder
    {
        /// <summary>Generate the full system prompt for the PowerShell tool.</summary>
        public static string GetPrompt()
        {
            var edition = PowerShellVersionDetector.DetectEdition();
            var versionStr = PowerShellVersionDetector.GetVersionString();

            var sb = new StringBuilder();

            sb.AppendLine("Executes a given PowerShell command with optional timeout. Working directory persists between commands; shell state (variables, functions) does not.");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: This tool is for terminal operations via PowerShell: git, npm, docker, and PS cmdlets. DO NOT use it for file operations (reading, writing, editing, searching, finding files) - use the specialized tools for this instead.");
            sb.AppendLine();

            // Edition-specific syntax guidance
            AppendEditionSection(sb, edition, versionStr);
            sb.AppendLine();

            // General PowerShell guidance
            AppendGeneralGuidance(sb);

            // Tool boundaries
            AppendToolBoundaries(sb);

            // Timeout info
            AppendTimeoutInfo(sb);

            // Background task note (placeholder for future)
            AppendBackgroundNote(sb);

            // Sleep guidance
            AppendSleepGuidance(sb);

            // Git guidance
            AppendGitGuidance(sb);

            return sb.ToString();
        }

        private static void AppendEditionSection(StringBuilder sb, PowerShellEdition edition, string versionStr)
        {
            switch (edition)
            {
                case PowerShellEdition.Desktop:
                    sb.AppendLine("PowerShell edition: Windows PowerShell 5.1 (powershell.exe)");
                    sb.AppendLine("   - Pipeline chain operators && and || are NOT available — they cause a parser error. To run B only if A succeeds: A; if ($?) { B }. To chain unconditionally: A; B.");
                    sb.AppendLine("   - Ternary (?:), null-coalescing (??), and null-conditional (?.) operators are NOT available. Use if/else and explicit $null -eq checks instead.");
                    sb.AppendLine("   - Avoid 2>&1 on native executables. In 5.1, redirecting a native command's stderr inside PowerShell wraps each line in an ErrorRecord (NativeCommandError) and sets $? to $false even when the exe returned exit code 0. stderr is already captured for you — don't redirect it.");
                    sb.AppendLine("   - Default file encoding is UTF-16 LE (with BOM). When writing files other tools will read, pass -Encoding utf8 to Out-File/Set-Content.");
                    sb.AppendLine("   - ConvertFrom-Json returns a PSCustomObject, not a hashtable. -AsHashtable is not available.");
                    break;

                case PowerShellEdition.Core:
                    sb.AppendLine("PowerShell edition: PowerShell 7+ (pwsh)");
                    sb.AppendLine("   - Pipeline chain operators && and || ARE available and work like bash. Prefer cmd1 && cmd2 over cmd1; cmd2 when cmd2 should only run if cmd1 succeeds.");
                    sb.AppendLine("   - Ternary ($cond ? $a : $b), null-coalescing (??), and null-conditional (?.) operators are available.");
                    sb.AppendLine("   - Default file encoding is UTF-8 without BOM.");
                    break;

                default:
                    sb.AppendLine("PowerShell edition: unknown — assume Windows PowerShell 5.1 for compatibility");
                    sb.AppendLine("   - Do NOT use &&, ||, ternary ?:, null-coalescing ??, or null-conditional ?.. These are PowerShell 7+ only and parser-error on 5.1.");
                    sb.AppendLine("   - To chain commands conditionally: A; if ($?) { B }. Unconditionally: A; B.");
                    break;
            }
        }

        private static void AppendGeneralGuidance(StringBuilder sb)
        {
            sb.AppendLine("Before executing the command, please follow these steps:");
            sb.AppendLine();
            sb.AppendLine("1. Directory Verification:");
            sb.AppendLine("   - If the command will create new directories or files, first use Get-ChildItem (or ls) to verify the parent directory exists and is the correct location");
            sb.AppendLine();
            sb.AppendLine("2. Command Execution:");
            sb.AppendLine("   - Always quote file paths that contain spaces with double quotes");
            sb.AppendLine("   - Capture the output of the command.");
            sb.AppendLine();
            sb.AppendLine("PowerShell Syntax Notes:");
            sb.AppendLine("   - Variables use $ prefix: $myVar = \"value\"");
            sb.AppendLine("   - Escape character is backtick (`), not backslash");
            sb.AppendLine("   - Use Verb-Noun cmdlet naming: Get-ChildItem, Set-Location, New-Item, Remove-Item");
            sb.AppendLine("   - Common aliases: ls (Get-ChildItem), cd (Set-Location), cat (Get-Content), rm (Remove-Item)");
            sb.AppendLine("   - Pipe operator | works similarly to bash but passes objects, not text");
            sb.AppendLine("   - Use Select-Object, Where-Object, ForEach-Object for filtering and transformation");
            sb.AppendLine("   - String interpolation: \"Hello $name\" or \"Hello $($obj.Property)\"");
            sb.AppendLine("   - Registry access uses PSDrive prefixes: HKLM:\\SOFTWARE\\... , HKCU:\\... — NOT raw HKEY_LOCAL_MACHINE\\...");
            sb.AppendLine("   - Environment variables: read with $env:NAME, set with $env:NAME = \"value\" (NOT Set-Variable or bash export)");
            sb.AppendLine("   - Call native exe with spaces in path via call operator: & \"C:\\Program Files\\App\\app.exe\" arg1 arg2");
            sb.AppendLine();
            sb.AppendLine("Interactive and blocking commands (will hang — this tool runs with -NonInteractive):");
            sb.AppendLine("   - NEVER use Read-Host, Get-Credential, Out-GridView, $Host.UI.PromptForChoice, or pause");
            sb.AppendLine("   - Destructive cmdlets (Remove-Item, Stop-Process, Clear-Content, etc.) may prompt for confirmation. Add -Confirm:$false when you intend the action to proceed. Use -Force for read-only/hidden items.");
            sb.AppendLine("   - Never use git rebase -i, git add -i, or other commands that open an interactive editor");
            sb.AppendLine();
            sb.AppendLine("Passing multiline strings (commit messages, file content) to native executables:");
            sb.AppendLine("   - Use a single-quoted here-string so PowerShell does not expand $ or backticks inside. The closing '@ MUST be at column 0 (no leading whitespace) on its own line.");
            sb.AppendLine("   - Use @'...'@ (single-quoted, literal) not @\"...\"@ (double-quoted, interpolated) unless you need variable expansion");
            sb.AppendLine("   - For arguments containing -, @, or other characters PowerShell parses as operators, use the stop-parsing token: git log --% --format=%H");
        }

        private static void AppendToolBoundaries(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("Usage notes:");
            sb.AppendLine("  - The command argument is required.");
            sb.AppendLine("  - It is very helpful if you write a clear, concise description of what this command does.");
            sb.AppendLine("  - Avoid using PowerShell to run commands that have dedicated tools, unless explicitly instructed:");
            sb.AppendLine("    - File search: Use GlobTool (NOT Get-ChildItem -Recurse)");
            sb.AppendLine("    - Content search: Use GrepTool (NOT Select-String)");
            sb.AppendLine("    - Read files: Use FileReadTool (NOT Get-Content)");
            sb.AppendLine("    - Edit files: Use FileEditTool");
            sb.AppendLine("    - Write files: Use FileWriteTool (NOT Set-Content/Out-File)");
            sb.AppendLine("    - Communication: Output text directly (NOT Write-Output/Write-Host)");
            sb.AppendLine("  - When issuing multiple commands:");
            sb.AppendLine("    - If the commands are independent and can run in parallel, make multiple PowerShell tool calls in a single message.");
            sb.AppendLine("    - If the commands depend on each other and must run sequentially, chain them in a single PowerShell call (see edition-specific chaining syntax above).");
            sb.AppendLine("    - Use ; only when you need to run commands sequentially but don't care if earlier commands fail.");
            sb.AppendLine("    - DO NOT use newlines to separate commands (newlines are ok in quoted strings and here-strings)");
            sb.AppendLine("  - Do NOT prefix commands with cd or Set-Location -- the working directory is already set to the correct project directory automatically.");
        }

        private static void AppendTimeoutInfo(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("  - You can specify an optional timeout in milliseconds (up to " +
                (PowerShellVersionDetector.MaxTimeoutMs / 60000) + " minutes). If not specified, commands will timeout after " +
                (PowerShellVersionDetector.DefaultTimeoutMs / 60000) + " minutes.");
            sb.AppendLine("  - If the output exceeds " + PowerShellVersionDetector.MaxOutputLength +
                " characters, output will be truncated before being returned to you.");
        }

        private static void AppendBackgroundNote(StringBuilder sb)
        {
            // Background tasks are not yet implemented in this port.
            // Placeholder for future: run_in_background parameter.
        }

        private static void AppendSleepGuidance(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("  - Avoid unnecessary Start-Sleep commands:");
            sb.AppendLine("    - Do not sleep between commands that can run immediately — just run them.");
            sb.AppendLine("    - Do not retry failing commands in a sleep loop — diagnose the root cause or consider an alternative approach.");
            sb.AppendLine("    - If you must sleep, keep the duration short (1-5 seconds) to avoid blocking the user.");
        }

        private static void AppendGitGuidance(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("  - For git commands:");
            sb.AppendLine("    - Prefer to create a new commit rather than amending an existing commit.");
            sb.AppendLine("    - Before running destructive operations (e.g., git reset --hard, git push --force, git checkout --), consider whether there is a safer alternative that achieves the same goal. Only use destructive operations when they are truly the best approach.");
            sb.AppendLine("    - Never skip hooks (--no-verify) or bypass signing (--no-gpg-sign, -c commit.gpgsign=false) unless explicitly needed. If a hook fails, investigate and fix the underlying issue.");
        }

        /// <summary>
        /// Generate a compact tool description for display in the tool catalog.
        /// </summary>
        public static string GetToolDescription()
        {
            return "Execute a PowerShell command in the project environment. " +
                   "Use for terminal operations: git, npm, docker, build tools, and PS cmdlets. " +
                   "Do NOT use for file read/write/edit/search — use the specialized file tools instead.";
        }

        /// <summary>
        /// Generate the JSON schema for the tool's input parameters.
        /// </summary>
        public static string GetInputSchemaJson()
        {
            return @"{
  ""type"": ""object"",
  ""properties"": {
    ""command"": {
      ""type"": ""string"",
      ""description"": ""The PowerShell command to execute.""
    },
    ""timeout"": {
      ""type"": ""integer"",
      ""description"": ""Optional timeout in milliseconds (max " + PowerShellVersionDetector.MaxTimeoutMs + @").""
    },
    ""description"": {
      ""type"": ""string"",
      ""description"": ""Clear, concise description of what this command does in active voice.""
    }
  },
  ""required"": [""command""]
}";
        }
    }
}
