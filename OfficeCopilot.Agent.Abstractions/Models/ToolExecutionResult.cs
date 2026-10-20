using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Abstractions
{
    public sealed class ToolExecutionResult
    {
        public ToolExecutionResult()
        {
            ToolCallId = string.Empty;
            ToolName = string.Empty;
            Content = string.Empty;
            StructuredContentJson = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ContentParts = new List<ToolResultContentPart>();
        }

        public string ToolCallId { get; set; }

        public string ToolName { get; set; }

        public bool Success { get; set; }

        public string Content { get; set; }

        public string StructuredContentJson { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public IDictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// 工具附带的非文本内容（目前只有图像）。只在模型支持图像输入时转交给模型，不写入会话持久化。
        /// </summary>
        public IList<ToolResultContentPart> ContentParts { get; set; }

        public ToolExecutionResult AddImage(string mediaType, byte[] data, string description)
        {
            if (data != null && data.Length > 0)
            {
                ContentParts.Add(new ToolResultContentPart
                {
                    Type = ToolResultContentPart.ImageType,
                    MediaType = string.IsNullOrWhiteSpace(mediaType) ? "image/png" : mediaType,
                    Base64 = Convert.ToBase64String(data),
                    Description = description ?? string.Empty
                });
            }

            return this;
        }

        public static ToolExecutionResult CreateSuccess(
            string toolName,
            string toolCallId,
            string content,
            string structuredContentJson = null)
        {
            return new ToolExecutionResult
            {
                ToolName = toolName ?? string.Empty,
                ToolCallId = toolCallId ?? string.Empty,
                Success = true,
                Content = content ?? string.Empty,
                StructuredContentJson = structuredContentJson ?? string.Empty
            };
        }

        public static ToolExecutionResult CreateFailure(
            string toolName,
            string toolCallId,
            string errorMessage,
            string errorCode = null,
            string content = null,
            string structuredContentJson = null)
        {
            return new ToolExecutionResult
            {
                ToolName = toolName ?? string.Empty,
                ToolCallId = toolCallId ?? string.Empty,
                Success = false,
                Content = content ?? string.Empty,
                StructuredContentJson = structuredContentJson ?? string.Empty,
                ErrorCode = errorCode ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }
    }

    public sealed class ToolResultContentPart
    {
        public const string ImageType = "image";

        public string Type { get; set; }

        public string MediaType { get; set; }

        public string Base64 { get; set; }

        public string Description { get; set; }
    }
}