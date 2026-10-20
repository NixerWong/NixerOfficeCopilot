# MCP 调用链排查报告

> 范围：从 MCP 服务器配置（`settings.json`）到工具调用（Broker 派发）的完整链路。
> 性质：只读代码审查，本文档未做任何代码改动，也未运行复现用例。
> 状态标记：**已确认** 指已逐行读代码核实；**待验证** 指需要真机或运行时复现。

## 1. 调用链概览

```
%APPDATA%\NixerOfficeCopilot\settings.json  (McpServers)
  │  OfficeCopilotSettingsFactory.NormalizeMcpServer      OfficeCopilot.Settings
  ▼
McpJsonConfig.FromSettingsJson（手工解析 PascalCase JSON）  OfficeCopilot.Mcp/McpServerConfig.cs
  ▼
McpRegistry.LoadAsync / RefreshFromSettingsAsync           OfficeCopilot.Mcp/McpRegistry.cs
  │  每个服务器 → NuGetMcpClient（ModelContextProtocol 1.4.1）
  ▼
BrokerApplication（启动时同步加载）+ AgentRuntimeService（每轮刷新）
  ▼
DefaultToolDispatcher.MergeMcpToolsIntoCatalogAsync        工具目录合并，命名 mcp__<server>__<tool>
  ▼
DefaultToolDispatcher.DispatchToolCallAsync
  │  技能白名单豁免 → 写入效果预检 → 权限闸门 → 确认弹窗
  ▼
TryRoute（解析名字、查 _clients） → DispatchToMcpAsync → NuGetMcpClient.CallToolAsync
```

涉及的主要文件：

| 层 | 文件 |
|---|---|
| 配置模型 | `OfficeCopilot.Settings.Abstractions/OfficeCopilotSettings.cs`（约 692-835 行） |
| 配置规范化 | `OfficeCopilot.Settings/OfficeCopilotSettingsFactory.cs`（约 276-345 行） |
| 配置解析 | `OfficeCopilot.Mcp/McpServerConfig.cs` |
| 命名 | `OfficeCopilot.Mcp/McpToolNaming.cs` |
| 注册表 | `OfficeCopilot.Mcp/McpRegistry.cs` |
| 客户端 | `OfficeCopilot.Mcp/NuGetMcpClient.cs` |
| 启动 / 每轮刷新 | `OfficeCopilot.Agent.Broker/BrokerApplication.cs`、`OfficeCopilot.Agent.Runtime/AgentRuntimeService.cs` |
| 目录合并与派发 | `OfficeCopilot.Agent.Runtime/Turn/DefaultToolDispatcher.cs` |
| 风险推导 | `OfficeCopilot.Agent.Abstractions/Models/ToolSafetyPolicy.cs` |

## 2. 问题清单

### 2.1 确认的缺陷

#### B1【高】服务器名或工具名含非字母数字字符时，调用必然失败 — 已确认

**状态：已修复**（`91fc3d7`，路由改用 `_routes` 表，不再依赖 `__` 切分）。

- `McpToolNaming.BuildFullToolName` 对服务器名和工具名都调用 `NormalizeName`，把 `.`、空格等替换为 `_`。
- `McpRegistry.LoadAsync` 用**原始名**作为 `_clients` 的 key（`McpRegistry.cs` 约 90 行）。
- `TryRoute` 用**归一化后**的服务器名查 `_clients`，查不到，返回 false。
- 工具名同理：目录里展示的是 `read_file`（原名 `read.file`），派发时 `DispatchToMcpAsync` 把归一化后的名字传给 server（`DefaultToolDispatcher.cs` 约 1129 行），server 不认识。
- 另外，服务器名中包含 `__` 时，`TryParse` 会切错。

影响：很多真实 MCP server 的工具名带 `.`，这类工具在目录中可见，但调用一定失败，且错误信息不指向根因。

建议方向：保留原始名与规范名的映射表，派发时用原始工具名；`_clients` 与路由使用同一套 key；补单元测试覆盖含 `.` 的服务器名和工具名。

