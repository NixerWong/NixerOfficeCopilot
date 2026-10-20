# 技能系统改进：问题清单与实施计划

> 状态：已实施 P-1 至 P-11、P-13、P-14（P-14 仅日志）；P-12 已完成。
> 范围：技能从设置、加载、规划到工具执行拦截的完整链路
> 依据：代码阅读与交叉检索；实施后补充了 Agent.Tests 单元测试（Skills 命名空间）。

> 实施说明（相对原方案的偏差）：
> - P-10：运行时注册表在用户 SKILL.md 变化时自动重载（`ReloadingSkillRegistry`，按文件路径、修改时间与长度计算指纹）。保存、导入、新建后下一条消息即生效，无需重启。`ISkillSource.ReloadAsync` 仍未接入调用方，注释待修正。
> - P-14：仅增加分层耗时日志，未改为后台加载；待日志确认占比后再决定。
> - P-3：启用状态写入 SKILL.md 的 `enabled` 字段。已有的 `skill:{id}:enabled` Metadata 值不再读取，未做迁移。
> - P-2：SKILL.md 可声明 `allow-high-risk-tools` / `allow-code-execution`，并输出告警。`.nsk` 导入可能带入授权，风险需知悉。
> - P-4：空白名单记录告警后仍放行全部工具（fail-open，按决策默认）。
> - P-6：`disable-model-invocation: true` 的技能不进入自动规划候选，也不能被模型通过 `skill` 工具调用；用户显式点名不受限制。`user-invocable: false` 的技能不能被用户显式点名，点名无效时回落到自动规划。

## 1. 背景与核对记录

本文档由一次只读代码分析产生，独立于 [ARCHITECTURE.md](./ARCHITECTURE.md)，避免与已完成的架构记录混在一起。

### 1.1 与 ARCHITECTURE.md §9.7（F12-2）的不一致

| 项 | ARCHITECTURE.md §9.7 F12-2 | 当前代码 |
|---|---|---|
| critical 硬门判定 | `AllowsCodeExecution(plan)` = `AllowHighRiskTools && AllowVbaFallback` | `SkillPlanSupport.AllowsCodeExecution` 仅返回 `skillPlan.AllowCodeExecution` |
| 依据 | — | 代码注释说明：旧的双标志实现因“漏配任意一个即白名单静默失效”（D9）已被替换 |

结论：§9.7 F12-2 描述的是已被替换的旧设计。`AllowVbaFallback` 在核心代码中已不存在，仅在 `fmSkillEditor` 中作为 `allow-code-execution` 的旧别名读取。**已于 P-12 中修正 ARCHITECTURE.md §9.7，改为单一标志的现行规则。**

### 1.2 核对中新发现的问题

- `OfficeCopilot.Skills` 项目中没有任何代码映射 `AllowCodeExecution` / `AllowHighRiskTools`。因此**文件型技能（内置 SKILL.md 与用户目录）无法获得代码执行或高风险授权**，这两个标志只能由 `OfficeSkillCatalog` 中的硬编码技能拥有。
- 编辑器写入的 `allow-code-execution` 被加载器忽略，所以用户在编辑器中勾选的代码执行授权永远不会生效。
- 内置 `skillify`、`record-to-memory` 原本同时存在于 `OfficeSkillCatalog`（第 1 层）和 `BuiltInSkills/`（第 2 层）。已决定删除 `BuiltInSkills/` 目录，内置技能只保留硬编码版本，设置面板不再列出内置技能，只显示用户自建的技能（见 P-13）。

## 2. 问题清单

严重程度：高 = 功能失效或授权错误；中 = 行为不一致或静默失败；低 = 体验或维护性问题。

