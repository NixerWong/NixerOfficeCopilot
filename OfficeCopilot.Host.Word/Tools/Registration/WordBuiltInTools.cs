using OfficeCopilot.Agent.Abstractions;
using System.Collections.Generic;

namespace OfficeCopilot.Host.Word.Tools
{
    /// <summary>
    /// Word 的工具注册选择。语义完全继承自 <see cref="ToolRegistrationOptions"/>，
    /// 保留独立类型只是为了让调用方的意图更清晰。
    /// </summary>
    public sealed class WordToolRegistrationOptions : ToolRegistrationOptions
    {
        public WordToolRegistrationOptions()
        {
        }

        public WordToolRegistrationOptions(IEnumerable<string> enabledGroupNames, IEnumerable<string> enabledToolNames)
            : base(enabledGroupNames, enabledToolNames)
        {
        }
    }

    /// <summary>
    /// Word 的工具目录选项。语义完全继承自 <see cref="ToolCatalogOptions"/>。
    /// </summary>
    public sealed class WordToolCatalogOptions : ToolCatalogOptions
    {
    }
}