#### B2【高】AcceptEdits 下确认逻辑倒挂：`high` 弹窗，`critical` 不弹窗 — 已确认（代码行为）

**状态：已修复**（`cfaabf7`）。按用户决定，AcceptEdits 下 `high` 与 `critical` 均需确认；同步更新了 ARCHITECTURE §9.2(a)。运行时交互尚未在真机验证。

- `DefaultToolDispatcher.cs` 约 582-584 行：`isHighRisk` 只判断 `"high"`，`modeSkipsConfirmation = AcceptEdits && !isHighRisk`。
- 因此 AcceptEdits 下：`high` 仍弹确认；`critical` 与 `medium` 直接执行。危险等级越高，拦截反而越少。
- `critical` 的来源有两类：内置 `runtime.powershell`（`DefaultToolDispatcher.cs` 约 208-211 行，F12-1 设为 critical），以及名字含 `powershell` / `macro` / `vba` / `run_command` 的 MCP 工具（`ToolSafetyPolicy.InferRiskLevel`）。两类都受影响。
- 历史：`git log -S isHighRisk` 显示该例外由提交 `74e0fc7`（2026-10-02）引入，此前 AcceptEdits 对所有工具都跳过确认。`runtime.powershell` 在 `91c1ce1`（2026-08-30）已是 critical，所以该例外从引入起就没有覆盖它。
- 注释写的是"High-risk document-wide operations still require an explicit user decision in AcceptEdits"，意图是保护高风险操作。`critical` 高于 `high`，按字面意图也应保护；但这属于设计判断，需要作者确认，本报告不擅自定性为"意图如此"或"遗漏"。
- `ARCHITECTURE.md` §9.2(a) 只写了"Mode 不是 AcceptEdits/Bypass"，没有提到 `high` 例外，文档与代码不一致（见第 4 节）。

建议方向：若确认 `critical` 也应受保护，把 `isHighRisk` 改为 `high || critical`（或等价的 `RiskLevel` 集合判断），并同步更新 §9.2(a) 文档；补一条 AcceptEdits + critical 的单元测试。

#### B3【中高】每轮对话都断开并重连所有 MCP 服务器 — 已确认

**状态：已修复**（`e0e8515`）。刷新改为按配置指纹增量对比，只重连新增或变化的 server。**行为变化**：连接失败的 server 不会每轮重试，只有其配置变化后才会重连（每次重试都会耗尽 90 秒连接超时）。

- `AgentRuntimeService.cs` 约 127-137 行，每轮都调用 `RefreshFromSettingsAsync`。
- 该方法先 Dispose 全部 client（stdio 子进程被终止），再重新 spawn、握手、列工具。
- 后果：每轮有额外启动延迟；`npx` 之类的服务器每轮都要重新拉起；两轮重叠时，另一轮可能读到空的 `_clients`。
- 异常被 `catch (Exception) {}` 完全吞掉（同文件 135 行），失败无日志。

#### B4【中】刷新是"先断连、后读配置"，读取失败会让本轮 MCP 工具全部消失 — 已确认

**状态：已修复**（`e0e8515`）。先读取并解析配置，失败则抛出且不触碰现有连接；文件不存在时保持现有连接。`AgentRuntimeService` 会把刷新失败写入运行日志。

- `RefreshFromSettingsAsync`（`McpRegistry.cs` 约 225-260 行）先 Dispose，再读取 settings。
- WinForms 保存时文件被占用会被解析为空配置；JSON 损坏则抛异常，被 B3 中的 `catch` 吞掉。
- 结果：本轮没有任何 MCP 工具，且没有任何提示。

建议方向：先读取并解析配置，成功后再与现有连接做差异更新，只替换变化的服务器。

#### B5【中】超时完全未生效，启动路径可能永久阻塞 — 已确认

**状态：已修复**（`91fc3d7`、`3f1be81`，后续统一为固定超时）。连接、`tools/list`、`tools/call` 均已加超时（固定 90 秒，超时后释放 transport 并返回失败），启动路径的异常改为记录日志。**设计取舍（已确认）**：所有 MCP 调用统一 90 秒，不提供单工具或服务器级超时配置。界面上没有对应控件，用户无法配置，因此移除了 per-tool `TimeoutSeconds`。

