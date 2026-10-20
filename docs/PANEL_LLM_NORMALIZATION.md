# Panel LLM 客户端归一化记录

日期：2026-08-25

## 目标

按照 `docs/ARCHITECTURE.md` 的 P1-1/C3 要求，删除 Panel 侧重复的多供应商 LLM 客户端，让标题和摘要生成统一经过 Broker 的 `Agent.Runtime`。

## 调查结论

- `OfficeCopilot.Panel.WebView/Services/ChatApiClient.cs` 约 1810 行，实际业务调用只有会话标题和摘要两处。
- `OfficeCopilot.Agent.Runtime/HttpModelClient.cs` 已负责 OpenAI-compatible、Ollama、Anthropic 等模型请求，是 Broker 侧唯一模型实现。
- Panel 已通过 `OfficeCopilot.Agent.Client` 的命名管道客户端连接 Broker，因此不需要新增 HTTP 或供应商协议代码。

## 实施内容

1. 在 `Agent.Client` 增加 `broker.completion.single.request/response` 协议、请求 DTO、响应 DTO，以及 `IAgentBrokerClient.CompleteAsync`。
2. 在 Broker/Runtime 增加无工具、非会话回合的 completion 路由；消息转换为 Runtime 的 `ModelClientMessage` 后调用 `IModelClient`。
3. `AgentRuntimeService` 只创建一个 `HttpModelClient`，同时供正常 turn、上下文压缩和单次 completion 使用。
4. Panel 标题/摘要改为调用 Broker completion；删除三套 TaskPaneManager 中的 `ChatApiClient` 构造和日志对象。
5. 删除 `ChatApiClient.cs`，并从 `Panel.WebView.csproj` 移除编译项。

## 验证

- `dotnet build OfficeCopilot.Agent.Client\OfficeCopilot.Agent.Client.csproj -c Debug`：通过。
- `dotnet build OfficeCopilot.Agent.Runtime\OfficeCopilot.Agent.Runtime.csproj -c Debug`：通过。
- `dotnet build OfficeCopilot.Agent.Broker\OfficeCopilot.Agent.Broker.csproj -c Debug`：通过。
- 全局搜索 `ChatApiClient`、`_apiClient`、`SendSingleResultRequestAsync`：无生产代码引用。

## 需要用户配合

Panel 和三个 VSTO 工程是 legacy .NET Framework/VSTO 工程，当前环境的 `dotnet build` 不负责完整验证它们。请在已安装 Office/VSTO 开发工具的 Visual Studio 中执行 Rebuild，至少验证 `OfficeCopilot.Panel.WebView` 及 Excel 加载项；若运行时验证标题/摘要，请确认 Broker 使用的是本次重建后的输出。