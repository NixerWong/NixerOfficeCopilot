using System;
using System.Collections.Generic;
using System.Diagnostics;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    public enum PowerPointToolSelfTestStatus
    {
        Success = 0,
        Failed = 1,
        Skipped = 2
    }

    public sealed class PowerPointToolSelfTestItemResult
    {
        public string ToolName { get; set; }

        public string DisplayName { get; set; }

        public PowerPointToolSelfTestStatus Status { get; set; }

        public long DurationMilliseconds { get; set; }

        public string Message { get; set; }
    }

    public sealed class PowerPointToolSelfTestGroupResult
    {
        public string GroupName { get; set; }

        public int RegisteredToolCount { get; set; }

        public IList<PowerPointToolSelfTestItemResult> Items { get; set; } = new List<PowerPointToolSelfTestItemResult>();
    }

    public static class PowerPointToolSelfTestRunner
    {
        private sealed class TestCase
        {
            public TestCase(string toolName, string displayName, string skipWithoutPresentation)
            {
                ToolName = toolName;
                DisplayName = displayName;
                SkipWithoutPresentation = skipWithoutPresentation;
            }

            public string ToolName { get; private set; }

            public string DisplayName { get; private set; }

            public string SkipWithoutPresentation { get; private set; }
        }

        public static IReadOnlyList<PowerPointToolSelfTestGroupResult> Run(IToolRegistry registry, IServiceProvider services)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (services == null) throw new ArgumentNullException(nameof(services));
            var host = services.GetService(typeof(IPowerPointToolHost)) as IPowerPointToolHost;
            if (host == null) throw new InvalidOperationException("未找到 PowerPoint 工具宿主服务。");

            var presentation = host.GetActivePresentationInfo();
            var contextCases = new[]
            {
                new TestCase(PowerPointToolNames.GetActivePresentation, "活动演示文稿信息", "当前没有打开的演示文稿。"),
                new TestCase(PowerPointToolNames.GetSelection, "当前选区信息", "当前没有打开的演示文稿。")
            };
            var readCases = new[] { new TestCase(PowerPointToolNames.ListSlides, "幻灯片列表", "当前没有打开的演示文稿。") };
            var results = new List<PowerPointToolSelfTestGroupResult>
            {
                RunGroup(registry, services, presentation, PowerPointToolGroups.Context, contextCases),
                RunGroup(registry, services, presentation, PowerPointToolGroups.Read, readCases)
            };
            return results;
        }

        public static string BuildReport(IReadOnlyList<PowerPointToolSelfTestGroupResult> results)
        {
            if (results == null || results.Count == 0) return "没有可显示的自检结果。";
            var success = 0;
            var failed = 0;
            var skipped = 0;
            var lines = new List<string> { "PowerPoint 内置工具自检报告", "生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), string.Empty };
            foreach (var group in results)
            {
                lines.Add("分组：" + group.GroupName);
                lines.Add("已注册工具数：" + group.RegisteredToolCount);
                foreach (var item in group.Items)
                {
                    if (item.Status == PowerPointToolSelfTestStatus.Success) success++;
                    else if (item.Status == PowerPointToolSelfTestStatus.Failed) failed++;
                    else skipped++;
                    var status = item.Status == PowerPointToolSelfTestStatus.Success ? "成功"
                        : item.Status == PowerPointToolSelfTestStatus.Failed ? "失败" : "跳过";
                    lines.Add("- [" + status + "] " + item.DisplayName + " / " + item.ToolName + " (" + item.DurationMilliseconds + " ms) " + item.Message);
                }
                lines.Add(string.Empty);
            }
            lines.Insert(2, "汇总：成功 " + success + "，失败 " + failed + "，跳过 " + skipped);
            return string.Join(Environment.NewLine, lines);
        }

        private static PowerPointToolSelfTestGroupResult RunGroup(
            IToolRegistry registry,
            IServiceProvider services,
            PowerPointPresentationInfo presentation,
            string groupName,
            IEnumerable<TestCase> cases)
        {
            var group = new PowerPointToolSelfTestGroupResult { GroupName = groupName };
            foreach (var definition in registry.GetDefinitions())
            {
                string registeredGroup;
                if (ToolCatalogBuilder.TryGetGroupName(definition, out registeredGroup)
                    && string.Equals(registeredGroup, groupName, StringComparison.OrdinalIgnoreCase)) group.RegisteredToolCount++;
            }
            foreach (var testCase in cases)
            {
                var item = new PowerPointToolSelfTestItemResult
                {
                    ToolName = testCase.ToolName,
                    DisplayName = testCase.DisplayName
                };
                IAgentTool tool;
                if (!registry.TryGetTool(testCase.ToolName, out tool) || tool == null)
                {
                    item.Status = PowerPointToolSelfTestStatus.Failed;
                    item.Message = "工具未注册。";
                }
                else if (!tool.Definition.IsReadOnly)
                {
                    item.Status = PowerPointToolSelfTestStatus.Skipped;
                    item.Message = "安全策略阻止执行非只读工具。";
                }
                else if (!presentation.IsAvailable && testCase.SkipWithoutPresentation != null)
                {
                    item.Status = PowerPointToolSelfTestStatus.Skipped;
                    item.Message = testCase.SkipWithoutPresentation;
                }
                else
                {
                    var request = new ToolExecutionRequest
                    {
                        ToolCallId = Guid.NewGuid().ToString("N"),
                        ToolName = testCase.ToolName,
                        ArgumentsJson = "{}"
                    };
                    var context = new ToolExecutionContext
                    {
                        ConversationId = "powerpoint-tool-self-test",
                        MessageId = request.ToolCallId,
                        HostApplicationName = "PowerPoint",
                        Services = services
                    };
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        var result = tool.ExecuteAsync(context, request).GetAwaiter().GetResult();
                        watch.Stop();
                        item.DurationMilliseconds = watch.ElapsedMilliseconds;
                        item.Status = result != null && result.Success ? PowerPointToolSelfTestStatus.Success : PowerPointToolSelfTestStatus.Failed;
                        item.Message = result == null ? "工具返回空结果。" : Clip(result.Content, 300);
                    }
                    catch (Exception ex)
                    {
                        watch.Stop();
                        item.DurationMilliseconds = watch.ElapsedMilliseconds;
                        item.Status = PowerPointToolSelfTestStatus.Failed;
                        item.Message = ex.Message;
                    }
                }
                group.Items.Add(item);
            }
            return group;
        }

        private static string Clip(string text, int limit)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length <= limit ? text : text.Substring(0, limit) + "…";
        }
    }
}