- 原问题（`3f1be81` 曾让 per-tool `TimeoutSeconds` 生效；因界面无法配置，后续移除，旧 settings.json 中的该键被忽略）：`FromSettingsJson` 曾不读取 per-tool 超时。
- 原问题（已由 `c35cac3` 处理）：camelCase 的路径重载已改为使用 settings.json 解析器（见 P11）。
- `BrokerApplication.cs` 约 216-232 行使用 `LoadAsync(...).GetAwaiter().GetResult()`，并传入 `CancellationToken.None`。连接在启动时是串行的，每个卡住的 stdio server（例如 `npx` 挂起）最多阻塞 90 秒，N 个卡住的 server 最多阻塞 N×90 秒。
- 启动期间的 MCP 加载异常现已记录日志（`BrokerApplication.cs` 约 233 行）。
- 工具调用超时已由 `RunWithTimeoutAsync` 覆盖，`CallToolAsync` 挂起时返回 `Success=false`，不再无限等待。
- 新发现（测试 `ConnectAsync_ServerExitsImmediately_*` 实测）：stdio 子进程启动即退出（如命令不存在后的 `cmd /c exit 1`）时，SDK 约 60 秒后才抛出 `ClientTransportClosedException`，不是立即失败。连接超时（90 秒）不会先触发。启动时串行连接，每个崩溃的 server 会阻塞启动约 60 秒。是否需要缩短，待定（见待决问题 5）。

#### B6【中】`WorkingDirectory` 拼接破坏裸命令，且该字段实际未生效 — 已确认

**状态：已修复**（`dd6d27f`）。仅对含路径分隔符的相对命令按 `WorkingDirectory` 解析；裸命令（如 `npx`）保持 PATH 查找；`WorkingDirectory` 通过 `StdioClientTransportOptions` 传给子进程。**未验证**：真机上相对路径可执行文件的启动。

- `McpServerConfig.cs` 约 72-73 行：当 `WorkingDirectory` 有值且 `Command` 不是绝对路径时，两者被拼接。
- 对 `npx`、`uvx` 这类依赖 PATH 的命令，拼接结果无效。
- 同时，`NuGetMcpClient` 没有把 `WorkingDirectory` 设置为子进程的工作目录，该字段形同虚设。

建议方向：不拼接 `Command`，而是把 `WorkingDirectory` 传给 `StdioClientTransportOptions`（需先确认 1.4.1 是否提供该选项）。

#### B7【中】per-tool `IsEnabled` 不生效 — 已确认

**状态：已修复**（`237a070`、`3f1be81`；按工具风险配置于本次重构中移除，见 P9）。`IsEnabled: false` 的工具不进入目录，也不可路由；切换该设置会触发该 server 重连。`TimeoutSeconds` 已移除（见 B5）。`RiskLevel`、`RequiresConfirmation`、`RequiresBackup` 不再作为 MCP 工具配置项，旧 settings.json 中的这些键被忽略。

- 工厂保留了 `McpToolSettings.IsEnabled`（`OfficeCopilotSettingsFactory.cs` 342 行），设置 UI 也能编辑（`fmMcpEditor.cs` 约 834 行）。
- 但 `McpJsonConfig.FromSettingsJson` 与 `ListAllToolsAsync` 都不按它过滤，被禁用的工具仍会进入目录，模型仍可调用。

#### B8【低】同名服务器静默覆盖 — 已确认

**状态：已修复**（`3f3f243`）。服务器名按大小写不敏感去重（保留第一个）；归一化后工具全名冲突时保留先注册的路由，并在该 server 的状态 `ErrorMessage` 中列出被忽略的工具；目录只列出实际可路由的工具；设置校验新增大小写不敏感的服务器名重复错误。

- `config.McpServers[name] = entry` 会覆盖重复项，且不报错。
- 校验器（`OfficeCopilotSettingsValidator.cs` 约 144-153 行）只检查 Id 重复，不检查 Name 重复。

