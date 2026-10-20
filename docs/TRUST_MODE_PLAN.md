# 工具执行权限模式（TrustMode）实施方案

> 状态：已实施（S1–S9 完成，测试通过）。实施中的偏差见 §8。
> 关联文档：[ARCHITECTURE.md](ARCHITECTURE.md) §9（F1–F12 工具确认与白名单）、§10（TrustMode 实施说明）

---

## 1. 需求

在 `fmSysConfig` 窗体中，由 `cbeTrustMode` 控件控制工具与技能调用的权限确认行为，两个选项：

| 选项 | 内部值 | 默认 | 行为 |
|---|---|---|---|
| 提示确认 | `need-confirm` | 是 | 保持现有的权限确认逻辑（写操作、高风险工具仍弹窗） |
| 始终放行 | `always-allow` | 否 | 无论哪一级权限确认都不提醒，直接执行 |

约束：

1. **Excel、Word、PowerPoint 三个插件各自保存自己的 TrustMode**，不能共用。其他设置可以共用。
2. **统一存放在主配置文件 `settings.json`**（`OfficeCopilotSettings`），不再使用 `builtin_tools.{app}.json`（这是对第一版方案的更正）。
3. 每个宿主使用**不同的参数名**区分。

---

## 2. 已确认的决策

| 问题 | 决策 | 来源 |
|---|---|---|
| “始终放行”是否跳过计划模式（Plan）阻止 | 否，仅 Agent 模式放行；Ask 模式（Plan）保持只读 | 用户确认（修正） |
| “始终放行”是否跳过高危开关（技能 `allow-code-execution` 硬闸，即 critical 级工具） | 是 | 用户确认 |
| 三宿主 TrustMode 的存放位置 | 主配置文件 `settings.json`，按宿主键区分 | 用户更正 |

---

## 3. 存储设计

在 `OfficeCopilotSettings` 中为三个宿主各增加一个独立的参数（顶层属性，直接存为 `settings.json` 的顶层字段）：

```csharp
public string ExcelToolTrustMode { get; set; }       // 值：need-confirm / always-allow
public string WordToolTrustMode { get; set; }
public string PowerPointToolTrustMode { get; set; }
```

- 宿主名通过 `HostApplicationNames.Normalize` 归一后，由 `OfficeCopilotSettingsFactory.GetToolTrustMode` / `SetToolTrustMode` 按 `switch` 映射到对应属性，调用方不直接接触属性名。
- 缺省或非法值一律视为 `need-confirm`（`Normalize` 中逐个补齐）。
- 常量位于 `OfficeCopilot.Settings.Abstractions/ToolTrustModeValues.cs`（`NeedConfirm` / `AlwaysAllow` / `IsValid`）。
- Agent 运行时不引用 Settings 工程，因此在 `OfficeCopilot.Agent.Abstractions/Models/ToolMetadataKeys.cs` 中另有一份同值常量（`ToolTrustMode` / `ToolTrustModeNeedConfirm` / `ToolTrustModeAlwaysAllow`）。两处取值必须保持一致。

迁移：旧版 `settings.json` 没有这三个字段，读取为空值；`GetToolTrustMode` 等读取入口将其视为 `need-confirm`，行为与现状一致。`JsonSettingsStore.Load` 不调用 `Normalize`，因此文件中的值保持为空直到下一次写入。

---

## 4. 涉及代码与处理步骤

### 4.1 调用链

```
fmSysConfig（cbeTrustMode）
  └─ SaveSettings → RefreshToolTrustModes → settings.json: ExcelToolTrustMode / WordToolTrustMode / PowerPointToolTrustMode
        │
Panel（CopilotPanelControl.ResolveToolTrustMode，每次发送时从存储重读）
  └─ request.Metadata[ToolMetadataKeys.ToolTrustMode] = always-allow / need-confirm
        │
Broker（BrokerTurnExecutor）
  ├─ ExecuteModelTurnAsync：AlwaysAllow 且非只读模式时，skillPlan.AllowCodeExecution = true
  └─ ExecuteToolCallAsync：_toolDispatcher.CurrentAlwaysAllowTools = IsAlwaysAllowTrustModeActive(request)
        │
DefaultToolDispatcher.DispatchToolCallAsync
  ├─ trustAlwaysAllow = CurrentAlwaysAllowTools 且非 Plan：跳过确认、写入效果确认、执行模式预览强制确认
  ├─ PowerShell Step 2：同样跳过确认（Step 1 Plan 阻止保留）
  └─ 宿主请求写入 tool-trust-mode 元数据（覆盖上游同名键）
        │
DefaultHostBridgeRequestHandler
  └─ trustAlwaysAllow 时跳过整个宿主确认闸（含范围越界强制确认）
```

