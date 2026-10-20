using System;
using System.Collections.Generic;

namespace OfficeCopilot.Panel.WebView.Models
{
    public enum InteractiveMode
    {
        Agent = 0,
        Ask = 1
    }

    public class ChatSessionState
    {
        /// <summary>新会话的默认标题；LLM 标题生成完成前一直显示这个。</summary>
        public const string DefaultNewSessionTitle = "新会话";

        public Guid Id { get; set; }
        public string Title { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int? ModelProviderId { get; set; }
        public List<ChatMessageState> Messages { get; set; }
    }

    public class ChatMessageState
    {
        public string Id { get; set; }
        public string Role { get; set; }
        public string Content { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> InfoItems { get; set; }
        public List<ChatToolCallState> ToolCalls { get; set; }
        public List<ChatCodeExecutionState> CodeExecutions { get; set; }
        /// <summary>
        /// 有序的内容片段列表，用于历史会话中按正确顺序交错渲染文本和工具调用。
        /// 非 null 时前端优先使用此列表渲染；为 null 时回退到 Content + ToolCalls 的旧行为。
        /// </summary>
        public List<ChatContentPart> ContentParts { get; set; }
    }

    /// <summary>
    /// 表示助手消息中的一个有序内容片段：文本或工具调用引用。
    /// </summary>
    public class ChatContentPart
    {
        /// <summary>"text" 或 "tool_call"</summary>
        public string Type { get; set; }
        /// <summary>Type=text 时的文本内容</summary>
        public string Text { get; set; }
        /// <summary>Type=tool_call 时引用 ToolCalls 中的 ToolCallId</summary>
        public string ToolCallId { get; set; }
    }

    public class ChatCodeExecutionState
    {
        public int CodeBlockIndex { get; set; }
        public string Language { get; set; }
        public string BackupPath { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ChatToolCallState
    {
        public string ToolCallId { get; set; }
        public string ToolName { get; set; }
        public bool Success { get; set; }
        public string Status { get; set; }
        public string Content { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool RequiresBackup { get; set; }
        public string BackupPath { get; set; }
    }

    public class ApiChatMessage
    {
        public ApiChatMessage()
        {
            Content = string.Empty;
            ContentParts = new List<ApiMessageContentPart>();
            Name = string.Empty;
            ToolCallId = string.Empty;
            ToolCalls = new List<ApiToolCall>();
        }

        public string Role { get; set; }

        public string Content { get; set; }

        public List<ApiMessageContentPart> ContentParts { get; set; }

        public string Name { get; set; }

        public string ToolCallId { get; set; }

        public List<ApiToolCall> ToolCalls { get; set; }
    }

    public class ApiMessageContentPart
    {
        public ApiMessageContentPart()
        {
            Type = string.Empty;
            Text = string.Empty;
            MediaType = string.Empty;
            Url = string.Empty;
            FileName = string.Empty;
            FileContentBase64 = string.Empty;
        }

        public string Type { get; set; }

        public string Text { get; set; }

        public string MediaType { get; set; }

        public string Url { get; set; }

        public string FileName { get; set; }

        public string FileContentBase64 { get; set; }
    }

    public class ApiToolCall
    {
        public ApiToolCall()
        {
            Id = string.Empty;
            Type = "function";
            Function = new ApiToolFunctionCall();
        }

        public string Id { get; set; }

        public string Type { get; set; }

        public ApiToolFunctionCall Function { get; set; }
    }

    public class ApiToolFunctionCall
    {
        public ApiToolFunctionCall()
        {
            Name = string.Empty;
            Arguments = "{}";
        }

        public string Name { get; set; }

        public string Arguments { get; set; }
    }

    public class AgentCompletionResult
    {
        public AgentCompletionResult()
        {
            Content = string.Empty;
            ReasoningContent = string.Empty;
            ToolCalls = new List<ApiToolCall>();
            FinishReason = string.Empty;
        }

        public string Content { get; set; }

        public string ReasoningContent { get; set; }

        public string FinishReason { get; set; }

        public List<ApiToolCall> ToolCalls { get; set; }

        public bool HasToolCalls
        {
            get { return ToolCalls != null && ToolCalls.Count > 0; }
        }
    }
}