### 2.2 待验证的疑点

| # | 问题 | 依据 | 验证方式 |
|---|---|---|---|
| P1 | HTTP 的 `TransportMode`（AutoDetect / StreamableHttp / Sse）被忽略 | `FromSettingsJson` 未读取；`fmMcpEditor.cs` 可编辑 | 已修复（`509d264`）：解析 `TransportMode`（枚举名或数值，大小写不敏感），映射到 SDK 的 `HttpTransportMode`；非法值跳过该 server 并给出原因。枚举映射已单测；真机对 DeepWiki（SSE）设置界面测试通过 |
| P2 | Windows 上 `.cmd` 形式的命令（如 `npx`）可能启动失败 | `NuGetMcpClient.cs` 约 54 行注释提到 `cmd.exe /c` 包裹，但代码中 `Command` 直接交给 `StdioClientTransport`，没有 cmd 包装，`.cmd` 能否解析取决于 SDK | **是缺陷，推翻此前"不是缺陷"的结论**。复现：`Process.Start("npx")`（`UseShellExecute=false`）报"系统找不到指定的文件"，因为 CreateProcess 只会自动补 `.exe`，不会解析 `.cmd`。`npx.cmd` 和完整路径可以启动。修复：新增 `McpCommandResolver`，裸名按 PATH + 可执行扩展名（.com/.exe/.bat/.cmd）解析为完整路径后再启动；裸名不查工作目录；运行时与设置界面"测试"按钮共用。设置界面原有的 `工作目录\命令` 拼接同样错误，已删除 |
| P3 | 连接失败原因不可见 | 失败时 `State=Failed`，但 `ErrorMessage` 未赋值；`ExecuteMcpStatus` 无法说明原因 | 已修复（`a28a1d8`）：失败时写入 `ErrorMessage`。原验证方式：配置错误命令，检查状态输出 |
| P4 | `LoadAsync(path)` 重载静默吞掉异常 | `McpRegistry.cs` 约 50 行 `catch (Exception)` | 已修复（`c35cac3`）：读取或解析失败直接抛出。原验证方式：传入损坏配置 |
| P5 | `ConnectAsync` 失败时可能泄漏已启动的 stdio 子进程 | `_innerClient` 为 null 时不会被 Dispose | 已修复（`a28a1d8`）：连接失败时释放 transport。原验证方式：配置一个启动后立即退出的命令，观察进程 |
| P6 | 子进程崩溃后仍显示已连接 | `IsConnected` 只判断 `_innerClient != null` | 已修复（`e386d4e`）：`IsConnected` 改为基于 SDK 公开的 `McpClient.Completion`，会话结束后路由停止，下次刷新重连。探针验证：杀掉子进程后该属性完成。原验证方式：杀掉 server 进程后调用工具 |
| P7 | 返回结果只保留文本块，图片、资源块被丢弃 | `CallToolAsync` 只提取 `TextContentBlock` | 已修复（`509d264`）：图片、音频、嵌入资源、资源链接都会转为可读文本描述（不内联二进制数据）。未用真实非文本输出的服务器测试 |
| P8 | `IsError` 结果虽标记失败，但文本仍照常返回 | `NuGetMcpClient.cs` 约 120-170 行 | 已修复（`509d264`）：`IsError` 时错误信息写入 `ErrorMessage`，结果文本仍保留 |
| P9 | 风险等级只靠工具名关键字推导，无关键字的外部工具为 `medium`；settings 中的 RiskLevel 等配置未被运行时读取 | `ToolSafetyPolicy.InferRiskLevel` 约 137-160 行；B7 的遗留项 | 已解决（本次重构）：按工具配置风险的设计被撤销。用户无法预知 MCP 工具名，设置界面也没有编辑这些字段的控件，只能手改 JSON，不可行。改为只按工具名推导风险（`InferMcpToolRiskLevel`，MCP 工具一律按写工具处理、需要确认）。`RiskLevel`、`RequiresConfirmation`、`RequiresBackup` 从 MCP 配置模型中移除，旧键被忽略 |
| P12 | MCP 工具的 `readOnlyHint` 注解被忽略，只读 MCP 工具与写工具一样需要确认 | `NuGetMcpClient.ListToolsAsync` 只读取 Name、Description、InputSchemaJson | 已修复（用户决定采纳）：读取 `ProtocolTool.Annotations.ReadOnlyHint`，只读工具不弹确认、Ask 模式可用。服务器自报，可能谎报 |
| P10 | 服务器级 `IsEnabled` 缺失或不是布尔值时，服务器被静默忽略 | `McpServerConfig.cs` 约 47-48 行：缺失或非布尔值直接 `continue`，无日志 | 已修复（`c35cac3`）：被跳过的 server 以 Failed 状态列出并说明原因。原验证方式：手工编辑 settings.json，删除 `IsEnabled` 后观察目录 |
| P11 | `McpJsonConfig` 的路径重载 `LoadAsync(string)` 用 System.Text.Json 反序列化，字段为 camelCase（`mcpServers`、`timeout` 等），与 settings.json 的 PascalCase 不一致。生产路径不调用它（Broker 用 `FromSettingsJson`），但它是 `IMcpRegistry` 的公开方法 | `McpRegistry.cs` 约 59-78 行；`McpServerConfig.cs` 14-15、189-227 行 | 已修复（`c35cac3`）：路径重载改为使用 settings.json 解析器（PascalCase），与生产路径一致。原验证方式：对 settings.json 调用该重载，观察是否加载失败 |

