using System;
using System.Collections.Generic;

namespace OfficeCopilot.Memory
{
    /// <summary>
    /// 树节点类型。
    /// </summary>
    public enum MemoryNodeType
    {
        /// <summary>根节点（虚拟）。</summary>
        Root = 0,

        /// <summary>MEMORY.md 入口文件节点。</summary>
        Entrypoint = 1,

        /// <summary>fragments 文件夹节点（虚拟，已弃用保留兼容）。</summary>
        FragmentFolder = 2,

        /// <summary>单个记忆片段。</summary>
        Fragment = 3,

        /// <summary>MEMORY.md 中的一条索引行，指向某个 fragment。</summary>
        IndexEntry = 4
    }

    /// <summary>
    /// 记忆管理器树节点，供 UI 层构建左侧树状结构。
    /// </summary>
    public sealed class MemoryTreeNode
    {
        /// <summary>节点唯一标识：
        /// Entrypoint: "entrypoint"；
        /// Fragment: fragment.Id（文件名不含扩展名）；
        /// 虚拟节点: "root" / "fragments"。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>显示名称。</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>节点类型。</summary>
        public MemoryNodeType NodeType { get; set; }

        /// <summary>标签（仅 Fragment 有效）。</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>作用域字符串（仅 Fragment 有效）。</summary>
        public string Scope { get; set; } = string.Empty;

        /// <summary>最后更新时间。</summary>
        public DateTime UpdatedAtUtc { get; set; }

        /// <summary>关联的 fragment ID（仅 IndexEntry 类型有效）。</summary>
        public string FragmentId { get; set; } = string.Empty;

        /// <summary>子节点。</summary>
        public List<MemoryTreeNode> Children { get; set; } = new List<MemoryTreeNode>();
    }
}
