using System;
using System.Collections.Generic;
using System.IO;

namespace OfficeCopilot.Agent.Runtime.PowerShell.Permissions
{
    /// <summary>
    /// Validates file paths extracted from PowerShell commands against
    /// the project's allowed directories.
    /// Ported from cc-haha <c>tools/PowerShellTool/pathValidation.ts</c>.
    /// </summary>
    public static class PowerShellPathValidator
    {
        /// <summary>
        /// Known value parameters that consume the next argument but are NOT file paths.
        /// e.g., -Encoding UTF8, -Filter *.txt — these should NOT be validated as paths.
        /// </summary>
        private static readonly HashSet<string> NonPathValueParams = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "-encoding", "-filter", "-include", "-exclude",
            "-erroraction", "-warningaction", "-informationaction",
            "-errorvariable", "-warningvariable", "-informationvariable",
            "-outvariable", "-outbuffer", "-pipelinevariable",
            "-first", "-last", "-skip", "-top", "-bottom",
            "-expandproperty", "-property", "-unique",
            "-casesensitive", "-culture", "-delimiter",
            "-head", "-tail", "-totalcount", "-readcount",
            "-width", "-view", "-groupby",
            "-verbose", "-debug", "-whatif", "-confirm"
        };

        /// <summary>
        /// Extract file path arguments from a PowerShell command for validation.
        /// Returns a list of extracted paths that need to be checked against allowed directories.
        /// </summary>
        public static List<string> ExtractPaths(string command)
        {
            var paths = new List<string>();
            if (string.IsNullOrWhiteSpace(command)) return paths;

            // Simple heuristic: find tokens that look like file paths
            // Full AST-based extraction is preferred, but this covers the common case.
            var tokens = TokenizeCommand(command);

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                // Skip parameters (start with - or /)
                if (token.StartsWith("-") || token.StartsWith("/"))
                {
                    // If this is a value parameter, skip the next token too
                    if (NonPathValueParams.Contains(token.ToLowerInvariant()))
                    {
                        i++; // consume value
                    }
                    continue;
                }

                // Skip operators
                if (token == "|" || token == ";" || token == "&" || token == "&&" || token == "||")
                    continue;

                // Check if token looks like a path
                if (LooksLikePath(token))
                {
                    var resolved = ResolvePath(token);
                    if (resolved != null)
                    {
                        paths.Add(resolved);
                    }
                }
            }

            return paths;
        }

        /// <summary>
        /// Validate that extracted paths are within allowed project directories.
        /// </summary>
        /// <param name="paths">Extracted file paths.</param>
        /// <param name="allowedDirectories">Set of allowed directory paths.</param>
        /// <returns>True if all paths are within allowed directories.</returns>
        public static bool ValidatePaths(List<string> paths, HashSet<string> allowedDirectories)
        {
            if (paths.Count == 0) return true;
            if (allowedDirectories == null || allowedDirectories.Count == 0)
            {
                // No allowlist = no path restrictions (acceptEdits/plan mode)
                return true;
            }

            foreach (var path in paths)
            {
                if (!IsPathInAllowedDirectories(path, allowedDirectories))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Check if a single path is within any of the allowed directories.
        /// </summary>
        public static bool IsPathInAllowedDirectories(string path, HashSet<string> allowedDirectories)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            if (allowedDirectories == null || allowedDirectories.Count == 0) return true;

            var normalized = NormalizePath(path);
            if (normalized == null) return false;

            foreach (var allowed in allowedDirectories)
            {
                var normalizedAllowed = NormalizePath(allowed);
                if (normalizedAllowed == null) continue;

                if (normalized.StartsWith(normalizedAllowed, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a path contains dangerous patterns (traversal, system paths).
        /// </summary>
        public static bool IsDangerousPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            var normalized = path.Replace('\\', '/').TrimStart('/');

            // Path traversal
            if (normalized.Contains("../") || normalized.Contains("..\\")) return true;

            // System paths
            if (normalized.StartsWith("c:/windows", StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized.StartsWith("c:/program files", StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized.StartsWith("/windows", StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized.StartsWith("/etc/", StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized.StartsWith("/system", StringComparison.OrdinalIgnoreCase)) return true;
            if (normalized.StartsWith("/boot", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private static List<string> TokenizeCommand(string command)
        {
            var tokens = new List<string>();
            var current = string.Empty;
            var inQuote = false;
            var quoteChar = '"';

            for (int i = 0; i < command.Length; i++)
            {
                var ch = command[i];

                if (inQuote)
                {
                    current += ch;
                    if (ch == quoteChar)
                    {
                        // End of quoted string
                        tokens.Add(current);
                        current = string.Empty;
                        inQuote = false;
                    }
                }
                else if (ch == '"' || ch == '\'')
                {
                    // Start of quoted string
                    if (current.Length > 0)
                    {
                        tokens.Add(current);
                        current = string.Empty;
                    }
                    inQuote = true;
                    quoteChar = ch;
                    current += ch;
                }
                else if (char.IsWhiteSpace(ch))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current);
                        current = string.Empty;
                    }
                }
                else if (ch == '|' || ch == ';' || ch == '&')
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current);
                        current = string.Empty;
                    }
                    tokens.Add(ch.ToString());
                }
                else
                {
                    current += ch;
                }
            }

            if (current.Length > 0)
                tokens.Add(current);

            return tokens;
        }

        private static bool LooksLikePath(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;

            // Quoted strings
            if (token.Length >= 2 && (token[0] == '"' || token[0] == '\'')) return true;

            // Contains path separators
            if (token.Contains("\\") || token.Contains("/")) return true;

            // Contains file extension
            if (token.Contains(".") && !token.StartsWith("$") && !token.StartsWith("@"))
                return true;

            // Drive letter pattern
            if (token.Length >= 2 && token[1] == ':' && char.IsLetter(token[0]))
                return true;

            return false;
        }

        private static string ResolvePath(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            // Strip quotes
            var path = token.Trim();
            if (path.Length >= 2 && (path[0] == '"' || path[0] == '\''))
                path = path.Substring(1, path.Length - 2);

            // Skip if empty after trim
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                // Resolve relative to current directory
                if (!Path.IsPathRooted(path))
                {
                    path = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
                }
                return Path.GetFullPath(path);
            }
            catch
            {
                return path; // Return as-is if resolution fails
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var resolved = Path.GetFullPath(path);
                return resolved.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }
    }
}