**第二轮复核（修复完成后逐行核对代码）**：P3、P4、P5、P6、P10、P11 在第二轮中确认存在，已分别在 `a28a1d8`、`c35cac3`、`e386d4e` 修复（见上表的验证方式列）。更正：此前报告认为 SDK 没有存活检测 API，实际 `McpClient.Completion` 为公开属性，无需反射。

### 2.3 已核实正常的部分

- 配置路径为 `%APPDATA%\NixerOfficeCopilot\settings.json`（`OfficeCopilotStoragePaths.cs`）。
- 配置由 Newtonsoft 默认 PascalCase 读写（`OfficeCopilotJson.cs` 没有配置 camelCase），与 `FromSettingsJson` 的字段名一致。
- `ModelContextProtocol` 版本锁定为 1.4.1（`Directory.Packages.props`）。
- 默认模式下，无写入预检的 MCP 工具会正常弹出确认（`DefaultToolDispatcher.cs` 约 582-596 行的 `shouldConfirm` 逻辑）。
- 技能白名单对 MCP 的处理与 F12-2、F12-3 设计一致（`SkillPlanSupport.cs`）：`IsToolPermittedBySkillPlan` 中 critical 硬门（197 行）排在 `IsWhitelistExemptTool` 豁免（199 行）之前，因此 `critical` 级 MCP 工具在非代码执行技能下仍被拦截，普通 MCP 工具不受白名单限制。
- 白名单名字在当前宿主目录中一个都不存在时放行（`GetSkillPlanViolationMessage` 约 247-249 行），与 `ARCHITECTURE.md` §9.4 规则 4 一致。

## 3. 设计层面的待决策问题

1. **刷新策略**：每轮刷新是否保留？建议改为"配置变化时才增量刷新"，或只在设置保存时触发。
2. **安全模型（AcceptEdits 确认）**：`critical` 级工具在 AcceptEdits 下是否应弹确认？这是 B2 的前提。另外，无关键字的外部 MCP 工具定为 `medium` 是否合适？F12-3 已选择"推导而非默认 high"，这一选择本身不再是待决问题，待决的是 AcceptEdits 下 `medium` 是否应放行。
3. **超时策略**：已确定固定 90 秒，所有 MCP 调用统一，不新增设置（界面无控件）。
4. **失败可见性**：连接失败、断连、调用超时是否需要在 UI 或日志中给出明确提示？
5. **崩溃的 stdio server 启动耗时**：子进程立即退出时，连接失败要等约 60 秒（SDK 行为）。是否要在进程退出时立即失败，还是接受该耗时？

