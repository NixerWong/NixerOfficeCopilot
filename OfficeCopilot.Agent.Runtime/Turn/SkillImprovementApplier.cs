using OfficeCopilot.Agent.Client;
using OfficeCopilot.Skills;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Applies skill improvement suggestions by calling a side-channel LLM
    /// to rewrite the SKILL.md file.
    /// Corresponds to cc-haha <c>applySkillImprovement()</c>.
    /// </summary>
    internal static class SkillImprovementApplier
    {
        /// <summary>
        /// Apply improvements to a skill file by reading current content,
        /// sending to LLM with update instructions, and writing the result.
        /// </summary>
        /// <param name="skillName">Skill name (directory name).</param>
        /// <param name="updates">List of detected improvements.</param>
        /// <param name="modelClient">Model client for the rewrite.</param>
        /// <param name="modelOptions">Model options (endpoint, key, etc.).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the file was successfully updated.</returns>
        public static async Task<bool> ApplyAsync(
            string skillName,
            string hostApplicationName,
            IReadOnlyList<SkillUpdate> updates,
            IModelClient modelClient,
            AgentModelOptions modelOptions,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                return false;

            if (updates == null || updates.Count == 0)
                return false;

            if (modelClient == null)
                throw new ArgumentNullException(nameof(modelClient));

            if (modelOptions == null)
                throw new ArgumentNullException(nameof(modelOptions));

            var filePath = GetSkillFilePath(skillName, hostApplicationName);
            if (filePath == null || !File.Exists(filePath))
            {
                return false;
            }

            string currentContent;
            try
            {
                currentContent = File.ReadAllText(filePath, Encoding.UTF8);
            }
            catch (Exception)
            {
                return false;
            }

            var updateList = BuildUpdateList(updates);
            var prompt = BuildApplyPrompt(currentContent, updateList);

            var messages = new List<ModelClientMessage>
            {
                new ModelClientMessage
                {
                    Role = "user",
                    Content = prompt
                }
            };

            // Use a zero-temperature system prompt for deterministic edits
            var effectiveOptions = new AgentModelOptions
            {
                Endpoint = modelOptions.Endpoint,
                ApiKey = modelOptions.ApiKey,
                ModelId = modelOptions.ModelId,
                SystemPrompt = "You edit skill definition files to incorporate user preferences. Output only the updated file content inside <updated_file> tags."
            };

            try
            {
                var completion = await modelClient.CompleteAsync(
                    effectiveOptions,
                    messages,
                    new List<ModelClientTool>(),
                    new ModelClientStreamingCallbacks(),
                    cancellationToken).ConfigureAwait(false);

                var responseText = completion?.Content ?? string.Empty;
                var updatedContent = ExtractUpdatedFile(responseText);

                if (string.IsNullOrWhiteSpace(updatedContent))
                {
                    return false;
                }

                // Create backup before overwriting
                var backupPath = filePath + ".bak";
                try
                {
                    File.Copy(filePath, backupPath, true);
                }
                catch
                {
                    /* non-critical */
                }

                File.WriteAllText(filePath, updatedContent, new UTF8Encoding(false));

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ========== Private helpers ==========

        /// <summary>
        /// 返回当前宿主专属目录下的 SKILL.md 路径；宿主无法识别时返回 null（fail-closed，不触碰任何文件）。
        /// </summary>
        private static string GetSkillFilePath(string skillName, string hostApplicationName)
        {
            var hostDirectory = SkillStorageLocations.GetHostDirectory(hostApplicationName);
            if (hostDirectory.Length == 0)
                return null;

            var sanitized = SanitizeDirectoryName(skillName);
            return Path.Combine(hostDirectory, sanitized, "SKILL.md");
        }

        private static string SanitizeDirectoryName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "untitled";
            var sb = new StringBuilder();
            foreach (var c in name.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                    sb.Append(c);
                else if (c == ' ')
                    sb.Append('-');
            }
            var result = sb.ToString();
            return string.IsNullOrWhiteSpace(result) ? "untitled" : result;
        }

        private static string BuildUpdateList(IReadOnlyList<SkillUpdate> updates)
        {
            var sb = new StringBuilder();
            foreach (var u in updates)
            {
                if (u == null) continue;
                sb.Append("- ").Append(u.Section ?? "?").Append(": ").Append(u.Change ?? string.Empty).AppendLine();
            }
            return sb.ToString();
        }

        internal static string BuildApplyPrompt(string currentContent, string updateList)
        {
            return $@"You are editing a skill definition file. Apply the following improvements to the skill.

<current_skill_file>
{currentContent}
</current_skill_file>

<improvements>
{updateList}
</improvements>

Rules:
- Integrate the improvements naturally into the existing structure
- Preserve frontmatter (--- block) exactly as-is, only modify body content
- Preserve the overall format and style
- Do not remove existing content unless an improvement explicitly replaces it
- Output the complete updated file inside <updated_file> tags";
        }

        internal static string ExtractUpdatedFile(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return null;

            var match = Regex.Match(responseText,
                @"<updated_file>\s*(.*?)\s*</updated_file>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }
}
