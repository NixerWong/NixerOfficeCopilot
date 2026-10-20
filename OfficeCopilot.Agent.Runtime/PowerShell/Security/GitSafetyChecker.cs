using System;
using System.IO;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Security
{
    /// <summary>
    /// Detects git bare-repo and hook injection attacks via PowerShell.
    /// Ported from cc-haha <c>tools/PowerShellTool/gitSafety.ts</c>.
    ///
    /// Two attack vectors:
    /// 1. Bare-repo: if cwd contains HEAD + objects/ + refs/ but no valid .git/HEAD,
    ///    Git treats cwd as a bare repository and runs hooks from cwd.
    /// 2. Git-internal write: creating HEAD/objects/refs/hooks/ then running git
    ///    executes the freshly-created malicious hooks.
    /// </summary>
    public static class GitSafetyChecker
    {
        /// <summary>
        /// Check if the current working directory is a bare git repo
        /// (has HEAD + objects/ + refs/ but missing .git/HEAD).
        /// </summary>
        public static bool IsCurrentDirectoryBareGitRepo()
        {
            try
            {
                var cwd = Environment.CurrentDirectory;
                var hasHead = File.Exists(Path.Combine(cwd, "HEAD"));
                var hasObjects = Directory.Exists(Path.Combine(cwd, "objects"));
                var hasRefs = Directory.Exists(Path.Combine(cwd, "refs"));
                var hasGitHead = File.Exists(Path.Combine(cwd, ".git", "HEAD"));

                return hasHead && hasObjects && hasRefs && !hasGitHead;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a path refers to a git-internal file that could be weaponized.
        /// Paths like hooks/pre-commit, HEAD, objects/..., refs/... are git internals.
        /// </summary>
        public static bool IsGitInternalPath(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath)) return false;

            var lower = normalizedPath.ToLowerInvariant().Replace('\\', '/').TrimStart('/');

            if (string.IsNullOrEmpty(lower)) return false;

            // Git internals: hooks/, info/, objects/, refs/, logs/, HEAD, config
            if (lower == "head" || lower == "config" || lower == "description") return true;

            if (lower.StartsWith("hooks/") || lower.StartsWith("hooks\\")) return true;
            if (lower.StartsWith("objects/") || lower.StartsWith("objects\\")) return true;
            if (lower.StartsWith("refs/") || lower.StartsWith("refs\\")) return true;
            if (lower.StartsWith("info/") || lower.StartsWith("info\\")) return true;
            if (lower.StartsWith("logs/") || lower.StartsWith("logs\\")) return true;

            // .git/ prefix (explicit git dir access)
            if (lower.StartsWith(".git/") || lower.StartsWith(".git\\")) return true;

            return false;
        }

        /// <summary>
        /// Check if a path is inside the .git directory (explicit git-dir targeting).
        /// </summary>
        public static bool IsDotGitPath(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath)) return false;
            var lower = normalizedPath.ToLowerInvariant().Replace('\\', '/').TrimStart('/');
            return lower == ".git" || lower.StartsWith(".git/") || lower.StartsWith(".git\\");
        }

        /// <summary>
        /// Check if a command involves creating or modifying git-internal paths
        /// followed by a git command execution (compound attack).
        /// This is a lightweight heuristic; full AST analysis is done in the security validator.
        /// </summary>
        public static bool HasGitCompoundRisk(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;

            var gitInternalKeywords = new[]
            {
                "hooks/", "hooks\\", "HEAD", "objects/", "refs/",
                "pre-commit", "post-commit", "pre-push", "post-checkout"
            };

            var lower = command.ToLowerInvariant();
            var mentionsGitInternal = false;
            foreach (var kw in gitInternalKeywords)
            {
                if (lower.Contains(kw))
                {
                    mentionsGitInternal = true;
                    break;
                }
            }

            if (!mentionsGitInternal) return false;

            // Check if git is also invoked in the same command
            return lower.Contains("git ") || lower.Contains("git\n") || lower.EndsWith("git");
        }
    }
}