## 4. ARCHITECTURE.md 对照

对照依据为 [ARCHITECTURE.md](./ARCHITECTURE.md) §9.2、§9.4、§9.7（F12）中与 MCP 或确认门相关的条目。结论只针对代码现状，逐条标注。

| 文档条目 | 代码现状 | 结论 |
|---|---|---|
| §9.7 F12-1：`runtime.powershell` 风险为 `critical` | `DefaultToolDispatcher.cs` 约 208-211 行为 `ToolRiskLevels.Critical` | 一致 |
| §9.7 F12-2：critical 硬门 = `AllowsCodeExecution(plan)` | `SkillPlanSupport.cs` 182-183、197 行 | 一致 |
| §9.7 F12-3：MCP 风险由 `ToolSafetyPolicy.InferRiskLevel` 推导 | `DefaultToolDispatcher.cs` 约 358-360 行 `InferMcpRiskLevel` | 一致 |
| §9.7 F12-3：`mcp__*` 豁免技能白名单，但 critical 硬门仍生效 | `SkillPlanSupport.cs` 174-175、197、199 行，顺序与文档一致 | 一致（见 2.3） |
| §9.4 风险分级表（`critical` / `high` / `medium` / `low` 的名字规则） | `ToolSafetyPolicy.InferRiskLevel` 与表一致 | 一致 |
| §9.4 规则 4：白名单名字都不在目录中时放行 | `SkillPlanSupport.cs` 247-249 行 | 一致 |
| §9.2(a)：Broker 门为"`RequiresConfirmation && !IsReadOnly && Mode 不是 AcceptEdits/Bypass`" | 代码为 `AcceptEdits && !isHighRisk`，`isHighRisk` 只判 `high`，`critical` 不在例外内 | 原为**不一致**（文档未写 `high` 例外），已在 `cfaabf7` 修正文档与代码。见 B2 |
| §9.2(b)、(c)：`NormalizeSafetyPolicy` 覆盖问题、白名单空实现（分别为 F3、F6） | 未逐行复核 | 未核实（仅依据文档自述） |
| §9.7 F12-4：放权标志只取主技能 | 未核实（`KeywordSkillPlanner.BuildSelectionResult`） | 未核实 |
| §3 拓扑：MCP 在 Broker 中的位置 | 拓扑层面与代码未发现矛盾 | 一致，但文档对 MCP 只有拓扑级描述，覆盖很薄 |

补充说明：
- 文档没有任何关于 MCP 配置解析、连接生命周期、超时、刷新策略的描述。B1、B3–B6、B7、B8 与文档无关，属于文档未覆盖的实现问题，不能说"文档与代码冲突"。
- §9.2(a) 的确认门描述没有覆盖 `74e0fc7` 引入的 `high` 例外，文档的最后更新早于该提交，建议与 B2 一并更新。

## 5. 未完成的检查

- 未运行 `OfficeCopilot.Mcp.TestServer` 做端到端复现，B1、B3、B5 及所有 P 项均为代码推断或待验证。
- 未能通过反射读取 ModelContextProtocol 1.4.1 的 transport 选项类型（加载类型时报错），因此 B6 与 P2 的修法需要先查阅该版本的 API。
- 修复过程中新增了 MCP 单元测试（路由、刷新、禁用工具、配置解析、连接超时、名称冲突），修复后全量 `OfficeCopilot.Agent.Tests` 为 734/734 通过。
- 用户指出：按工具配置风险（RiskLevel 等）不可行，因为用户事先不知道 MCP 工具名，设置界面也没有对应控件。该设计已撤销（见 P9），验证方式也随之改为"按工具名推导"。
- 手工验证计划尚未执行：需要的测试服务器工具（`delete_*`、`run_command`、慢工具、图片工具）目前不存在，需先征得同意再添加。
- 工作区中 `OfficeCopilot.Agent.Runtime/BrokerTurnExecutor.cs` 有未提交改动（DSML 工具调用标记相关，与 MCP 无关）。本次审查未修改该文件，也未纳入结论。

