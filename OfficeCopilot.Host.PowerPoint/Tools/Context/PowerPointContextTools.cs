using System;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    public sealed class PowerPointGetActivePresentationTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.GetActivePresentation,
            DisplayName = "读取当前演示文稿",
            Description = "返回当前演示文稿信息：名称、路径（未保存为空）、是否只读、页数、当前页、页面尺寸（磅）。"
                + "若文稿由演示工作区构建，还返回 deckId，可直接用 ppt_deck_get_slides 重开该工作区继续修改。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var pres = GetHost(context).GetActivePresentationInfo();
                if (pres == null || !pres.IsAvailable)
                    return Task.FromResult(Fail(request, "当前没有打开的演示文稿。", "ppt.no_active_presentation"));

                var sb = new StringBuilder();
                sb.AppendLine("演示文稿：" + pres.Name);
                sb.AppendLine("路径：" + (pres.IsSaved ? pres.FullName : "(未保存)"));
                sb.AppendLine("页数：" + pres.SlideCount + "，当前页：" + pres.CurrentSlideIndex + (pres.IsBlank ? "（空白文稿）" : string.Empty));
                sb.AppendLine("尺寸：" + pres.SlideWidth + " × " + pres.SlideHeight + " pt");
                sb.Append("只读：" + (pres.IsReadOnly ? "是" : "否"));
                if (!string.IsNullOrEmpty(pres.DeckId)) sb.AppendLine().Append("关联工作区：" + pres.DeckId);
                return Task.FromResult(Success(request, sb.ToString(), ToJson(pres)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.get_active_presentation_failed"));
            }
        }
    }

    public sealed class PowerPointGetSelectionTool : PowerPointToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = PowerPointToolNames.GetSelection,
            DisplayName = "读取当前选区",
            Description = "返回当前选区：类型（none/slides/shapes/text）、所在页、选中形状名、选中文本。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "PowerPoint",
            IsReadOnly = true,
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
        };

        public override ToolDefinition Definition => _definition;

        public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request)
        {
            try
            {
                var sel = GetHost(context).GetSelectionInfo();
                if (sel == null)
                    return Task.FromResult(Fail(request, "当前演示文稿没有选区。", "ppt.no_selection"));

                var sb = new StringBuilder();
                sb.AppendLine("选区类型：" + (sel.SelectionType ?? "none"));
                sb.AppendLine("所在页：" + sel.SlideIndex);
                if (sel.ShapeNames != null && sel.ShapeNames.Count > 0)
                    sb.AppendLine("形状：" + string.Join("、", sel.ShapeNames));
                sb.Append("文本：" + (string.IsNullOrEmpty(sel.Text) ? "(空)" : sel.Text));
                return Task.FromResult(Success(request, sb.ToString(), ToJson(sel)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(request, ex, "ppt.get_selection_failed"));
            }
        }
    }
}
