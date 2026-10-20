using System;
using System.Collections.Generic;

namespace OfficeCopilot.Memory
{
    /// <summary>记忆作用域（与 cc-haha 对齐）。</summary>
    public enum MemoryScope
    {
        /// <summary>用户级：跨所有项目和会话。</summary>
        User = 0,

        /// <summary>工作区级：当前项目/宿主应用。</summary>
        Workspace = 1,

        /// <summary>会话级：仅当前会话。</summary>
        Session = 2
    }

    /// <summary>单条记忆片段。</summary>
    public sealed class MemoryFragment
    {
        /// <summary>唯一标识（文件名不含扩展名）。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>标签列表。</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>作用域。</summary>
        public MemoryScope Scope { get; set; } = MemoryScope.User;

        /// <summary>记忆类型：user / feedback / project / reference。</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>记忆正文（Markdown）。</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>创建时间。</summary>
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间。</summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>入口文件截断信息。</summary>
    public sealed class EntrypointLoadResult
    {
        public string Content { get; set; } = string.Empty;
        public int LineCount { get; set; }
        public int ByteCount { get; set; }
        public bool WasLineTruncated { get; set; }
        public bool WasByteTruncated { get; set; }
    }
}