## 6. 建议修复顺序

1. B2：确认 AcceptEdits 下 `critical` 的设计意图后，收紧确认条件（安全相关，改动小）。
2. B1：统一命名与路由的 key，补单元测试（功能性缺陷）。
3. B5：为连接和调用加超时，启动路径改为异步、可取消，并记录异常。
4. B4 + B3：重构刷新逻辑，避免全量断连和吞异常。
5. B6、B7、P1、P2：配置项落地。
6. 其余低优先级项按需处理。

## 7. 修复状态汇总

| 项 | 状态 | 提交 |
|---|---|---|
| B1 命名与路由 | 已修复 | `91fc3d7` |
| B2 AcceptEdits 确认门（high + critical 均确认） | 已修复 | `cfaabf7` |
| B3 每轮全量重连 | 已修复（失败 server 不再每轮重试） | `e0e8515` |
| B4 先断连后读配置 | 已修复 | `e0e8515` |
| B5 超时与启动日志 | 已修复：所有 MCP 调用统一固定 90 秒（无可配置超时，界面无控件） | `91fc3d7`、`3f1be81`、本次修改 |
| B6 WorkingDirectory | 已修复；真机已验证（`npx` 启动，设置界面测试通过） | `dd6d27f`、`0494e2b` |
| B7 per-tool 设置 | 已修复：`IsEnabled` 生效；按工具风险与超时配置均已移除（见 P9、B5） | `237a070`、`3f1be81`、`d058d91`、本次修改 |
| B8 同名覆盖 | 已修复 | `3f3f243` |
| B9 设置界面"测试"按钮应答匹配 | 已修复；真机已验证（`b6-npx` 测试通过）：stdio 握手直接读取下一行作为应答，服务端在应答前推送 `notifications/*` 通知（如 `server-everything` 的 `notifications/tools/list_changed`）时误判为"既无 result 也无 error"。改为按请求 id 匹配应答，跳过通知、服务端请求与非 JSON 行 | 本次修改 |
| HTTP 测试按钮：SSE 应答与 TLS 1.2 | 已修复；真机已验证（DeepWiki 设置界面测试通过）。SSE 应答按 `data:` 行与请求 id 匹配，并携带 `Mcp-Session-Id`；`ServicePointManager` 启用 TLS 1.2（.NET Framework 进程默认协议不含 TLS 1.2，握手失败），设置页测试与运行时 HTTP 分支均已启用 | `d18274c`、本次提交 |
| P3、P4、P5、P6、P10、P11 | 已修复 | `a28a1d8`、`c35cac3`、`e386d4e` |
| P1、P7、P8 | 已修复 | `509d264` |
| P9 | 已解决：撤销按工具风险配置，改为按工具名推导 | 本次重构 |
| P12 readOnlyHint | 已采纳（用户确认，接受服务器可能谎报的风险）：`readOnlyHint=true` 的工具不弹确认、风险 low、Ask 模式可用 | 本次修改 |
| P2 | 已修复；真机已验证（`npx` 经 `McpCommandResolver` 解析后启动，设置界面测试通过） | `0494e2b` |
| B10 Broker 启动被 MCP 加载阻塞 | 已修复；真机已验证（`ask_wiki_question` 调用成功，说明 Broker 已正常启动）。根因：`BrokerApplication.CreateTurnService` 在创建命名管道前同步执行 `McpRegistry.LoadAsync`（stdio 冷启动 npx、HTTP 握手等合计超过 10 秒）。宿主 `WaitForBrokerReadyNoLock` 的 Ping 探测约 5 秒超时，于是以 `pipe_not_ready` 杀掉 Broker，界面报"Broker 启动失败"。日志表现为 "Broker process starting" 之后直接跳到 "Layer1 hardcoded"，中间没有管道启动记录。修复：MCP 加载改为后台 `Task`，管道先启动；`BrokerTurnService.ExecuteAsync` 在首轮等待 MCP 就绪，最长 30 秒，超时则记录警告并不带未加载的 MCP 工具继续。本地探测：管道约 1.2 秒内启动 | `a55ba13` |

