using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Agent.Abstractions
{
    /// <summary>
    /// 工具注册选择：用户在设置界面里勾选的工具组与工具名。
    /// 三个宿主（Excel / Word / PowerPoint）共用同一份语义，各自的派生类型仅用于保持
    /// 调用方的可读性与向后兼容。
    /// </summary>
    public class ToolRegistrationOptions
    {
        public ToolRegistrationOptions()
            : this(null, null)
        {
            UseExplicitSelection = false;
        }

        public ToolRegistrationOptions(IEnumerable<string> enabledGroupNames, IEnumerable<string> enabledToolNames)
        {
            EnabledGroupNames = CreateSet(enabledGroupNames);
            EnabledToolNames = CreateSet(enabledToolNames);
            UseExplicitSelection = true;
        }

        public ISet<string> EnabledGroupNames { get; private set; }

        public ISet<string> EnabledToolNames { get; private set; }

        /// <summary>
        /// 为 false 时表示用户从未配置过，注册全部工具。
        /// </summary>
        public bool UseExplicitSelection { get; set; }

        private static ISet<string> CreateSet(IEnumerable<string> values)
        {
            var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values == null)
            {
                return items;
            }

            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                items.Add(value.Trim());
            }

            return items;
        }
    }

    /// <summary>
    /// 构建宿主工具目录时的完整选项：注册选择 + 用户对工具定义的覆盖。
    /// </summary>
    public class ToolCatalogOptions
    {
        public ToolCatalogOptions()
        {
            Registration = new ToolRegistrationOptions();
            DefinitionOverrides = new List<ToolDefinitionOverride>();
        }

        public ToolRegistrationOptions Registration { get; set; }

        public IList<ToolDefinitionOverride> DefinitionOverrides { get; private set; }

        public bool HasDefinitionOverrides
        {
            get
            {
                return DefinitionOverrides != null
                    && DefinitionOverrides.Any(p => p != null && !string.IsNullOrWhiteSpace(p.ToolName));
            }
        }
    }
}