### 4.2 逐步实施

| 步骤 | 内容 | 涉及文件 |
|---|---|---|
| S1 | 增加三个宿主的独立 TrustMode 参数（`ExcelToolTrustMode` / `WordToolTrustMode` / `PowerPointToolTrustMode`）与 `ToolTrustModeValues` 常量 | `OfficeCopilot.Settings.Abstractions/OfficeCopilotSettings.cs`，新增 `ToolTrustModeValues.cs` |
| S2 | 规范化：三个宿主参数缺省或非法时回退为提示确认 | `OfficeCopilot.Settings/OfficeCopilotSettingsFactory.cs` |
| S3 | 窗体绑定：`cbeTrustMode` 的选项（提示确认/始终放行）、读取、保存、`EditValueChanged` 接入 `GeneralSetting_Changed` | `OfficeCopilot.Settings.WinForms/fmSysConfig.cs`（设计器控件已存在，见 `fmSysConfig.Designer.cs`） |
| S4 | Panel 下发：发送时把当前宿主的 TrustMode 写入 `request.Metadata`；永久授权等路径的整体写回改为保留 TrustMode | `OfficeCopilot.Panel.WebView/CopilotPanelControl.cs`（`ResolveToolTrustMode`、`SaveSettingsPreservingToolTrustModes`），`CopilotPanelControl.ToolSafety.cs`（`ApproveToolPermanently`） |
| S5 | Broker 接收并设置：解析元数据；技能 `AllowCodeExecution` 在本轮计划上置位；设置调度器的 `CurrentAlwaysAllowTools` | `OfficeCopilot.Agent.Runtime/BrokerTurnExecutor.cs`（`IsAlwaysAllowTrustModeActive`） |
| S6 | 调度器放行：跳过确认、写入效果确认与执行模式预览强制确认；PowerShell Step 2 跳过确认；写入宿主元数据 | `OfficeCopilot.Agent.Runtime/Turn/DefaultToolDispatcher.cs`（`IsTrustAlwaysAllowActive`、`DispatchToolCallAsync`、`HandlePowerShellWithPermissionGateAsync`、`AppendTrustModeMetadata`） |
| S7 | 宿主放行：确认闸跳过，因为调度器不再发送 `confirmation-granted` | `OfficeCopilot.HostBridge.Server/DefaultHostBridgeRequestHandler.cs`（`trustAlwaysAllow`） |
| S8 | 测试 | `OfficeCopilot.Agent.Tests/Turn/ToolTrustModeTests.cs`，`OfficeCopilot.Agent.Tests/ToolTrust/ToolTrustModeSettingsTests.cs` |
| S9 | 同步文档：本文档状态与 §8，ARCHITECTURE.md 变更记录与 §9 | `docs/TRUST_MODE_PLAN.md`，`docs/ARCHITECTURE.md` |

### 4.3 需要同步修改的既有逻辑

- `DefaultToolDispatcher` 中 `modeSkipsConfirmation`、`shouldConfirm`、写入效果分支（Append/Modify）与 `exceedsExecutionModePreview` 强制确认。`AlwaysAllow` 必须在这些分支之后统一覆盖为“不确认”，否则会漏掉某些路径。
- `HandlePowerShellWithPermissionGateAsync` 的 Step 1（Plan 阻止）与 Step 2（确认）。
- `CopilotPanelControl.ToolSafety.cs` 的永久授权缓存（`allow-always` 是逐工具授权，与 TrustMode 是不同概念，互不替代）。

---

## 5. 安全影响（需用户知悉）

“始终放行”开启后：