**待决策 / 遗留：**
- 只读判定决定（用户确认，保持现状）：只信任服务器声明的 `readOnlyHint=true`，未声明的 MCP 工具一律弹确认。实测 DeepWiki（`https://mcp.deepwiki.com/mcp`）的 `tools/list` 对 `ask_wiki_question`、`read_wiki_contents`、`read_wiki_structure` 都没有返回 `annotations`，因此这些查询类工具仍会确认。若要免确认，需要后续增加按服务器的信任设置，目前未做。
- 按需加载决定（用户确认，保持现状）：暂不做工具定义缓存。Broker 启动时后台加载 MCP，首轮对话最多等待 30 秒。已知遗留：每轮对话前的 `RefreshFromSettingsAsync` 仍会同步连接新增或配置变化的 server，每轮 `ListAllToolsAsync` 也会向已连接 server 发一次 `tools/list`。若以后需要降低延迟，再考虑缓存方案。
- 超时设计已确认：所有 MCP 调用固定 90 秒，不新增单工具或服务器级超时设置。
- P9 决策（用户确认，取代此前"只允许加严"的方案）：MCP 工具的风险等级只由工具名推导（`InferMcpToolRiskLevel`，`IsReadOnly=false`），不读取任何按工具的风险配置。确认门对 MCP 工具始终需要确认，`RequiresBackup` 为 false。理由：用户无法预先知道工具名，设置界面没有风险字段的编辑控件，手改 JSON 不可行。测试见 `McpToolRiskInferenceTests`。
- P12 决定（用户确认，推翻此前"不采用"的决定）：读取服务器声明的 `readOnlyHint`。`readOnlyHint=true` 的 MCP 工具 `IsReadOnly=true`、`RequiresConfirmation=false`、风险 low，默认模式下不弹确认，Ask 模式下可用。其余 MCP 工具仍按写入类处理（必须确认，风险按工具名推导）。风险：`readOnlyHint` 由服务器自报，恶意或错误的服务器可以把写入工具标成只读而绕过确认。这是用户明确接受的风险。实现见 `DefaultToolDispatcher.CreateMcpToolDescriptor`、`NuGetMcpClient.ListToolsAsync`；测试见 `McpToolRiskInferenceTests`。
- 手测约束：Agent 模式下，写入类 MCP 工具仍需确认，只读 MCP 工具不再弹框。Ask 模式下只能看到声明只读的 MCP 工具，写入类 MCP 工具仍不可见。
- B3 已确认（用户决定）：连接失败的 server 不自动重试，需要用户修改配置或重启。（P6 修复后，已连接但进程退出的 server 会在下一轮刷新时重连，不受此限制。）
- 真机端到端验证：已通过 B2 确认门（Agent 模式下写入工具弹框，`echo` 不弹）、写入工具拒绝路径（下拉框选"拒绝授权"后确认，工具未执行）、B6 与 B9（`b6-npx` 测试通过）。HTTP/SSE 已真机验证设置界面测试（`https://mcp.deepwiki.com/mcp`，服务端返回 `text/event-stream`）。该验证暴露两个问题并已修复：SSE 应答未解析（`d18274c`）；应用进程默认协议不含 TLS 1.2，握手失败（本次提交）。运行时通过 HTTP 调用工具已真机验证：DeepWiki 的 `ask_wiki_question` 调用通过（该工具未声明 `readOnlyHint`，因此调用前弹出确认，确认后正常返回）。P6 已真机验证：结束服务进程后，下一次调用时自动重新拉起并正常返回。P7 已真机验证：`get_image` 返回 `image` 内容块，未弹确认框。90 秒超时已真机验证：`slow_task(120)` 客户端在约 90 秒超时，服务端在约 120 秒后完成，超时后同一连接的后续调用正常返回。Word 与 PPT 宿主已真机验证：按约定分别测试 `echo`（直接返回，不弹确认）与写入类工具（弹确认，拒绝后工具未执行），用户反馈均通过，未发现问题。
