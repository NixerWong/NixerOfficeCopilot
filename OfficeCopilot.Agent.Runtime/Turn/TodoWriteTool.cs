using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// Phase 2: Todo 管理工具 — 模型通过此工具管理会话中的任务列表。
    /// 对应 cc-haha <c>TodoWriteTool.ts</c>。
    ///
    /// 输入：{ "todos": [{ "content": "任务描述", "status": "pending"|"in_progress"|"completed" }] }
    /// 输出：{ "oldTodos": [...], "newTodos": [...] }
    ///
    /// 状态变更通过 Status 事件发送到 Panel 进行 UI 渲染。
    /// </summary>
    internal sealed class TodoWriteTool
    {
        public const string ToolName = "runtime.todo_write";

        /// <summary>会话作用域的任务列表。key = conversationId。</summary>
        private static readonly Dictionary<string, List<TodoItem>> _store =
            new Dictionary<string, List<TodoItem>>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _lock = new object();

        /// <summary>获取指定会话的任务列表副本。</summary>
        public static IReadOnlyList<TodoItem> GetTodos(string conversationId)
        {
            lock (_lock)
            {
                if (!string.IsNullOrWhiteSpace(conversationId) && _store.TryGetValue(conversationId, out var list))
                    return list.ToList();
                return Array.Empty<TodoItem>();
            }
        }

        /// <summary>执行 Todo 写操作。返回 (oldTodos, newTodos) 用于构造响应。</summary>
        public ToolExecutionOutcome Execute(
            string toolCallId,
            string toolName,
            string argumentsJson,
            string conversationId,
            Action<string> onTodosChanged)
        {
            List<TodoItem> newTodos;
            List<TodoItem> oldTodos;

            try
            {
                using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
                if (!doc.RootElement.TryGetProperty("todos", out var todosElement) || todosElement.ValueKind != JsonValueKind.Array)
                {
                    return new ToolExecutionOutcome
                    {
                        ToolCallId = toolCallId ?? string.Empty,
                        ToolName = toolName ?? string.Empty,
                        Success = false,
                        ErrorCode = "todo.invalid_input",
                        ErrorMessage = "Expected JSON with 'todos' array."
                    };
                }

                newTodos = new List<TodoItem>();
                foreach (var item in todosElement.EnumerateArray())
                {
                    var content = item.TryGetProperty("content", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                    var statusStr = item.TryGetProperty("status", out var s) ? s.GetString() ?? "pending" : "pending";
                    if (!IsValidStatus(statusStr)) statusStr = "pending";
                    newTodos.Add(new TodoItem { Content = content, Status = statusStr });
                }

                lock (_lock)
                {
                    var key = conversationId ?? string.Empty;
                    if (!_store.TryGetValue(key, out oldTodos))
                        oldTodos = new List<TodoItem>();
                    else
                        oldTodos = oldTodos.ToList();

                    _store[key] = newTodos;
                }
            }
            catch (JsonException ex)
            {
                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? string.Empty,
                    Success = false,
                    ErrorCode = "todo.parse_error",
                    ErrorMessage = $"Failed to parse todo input: {ex.Message}"
                };
            }

            // Emit status event for Panel UI update
            if (onTodosChanged != null)
            {
                try
                {
                    var statusJson = JsonSerializer.Serialize(new
                    {
                        type = "todo.update",
                        todos = newTodos.Select(t => new { content = t.Content, status = t.Status }).ToList(),
                        oldTodos = oldTodos.Select(t => new { content = t.Content, status = t.Status }).ToList()
                    });
                    onTodosChanged(statusJson);
                }
                catch { }
            }

            var allDone = newTodos.All(t => t.Status == "completed");
            var resultSb = new StringBuilder();
            resultSb.Append("Todo list updated. ");
            if (newTodos.Count == 0)
                resultSb.Append("All tasks cleared.");
            else
            {
                var pending = newTodos.Count(t => t.Status == "pending");
                var inProgress = newTodos.Count(t => t.Status == "in_progress");
                var done = newTodos.Count(t => t.Status == "completed");
                resultSb.Append(pending).Append(" pending, ").Append(inProgress).Append(" in progress, ").Append(done).Append(" completed.");
                if (allDone) resultSb.Append(" All tasks complete!");
            }

            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? string.Empty,
                Success = true,
                Content = resultSb.ToString(),
                StructuredContentJson = JsonSerializer.Serialize(new
                {
                    oldTodos = oldTodos.Select(t => new { content = t.Content, status = t.Status }).ToList(),
                    newTodos = newTodos.Select(t => new { content = t.Content, status = t.Status }).ToList()
                })
            };
        }

        private static bool IsValidStatus(string status)
        {
            return status == "pending" || status == "in_progress" || status == "completed";
        }
    }

    /// <summary>
    /// 单个 Todo 条目。
    /// </summary>
    public sealed class TodoItem
    {
        public string Content { get; set; } = string.Empty;
        public string Status { get; set; } = "pending";
    }
}
