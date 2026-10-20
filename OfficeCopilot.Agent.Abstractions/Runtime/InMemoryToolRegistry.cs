using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Agent.Abstractions
{
    public sealed class InMemoryToolRegistry : IToolRegistry
    {
        private readonly object _syncRoot;
        private readonly Dictionary<string, IAgentTool> _tools;
        private readonly Dictionary<string, string> _aliases;

        public InMemoryToolRegistry()
            : this(null)
        {
        }

        public InMemoryToolRegistry(IEnumerable<IAgentTool> tools)
        {
            _syncRoot = new object();
            _tools = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);
            _aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (tools != null)
            {
                RegisterRange(tools);
            }
        }

        public void Register(IAgentTool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            var definition = tool.Definition;
            if (definition == null)
            {
                throw new InvalidOperationException("工具定义不能为空。");
            }

            if (string.IsNullOrWhiteSpace(definition.Name))
            {
                throw new InvalidOperationException("工具名称不能为空。");
            }

            var toolName = definition.Name.Trim();

            lock (_syncRoot)
            {
                if (_tools.ContainsKey(toolName))
                {
                    throw new InvalidOperationException("已存在同名工具：" + toolName);
                }

                _tools.Add(toolName, tool);
            }
        }

        public void RegisterRange(IEnumerable<IAgentTool> tools)
        {
            if (tools == null)
            {
                return;
            }

            foreach (var tool in tools)
            {
                Register(tool);
            }
        }

        /// <summary>
        /// 登记旧工具名别名。别名只影响 <see cref="TryGetTool"/> 的查找，不出现在工具列表里，
        /// 因此模型只看到新名，而历史会话 / 技能里的旧名调用仍能路由到新工具。
        /// 已存在同名真实工具时以真实工具为准。
        /// </summary>
        public void RegisterAlias(string alias, string toolName)
        {
            if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(toolName))
            {
                return;
            }

            lock (_syncRoot)
            {
                _aliases[alias.Trim()] = toolName.Trim();
            }
        }

        public bool Unregister(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            lock (_syncRoot)
            {
                return _tools.Remove(toolName.Trim());
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _tools.Clear();
                _aliases.Clear();
            }
        }

        public IReadOnlyList<IAgentTool> GetTools()
        {
            lock (_syncRoot)
            {
                return _tools.Values
                    .Where(IsVisibleTool)
                    .OrderBy(t => t.Definition.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public IReadOnlyList<ToolDefinition> GetDefinitions()
        {
            lock (_syncRoot)
            {
                return _tools.Values
                    .Where(IsVisibleTool)
                    .Select(t => t.Definition)
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public bool TryGetTool(string toolName, out IAgentTool tool)
        {
            tool = null;

            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            lock (_syncRoot)
            {
                IAgentTool candidate;
                string target;
                var name = toolName.Trim();
                if (!_tools.TryGetValue(name, out candidate)
                    && !(_aliases.TryGetValue(name, out target) && _tools.TryGetValue(target, out candidate)))
                {
                    return false;
                }

                if (!IsVisibleTool(candidate))
                {
                    return false;
                }

                tool = candidate;
                return true;
            }
        }

        private static bool IsVisibleTool(IAgentTool tool)
        {
            return tool != null
                && tool.Definition != null
                && !string.IsNullOrWhiteSpace(tool.Definition.Name)
                && tool.Definition.IsEnabled;
        }
    }
}