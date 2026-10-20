# NixerOfficeCopilot

面向 Microsoft Word、Excel 和 PowerPoint 的 VSTO Copilot。项目采用 Office 宿主加载项与独立 Agent Broker 分层运行，提供统一的对话面板、模型接入、Office 工具调用，以及 MCP、Skills 和记忆扩展能力。

[English summary](#overview) · [架构](#架构) · [构建与测试](#构建与测试) · [许可证](LICENSE)

## 功能概览

- **三个 Office 宿主**：Word、Excel、PowerPoint 各自提供 VSTO 加载项，共享面板和 Agent 运行时。
- **多模型配置**：支持 OpenAI、Azure OpenAI、DeepSeek、Ollama、LM Studio、Anthropic，以及自定义 OpenAI 兼容端点。
- **Office 工具调用**：通过宿主专属工具读取和修改当前文档；写入操作可按工具策略要求确认或备份。
- **可扩展能力**：支持 MCP（stdio / HTTP）、Skills、用户记忆和可配置的内置工具。
- **PowerPoint 工作流**：包含样式与字体目录、幻灯片模板、预览及演示文稿构建能力。
- **本地会话数据**：会话以 JSONL 持久化，并提供索引；运行数据按 Windows 用户隔离。
- **桌面面板**：使用 WinForms 承载 WebView2 前端，模型凭据和 Agent 执行逻辑由 Broker 管理。

## 架构

```text
Office 宿主进程（Word / Excel / PowerPoint）
  VSTO 加载项
    ├─ 共享任务窗格（WinForms + WebView2）
    ├─ 宿主适配器与原生 Office 工具
    └─ HostBridge 命名管道服务

共享任务窗格  <== Pipe A：JSON 请求与事件 ==>  Agent Broker
HostBridge    <== Pipe B：上下文与工具调用 ==>  Agent.Runtime
Agent.Runtime ==> HTTPS / 流式响应 ========>  模型服务/API

Agent Broker（每个 Office 窗口独立运行）
  Broker 管道服务 → Agent.Runtime → 工具调度与回合执行
                                  ├─ Planning / Skills / MCP
                                  ├─ Memory / 会话持久化
                                  └─ 模型适配器
```

每个 Office 窗口拥有独立的 Broker 运行实例。面板与 Broker、Broker 与 Office 宿主分别通过命名管道通信。模型 HTTP 请求、回合编排和工具权限决策位于 Broker；Office 专属的文档访问仍由对应宿主进程中的适配器执行。

## 项目结构

| 工程 | 职责 |
| --- | --- |
| `NixerWordCopilot`、`NixerExcelCopilot`、`NixerPowerpointCopilot` | VSTO 加载项入口、Ribbon 与宿主窗口生命周期 |
| `OfficeCopilot.Panel.WebView`、`OfficeCopilot.Panel.WebAssets` | 共享任务窗格、WebView2 桥接及前端资源 |
| `OfficeCopilot.Host.*`、`OfficeCopilot.Host.Abstractions` | Word、Excel、PowerPoint 的上下文读取和原生 Office 工具 |
| `OfficeCopilot.HostBridge.*` | Office 宿主与 Broker 之间的请求/响应和事件协议 |
| `OfficeCopilot.Agent.Client`、`OfficeCopilot.Agent.Broker` | Broker IPC 客户端、独立进程入口及管道服务 |
| `OfficeCopilot.Agent.Runtime`、`OfficeCopilot.Agent.Planning` | Agent 回合执行、上下文、模型、工具调度和 Skill 规划 |
| `OfficeCopilot.Agent.Persistence`、`OfficeCopilot.Memory` | 会话持久化与本地记忆 |
| `OfficeCopilot.Mcp`、`OfficeCopilot.Skills` | MCP 服务注册、工具调用及 Skills 加载 |
| `OfficeCopilot.OpenXml.*`、`OfficeCopilot.Slides.*` | Office Open XML 文档能力及 PowerPoint 渲染、模板和样式功能 |
| `OfficeCopilot.Settings.*`、`OfficeCopilot.Diagnostics` | 配置契约、配置界面、存储路径和统一日志 |
| `OfficeCopilot.Agent.Tests`、`OfficeCopilot.Eval`、`OfficeCopilot.OpenXml.SmokeTest` | 单元测试、评测和 Open XML 冒烟验证 |

详细文件导航和模块地图见 [`docs/CODE_MAP.md`](docs/CODE_MAP.md)；更深入的架构背景见 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)。

## 环境要求

- Windows。
- Microsoft Word、Excel 或 PowerPoint 桌面版；构建和调试 VSTO 加载项需要相应的 Office 开发组件。
- Visual Studio 2022 或更高版本、Visual Studio MSBuild，以及 .NET Framework 4.7.2 targeting pack。
- Microsoft Edge WebView2 Runtime。
- 项目依赖的 NuGet 包和 UI 组件引用（包括现有工程使用的 DevExpress 26.1）。
- 运行时需在模型配置中提供可访问的模型服务端点和相应凭据；也可配置本地 Ollama 等服务。

## 构建与测试

仓库包含 legacy VSTO 和 `packages.config` 工程，**不要使用 `dotnet build` 构建整个解决方案**。请在 Windows PowerShell 中从仓库根目录运行统一脚本：

```powershell
# Debug 构建
.\build.ps1

# Release 重建并运行 Agent 单元测试
.\build.ps1 -Configuration Release -Rebuild -Test
```

脚本会查找 Visual Studio MSBuild、还原 NuGet 包、构建解决方案，并检查独立 Broker 输出中的 Runtime 程序集是否为最新版本。`OfficeCopilot.Agent.Tests` 是 legacy MSTest 工程，测试通过 Visual Studio Test Platform 运行。

部署文件暂存可使用 `installer/Stage-Package.ps1`；该脚本将三个加载项与 Broker 输出整理到 `installer/staging/`，供后续安装包流程使用。

## 配置与本地数据

首次运行后，可从各 Copilot 的 Ribbon 打开模型、MCP、Skills、记忆和工具配置界面。默认用户数据根目录为：

```text
%APPDATA%\NixerOfficeCopilot\
```

其中包含 `settings.json`、会话数据、提示词、Skills 和日志等。设置文件可能包含模型凭据；请勿将其或用户会话数据提交到版本库。

## 文档

- [代码地图](docs/CODE_MAP.md)
- [架构说明与审查记录](docs/ARCHITECTURE.md)
- [MCP 调用链审查](docs/MCP_CALL_CHAIN_AUDIT.md)
- [PowerPoint Copilot 设计与实现说明](docs/POWERPOINT_COPILOT_BLUEPRINT.md)
- [Word 测试 Harness](docs/word_harness.md)

## Overview

NixerOfficeCopilot is a Windows desktop copilot for Microsoft Word, Excel, and PowerPoint. VSTO add-ins host a shared WebView2 task pane and communicate with a per-window Agent Broker over named pipes. The Broker owns model requests, turn orchestration, tool policy, MCP/Skills integration, and conversation persistence; document operations are executed by host-specific adapters inside Office.

See the Chinese sections above for architecture, prerequisites, build commands, and local data locations.

## License and contact

See [`LICENSE`](LICENSE). Questions and bug reports: [GitHub Issues](https://github.com/NixerWong/NixerOfficeCopilot/issues).
