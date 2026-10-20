using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.Planning;
using OfficeCopilot.Skills;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Post-turn hook that detects user corrections/preferences during skill execution
    /// and suggests improvements to the skill definition.
    /// Corresponds to cc-haha <c>skillImprovement.ts</c>.
    /// </summary>
    internal sealed class SkillImprovementHook
    {
        private const int TurnBatchSize = 5;

        private readonly ISkillRegistry _skillRegistry;
        private readonly IModelClient _modelClient;
        private int _lastAnalyzedUserCount;
        private int _lastAnalyzedMessageIndex;

        public SkillImprovementHook(ISkillRegistry skillRegistry, IModelClient modelClient)
        {
            _skillRegistry = skillRegistry ?? throw new ArgumentNullException(nameof(skillRegistry));
            _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        }

        /// <summary>
        /// Whether the hook should run for this turn.
        /// Conditions: active project skill exists AND at least <see cref="TurnBatchSize"/> new user messages since last check.
        /// </summary>
        public bool ShouldRun(BrokerConversationState conversation, RuntimeSkillPlan skillPlan)
        {
            if (conversation?.Messages == null || conversation.Messages.Count == 0)
                return false;

            // Only run when a non-built-in skill is active
            if (skillPlan == null || !skillPlan.HasSkill || string.IsNullOrWhiteSpace(skillPlan.SkillName))
                return false;

            // Skip meta/built-in skills like skillify, read-analysis
            if (IsMetaSkill(skillPlan.SkillName))
                return false;

            var userCount = CountUserMessages(conversation.Messages);
            if (userCount - _lastAnalyzedUserCount < TurnBatchSize)
                return false;

            _lastAnalyzedUserCount = userCount;
            return true;
        }

        /// <summary>
        /// Build the analysis prompt and send to model. Returns detected updates (or empty list).
        /// </summary>
        public async Task<IReadOnlyList<SkillUpdate>> AnalyzeAsync(
            BrokerConversationState conversation,
            RuntimeSkillPlan skillPlan,
            AgentModelOptions modelOptions,
            CancellationToken cancellationToken)
        {
            if (conversation?.Messages == null)
                return Array.Empty<SkillUpdate>();

            var skill = _skillRegistry.FindByName(skillPlan.SkillName);
            if (skill == null)
                return Array.Empty<SkillUpdate>();

            var skillContent = GetSkillContent(skill);
            if (string.IsNullOrWhiteSpace(skillContent))
                return Array.Empty<SkillUpdate>();

            // Only analyze messages since last check
            var newMessages = conversation.Messages.Skip(_lastAnalyzedMessageIndex).ToList();
            _lastAnalyzedMessageIndex = conversation.Messages.Count;

            var recentMessagesText = FormatRecentMessages(newMessages);
            if (string.IsNullOrWhiteSpace(recentMessagesText))
                return Array.Empty<SkillUpdate>();

            var prompt = BuildAnalysisPrompt(skillPlan.SkillName, skillContent, recentMessagesText);

            var messages = new List<ModelClientMessage>
            {
                new ModelClientMessage
                {
                    Role = "user",
                    Content = prompt
                }
            };

            try
            {
                var completion = await _modelClient.CompleteAsync(
                    modelOptions,
                    messages,
                    new List<ModelClientTool>(),
                    new ModelClientStreamingCallbacks(),
                    cancellationToken).ConfigureAwait(false);

                var responseText = completion?.Content ?? string.Empty;
                return ParseUpdates(responseText);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Array.Empty<SkillUpdate>();
            }
        }

        /// <summary>
        /// Reset tracking state (e.g., when conversation changes).
        /// </summary>
        public void Reset()
        {
            _lastAnalyzedUserCount = 0;
            _lastAnalyzedMessageIndex = 0;
        }

        // ========== Private helpers ==========

        private static bool IsMetaSkill(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName))
                return true;

            // Built-in meta/analysis skills that don't benefit from improvement hooks
            var metaSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "skillify",
                "read-analysis"
            };

            return metaSkills.Contains(skillName);
        }

        private static int CountUserMessages(IList<BrokerConversationMessage> messages)
        {
            if (messages == null) return 0;
            var count = 0;
            foreach (var m in messages)
            {
                if (m != null && string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
                    count++;
            }
            return count;
        }

        private static string GetSkillContent(SkillDefinition skill)
        {
            if (skill == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("name: " + (skill.Name ?? string.Empty));
            sb.AppendLine("description: " + (skill.Description ?? string.Empty));

            if (skill.AllowedToolNames != null && skill.AllowedToolNames.Count > 0)
            {
                sb.AppendLine("allowed-tools:");
                foreach (var t in skill.AllowedToolNames)
                    sb.AppendLine("  - " + t);
            }

            // Include when_to_use from metadata if present
            if (skill.Metadata != null)
            {
                if (skill.Metadata.TryGetValue(SkillDefinitionMapper.SkillWhenToUseKey, out var whenToUse) &&
                    !string.IsNullOrWhiteSpace(whenToUse))
                    sb.AppendLine("when_to_use: " + whenToUse);

                if (skill.Metadata.TryGetValue(SkillDefinitionMapper.SkillArgumentHintKey, out var argHint) &&
                    !string.IsNullOrWhiteSpace(argHint))
                    sb.AppendLine("argument-hint: \"" + argHint + "\"");
            }

            sb.AppendLine("---");

            var body = skill.GetSkillPromptBody();
            if (!string.IsNullOrWhiteSpace(body))
                sb.AppendLine(body);

            return sb.ToString();
        }

        private static string FormatRecentMessages(IList<BrokerConversationMessage> messages)
        {
            if (messages == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var m in messages)
            {
                if (m == null) continue;

                string role;
                if (string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
                    role = "User";
                else if (string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                    role = "Assistant";
                else
                    continue;

                var content = m.Content ?? string.Empty;
                if (content.Length > 500)
                    content = content.Substring(0, 500);

                if (sb.Length > 0)
                    sb.AppendLine().AppendLine();

                sb.Append(role).Append(": ").Append(content);
            }

            return sb.ToString();
        }

        internal static string BuildAnalysisPrompt(string skillName, string skillContent, string recentMessages)
        {
            return $@"You are analyzing a conversation where a user is executing a skill (a repeatable process).
Your job: identify if the user's recent messages contain preferences, requests, or corrections that should be permanently added to the skill definition for future runs.

<skill_definition>
{skillContent}
</skill_definition>

<recent_messages>
{recentMessages}
</recent_messages>

Look for:
- Requests to add, change, or remove steps
- Preferences about how steps should work
- Corrections: ""no, do X instead"", ""always use Y""
- New requirements or constraints the user mentions

Ignore:
- Routine conversation that doesn't generalize (one-time answers, chitchat)
- Things the skill already does

Output a JSON array inside <updates> tags. Each item: {{""section"": ""which step/section to modify or 'new step'"", ""change"": ""what to add/modify"", ""reason"": ""which user message prompted this""}}.
Output <updates>[]</updates> if no updates are needed.";
        }

        internal static IReadOnlyList<SkillUpdate> ParseUpdates(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
                return Array.Empty<SkillUpdate>();

            var updatesMatch = Regex.Match(responseText,
                @"<updates>\s*(.*?)\s*</updates>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (!updatesMatch.Success)
                return Array.Empty<SkillUpdate>();

            var json = updatesMatch.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
                return Array.Empty<SkillUpdate>();

            try
            {
                var result = JsonSerializer.Deserialize<List<SkillUpdate>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result != null
                    ? new List<SkillUpdate>(result).AsReadOnly()
                    : Array.Empty<SkillUpdate>();
            }
            catch
            {
                return Array.Empty<SkillUpdate>();
            }
        }
    }

    /// <summary>
    /// A detected skill improvement suggestion.
    /// </summary>
    internal sealed class SkillUpdate
    {
        public string Section { get; set; }
        public string Change { get; set; }
        public string Reason { get; set; }
    }
}