| # | 严重程度 | 问题 | 证据（文件 / 位置） |
|---|---|---|---|
| P-1 | 高 | 编辑器写出的 kebab-case 键（`use-when`、`allowed-tools`、`allow-code-execution` 等）被加载器忽略，因为加载器只按 camelCase 读取且 `IgnoreUnmatchedProperties` 静默丢弃未知键 | `fmSkillEditor.BuildYaml`（约 262–273 行）；`FileSystemSkillLoader`；`BuiltInSkillLoader` 只接受 `usewhen` / `use_when` 等变体 |
| P-2 | 高 | 授权标志（`AllowHighRiskTools`、`AllowCodeExecution`）不从 SKILL.md 映射，文件型技能无法获得 critical 工具授权 | `SkillDefinitionMapper` 未映射；`OfficeSkillCatalog` 仅硬编码赋值 |
| P-3 | 高 | 启用开关无效。界面写入 `skill:{id}:enabled`，无人读取；映射器固定 `IsEnabled = true` | `fmSkillsConfig`（约 186、399 行）、`fmConfig`（约 815 行）；`SkillDefinitionMapper` |
| P-4 | 高 | 白名单为 fail-open：技能 `AllowedToolNames` 为空时 `IsToolPermittedBySkillPlan` 直接放行所有工具；工具名全不在当前宿主目录时也不拦截。叠加 P-1 后，编辑器创建的技能实际上不限制任何工具 | `SkillPlanSupport.IsToolPermittedBySkillPlan`、`GetSkillPlanViolationMessage` |
| P-5 | 中 | 编辑器字段在加载端缺失：`PreferredTools`、`CompanionSkills`、`FallbackSkills`、`RequireVerification`、`DisplayName` 无对应 `SkillFrontmatter` 字段；`Model` 已解析但未映射 | `SkillYamlModel` vs `SkillFrontmatter`；`SkillMetadataKeys` 中的 `preferred-tool`、`fallback-skill` 无人填充 |
| P-6 | 中 | `disableModelInvocation` / `userInvocable` 只写不读。`skillify` 与 `SkillCreatorTool` 写入它们，但运行时不执行，也没有用户侧斜杠调用入口 | `SkillCreatorTool.BuildSkillMarkdown`（约 112–134 行）；`SkillTool`；`skillify/SKILL.md` |
| P-7 | 中 | 两套解析器行为不一致：`BuiltInSkillLoader` 手写解析，不支持块标量（`>`、`|`）与转义，列表项扫描不限定所属键；`FileSystemSkillLoader` 使用 YamlDotNet | `BuiltInSkillLoader.ParseBuiltInFrontmatter` / `ParseListItems` |
| P-8 | 中 | 用户技能加载失败静默消失：`FileSystemSkillLoader.Load()` 的 `catch (Exception) {}` 为空，无日志 | `FileSystemSkillLoader.Load` |
| P-9 | 中 | 用户技能（第 3 层）静默覆盖同名内置/硬编码技能，无任何告警 | `CreateSkillRegistry`、`InMemorySkillRegistry.AddOrUpdate` |
| P-10 | 低 | 技能只在启动时加载一次。`ReloadAsync` 仅重复调用 `Load()` 且无调用方，保存或导入后需重启；`ISkillSource` 注释容易误导 | `FileSystemSkillLoader.ReloadAsync`、`BuiltInSkillLoader.ReloadAsync` |
| P-11 | 低 | `SplitFrontmatter` 使用未指定 `StringComparison` 的 `StartsWith` / `IndexOf`，存在文化相关比较风险 | `FileSystemSkillLoader.SplitFrontmatter` |
| P-12 | 低 | ARCHITECTURE.md §9.7 F12-2 与代码不一致（见 1.1）。已修正：改为单一标志 `AllowCodeExecution`，并说明 `runtime.powershell` 豁免与 `AllowHighRiskTools` 不再参与硬门 | `docs/ARCHITECTURE.md` §9.7 |
| P-13 | 中 | 设置面板与运行时的内置技能来源不一致（已解决）。原先面板只读自身目录下的 `BuiltInSkills`，运行时由 Broker 加载，导致面板只在 Excel 显示。决定：删除 `OfficeCopilot.Skills/BuiltInSkills/` 及 csproj 的 `Content` 规则，内置技能仅由 `OfficeSkillCatalog` 提供，面板只列用户自建技能。原文件可通过 git 历史恢复 | `OfficeCopilot.Skills.csproj`；`fmSkillsConfig.GetBuiltInSkillsDirectory`、`fmConfig.GetBuiltInSkillsDirectory` 现在找不到目录，内置列表为空；`BuiltInSkillLoader` 与 `CreateSkillRegistry` 第 2 层代码保留，目录缺失时仅记录日志 |

