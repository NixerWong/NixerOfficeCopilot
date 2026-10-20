using System;
using System.Collections.Generic;

namespace OfficeCopilot.Agent.Abstractions
{
    public interface IToolExecutionPreflight
    {
        ToolExecutionPreflightResult Preflight(
            ToolExecutionContext context,
            ToolExecutionRequest request);
    }

    public sealed class ToolExecutionPreflightResult
    {
        public bool Success { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public string DetailsJson { get; set; }

        public static ToolExecutionPreflightResult CreateSuccess(string detailsJson)
        {
            return new ToolExecutionPreflightResult
            {
                Success = true,
                DetailsJson = detailsJson ?? string.Empty
            };
        }

        public static ToolExecutionPreflightResult CreateFailure(string errorCode, string errorMessage)
        {
            return new ToolExecutionPreflightResult
            {
                ErrorCode = errorCode ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }
    }

    public sealed class ToolExecutionRequest
    {
        public ToolExecutionRequest()
        {
            ToolCallId = string.Empty;
            ToolName = string.Empty;
            ArgumentsJson = "{}";
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ToolCallId { get; set; }

        public string ToolName { get; set; }

        public string ArgumentsJson { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }
}