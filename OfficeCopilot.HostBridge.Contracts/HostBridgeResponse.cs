using System;
using System.Collections.Generic;

namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class HostBridgeResponse
    {
        public HostBridgeResponse()
        {
            RequestId = string.Empty;
            Operation = string.Empty;
            PayloadJson = "{}";
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string RequestId { get; set; }

        /// <summary>回显 <see cref="HostBridgeRequest.Operation"/>，与 Broker 协议响应回显 MessageType 对齐。</summary>
        public string Operation { get; set; }

        public bool Success { get; set; }

        public string PayloadJson { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public IDictionary<string, string> Metadata { get; set; }

        public static HostBridgeResponse CreateSuccess(string requestId, string payloadJson)
        {
            return CreateSuccess(requestId, string.Empty, payloadJson);
        }

        public static HostBridgeResponse CreateSuccess(string requestId, string operation, string payloadJson)
        {
            return new HostBridgeResponse
            {
                RequestId = requestId ?? string.Empty,
                Operation = operation ?? string.Empty,
                Success = true,
                PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson
            };
        }

        public static HostBridgeResponse CreateFailure(string requestId, string errorCode, string errorMessage)
        {
            return CreateFailure(requestId, string.Empty, errorCode, errorMessage);
        }

        public static HostBridgeResponse CreateFailure(string requestId, string operation, string errorCode, string errorMessage)
        {
            return new HostBridgeResponse
            {
                RequestId = requestId ?? string.Empty,
                Operation = operation ?? string.Empty,
                Success = false,
                ErrorCode = errorCode ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }
    }
}
