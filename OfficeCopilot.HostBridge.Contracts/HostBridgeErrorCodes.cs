namespace OfficeCopilot.HostBridge.Contracts
{
    /// <summary>
    /// HostBridge 协议的错误码常量，与 Panel↔Broker 协议的
    /// <c>AgentBrokerMessageTypes</c> 保持同一种「常量化 + 点分小写」风格。
    /// </summary>
    public static class HostBridgeErrorCodes
    {
        // ---- 传输层 ----

        /// <summary>客户端尚未配置真实的 HostBridge 连接（<c>NullHostBridgeClient</c>）。</summary>
        public const string NotConnected = "host_bridge.not_connected";

        /// <summary>服务端读到空请求行。</summary>
        public const string EmptyRequest = "host_bridge.empty_request";

        /// <summary>请求行不是合法 JSON。</summary>
        public const string InvalidRequest = "host_bridge.invalid_request";

        /// <summary>客户端读到空响应行（通常是宿主进程已退出）。</summary>
        public const string EmptyResponse = "host_bridge.empty_response";

        /// <summary>响应行不是合法 JSON。</summary>
        public const string InvalidResponse = "host_bridge.invalid_response";

        /// <summary>响应信封反序列化后为 null。</summary>
        public const string NullResponse = "host_bridge.null_response";

        /// <summary>调用方完全没有拿到响应。</summary>
        public const string NoResponse = "host_bridge.no_response";

        // ---- 分发层 ----

        /// <summary>请求处理器抛出未捕获异常。</summary>
        public const string HandlerError = "host_bridge.handler_error";

        /// <summary><see cref="HostBridgeRequest.Operation"/> 不在 <see cref="HostBridgeOperationNames"/> 之内。</summary>
        public const string OperationUnsupported = "host_bridge.operation.unsupported";

        /// <summary>请求 <c>PayloadJson</c> 无法反序列化为对应操作的 DTO。</summary>
        public const string InvalidPayload = "host_bridge.invalid_payload";

        // ---- 宿主能力层 ----

        public const string HostUnavailable = "host_bridge.host_unavailable";
        public const string InsertTextFailed = "host_bridge.insert_text_failed";
        public const string CodeExecutionUnavailable = "host_bridge.code_execution_unavailable";
        public const string CodeExecutionUnsupportedLanguage = "host_bridge.code_execution_unsupported_language";
        public const string CodeExecutionFailed = "host_bridge.code_execution_failed";
        public const string ToolRegistryUnavailable = "host_bridge.tool_registry_unavailable";
        public const string InvalidToolPayload = "host_bridge.invalid_tool_payload";
        public const string ToolExecutionFailed = "host_bridge.tool_execution_failed";
    }
}