| P-14 | 中 | 技能注册表在 Broker 启动关键路径上同步构建：`CreateTurnService` 中的 `BuildServiceProvider().GetRequiredService<IAgentRuntime>()` 会触发 `CreateSkillRegistry()`，早于命名管道开始监听。MCP 则是 `Task.Run` 后台加载，首轮 turn 前最多等待 30 秒。健康启动日志中第 1 层到第 3 层耗时约 15–30 毫秒，不是超时的已知原因；但加载过程没有耗时日志，超时记录中配置读取后约 5 秒没有任何 Broker 日志，因此无法排除其影响 | `BrokerApplication.CreateTurnService`（约 221–254 行）；`BrokerTurnService.WaitForMcpReadyAsync`（30 秒上限）；`OfficeCopilotSettings.BrokerReadyTimeoutSeconds` 默认 5 秒；`AgentRuntimeServiceCollectionExtensions.CreateSkillRegistry`；日志 `application.log` 中 12:24:25 与 12:51:50 两段启动记录 |

## 3. 设计决策（已按方案 A 实施）

1. **P-4 白名单空列表的语义**（已实施方案 A）
   - 方案 A（推荐）：空白名单在加载时告警，运行时仍放行，保持现有行为，只补充可观测性。
   - 方案 B：空白名单视为“不允许任何工具”（fail-closed）。影响面大，内置技能需逐个审计。
2. **P-3 启用状态的存放位置**（已实施方案 A）
   - 方案 A（推荐）：写入 SKILL.md 的 `enabled` 字段，启用状态随技能文件走，可导出导入。内置技能的开关通过用户目录覆盖文件实现。
   - 方案 B：保留 `Settings.Metadata` 的 `skill:{id}:enabled`，在 `CreateSkillRegistry` 中读取并应用。
3. **P-6 两个标志的处理**（已实施方案 A）
   - 方案 A（推荐）：实现 `disableModelInvocation`（从规划器与 `runtime.invoke_skill` 的可选列表中排除）与 `userInvocable`（仅记录，待斜杠入口再实现）。
   - 方案 B：从模板、`SkillCreatorTool` 与 `skillify` 中删除这两个字段。
4. **P-2 授权标志的开放方式**（已实施方案 A）
   - 方案 A（推荐）：在 SKILL.md 中允许 `allowHighRiskTools` / `allowCodeExecution`，并在加载时对 critical 授权打印告警日志。
   - 方案 B：文件型技能永远不能获得 critical 授权，仅硬编码技能可以。这是更保守的选择，但需要在编辑器中说明。

## 4. 实施计划

### 阶段 0：正确性修复（P-1、P-2、P-3、P-5）

1. 统一键约定。加载器同时接受 camelCase 与 kebab-case，可通过 YamlDotNet 的 `NamingConvention` 包装或别名映射实现。
2. 补齐 `SkillFrontmatter` 与 `SkillDefinitionMapper`：增加 `PreferredTools`、`CompanionSkills`、`FallbackSkills`、`RequireVerification`、`AllowHighRiskTools`、`AllowCodeExecution`、`DisplayName`；`Model` 要么映射，要么从模板中删除。
3. 按设计决策 2 实现启用状态。
4. 按设计决策 4 实现授权标志的加载。
5. 新增往返测试：`fmSkillEditor.BuildYaml` 的输出 → `FileSystemSkillLoader` → `SkillDefinition`，断言所有字段保留。

涉及文件：`OfficeCopilot.Skills/SkillFrontmatter.cs`、`SkillDefinitionMapper.cs`、`FileSystemSkillLoader.cs`、`BuiltInSkillLoader.cs`；`OfficeCopilot.Settings.WinForms/fmSkillEditor.cs`、`fmSkillsConfig.cs`、`fmConfig.cs`；`OfficeCopilot.Agent.Runtime/Composition/AgentRuntimeServiceCollectionExtensions.cs`（如选方案 B）。

### 阶段 1：可观测性与一致性（P-7、P-8、P-9）

1. 用统一的 YamlDotNet 解析器替换 `BuiltInSkillLoader` 的手写解析，保留现有内置 SKILL.md 的输出结果不变。
2. 删除空 `catch`，改为通过 `OfficeCopilotLog` 记录技能文件路径与异常信息。
3. 未知 frontmatter 键、空白名单、同名覆盖均记录告警日志。

### 阶段 2：规划与调用语义（P-4、P-6）

1. 按设计决策 1 处理空白名单。
2. 按设计决策 3 处理 `disableModelInvocation` / `userInvocable`。
3. 对 `SkillPlanSupport` 相关测试（`SkillPlanSupportTests`）补充新语义的用例。

### 阶段 3：体验与细节（P-10、P-11、P-12）

