# NixerOfficeCopilot

[简体中文](README.zh.md) | **English** | [日本語](README.ja.md) | [Deutsch](README.de.md) | [Español](README.es.md)

A VSTO copilot for Microsoft Word, Excel, and PowerPoint. The project separates Office host add-ins from a standalone Agent Broker, providing a shared conversation pane, model integrations, Office tool calls, and extensibility through MCP, Skills, and memory.

## Features

- **Three Office hosts**: Word, Excel, and PowerPoint each have a VSTO add-in and share the pane and Agent runtime.
- **Multiple model providers**: OpenAI, Azure OpenAI, DeepSeek, Ollama, LM Studio, Anthropic, and custom OpenAI-compatible endpoints.
- **Office tool calls**: Host-specific tools read and modify the active document; write operations require confirmation or backups according to each tool's policy.
- **Extensibility**: MCP (stdio / HTTP), Skills, user memory, and configurable built-in tools.
- **PowerPoint workflows**: Style and font catalogs, slide templates, previews, and presentation building.
- **Local conversation data**: Conversations are persisted as JSONL with an index; runtime data is isolated per Windows user.
- **Desktop pane**: A WinForms host presents the WebView2 frontend, while the Broker manages model credentials and Agent execution.

## Demo videos

| Word | Excel | PowerPoint |
| --- | --- | --- |
| ![Word demo](Resources/v/word.webp) | ![Excel demo](Resources/v/excel.webp) | ![PowerPoint demo](Resources/v/ppt.webp) |

## Architecture

```text
Office host process (Word / Excel / PowerPoint)
  VSTO add-in
    ├─ Shared task pane (WinForms + WebView2)
    ├─ Host adapters and native Office tools
    └─ HostBridge named-pipe service

Shared task pane <== Pipe A: JSON requests and events ==> Agent Broker
HostBridge       <== Pipe B: context and tool calls ===> Agent.Runtime
Agent.Runtime    ==> HTTPS / streaming responses ======> Model service/API

Agent Broker (one instance per Office window)
  Broker pipe service → Agent.Runtime → tool dispatch and turn execution
                                    ├─ Planning / Skills / MCP
                                    ├─ Memory / conversation persistence
                                    └─ Model adapters
```

Each Office window has its own Broker instance. The pane and Broker, and the Broker and Office host, communicate over separate named pipes. Model HTTP requests, turn orchestration, and tool-permission decisions run in the Broker; Office-specific document access is performed by adapters in the corresponding host process.

## Project structure

| Project | Responsibility |
| --- | --- |
| `NixerWordCopilot`, `NixerExcelCopilot`, `NixerPowerpointCopilot` | VSTO add-in entry points, Ribbon, and host window lifecycle |
| `OfficeCopilot.Panel.WebView`, `OfficeCopilot.Panel.WebAssets` | Shared task pane, WebView2 bridge, and frontend assets |
| `OfficeCopilot.Host.*`, `OfficeCopilot.Host.Abstractions` | Context reading and native Office tools for Word, Excel, and PowerPoint |
| `OfficeCopilot.HostBridge.*` | Request/response and event protocol between Office hosts and the Broker |
| `OfficeCopilot.Agent.Client`, `OfficeCopilot.Agent.Broker` | Broker IPC client, standalone process entry point, and pipe service |
| `OfficeCopilot.Agent.Runtime`, `OfficeCopilot.Agent.Planning` | Agent turn execution, context, models, tool dispatch, and Skill planning |
| `OfficeCopilot.Agent.Persistence`, `OfficeCopilot.Memory` | Conversation persistence and local memory |
| `OfficeCopilot.Mcp`, `OfficeCopilot.Skills` | MCP service registration, tool calls, and Skills loading |
| `OfficeCopilot.OpenXml.*`, `OfficeCopilot.Slides.*` | Office Open XML capabilities and PowerPoint rendering, templates, and styles |
| `OfficeCopilot.Settings.*`, `OfficeCopilot.Diagnostics` | Configuration contracts, settings UI, storage paths, and unified logging |
| `OfficeCopilot.Agent.Tests`, `OfficeCopilot.Eval`, `OfficeCopilot.OpenXml.SmokeTest` | Unit tests, evaluation, and Open XML smoke validation |

See [`docs/CODE_MAP.md`](docs/CODE_MAP.md) for file and module navigation, and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for more architecture background.

## Requirements

- Windows.
- Desktop Microsoft Word, Excel, or PowerPoint. The corresponding Office development components are required to build and debug VSTO add-ins.
- Visual Studio 2022 or later, Visual Studio MSBuild, and the .NET Framework 4.7.2 targeting pack.
- Microsoft Edge WebView2 Runtime.
- The NuGet packages and UI component references required by the project, including DevExpress 26.1 used by the existing projects.
- A reachable model-service endpoint and credentials configured in model settings. Local services such as Ollama can also be configured.

## Build and test

The repository contains legacy VSTO and `packages.config` projects. **Do not use `dotnet build` to build the entire solution.** From the repository root, run the unified script in Windows PowerShell:

```powershell
# Debug build
.\build.ps1

# Release rebuild and Agent unit tests
.\build.ps1 -Configuration Release -Rebuild -Test
```

The script locates Visual Studio MSBuild, restores NuGet packages, builds the solution, and checks that the Runtime assembly in the standalone Broker output is up to date. `OfficeCopilot.Agent.Tests` is a legacy MSTest project and runs through the Visual Studio Test Platform.

To stage deployment files, use `installer/Stage-Package.ps1`. It arranges the three add-ins and Broker output under `installer/staging/` for the subsequent installer workflow and validates the satellite assemblies for the app's own UI resources. The script recreates the staging directory. Before packaging, run the script, then copy the `<Culture>` folders downloaded from DevExpress (such as `zh-Hans`, `de`, and `es`) into `installer/staging/<Culture>/` so they are at the root of the final distribution directory.

## Configuration and local data

After first launch, open the model, MCP, Skills, memory, and tool settings from the Ribbon in any Copilot add-in. The default user data root is:

```text
%APPDATA%\NixerOfficeCopilot\
```

It contains `settings.json`, conversation data, prompts, Skills, logs, and other files. Settings may contain model credentials; do not commit them or user conversation data to the repository.

## Documentation

- [Code map](docs/CODE_MAP.md)
- [Architecture and review history](docs/ARCHITECTURE.md)
- [MCP call-chain audit](docs/MCP_CALL_CHAIN_AUDIT.md)
- [PowerPoint Copilot design and implementation](docs/POWERPOINT_COPILOT_BLUEPRINT.md)
- [Word test harness](docs/word_harness.md)

## License and contact

See [`LICENSE`](LICENSE). For questions or bug reports, visit [GitHub Issues](https://github.com/NixerWong/NixerOfficeCopilot/issues).