1. 写操作、高风险与 PowerShell 的确认弹窗全部跳过。
2. **Ask 模式保持只读**：Plan 阻止不受 TrustMode 影响，只有 Agent 模式才完全放行。
3. **critical 级工具（VBA、任意代码执行）不再需要技能声明 `allow-code-execution`**。
4. `ValidateExecutionMode`（执行模式声明的写入上限）属于数据契约校验，不是确认弹窗，本方案**不改动**。

---

## 6. 已确认的开放问题

**问题：Ask 模式在“始终放行”下是否仍保持只读？**

决策：**Ask 模式保持只读**，仅 Agent 模式完全放行。

- 理由：Ask 模式只靠 Plan 阻止 + 只读工具目录两层保证只读；目录过滤只影响下发，不能完全防止历史上下文中的写工具调用。
- 实现：`AlwaysAllow` 跳过确认与 critical 硬闸；Plan 阻止（Ask 模式）仍然生效。
- 对应代码：`PermissionMode.Plan` 时不受 TrustMode 影响。

---

## 7. 验证计划

1. `dotnet build` 覆盖 Settings、Agent.Runtime、Panel.WebView、HostBridge.Server 工程；VSTO 工程用 VS/MSBuild 构建。**已完成**：整个解决方案 MSBuild 构建通过（退出码 0）。
2. 单元测试（**已完成**，相关套件共 60 例全部通过）：
   - 规范化：缺省、非法值、未知宿主键、大小写键（`ToolTrustModeSettingsTests`，7 例）。
   - 调度器：`need-confirm` 下弹确认一次；`always-allow` 下 Write 与 critical 工具不弹确认；Plan 阻止在两种模式下都保留（`ToolTrustModeTests`，5 例）。
   - 既有套件回归：`SkillPlanSupportTests`、`ExcelWriteEffectConfirmationTests`。
3. 手测（**待用户执行**）：Excel 中把 TrustMode 设为“始终放行”，写入数据不弹窗；切换到 Word，Word 仍为“提示确认”，互不影响。
4. 确认 `settings.json` 中三宿主键分别保存（**待用户执行**；持久化逻辑已由设置测试覆盖）。

---

## 8. 实施偏差与已知限制

实施过程中以下决策与第 4、5 节的初稿不完全一致，以本节为准：

| # | 初稿 | 实际实现 | 原因 |
|---|---|---|---|
| D1 | 宿主侧仅跳过“范围越界”强制确认 | `trustAlwaysAllow` 时跳过**整个**宿主确认闸 | 调度器在 AlwaysAllow 下不再发送 `confirmation-granted`，宿主若仍保留确认闸会二次弹窗，违背“一概不提醒” |
| D2 | 技能硬闸放行写在 `SkillPlanSupport` 中 | Broker 在每轮构建的 `RuntimeSkillPlan` 上置位 `AllowCodeExecution` | `SkillPlanSupport` 本身无状态，按轮次覆盖计划比改签名更局部 |
| D3 | 未明确 Plan 模式与 AlwaysAllow 的关系 | Plan 阻止始终保留（`IsTrustAlwaysAllowActive` 在 Plan 下返回 false） | 对应 §6 的用户决策：Ask 模式保持只读 |
| D4 | 未明确 EnterPlanMode 中途切换 | 放行判定在调用时检查当前模式，中途进入 Plan 立即生效 | 避免计划模式下的写操作因启动时模式不同而漏过 |

**已知限制**：

- **PowerShell Step 2 与宿主确认闸没有自动化测试**。PowerShell 测试会真正执行 shell，因此不写；宿主确认闸依赖 `DefaultHostBridgeRequestHandler` 的完整运行环境，单元测试成本高。两处的 AlwaysAllow 分支仅经过代码审查。
- **既有缺陷（范围外，未修复）**：Panel 与设置窗体各自缓存整份 `OfficeCopilotSettings` 并整体写回，最后写入者胜出，非 TrustMode 字段可能被覆盖。TrustMode 已通过 `RefreshToolTrustModes` 与 `SaveSettingsPreservingToolTrustModes` 单独保护，不受影响。
- **常量重复**：`"always-allow"` / `"need-confirm"` 在 `ToolTrustModeValues` 与 `ToolMetadataKeys` 中各有一份，修改时需同步。
- **`cbeTrustMode` 控件宽度**（80×20）对“始终放行”是否够宽，尚未在界面上目视确认。