1. 已完成：保存或导入后注册表自动刷新（`ReloadingSkillRegistry`）。待办：修正 `ReloadAsync` 与 `ISkillSource` 注释。
2. `SplitFrontmatter` 显式使用 `StringComparison.Ordinal`。
3. 已完成：修正 ARCHITECTURE.md §9.7 F12-2 的描述（P-12）。
4.（P-14）先为 `CreateSkillRegistry` 的三层加载增加耗时日志，确认对启动的实际占比。若确有影响，改为与 MCP 相同的后台加载：注册表构建放入 `Task`，由 `BrokerTurnService` 在首轮 turn 前等待，命名管道则不再被阻塞。

## 5. 验证方式

- 单元测试：在 `OfficeCopilot.Agent.Tests`（MSTest，net472）中运行与技能相关的测试类，包括 `SkillPlanSupportTests`、`OfficeSkillCatalogTests`、`TraceabilityIntentPolicyTests`，以及阶段 0 新增的往返测试。
- 构建：完成各阶段后执行解决方案构建，确认 `OfficeCopilot.Settings.WinForms` 与 `OfficeCopilot.Skills` 均可编译。
- 手工验证：在设置界面创建一个带 `allowed-tools` 与 `enabled: false` 的技能，重启后确认规划器不再选中它，且白名单生效。
- 回归：内置技能（`skillify`、`record-to-memory`）的加载结果与改动前一致。

## 6. 不在本次范围内

- PowerPoint 工具覆盖面差距（属产品排期）。
- `SubagentRunner` 的工具计划过滤（子代理目前不执行工具，见 ARCHITECTURE.md §9.7）。
- ClickOnce / MSI 分发形态。

## 7. 用户技能按宿主隔离（已实施）

- 问题：用户技能统一存放在 `%APPDATA%\NixerOfficeCopilot\skills\<name>\`，没有宿主标识。`SkillHostMatcher` 对空 host 视为匹配所有宿主，因此 Excel、Word、PowerPoint 三端互相可见。
- 方案：
  - 共享根目录不变，按宿主分子目录：`excel\`、`word\`、`powerpoint\`（见 `SkillStorageLocations`）。三个插件仍共用同一安装包与同一根目录。
  - 每个 Office 插件启动各自的 Broker，经 `--hostApplicationName` 传入宿主，Broker 只加载本宿主目录。
  - 加载时由目录强制写入 `host` 元数据，覆盖 SKILL.md 中声明的 `host:`，防止文件内容越过隔离边界。
  - 宿主未知或为空时不加载任何用户技能（fail-closed）；`SkillCreatorTool` 与 `SkillImprovementApplier` 同样拒绝写入。
  - 设置面板 `fmSkillsConfig` 由各 Ribbon 传入宿主，只显示和编辑本宿主目录。
- 不变：第 1 层（`OfficeSkillCatalog`）与第 2 层（`BuiltInSkillLoader`）未改动，内置与公共技能行为一致。
- 迁移：旧版扁平目录 `skills\<name>\` 不再加载，Broker 启动时记录告警（`FindLegacyFlatSkillFolders`）。已有技能 `local-ip-to-sheet` 移入 `excel\`。
- 验证：`SkillStorageLocationsTests` 覆盖宿主目录映射、未知宿主、空根目录、宿主隔离、宿主元数据覆盖。Agent.Tests 中 98 个技能相关测试通过。
- 遗留：`fmConfig` 窗体仓库中没有任何实例化代码，当前不会运行；但其内部（`fmConfig.cs` 约 542 行起）仍使用旧的扁平 `SkillsDirectory`。若将来启用该窗体，需改为宿主目录，或直接删除。

## 8. 设置面板技能名称显示统一（已实施）

- 问题：`fmSkillsConfig` 列表中的技能名有时带双引号、有时不带。原因是编辑器保存 SKILL.md 时，`fmSkillEditor.EscapeYamlValue` 会给含 `-`、`:` 等字符的值加双引号（如 `name: "local-ip-to-sheet"`），而手写文件不加；列表用原始正则直接截取，没有去掉引号。
- 修复：新增 `SkillFrontmatterText.ReadScalar` 与 `UnquoteScalar`，按 YAML 规则去掉双引号（处理 `\x` 转义）与单引号（处理 `''`）后再显示和去重。`name`、`description`、`category` 均走此路径。
- 不变：运行时使用 YamlDotNet 解析，本来就不受影响；磁盘上的文件格式和编辑器的写入规则也未改动。
- 遗留：`fmConfig.cs` 中仍有同样的原始正则（该窗体不运行，见第 7 节）。
