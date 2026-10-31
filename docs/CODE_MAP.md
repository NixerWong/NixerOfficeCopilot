# NixerOfficeCopilot 代码地图

> 用途：**导航索引**。回答"某功能在哪个工程/哪个文件"，供后续交互快速定位，避免重复全仓扫描。
> 数据口径：2026-08-29 实测。排除 `bin/` `obj/` `packages/` `TestResults/`；行数 = `[System.IO.File]::ReadAllLines().Count`。
> 规模：**34 个工程 / 528 个 `.cs` 文件 / 99,123 行**。文件编码：全仓 UTF-8 无 BOM。
>
> 相关文档：[ARCHITECTURE.md](ARCHITECTURE.md)（架构审查与问题库）· [REMEDIATION_PLAN.md](REMEDIATION_PLAN.md)（整改进度）· [BACKLOG.md](BACKLOG.md)

---

## 目录

1. [三进程运行时模型](#1-三进程运行时模型)
2. [分层与工程清单](#2-分层与工程清单)
3. [按功能查文件（速查表）](#3-按功能查文件速查表)
4. [核心工程内部结构](#4-核心工程内部结构)
5. [关键数据流](#5-关键数据流)
6. [协议与契约](#6-协议与契约)
7. [构建与测试](#7-构建与测试)
8. [大文件索引](#8-大文件索引)

---

## 1. 三进程运行时模型

```
┌─ EXCEL.EXE / WINWORD.EXE / POWERPNT.EXE（Office 宿主进程）───────────┐
│  NixerXxxCopilot（VSTO 入口）                                        │
│    └─ CopilotTaskPaneManagerBase<TWindow>（Panel.WebView/Composition）│
│         ├─ CopilotPanelControl（WinForms + WebView2 + DevExpress）    │
│         ├─ Host.{Excel,Word,PowerPoint} + InMemoryToolRegistry       │
│         └─ NamedPipeHostBridgeServer                                 │
└──────────┬────────────────────────────────────┬──────────────────────┘
           │ 管道 A                              │ 管道 B（反向）
           │ officecopilot-agent-broker-         │ officecopilot-host-bridge-
           │   {pid}-{windowKey}                 │   {pid}-{windowKey}
           ▼                                     ▲
┌─ OfficeCopilot.Agent.Broker.exe（独立进程，加入 JobObject）──────────┐
│  NamedPipeBrokerHost → BrokerEnvelopeDispatcher → BrokerTurnService  │
│    └─ Agent.Runtime：BrokerTurnExecutor（Turn 循环）                 │
│         ├─ DefaultToolDispatcher（工具目录 + 调度）                  │
│         ├─ ModelProviders（OpenAI 兼容 / Anthropic / Ollama）        │
│         ├─ Context（压缩）· Planning（Skill）· Memory · Mcp          │
│         └─ Persistence（JSONL + SQLite 索引）                        │
└──────────────────────┬───────────────────────────────────────────────┘
                       ▼ HTTPS/SSE
                    LLM API
```

**关键约束**
- **每个 Office 窗口一个 Broker 子进程**，管道名由 `HostBridgePipeNameFactory` 从 `{pid}-{windowKey}` 派生。
- Broker 加入 **JobObject**，Office 崩溃时随之终止。
- Panel **不持有 API Key**：标题/摘要也走 `broker.completion.single.request` 下沉到 Broker。
- 前端 WebView2 通过 `SetVirtualHostNameToFolderMapping` 以 `https://appassets.local` 同源加载。

---

## 2. 分层与工程清单

| 层 | 工程 | 文件 | 行数 | 格式 | TFM |
|---|---|---:|---:|---|---|
| **L0 · VSTO 外壳** | NixerExcelCopilot | 9 | 1255 | legacy | v4.7.2 |
| | NixerWordCopilot | 9 | 1246 | legacy | v4.7.2 |
| | NixerPowerpointCopilot | 9 | 1120 | legacy | v4.7.2 |
| **L1 · UI 面板** | OfficeCopilot.Panel.WebView | 29 | 12521 | legacy | v4.7.2 |
| | OfficeCopilot.Panel.WebAssets | 2 | 78 | legacy | v4.7.2 |
| | OfficeCopilot.Panel.Abstractions | 3 | 241 | legacy | v4.7.2 |
| | OfficeCopilot.Settings.WinForms | 25 | **16006** | legacy | v4.7.2 |
| **L2 · 宿主能力** | OfficeCopilot.Host.Excel | 63 | 13861 | legacy | v4.7.2 |
| | OfficeCopilot.Host.Word | 37 | 4424 | SDK | net472 |
| | OfficeCopilot.Host.PowerPoint | 8 | 759 | SDK | net472 |
| | OfficeCopilot.Host.Abstractions | 23 | 3254 | legacy | v4.7.2 |
| | OfficeCopilot.Agent.Abstractions | 18 | 1436 | legacy | v4.7.2 |
| **L3 · 进程桥** | OfficeCopilot.HostBridge.Server | 7 | 1102 | SDK | net472 |
| | OfficeCopilot.HostBridge.Contracts | 12 | 487 | SDK | netstandard2.0 |
| | OfficeCopilot.HostBridge.Client | 4 | 199 | SDK | netstandard2.0 |
| | OfficeCopilot.Agent.Client | 35 | 2190 | SDK | netstandard2.0 |
| **L4 · Agent 内核** | OfficeCopilot.Agent.Runtime | 88 | **16060** | SDK | net472 |
| | OfficeCopilot.Agent.Broker | 17 | 2423 | SDK | net472 |
| | OfficeCopilot.Agent.Planning | 14 | 2311 | SDK | netstandard2.0 |
| | OfficeCopilot.Agent.Persistence | 8 | 1494 | SDK | net472 |
| **L5 · 能力扩展** | OfficeCopilot.OpenXml.Tools | 26 | 1893 | SDK | net472 |
| | OfficeCopilot.OpenXml.Excel | 9 | 1587 | SDK | netstandard2.0 |
| | OfficeCopilot.OpenXml.Word | 5 | 1569 | SDK | netstandard2.0 |
| | OfficeCopilot.OpenXml.PowerPoint | 7 | 1184 | SDK | netstandard2.0 |
| | OfficeCopilot.OpenXml.Core | 7 | 719 | SDK | netstandard2.0 |
| | OfficeCopilot.Memory | 5 | 1037 | SDK | netstandard2.0 |
| | OfficeCopilot.Mcp | 5 | 912 | SDK | netstandard2.0 |
| | OfficeCopilot.Skills | 6 | 806 | SDK | netstandard2.0 |
| **横切** | OfficeCopilot.Settings | 10 | 1826 | legacy | v4.7.2 |
| | OfficeCopilot.Settings.Abstractions | 4 | 1448 | legacy | v4.7.2 |
| | OfficeCopilot.Diagnostics | 13 | 1078 | legacy | v4.7.2 |
| **测试/工具** | OfficeCopilot.Agent.Tests | 31 | 5772 | legacy | v4.7.2 |
| | OfficeCopilot.OpenXml.Word.SmokeTest | 3 | 2145 | SDK | net472 |
| | OfficeCopilot.Eval | 7 | 2517 | legacy | v4.7.2 |
| | OfficeCopilot.Mcp.TestServer | 1 | 359 | SDK | net8.0 |

> ⚠️ 目录 `OfficeCopilot.OpenXml.Word.SmokeTest/` 内的 csproj 实际叫 `OfficeCopilot.OpenXml.SmokeTest.csproj`（名称不一致）。
> **14 个 legacy 工程 / 9 个 `packages.config`**；其中 3 个 VSTO 必须保留 legacy，其余 11 个是 E3 迁移目标。

---

## 3. 按功能查文件（速查表）

### 启动与装配

| 我要找 | 去哪里 |
|---|---|
| VSTO 启动入口 | `NixerXxxCopilot/ThisAddIn.cs`（三份同构，各 ~52 行） |
| 功能区 7 个按钮 | `NixerXxxCopilot/XxxCopilotRibbon.cs`（262/261/251 行，三份同构） |
| **面板装配主流程** | [OfficeCopilot.Panel.WebView/Composition/CopilotTaskPaneManagerBase.cs](../OfficeCopilot.Panel.WebView/Composition/CopilotTaskPaneManagerBase.cs)（457） |
| 宿主差异（8 个抽象成员） | `NixerXxxCopilot/Composition/XxxCopilotTaskPaneManager.cs`（112/119/110） |
| 管道命名 | `Panel.WebView/Composition/HostBridgePipeNameFactory.cs`（21） |
| Broker 子进程生命周期 | [OfficeCopilot.Panel.WebView/Services/BrokerRuntimeHost.cs](../OfficeCopilot.Panel.WebView/Services/BrokerRuntimeHost.cs)（1254） |
| **Runtime DI 装配根** | `Agent.Runtime/Composition/AgentRuntimeServiceCollectionExtensions.cs`（197）→ `AddOfficeCopilotAgentRuntime` |
| Broker 进程入口 | `Agent.Broker/Program.cs` → `BrokerApplication.cs`（303） |

### 对话与 UI

| 我要找 | 去哪里 |
|---|---|
| 面板主控件（UI 编排、会话状态） | [OfficeCopilot.Panel.WebView/CopilotPanelControl.cs](../OfficeCopilot.Panel.WebView/CopilotPanelControl.cs)（3611） |
| WebView2 初始化 / JS↔C# 消息 | `Panel.WebView/Presentation/PanelWebViewBridge.cs`（212） |
| 模型选择菜单 | `Panel.WebView/Presentation/PanelModelMenuPresenter.cs`（235） |
| 待决用户交互登记簿 | `Panel.WebView/Presentation/PanelInteractionCoordinator.cs`（97） |
| 会话持久化 IPC | `Panel.WebView/Services/BrokerSessionRepository.cs`（262） |
| Broker 事件 → UI 模型转换 | `Panel.WebView/Services/BrokerToPanelConverter.cs`（346） |
| 工具确认对话框链路 | `Panel.WebView/CopilotPanelControl.ToolSafety.cs`（416）、`ChatToolConfirmationHandler.cs`（63） |
| Office/PDF Markdown 转换 | `OfficeCopilot.Documents.Markdown/`（Word、Excel、PowerPoint、PDF、旧格式归一化及缓存） |
| 附件准备与按需读取 | `Panel.WebView/Services/References/ReferencePipeline.cs`（2188）、`GetReferenceContentTool.cs`、`ReferenceContracts.cs`、`WebReferenceImporter.cs`、`PdfToPngConverter.cs` |
| 工具页图直传模型 | `Agent.Runtime/Turn/ToolImageMessages.cs`（能力降级提示、每轮图片预算）；页图生产端见 `Panel.WebView/Services/References/GetReferenceContentTool.cs` |
| 附件转换对比评测 | `scripts/Compare-AttachmentMarkdown.ps1`、`OfficeCopilot.OpenXml.SmokeTest.exe --export-attachment-fixtures`、`docs/attachment-eval/` |
| Excel 分析 Eval 夹具 | `OfficeCopilot.OpenXml.SmokeTest.exe --export-excel-eval-fixtures`、`OfficeCopilot.OpenXml.SmokeTest/ExcelEvalFixtures/`；生成文件位于被忽略的 `TestResults/ExcelEval/` |
| Excel 分析 Eval Runner 与报告 | `OfficeCopilot.Eval/Excel/`；按 Excel 进程发现 Broker 端点，完成后输出 `summary.json` / `report.md`，用例见 `docs/excel-eval/README.md` |
| 附件 UI 编排 | `Panel.WebView/CopilotPanelControl.AttachmentReferences.cs` |
| 代码执行面板 | `Panel.WebView/CopilotPanelControl.CodeExecution.cs`（365） |
| AskUserQuestion 弹窗 | `Panel.WebView/AskUserQuestionDialog.cs`（375） |

### 前端（`Panel.WebAssets/wwwroot/js/`，19 个原生 ES Module，≈3560 行）

入口 `main.js`(61)，共享可变状态在 `state.js`(27) 的 `state` 对象上（跨模块赋值必须走对象属性）。

| 模块 | 行 | 职责 |
|---|---:|---|
| `markdown.js` | 481 | Markdown 渲染 |
| `tool-calls.js` | 384 | 工具调用卡片 |
| `code-blocks.js` | 349 | 代码块渲染/复制 |
| `ask-user.js` | 310 | 提问交互 |
| `code-execution-state.js` | 295 | 代码执行状态机 |
| `assistant-message.js` | 288 | 助手消息 |
| `assistant-info.js` | 240 | 助手信息栏 |
| `thinking.js` | 194 | 推理流展示 |
| `host-message.js` | 172 | 宿主消息分发 |
| `tool-confirmation-ui.js` / `tool-confirmation.js` | 158 / 107 | 确认门 UI 与逻辑 |
| `scroll.js` / `conversation.js` / `plan-approval.js` / `clipboard.js` / `todo.js` / `host-bridge.js` | 131 / 132 / 90 / 70 / 66 / 5 | 辅助 |

> 无打包器、无 source map、无 lint（方案 B 主动放弃）。`index.html` 用 `<script type="module">`。

### Agent 内核（`OfficeCopilot.Agent.Runtime`）

| 我要找 | 去哪里 |
|---|---|
| **Turn 编排主循环** | [OfficeCopilot.Agent.Runtime/BrokerTurnExecutor.cs](../OfficeCopilot.Agent.Runtime/BrokerTurnExecutor.cs)（1371；回合结束事件附带 V3/V5 质量摘要） |
| 运行时服务门面 | `Agent.Runtime/AgentRuntimeService.cs`（547，构造函数已收敛为 1 个） |
| **工具目录构建 + 调度** | `Agent.Runtime/Turn/DefaultToolDispatcher.cs`（1021；宿主工具 schema 调用前诊断，当前仅记录） |
| 工具参数 schema 诊断 | `Agent.Runtime/Turn/ToolArgumentValidator.cs`（421；递归校验 type / required / enum / items / properties / minimum / maximum，当前不拦截） |
| 技能白名单/风险判定 | `Agent.Runtime/Turn/SkillPlanSupport.cs`（393） |
| 系统提示词/上下文组装 | `Agent.Runtime/Turn/DefaultContextBuilder.cs`（312） |
| 流式 token 刹车 | `Agent.Runtime/Turn/TokenBudget.cs`（131） |
| 循环护栏 | `Agent.Runtime/Turn/LoopGuardPolicy.cs`（238） |
| 工具确认门（Broker 侧唯一决策点） | `Agent.Runtime/Turn/PermissionGate.cs`（102） |
| 备份路径管理 | `Agent.Runtime/Turn/BackupPathManager.cs`（104） |
| Excel 回合可信度 Ledger / 页脚 | `Agent.Runtime/Verification/VerificationLedger.cs`、`TrustFooterBuilder.cs` |
| 数字证据 Ledger | `Agent.Runtime/Verification/EvidenceLedger.cs` |
| 最终回答数字抽取与证据匹配 | `Agent.Runtime/Verification/NumericValueParser.cs`、`NumberExtractor.cs`、`NumberMatcher.cs` |
| 模型调用抽象 | `Agent.Runtime/Turn/IModelInvoker.cs` / `DefaultModelInvoker.cs`（126）/ `ModelStreamEvent.cs`（113） |
| 日志门面（Runtime 侧） | `Agent.Runtime/Turn/RuntimeTrace.cs`（12） |

**Runtime 内置工具**（`runtime.*`，均在 `Agent.Runtime/Turn/`）

| 工具 | 文件 | 行 |
|---|---|---:|
| 确定性 decimal 算术 / 溯源 | `CalculateTool.cs` | 新增 |
| 当前本地时间（每轮采集进宿主上下文并注入系统提示词；`runtime.get_current_time` 工具按需返回同一格式） | `CurrentTimeFormatter.cs`、`CurrentTimeTool.cs`、`BrokerTurnExecutor.GetEffectiveSnapshotAsync` | 新增 |
| PowerShell 执行 | `PowerShellTool.cs` | 285 |
| 子 Agent | `AgentTool.cs`(164) + `SubagentRunner.cs`(217) | |
| Skill 调用 / 创建 / 改进 | `SkillTool.cs`(153)、`SkillCreatorTool.cs`(263)、`SkillImprovementHook.cs`(296)、`SkillImprovementApplier.cs`(195) | |
| Todo / Brief / 通知 | `TodoWriteTool.cs`(155)、`BriefTool.cs`(161)、`NotificationTools.cs`(146) | |
| 计划模式 | `PlanModeTools.cs`(196)、`PlanFileManager.cs`(77) | |
| 记忆写入 | `MemoryWriteTool.cs`(144)、`MemoryContextInjector.cs`(95)、`Memory/MemoryExtractor.cs`(184) | |

**模型供应商适配**（`Agent.Runtime/ModelProviders/`，8 文件 / 2761 行）

`HttpModelClient.cs`（62 行门面）→ `IModelProviderAdapter` →
`AnthropicProviderAdapter.cs`(873) / `OpenAiCompatibleProviderAdapter.cs`(568) / `OllamaProviderAdapter.cs`(471)；
共享静态类：`ModelRequestParameters.cs`(268) / `ModelJsonHelpers.cs`(217) / `ModelHttpTransport.cs`(196) / `ModelStreamingSupport`。

**上下文压缩**（`Agent.Runtime/Context/`，10 文件 / 2175 行）

`ContextCompactionEngine.cs`(653) · `AutoCompactPolicy.cs`(250) · `CompactionSummarizer.cs`(238) ·
`ContextWindowThresholds.cs`(219) · `MicroCompactPolicy.cs`(189) · `CompositeContextWindowPolicy.cs`(157) ·
`RoughTokenEstimator`（CJK 感知：宽字符 ×1.0，其余 ÷4）· `TurnSegmenter` · `CompactionBudget`。

**PowerShell 沙箱**（`Agent.Runtime/PowerShell/`，17 文件 / 2647 行）

| 子目录 | 内容 |
|---|---|
| （根，6 文件/811） | `SimplePowerShellRunner.cs`(270)、`PowerShellPromptBuilder.cs`(202)、`CommandSemanticInterpreter.cs`(186)、`PowerShellVersionDetector.cs`(77) |
| `Permissions/`（4/734） | `PowerShellPathValidator.cs`(279)、`PowerShellPermissionChecker.cs`(244)、`PermissionPrefixExtractor.cs`(120)、`ModeBasedAutoAllowPolicy.cs`(91) |
| `Security/`（7/1102） | `PowerShellSecurityValidator.cs`(303)、`ReadOnlyClassifier.cs`(228)、`ConstrainedLanguageModeCatalog.cs`(179)、`DestructiveCommandDetector.cs`(129)、`GitSafetyChecker.cs`(109)、`DangerousCmdletCatalog.cs`(102) |

### Skill 规划（`OfficeCopilot.Agent.Planning`）

`Runtime/HybridSkillScorer.cs`(656) · `OfficeSkillCatalog.cs`(570) · `KeywordSkillPlanner.cs`(450) ·
`SkillExecutionPromptBuilder.cs`(165) · `SkillToolFilter.cs`(117) · `InMemorySkillRegistry.cs`(59)；
模型在 `Models/`（`SkillDefinition` 101 / `SkillSelectionResult` 67 …）。
Runtime 侧计划对象：`Agent.Runtime/Planning/RuntimeSkillPlan.cs`(407) + `SkillPlanningCoordinator.cs`(105)。
SKILL.md 解析/加载在 `OfficeCopilot.Skills`（`BuiltInSkillLoader` / `FileSystemSkillLoader` / `SkillFrontmatter`）。

### 宿主工具

**Excel**（63 文件 / 13861 行 · 约 60 个工具）

| 位置 | 内容 |
|---|---|
| 服务层（根） | `ExcelRangeService.cs` · `ExcelVbaService.cs`（VBA 执行与 staged-only 保护包装）· `ExcelStagedWorksheetProtection.cs`（全表锁态预检、无密码临时保护、恢复日志和启动恢复）· `ExcelAnalysisService.cs` · `ExcelServiceBase.cs` · `ExcelConnectionService.cs` · `ExcelDataModelService` · `ExcelTableService` · `ExcelBackupService` |
| `Com/` | `ExcelComDispatcher.cs`（Excel UI 线程封送、只读 busy 重试）· `ExcelComErrorClassifier.cs`（COM busy HRESULT 识别）· `ExcelColumnReader.cs`（按连续列区域分块读取 Value2，并校验返回矩阵尺寸） |
| `Analysis/` | `ExcelAggregationEngine.cs`（画像、聚合、对账及覆盖统计；将已有 reconcile totals/groups 交给统一 Comparator）· `Accumulators/`（标量、分组、去重、key-set 累加器；供快照与流式路径共用）· `ExcelAggregationService.cs`（画像、聚合、对账的流式 sink；保留整行全源列空白语义） |
| `Verification/` | `VerificationPlanner.cs`（断言校验、表头模式传递、查询构造、按能力/成本/独立性路由；排除行查询路由到托管端）· `VerificationEngine.cs`（真实成本估算委托、查询执行与报告编排；供 excel_assert 和 aggregate 独立复核使用）· `Comparator.cs`（多断言比对、覆盖失败闭合、差异截断与可信度分档）· `DataQualityInspector.cs`（画像托管标记与有限 COM 指纹合并）；各工具通过 `ExcelVerificationResult` 输出统一摘要并在 `details` 保留原始报告 |
| `Verification/Backends/` | `IExcelCalcBackend.cs`（COM / managed 计算后端契约；V2-1） |
| `Verification/Backends/Com/` | `ComCalcBackend.cs`（Excel 原生标量计算、SUMIFS 与条件成本路由）· `SumIfsCriteriaBuilder.cs`（筛选标准转义及区域格式化）· `DataQualityFingerprint.cs`（有行数/公式地址上限的 Excel 原生质量指纹，含 SUBTOTAL 公式行样本与合并单元格状态） |
| `Verification/Backends/Managed/` | `ManagedCalcBackend.cs`（复用分块读取和流式累加器执行查询，覆盖分类计数可用；支持断言侧显式行号与小计标签行排除，覆盖仍统计全扫描区） |
| 门面 | `ExcelHostAdapter.cs` + `ExcelHostAdapter.Facade.cs`；Facade 统一处理 VBA expectation、aggregate 独立复核和断言验证 |
| `Tools/Data/` | 读取与分析，含只读 `ExcelAssertTool.cs`；assert、aggregate、reconcile 工具均输出统一 verification envelope |
| `Tools/Advanced/`（16/1152） | PowerQuery / 连接 / 数据模型 |
| `Tools/Write/`（5/1341） | `ExcelWriteRangeTool.cs`(418)、`ExcelSetRangeFormatTool.cs`(358)、`ExcelExecuteVbaTool.cs`(252)、`ExcelSetRangeFormulasTool.cs`(244) |
| `Tools/Diagnostics/`（2/1147） | `ExcelToolSelfTestRunner.cs`(908)、`VbaSyntaxValidator.cs`(239) |
| `Tools/Registration/`（4/361） | `ExcelBuiltInToolCatalog.cs`(212) |
| `Tools/Common/`（3/331） | `ExcelToolRequestNormalizer.cs`(234)、`ExcelToolBase.cs`(58) |

**Word**（37 文件 / 4424 行 · 29 个工具）：`WordHostAdapter.cs`(1901) + `Tools/{Context,Read,Write,Navigation,Advanced,File,Common,Registration}/`，工具多为 50–135 行。

**PowerPoint**（8 文件 / 759 行 · 2 个工具）：`PowerPointHostAdapter.cs`(392) + `Tools/Context/` 2 个工具。**能力覆盖面远小于 Excel/Word**（属功能排期，非架构缺陷）。

**三宿主共享**：`Host.Abstractions` 的 `ICopilotHostAdapter` / `IExcelToolHost` / `IToolConfirmationHandler`；
工具目录组装统一走 `ToolCatalogBuilder` / `ToolCatalogOptions` / `ToolRegistrationOptions`。

**离线 OpenXml 工具**（不依赖 Office 进程）：`OpenXml.Core`（`IDocumentEngine` / `DocumentEngineRegistry`）+ `OpenXml.{Word,Excel,PowerPoint}` + `OpenXml.Tools`（工具封装 26 文件/1893）。

### 持久化 / 设置 / 日志 / MCP / 记忆

| 我要找 | 去哪里 |
|---|---|
| 会话 JSONL 存储 | `Agent.Persistence/Jsonl/JsonlConversationStore.cs`(696)、`JsonlPaths.cs`(108) |
| SQLite 会话索引 | `Agent.Persistence/Jsonl/SqliteConversationIndex.cs`(338) |
| 会话存储契约 | `Agent.Persistence/IConversationStore.cs`(58)、`BrokerConversationMessage.cs`(56) |
| **设置模型** | `Settings.Abstractions/OfficeCopilotSettings.cs`(992) |
| 设置归一/校验/IO | `Settings/OfficeCopilotSettingsFactory.cs`(519)、`OfficeCopilotSettingsValidator.cs`(210)、`JsonSettingsStore.cs`(372)、`OfficeCopilotStoragePaths.cs`(52) |
| 内置工具开关持久化 | `Settings/BuiltinToolSettingsStore.cs`(270) |
| **设置主界面** | [OfficeCopilot.Settings.WinForms/fmConfig.cs](../OfficeCopilot.Settings.WinForms/fmConfig.cs)（3627，全仓最大类） |
| MCP / 模型 / 工具编辑器 | `Settings.WinForms/fmMcpEditor.cs`(1096)、`fmModelEditor.cs`(742)、`fmToolEditor.cs`(811) |
| **统一日志门面** | `Diagnostics/Logging/OfficeCopilotLog.cs`(84) → `FileLoggerProvider.cs`(253) / `DebugTraceLogger.cs`(83) |
| 模型会话转录 | `Diagnostics/Transcripts/FileModelTranscriptLogger.cs`(169) |
| 工具调用结构化日志 | `Diagnostics/Logging/ToolCallLogSink.cs`(64) + `Agent.Abstractions/Runtime/ToolCallDiagnosticsLogger.cs` |
| MCP 客户端 | `OfficeCopilot.Mcp`：`McpRegistry` / `NuGetMcpClient` / `McpToolNaming` |
| 长期记忆 | `OfficeCopilot.Memory`：`FileSystemMemoryStore` / `MemoryTreeNode` |

---

## 4. 核心工程内部结构

### `OfficeCopilot.Agent.Runtime`（88 文件 / 16060 行）

| 子目录 | 文件 | 行数 | 职责 |
|---|---:|---:|---|
| `Turn/` | 34 | 5518 | Turn 执行、工具调度、内置工具 |
| `ModelProviders/` | 8 | 2761 | 三家 LLM 供应商适配 |
| `PowerShell/`（含 2 子目录） | 17 | 2647 | 沙箱、权限、安全分类 |
| `Context/` | 10 | 2175 | 上下文压缩 |
| （根） | 15 | 2066 | `BrokerTurnExecutor` / `AgentRuntimeService` / 转录 |
| `Planning/` | 2 | 512 | Runtime 侧 Skill 计划 |
| `Composition/` | 1 | 197 | DI 装配根 |
| `Memory/` | 1 | 184 | 记忆抽取 |

### `OfficeCopilot.Panel.WebView`（29 文件 / 12521 行）

| 子目录 | 文件 | 行数 |
|---|---:|---:|
| （根，含 5 个 partial） | 9 | 6657 |
| `Services/References/` | 4 | 2277 |
| `Services/` | 7 | 2165 |
| `Presentation/` | 3 | 544 |
| `Composition/` | 3 | 502 |
| `Models/`（`ChatModels.cs`） | 1 | 178 |

### `OfficeCopilot.Agent.Broker`（17 文件 / 2423 行，扁平）

`BrokerEnvelopeDispatcher.cs`(701) · `BrokerApplication.cs`(303) · `NamedPipeBrokerHost.cs`(301) ·
`BrokerConfig.cs`(282) · `BrokerTurnCancellationRegistry.cs`(176) · `BrokerPendingUserInteractionRegistry.cs`(173)。

---

## 5. 关键数据流

### 5.1 面板装配（`CopilotTaskPaneManagerBase.EnsurePane`）

```
EnsurePane(windowKey = TryGetActiveWindow())
├─ JsonSettingsStore.Load() + OfficeCopilotSettingsFactory.Normalize
├─ OfficeCopilotDiagnostics.CreateLoggerFactory / CreateModelTranscriptLogger
├─ CreateHostAdapter()          ← 宿主重写
├─ RegisterHostServices(...)    ← 宿主重写
├─ InMemoryToolRegistry + RegisterTools(registry, settings)
│    └─ 基类按 HostApplicationName 合并宿主工具设置；ToolCatalogBuilder 统一注册默认值与显式禁用语义
├─ new CopilotPanelControl(...) + 属性注入 HostAdapter / AgentServices / ToolRegistry
├─ pipeName = HostBridgePipeNameFactory.Create(pid, windowKey)
├─ NamedPipeHostBridgeServer.StartAsync
│    └─ DefaultHostBridgeRequestHandler(adapter, registry, services)
├─ BrokerRuntimeHost(...) → CreateClientNoStart()      ← 不阻塞 UI
├─ CreateTaskPane(...)          ← 宿主重写
└─ Task.Run(() => brokerRuntimeHost.EnsureStarted())   ← 后台预热 broker.exe
```

### 5.2 一次对话（`BrokerTurnExecutor.ExecuteAsync`）

```
ExecuteAsync
├─ EmitTurnStarted
├─ PrepareTurnAsync
│  ├─ GetEffectiveSnapshotAsync   → host.get_context_snapshot
│  ├─ GetCapabilitiesAsync        → host.get_capabilities
│  ├─ DefaultToolDispatcher.BuildToolCatalog(capabilities)
│  │     ├─ 15 个 runtime.* 内置工具
│  │     └─ 宿主工具（HostBridgeCapabilities.Tools）
│  └─ MergeMcpToolsIntoCatalogAsync
├─ ExecuteTurnBodyAsync
│  ├─ [A] PlannedToolCallParser 有显式调用 → ExecuteExplicitToolCallsAsync
│  ├─ [B] ModelOptions.Endpoint 非空 → ExecuteModelTurnAsync   ← 主路径
│  └─ [C] 兜底 → BuildAssistantResponse
├─ TryExtractMemoriesAsync / TryDetectSkillImprovementsAsync
└─ EmitTurnCompleted + ContextUsage 回填
```

`ExecuteModelTurnAsync` 内循环：

```
BuildSkillPlanAsync → BuildModelMessages → InjectMemoryContext
while true:
    EvaluateContextWindowAndCompactAsync     ← 每轮前评估压缩
    TokenBudget.Reset
    BuildModelTools(toolCatalog, skillPlan)  ← SkillPlanSupport 白名单过滤可见性
    IModelInvoker.StreamAsync → ModelStreamEvent
        ├─ TextDelta      → EmitContentDelta
        ├─ ReasoningDelta → EmitThinkingDelta
        └─ ToolCallsReady → 收集 ModelClientToolCall
    无 tool_calls → break
    逐个 ExecuteToolCallAsync → 结果回写 modelMessages
```

### 5.3 工具确认门（唯一决策点在 Broker）

```
模型 tool_call
  └─ DefaultToolDispatcher.DispatchToolCallAsync
       └─ PermissionGate：RequiresConfirmation && !IsReadOnly && Mode ∉ {AcceptEdits, Bypass}
            → CurrentUserInteractionEmitter → Panel → WebView 弹窗
            → decision(allow-once / allow-session / allow-always) 写回授权缓存
  └─ 通过后 → host.invoke_tool
       └─ DefaultHostBridgeRequestHandler 重验 staged-only 条件；可安全保护时免确认，否则走宿主确认
```

`critical` 工具（VBA 等任意代码执行）的硬门是技能的 `AllowCodeExecution`（`SkillPlanSupport.IsCodeExecutionGated`）；`runtime.powershell` 豁免该硬门，改由 `PowerShellPermissionChecker`（只读自动放行、其余弹确认）控制。`RiskLevel` 由 `ToolSafetyPolicy` 推导为四级。

---

## 6. 协议与契约

### 6.1 工具的三重身份

| 层 | 类型 | 定义位置 |
|---|---|---|
| 宿主实现 | `IAgentTool` + `ToolDefinition` | `Agent.Abstractions` |
| 跨进程传输 / Runtime 目录 | `HostBridgeToolDescriptor` | `HostBridge.Contracts` |
| 发给模型 | `ModelClientTool` | `Agent.Runtime/ModelClientContracts.cs`(123) |

归一化逻辑在 `Agent.Runtime/Turn/RuntimeToolDescriptors.cs`(36)。

### 6.2 两套 IPC

| | Panel ↔ Broker | Broker ↔ Office |
|---|---|---|
| 契约工程 | `Agent.Client` | `HostBridge.Contracts` |
| 消息标识 | `MessageType`（`broker.turn.execute.request`） | `Operation`（`host.invoke_tool`） |
| 常量表 | `AgentBrokerMessageTypes`（22 个） | `HostBridgeOperationNames`(26) |
| 错误码 | 同上常量 | `HostBridgeErrorCodes.cs`(54) |
| 信封 | 一行一个 JSON（**不得含裸换行**） | 同左 |
| 序列化器 | `System.Text.Json` | `Newtonsoft.Json`（两端统一） |
| 事件推送 | `IAgentBrokerEventSink`；`turn.completed` 附带可选 `AgentBrokerTurnQualitySummary` | 无（纯请求-响应） |

**Panel 侧传输**：`TransportAgentBrokerClient.cs`(463) + `NamedPipeAgentBrokerTransport.cs`(206)。
**Broker 侧分发**：`BrokerEnvelopeDispatcher.cs`(701)。
**Office 侧处理**：`DefaultHostBridgeRequestHandler.cs`（工具确认、Excel UI 线程锁态预检与内部 staged-only metadata）。

### 6.3 存储位置

| 内容 | 路径 |
|---|---|
| 前端资源释放目录 | `%LOCALAPPDATA%\NixerOfficeCopilot\WebAssets\wwwroot` |
| 设置 / 会话 / 日志 | 见 `Settings/OfficeCopilotStoragePaths.cs`(52) |
| 会话 JSONL | 按日期分目录，文件名由 `JsonlPaths.EncodeConversationFileName` 编码 |

---

## 7. 构建与测试

```powershell
# 全量构建（VSTO 工程必须用 VS MSBuild，dotnet build 会报 MSB4019/MSB4803）
$msb = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
& $msb 'NixerOfficeCopilot.sln' /nologo /v:m /p:Configuration=Debug /m

# 测试（legacy packages.config MSTest 工程，dotnet test 无效）
$vstest = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -find '**\vstest.console.exe' | Select-Object -First 1
& $vstest 'OfficeCopilot.Agent.Tests\bin\Debug\OfficeCopilot.Agent.Tests.dll' /Logger:console`;verbosity=minimal

# 或统一入口（含 Broker bin 刷新校验）
.\build.ps1 -Rebuild -Test
```

**⚠️ Broker rebuild trap**：`Agent.Broker` 是独立 exe，不是任何 VSTO 工程的依赖。改动 Runtime/Persistence/Broker 后
必须确保 `Agent.Broker\bin\Debug\net472\OfficeCopilot.Agent.Runtime.dll` 时间戳新于源码，否则 Excel 跑的是旧代码。
`build.ps1` 已内置该校验。

**测试现状**：`OfficeCopilot.Agent.Tests` 最近一次构建运行 **424 个测试全部通过**（含 Excel 分块读取、流式 sink、计算与断言/报告契约序列化及旧画像契约字段缺省、COM 路由/条件构造、托管计算后端及断言行排除、质量指纹与 V6 画像标记纯逻辑、Excel 默认提示词质量口径规则、`_wip_` 豁免绕过、动态新建表约束及 HostBridge 运行时预检路由、工具 schema 诊断与日志模式不拦截、Fake 计算后端、VBA expectation 与早退回退、统一 verification envelope、口径歧义提示词与技能工具可用性、回合级 VerificationLedger 产出覆盖计数、Broker 质量事件及 Eval 覆盖/数字匹配率、完整用户/工具数字 EvidenceLedger、带操作数溯源的 `runtime.calculate`、最终回答数字抽取/匹配/一次订正、A/B/C TrustFooter 及 Eval 指标测试；未覆盖真实 Excel COM 保护/恢复及托管 Value2 集成行为）。

| 目录 | 覆盖 | 行 |
|---|---|---:|
| `Context/`（4 文件） | 压缩预算、token 估算、分段、策略边界 | 571 |
| `Persistence/`（2） | JSONL 读写 / 损坏行容错 / 内存实现 | 437 |
| `Turn/`（5） | SkillPlanSupport、计算工具、工具参数 schema 诊断、回合事件及追问策略 | 755 |
| `Client/`（1） | Broker 信封往返 + 22 个消息常量命名 | 222 |
| `HostBridge/`（3） | HostBridge 信封、确认策略边界与 staged-only 运行时预检/确认路由 | 568 |
| `Composition/`（1） | DI 装配冒烟 | 86 |
| `Security/`（1） | 权限门 | 40 |

> 测试工程**不引用** `Panel.WebView`，因此 Panel 侧协作类（均为 `internal`）无单测覆盖。

---

## 8. 大文件索引

> 超过 1000 行的 `.cs` 文件共 **10 个**。带 ⏳ 的是 [REMEDIATION_PLAN.md](REMEDIATION_PLAN.md) 中仍待拆解的项。

| 行数 | 文件 | 备注 |
|---:|---|---|
| 3627 | `Settings.WinForms/fmConfig.cs` | ⏳ 全仓最大，尚未纳入任何拆解批次 |
| 3611 | `Panel.WebView/CopilotPanelControl.cs` | F1 已拆一轮（4093→3611）；`_sessions`/`_currentSession` 有意保留 |
| 2457 | `Host.Excel/ExcelRangeService.cs` | F3 拆分产物 |
| 1901 | `Host.Word/WordHostAdapter.cs` | ⏳ 未纳入批次 F |
| 1744 | `Settings.WinForms/fmConfig.Designer.cs` | 设计器生成 |
| 2188 | `Panel.WebView/Services/References/ReferencePipeline.cs` | Office/PDF 视觉页处理、附件预处理、Markdown chunks 与按需读取 |
| 1254 | `Panel.WebView/Services/BrokerRuntimeHost.cs` | ⏳ 文档未提及 |
| 1164 | `Panel.WebView/CopilotPanelControl.AttachmentReferences.cs` | partial |
| 1096 | `Settings.WinForms/fmMcpEditor.cs` | ⏳ 文档未提及 |
| 1010 | `Host.Excel/ExcelVbaService.cs` | F3 拆分产物 |

**结构异味（待清理）**
- `Host.Excel/Tools/Write/ExcelWriteRangeTool.cs`(418) 内混装 `ExcelToolDiagnosticsLogger` 静态类。
- `Host.Abstractions/HostContracts.cs`(228) 是"杂物袋"，声明 **16 个类型**。
- `OfficeCopilot.OpenXml.Word.SmokeTest/` 目录名 ≠ csproj 名，722 行全在 `Program.cs`。
- `Host.Excel/ExcelHostAdapter.Backup.Interface.cs` 仅 8 行，疑似残留空壳。
- 三份 `ThisAddIn.cs` + 三份 `*Ribbon.cs`（约 930 行）逐行重复——**有意保留**（强依赖 VSTO 设计器生成代码）。

**其他已知计数**
- 空 `catch {}` / `catch (Exception) {}`：**92 处 / 41 个文件**（集中在 `ExcelRangeService` ×21、`DefaultToolDispatcher` ×5）。
- `.GetAwaiter().GetResult()`：**10 处 / 8 个文件**，均在进程入口、关闭同步边界或加载器构造，非对话回合路径。
- UTF-8 BOM 残留：**1 个** — `NixerWordCopilot/Properties/Resources.Designer.cs`。
- `U+FFFD`（乱码）：**0**。
