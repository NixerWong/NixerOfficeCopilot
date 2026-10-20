# Excel Copilot 行为契约（读写级别 · 执行方式 · 多表计算）实施计划

> 目标：让用户在发出指令前就能预判 Excel Copilot **会不会写、写到哪里、算哪些表**，并让执行结果可验证、可复现。
>
> 核心思想：**安全由运行时保证，提示词只负责告知；写入级别按每次调用的实际影响判定，而不是按工具或技能判定；无法确定执行方式时，先问用户，用户的选择就是硬上限。**

## 目录

1. [背景与现状诊断](#1-背景与现状诊断)
2. [设计原则](#2-设计原则)
3. [写入级别：定义与判定](#3-写入级别定义与判定)
4. [确认策略与确认摘要](#4-确认策略与确认摘要)
5. [执行预告：runtime.declare_execution_mode](#5-执行预告runtimedeclare_execution_mode)
6. [执行方式询问](#6-执行方式询问)
7. [VBA 专章](#7-vba-专章)
8. [工作簿地图](#8-工作簿地图)
9. [多表计算规范](#9-多表计算规范)
10. [技能与路由调整](#10-技能与路由调整)
11. [提示词与执行一致性](#11-提示词与执行一致性)
12. [工具元数据修正清单](#12-工具元数据修正清单)
13. [可测性与评测](#13-可测性与评测)
14. [分阶段实施计划](#14-分阶段实施计划)
15. [进度追踪](#15-进度追踪)
16. [风险与待定事项](#16-风险与待定事项)
17. [附录：已核实的代码实况与构建注意事项](#17-附录已核实的代码实况与构建注意事项)

---

## 1. 背景与现状诊断

当前 Agent 模式下"读还是写"由 6–7 层机制叠加决定，层与层之间互不一致。Ask 模式例外：它是硬只读的（`InteractiveModePolicy` 把 Ask 映射为 `PermissionMode.Plan`，并通过 `FilterToReadOnlyTools` 过滤工具目录）。

| # | 层 | 现状 | 导致的不可预测性 |
|---|---|---|---|
| D1 | 技能路由 | `SkillPlanningCoordinator.PlanAsync` 每轮按**用户原话**重新打分（`BrokerTurnExecutor.BuildSkillPlanAsync`，约第 1307 行），**不继承上一轮** | "好的，写进去"这类追问会被路由到完全不同的技能 |
| D2 | 打分方式 | `HybridSkillScorer` 意图判断用子串匹配（第 472–505 行）：`"表"` 几乎句句命中；`"set"` 会命中 `reset`；`"sum"` 会命中 `summary`；`"计算"` 被当成公式意图；read-analysis 总分为 0 时被强制设为 1 | 同一意图换个说法就会换技能 |
| D3 | 无技能漏洞 | `KeywordSkillPlanner.ShouldSkipSkillSelection`（第 539 行）：Keyword=0、Intent=0、Similarity≤1 时不选技能 → 没有白名单、没有计划、没有验证 | 短追问反而权限最宽 |
| D4 | 白名单拦截 | `SkillPlanSupport.GetSkillPlanViolationMessage`（第 220–261 行）让模型"调用 `runtime.invoke_skill` 后重试"，但 `SkillTool` 只返回文本，不替换 `CurrentSkillPlan` | 出路是假的，模型反复重试后放弃或绕路 |
| D5 | 写入催促 | `SkillPlanSupport.ShouldContinueForPendingMutation`（第 278 行）：路由误入写档时，会继续把模型推向写入 | 用户只想看看，结果被写了 |
| D6 | 技能名与能力不符 | read-analysis 持有 `excel_execute_vba`，且 `AllowCodeExecution=true`、`AllowHighRiskTools=true`；`_wip_` 只是免确认条件，不是限制 | "只读分析"实际上能写任何地方 |
| D7 | 伴生技能 | 伴生技能会合并工具，但放权标志只取主技能 | 权限组合不直观 |
| D8 | 确认门 | Broker 侧（`DefaultToolDispatcher` 约第 420–445 行）和宿主侧（`DefaultHostBridgeRequestHandler` 约第 639 行）各有一道；Broker 确认框只显示参数 JSON 前 200 字符（`TruncateArgumentsForDisplay`） | 用户在确认框里看不出这次写入会覆盖什么 |
| D9 | 工具元数据 | screenshot、validate_power_query 标为非只读；refresh_power_query 非只读却不需要确认；cleanup_workspace 需要确认 | 确认弹窗的出现时机看不出规律 |
| D10 | 提示词三层叠加 | `BuiltInHostPrompts.ExcelAgent`（中文）+ `SkillExecutionPromptBuilder`（英文、按白名单动态拼接，只读档写着"写工具故意不可用"）+ 工具描述 | 三层规则相互冲突，模型只能择其一 |
| D11 | 上下文 | `ExcelHostAdapter.GetContextSnapshot`（第 29 行）只有工作簿名、活动表、选区地址和 5×5 预览；`excel_list_worksheets` 只返回名称、序号、是否可见 | 模型不知道工作簿里有哪些表、哪些是数据源，多表任务只能靠猜 |
| D12 | 多表能力 | `excel_aggregate` 只支持单源，没有 union/join；只有 `excel_compare_key_sets`、`excel_reconcile` | 多表汇总只能写 VBA 或多次单表聚合再手工拼，口径不可控 |
| D13 | 评测 | `ExcelEvalRunner`（约第 276 行）用 `SkillId=task.Skill` 钉死技能；`tasks.json` 的提示词规定得极细 | 路由从未被评测；真实用户的模糊说法没有覆盖 |

**结论**：问题的根源不是某一层写错了，而是**安全语义分散在路由、白名单、技能标志、工具元数据、提示词里**。路由天然是概率性的，却承担了安全职责。本计划要把安全职责收拢到一个确定性的位置：**按调用判定的写入级别闸门**。

---

## 2. 设计原则

### P1 提示词与运行时分工

每条规则必须归入以下三类之一：

| 类别 | 例子 | 由谁保证 | 提示词的角色 |
|---|---|---|---|
| **硬约束** | 不得覆盖原始数据而不经确认；用户选定"只告诉我结果"后不得写正式表 | 运行时（闸门、宿主预检、锁表） | 只告知规则存在，不依赖模型遵守 |
| **策略偏好** | 优先用 `excel_aggregate` 而不是 VBA；结果默认写到新表；多表先画像再合并 | 提示词 + 工具设计（默认值、参数结构） | 主要载体 |
| **事实** | 工作簿里有哪些表、哪些表头相同、某区域是否为空 | 工具结果与上下文注入 | 不能写在提示词里"让模型去假设" |

### P2 工具的报错信息是最有效的提示词

被拦截时，报错必须说明：**拦截原因 + 实际判定级别 + 一条真实可走的出路**（例如"请调用 `runtime.ask_user_question` 让用户选择'直接改原表'"或"请改写到新工作表"）。禁止出现走不通的出路（参见 D4）。

### P3 可靠核心 + 可预期外观

- **核心（A）**：每次写入调用由宿主测量实际级别，按级别确认或拦截。可靠性不依赖模型。
- **外观（B）**：每轮第一次写入前由模型声明预告，面板显示"本轮将：生成到新表 `汇总`"。预告不拦截；实际超出预告时强制确认并标注"超出预告"。

### P4 用户的选择是硬上限，模型的推断只是预告

| 执行方式来源 | 效力 |
|---|---|
| 用户在询问卡片中选择 | **硬上限**：超出直接拦截，模型必须回来重新问 |
| 用户原话已明确（如"直接改原表"、"不要动原数据"） | 预告（由 A 兜底） |
| 模型推断 | 预告（由 A 兜底） |

> 说明：用户原话不当作硬上限，是因为从原话抽取意图本身就是概率性的；把概率性结果当成硬约束，会出现莫名其妙的拦截。

### P5 安全不依赖路由

技能只提供策略提示和推荐工具，**不再按白名单拦截**（Excel 范围内）。路由选错技能最多导致"方法不好"，不会导致"误写原数据"。

### P6 格式也是修改

不设 `modify-format` 级别。改动原始数据区域的格式（数字格式、颜色、列宽、条件格式等）一律算 modify。理由：级别过细会让用户无法理解，确认疲劳反而更危险。

---

## 3. 写入级别：定义与判定

### 3.1 四个级别

| 级别 | 含义 | 典型场景 |
|---|---|---|
| `none` | 不改变工作簿 | 读取、画像、聚合后只在对话里返回结果 |
| `scratch` | 只写**本会话登记过**的 `_wip_` 工作表 | 中间计算、验证用的草稿区 |
| `append` | 写入工作簿，但**不改变基线中已有的内容** | 新建结果表；在已有表的空白区域写结果；在新表写公式 |
| `modify` | 改变基线中已有的任何内容或结构 | 覆盖单元格、改原数据格式、排序、删除行列、写入表格对象内部或紧邻区域等 |

级别严格有序：`none < scratch < append < modify`。

### 3.2 工具只声明上限，实际级别按调用判定

- `ToolDefinition` 新增 `MaxWriteEffect`（取代 `IsReadOnly` 在 Excel 写入判断中的作用；`IsReadOnly` 保留用于兼容，并由 `MaxWriteEffect == none` 推导）。
- 每次调用写入类工具前，宿主执行**预检（preflight）**，得出本次调用的实际级别 `EffectiveWriteEffect ≤ MaxWriteEffect`。
- 判不出来时，按 `MaxWriteEffect` 处理（保守）。

### 3.3 基线（Baseline）

- **何时拍**：每轮用户消息开始时（对已打开的目标工作簿），复用工作簿地图（第 8 章）的元数据，不读取单元格值。
- **记录内容**（每张表）：名称、CodeName、UsedRange 地址、ListObject 名称与范围、透视表的源区域与输出区域、保护状态、是否可见。
- **生命周期**：
  - 本轮新建的正式表，在本轮内写入仍算 append；下一轮开始时它进入基线。
  - 本会话登记的 `_wip_` 表另记在**会话 WIP 登记表**里（第 3.6 节）；工作表元数据仍用于识别同名替换，但写入级别优先按登记表判为 scratch。
- **存储**：宿主内存中，以 `(工作簿 FullName, 轮次 Id)` 为键；只保留最近一轮。

### 3.4 单元格写入的判定算法

对写值、写公式、清除、设置格式这类"目标是一个区域"的调用：

```
target := 本次调用的目标区域（解析后的工作表 + 地址）
if target.Sheet 属于会话 WIP 登记表        → scratch
if target.Sheet 是本轮新建的正式表           → append
if target.Workbook 不是本轮的目标工作簿     → modify
if 命中任意结构性规则（3.5）               → modify
if target.Sheet 是契约声明的数据源          → modify   // 即使写入空白区域
inter := target ∩ Baseline[target.Sheet].UsedRange
if inter 为空                              → append
if CountA(inter) == 0                      → append
else                                       → modify
```

补充规则：

- `excel_create_worksheet`：创建新的正式表判为 append；创建名称符合 `_wip_NN_描述` 的新表，在同一次受控调用成功后登记为本会话 WIP，因此创建动作本身按 scratch 判定。
- **格式类调用**：目标区域与基线 UsedRange 有交集即算 modify（不看 CountA）。原因：空白单元格的格式也可能属于原数据区域的一部分（如整列数字格式）。
- **清除类调用**：交集非空且 CountA > 0 → modify；全空 → append（实际等于无操作，可以直接短路返回）。
- **预检结果附带影响摘要**：`覆盖非空单元格数 / 其中公式数 / 涉及的 ListObject / 透视表`，供确认框使用（第 4 章）。

### 3.5 结构性操作清单（一律 modify）

- 插入或删除行、列、单元格（会引起位移）
- 排序、筛选后删除、剪切或移动区域
- 写入 ListObject 内部，或紧邻 ListObject 的下一行或右一列（会触发自动扩展）
- 写入任何透视表的源区域或输出区域；刷新透视表
- 删除、重命名、移动、隐藏正式工作表
- 修改命名区域、数据验证、条件格式规则（作用于基线区域时）
- 刷新 Power Query 或数据连接（会覆盖查询输出表）
- 写入其他工作簿

### 3.6 `_wip_` 会话登记表

- 只有通过专用建表工具创建，或由 VBA 明确声明为 `outputSheet` 且执行前快照中不存在的表，才会尝试登记；宿主还会核对本轮基线、执行前工作表快照和 `_wip_NN_描述` 命名规范。
- 用户原本就有的 `_wip_xxx` 表不算 scratch，按正式表处理。
- 登记表按会话和工作簿 FullName 隔离，并通过工作表自定义属性中的 GUID 身份标记识别工作表，避免 `.xlsx` 工作表的 `CodeName` 为空或删除后重建同名工作表时误继承登记；旧登记仍兼容按 CodeName 匹配。身份标记重复时登记失效，不会猜测。登记表除宿主内存外，还原子持久化至 `%LOCALAPPDATA%\\NixerOfficeCopilot\\excel-wip-registry.json`。每次使用前都与当前工作簿核对，持久化读取/写入失败会显式阻止登记或返回错误，不会降级为已登记。
- 若新表已创建但登记失败，工具会明确返回失败；该表仍保留，不能当作 scratch 使用，也不能再次创建同名表，须先核实实际工作簿状态。
- 新建工作表后显式失效工作簿地图缓存，避免紧随其后的工作表清单回读到旧结果。
- 结构改造阶段可用 `excel_cleanup_workspace` 的 `dryRun=true` 模式验证新建 WIP 表登记；只有同一阶段创建的 WIP 表全部出现在 `matchedSheets` 且 `removedSheets` 为空时才算通过。该证据只验证表存在/已登记，不代替对单元格数据的断言；dry-run 不按写操作推进执行计划。
- `excel_cleanup_workspace` 只能删除登记表中仍存在且身份匹配的表，此时实际级别为 scratch，因此不需要确认（修正 D9）。

### 3.7 执行时复核（防止 TOCTOU）

预检与执行之间可能有用户编辑或其他调用。宿主在**真正执行前**（同一次 COM 调度内）对目标区域重算一次级别：

- 复核级别 ≤ 已确认级别 → 执行；
- 复核级别 > 已确认级别 → 拒绝执行，返回 `WRITE_EFFECT_ESCALATED`，附新的影响摘要，由模型重新发起调用（会触发新的确认）。

### 3.8 判定落点

| 位置 | 职责 |
|---|---|
| `OfficeCopilot.Host.Excel`（新增 `ExcelWriteEffectEvaluator`） | 计算实际级别与影响摘要；持有基线和 WIP 登记表 |
| HostBridge 新增请求 `PreflightToolCall` | Broker 在确认前调用，拿到级别与摘要 |
| `DefaultHostBridgeRequestHandler` | 执行前复核；替换现有的 `stagedWriteCandidate` 判断 |
| `DefaultToolDispatcher` | 根据预检级别 + 执行方式上限 + 权限模式决定：放行 / 确认 / 拦截 |

---

## 4. 确认策略与确认摘要

### 4.1 按级别确认

| 级别 | Default 模式 | AcceptEdits 模式 | BypassPermissions 模式 |
|---|---|---|---|
| none | 静默 | 静默 | 静默 |
| scratch | 静默 | 静默 | 静默 |
| append | **每轮、每张目标表**确认一次 | 静默 | 静默 |
| modify | **每次**强确认（展示影响摘要） | **每次**强确认 | 静默 |

- 超出预告（第 5 章）的写入：无论什么模式都强制确认，并在确认框里标注"超出预告"。
- 超出用户选定的执行方式（第 6 章）：直接拦截，不弹确认。
- Plan / Ask 模式：仍按现有逻辑过滤为只读工具目录；新增的级别闸门作为第二道保险（任何非 none 级别都拦截）。

### 4.2 与"按工具关闭确认"设置（F4）的关系

`ToolDefinitionOverride.RequiresConfirmation = false` **只对 append 级别生效**；modify 级别始终确认（BypassPermissions 模式除外）。设置界面上要补充一行说明："关闭确认只对不覆盖原数据的写入生效。"

### 4.3 确认摘要

替代现有的"参数 JSON 前 200 字符"（`TruncateArgumentsForDisplay`）。确认框统一展示：

```
[直接改原表] 销售明细!B2:F1200
将覆盖 5,995 个非空单元格（其中公式 1,198 个）
涉及表格对象：tblSales（会改变其数据）
已备份到：…\Backups\销售.xlsx.20250101-1030.bak
[超出预告：本轮预告为"生成到新表"]
```

- 摘要由宿主预检生成（`OperationSummary` 已存在，扩展字段即可），Broker 只负责渲染。
- 原始参数放进"详情"折叠区。

### 4.4 备份

维持现状：`ToolSafetyPolicy.Normalize` 对非只读工具自动设置 `RequiresBackup`；宿主在 `PrepareToolExecution` 中先备份，备份失败即阻断。优化：**只有实际级别为 modify 时才备份**（append 不改已有内容，scratch 写的是草稿表）。这会减少大工作簿的备份开销。

---

## 5. 执行预告：runtime.declare_execution_mode

### 5.1 工具定义

```json
{
  "name": "runtime.declare_execution_mode",
  "input": {
    "candidates": [
      {
        "mode": "result_only | new_sheet_static | new_sheet_formula | modify_in_place",
        "targets": ["汇总"],              // 计划写入的工作表或区域（可为新表名）
        "sources": ["1月", "2月", "3月"], // 本次计算的数据源
        "summary": "按地区汇总三个月销售额，结果写到新表“汇总”"
      }
    ],
    "reason": "可选：为什么有多个候选"
  }
}
```

执行方式与级别的映射：

| mode | 用户看到的名称 | 级别上限 |
|---|---|---|
| `result_only` | 只告诉我结果 | scratch |
| `new_sheet_static` | 生成到新表（静态值） | append |
| `new_sheet_formula` | 写成公式（会随源数据更新） | append |
| `modify_in_place` | 直接改原表（会先备份） | modify |

### 5.2 运行时行为

- **本轮第一次级别 > scratch 的写入之前必须调用**；未调用时，运行时拦截写入并提示"请先调用 `runtime.declare_execution_mode`"。
- `candidates` 只有 1 个 → 作为**预告**：面板显示，不弹窗。
- `candidates` ≥ 2 个 → 运行时**自动转成询问卡片**（第 6 章），用户的选择成为硬上限。
- 已有继承的执行方式（追问或"本会话都按这个方式"）时，可以不调用，直接沿用。
- 预告记录在 `TurnExecutionContract` 中（新类型，挂在 turn state 上），写入 trace。

### 5.3 面板呈现

聊天区在本轮回复的顶部显示一条执行方式条："本轮将：生成到新表「汇总」 · 数据源：1月、2月、3月"。实际写入一旦超出，这一条变成警示色并附上超出的调用。

---

## 6. 执行方式询问

### 6.1 什么时候问

由模型判断并通过 `declare_execution_mode` 提交多个候选。提示词给出明确的触发条件：

1. 用户没有说明结果放在哪里，并且"写成新表"和"直接改原表"都说得通（例如"把金额列改成含税价"）。
2. "静态值"和"公式"都合理，并且这个选择会影响后续使用（例如"汇总一下各部门数据"）。
3. 数据源有多个候选（来自工作簿地图），用户没有指定（第 8.5 节）。

以下情况**不问**：

- Ask 模式；
- 纯读取或分析问题（候选只有 `result_only`）；
- 追问继承了上一轮的执行方式；
- 用户已经选择"本会话都按这个方式"。

### 6.2 询问卡片

复用 `runtime.ask_user_question`（一张卡片最多 4 个问题，每题 2–4 个选项，选项可带说明和预览）。运行时根据 candidates 生成卡片：

```
问题 1：结果怎么给你？
  ○ 只告诉我结果        —— 不修改工作簿
  ○ 生成到新表（静态值）—— 新建「汇总」，原表不动
  ○ 写成公式            —— 新建「汇总」，随源数据自动更新
  ○ 直接改原表          —— 修改「销售明细」，执行前自动备份
  □ 本会话都按这个方式
```

- 选项**按产物表述**，不出现 append/modify 等术语。
- `declare_execution_mode` 可带经过校验的 `clarifications[]`，最多 2 个单选问题；例如"金额是否含税"、"按哪个日期字段统计"。它们与执行方式和是否记住方式合并到同一张卡片，答案随工具结果返回模型。
- 仅有一个执行方式候选但同时需要口径澄清时，也要显示交互卡，要求用户明确采用建议方式或取消写入；不能将模型的单候选预告当作用户选择。缺少必需口径答复、交互失败或取消时，本轮写入闸门保持关闭。
- 预览字段可以放候选的目标表名和前几行结果样例（如果模型已经在 scratch 中算好）。

### 6.3 选择后的效力

- 选择结果写入 `TurnExecutionContract.UserSelectedMode`，成为**硬上限**：本轮任何实际级别超过上限的调用都直接拦截，返回 `EXECUTION_MODE_EXCEEDED`，并提示"用户选择了 X，本次调用需要 Y；如确需更改，请再次询问用户"。
- **追问继承**：下一轮如果被判定为追问（第 10.3 节），沿用上一轮的执行方式；用户原话明确改变方式时（如"还是直接改原表吧"），以原话为准，转为预告，模型应重新声明。
- **本会话记住**：勾选后写入会话级偏好，后续所有轮次默认使用；面板提供"清除默认值"入口。清除仅移除会话默认偏好，已有关联追问仍可沿用上一轮执行方式。
- **超时与取消**：没有必需口径澄清的问题时，沿用交互超时策略并采用**最保守的执行方式候选**；如果用户取消了必须回答的口径澄清，则不继续本轮写入。

### 6.4 控制询问频率

- trace 记录每轮是否询问、询问原因；
- 评测中设置指标：在"指令已明确"的任务集上询问率 ≤ 5%；在"指令模糊"的任务集上询问率 ≥ 80%；
- 如果实际使用中询问过多，先调整提示词中的触发条件，不调整闸门。

---

## 7. VBA 专章

VBA 能做任何事，静态分析无法完全判定其影响，因此采用**声明 + 静态检查 + 物理锁表 + 执行后核对**四道措施。

### 7.1 声明

`excel_execute_vba` 新增参数：

```json
{
  "code": "...",
  "declaredWriteTargets": [
    { "sheet": "_wip_calc", "range": null },
    { "sheet": "汇总", "range": "A1:F200", "isNewSheet": true }
  ]
}
```

- 声明的目标按第 3.4 节的规则逐个预检，取最高级别作为本次调用的级别。
- 未声明 → 按 modify 处理。

### 7.2 静态检查

复用并扩展 `StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite` 的正则分析：

- 出现 `ActiveSheet`、`Selection`、`ActiveCell`、`Sheets(变量)`、`Worksheets(i)`、`Range("...")` 而没有限定工作表、`Delete`、`Sort`、`Insert`、`Workbooks.Open`、`SaveAs` 等 → 判定为"无法证明只写声明目标"，级别提升到 modify。
- 代码中引用的工作表名必须都在声明目标或数据源（只读）中。

### 7.3 物理锁表

宿主已有 `ExcelStagedWorksheetProtection`：执行 `_wip_` VBA 期间锁定所有非 `_wip_` 的正式表，并写恢复清单（崩溃后可恢复）。扩展方案：

- 新增 `ProtectWorksheetsExcept(workbook, allowedSheetNames)`：锁定**声明目标以外**的所有正式表（现有 `ProtectFormalWorksheets` 是其特例：允许列表 = WIP 登记表）。
- 新建表的目标不需要处理（执行前不存在）；VBA 新建的表在执行后核对中识别。
- **现有前置条件仍然适用**：
  - 工作簿必须已保存（恢复清单需要 FullName）；
  - 被锁的表必须所有单元格都处于锁定状态，否则保护挡不住写入（`GetStagedProtectionAvailabilityFailure`）。
- 前置条件不满足时：不能物理锁表 → 退回执行后核对（7.4），并且本次调用的级别至少为 append（需要确认），不能作为免确认的 scratch。

### 7.4 执行后核对

- **结构比对**（必做，成本低）：重新读取各表元数据，与基线对比：表的增删改名、UsedRange 变化、ListObject 范围变化。
- **值哈希**（可选，成本高）：只在静态检查无法证明安全、且物理锁表不可用时，对声明为"不触碰"的数据源表做执行前后的值哈希比对。上限：单表 20 万单元格，超出则只做结构比对并在结果中注明。
- 发现越界（实际影响超过声明，或超过已确认级别）：
  - 工具结果标记为失败，返回 `VBA_SCOPE_VIOLATION` 和具体差异；
  - 告知用户已有备份路径，提示可恢复；
  - 本轮后续写入一律强制确认。
- VBA 新建的 `_wip_` 表在核对后登记进会话 WIP 登记表。

---

## 8. 工作簿地图

### 8.1 内容

**每张工作表**：

| 字段 | 说明 |
|---|---|
| name / codeName / index / visible | 基本信息 |
| usedRange（地址、行数、列数） | 基线核心 |
| listObjects[]（名称、范围、是否有汇总行） | 结构性规则判定 |
| pivotTables[]（名称、输出区域、源区域） | 结构性规则判定 |
| headerSignature | 首个非空行的规范化表头（去空格、统一全半角），用于同构判定 |
| hasFormulas | 是否含公式（`SpecialCells(xlCellTypeFormulas)` 是否存在） |
| protection | 是否保护 |
| roleGuess | `data` / `summary` / `lookup` / `wip` / `other`（启发式：行数、表头、公式比例、表名） |

**工作簿级**：命名区域、Power Query 查询及输出表、是否有数据模型、是否只读、计算模式、是否已保存。

**跨表推断**：

- **表头相似候选组**（union 候选）：对表头先做 FormKC、首尾裁剪和连续空白折叠，再按不区分大小写的列名集合计算 Jaccard 相似度；组内每对工作表都必须达到 ≥ 80%。该推断不验证列顺序、数据类型或行语义，不能单独作为自动 union 的许可。
- **跨表共享列候选**（lookup 候选）：列名出现在至少两张可成功检查且有表头的工作表中。此阶段只比较列名、不读取值，也不推断唯一性；连接前仍须由画像/连接校验确认键唯一性及值域。
- 相似组按工作表索引顺序输出，组内工作表也按索引排序；共享列候选按列名排序，候选中的工作表名按不区分大小写的字母序排列。检查失败的工作表不参与推断。空表头及重复列名分别忽略和按集合折叠。

### 8.2 成本与限制

- 每张表 2–3 次 COM 调用（UsedRange、ListObjects、表头行一次性读取）；
- 上限 50 张表，超出时只列名称和 UsedRange，并注明"已截断"；
- 通过 `ExcelComDispatcher` 在 UI 线程执行，单次构建超时 2 秒，超时则返回部分结果。

### 8.3 缓存与失效

- 按工作簿缓存；
- VSTO 订阅 `SheetChange`（只把对应表标记为脏）、`NewSheet`、`SheetBeforeDelete`、`WorkbookOpen/BeforeClose`、`SheetActivate`（只更新活动表）事件；
- 每轮开始时只重建脏表，再生成基线。

### 8.4 注入方式

- `HostContextSnapshotDto` 新增 `WorkbookMap`（结构化）字段；`DefaultContextBuilder` 渲染为当前约 1–1.5k token 的摘要（表名、行列数、角色、表头相似候选组、共享列候选）。此为已实现的近似限制，按用户决定保留现状；不再做精确 token 预算、token 用量测算或以节省 token/成本为目的的优化。
- 完整版通过增强后的 `excel_list_worksheets`（新增 `detail: "summary" | "full"`）或新增 `excel_describe_workbook` 获取。

### 8.5 使用规则（写入提示词）

- 用户提到"选中的"、"这里"、"当前区域"时，才把选区作为数据源；否则按地图匹配数据源。
- 用户说"各月"、"所有分表"、"每个部门的表"时，用同构组确定 union 的源。
- 数据源有多个候选且无法确定时，在执行方式询问卡片中加一个"用哪些表"的多选问题。
- 声明执行方式时必须填写 `sources`；这些表在本轮被视为**契约数据源**，写入它们一律算 modify（第 3.4 节）。

---

## 9. 多表计算规范

### 9.1 契约扩展

`ExcelBasisPolicy` 中的口径声明新增：

```json
"basis": {
  "sources": [
    { "sheet": "1月", "range": "A1:F500", "alias": "m1" },
    { "sheet": "2月", "range": "A1:F480", "alias": "m2" }
  ],
  "topology": "single | union | lookup | compare",
  "keys": ["订单号"],
  "join": "left | inner",
  ...
}
```

### 9.2 拓扑与规则

| 拓扑 | 场景 | 必做步骤 | 禁止 |
|---|---|---|---|
| single | 单表 | 画像 → 计算 → 守恒验证 | — |
| union | 多张同构表纵向合并（各月、各部门） | 逐表画像 → 表头一致性检查（列名、类型；不一致必须报告并询问映射）→ 合并计算 | avg、去重计数、占比、TopN、中位数**不得**用各表子结果再合并；必须在合并后的数据上算 |
| lookup | 主表 + 维度表（按键查找） | 检查维度表键唯一性；声明 join 类型；报告未匹配行数 | 键不唯一时静默取第一条 |
| compare | 两表比对差异 | 使用 `excel_compare_key_sets` / `excel_reconcile` | 用 VBA 手写比对（除非工具不支持） |

**可加指标**（sum、count、非空计数、min、max）可以逐表计算再合并；**不可加指标**必须一次性在 union 后计算。

### 9.3 工具扩展

- `excel_aggregate`：
  - 新增 `sources[]`（替代单个 `range`；保留 `range` 兼容）。union 时自动添加伪列 `__source`（值为 alias），可用于分组。
  - 新增 `lookups[]`：`{ source, key, foreignKey, columns[], join, onDuplicateKey: "error" | "first" }`，默认 `error`。
  - 结果中逐源返回行数、被过滤行数、未匹配行数。
- `excel_assert`：union basis 的 scalar `source` 可按 `basis.sources` 对各来源分别执行同一计算后求和，用于与 target 结果做守恒比较。仅支持 `sum`、`count`、`counta`、`rowCount`、`nonBlank`、`errorCount` 等可加指标；`average`、`countDistinct`、`min/max` 等不可加指标不得拆源后合并。来源级筛选使用相同过滤口径；绝对行号和小计标签排除不跨源套用。
- `excel_profile_range`：`sources[]` 每项显式指定唯一 `alias` 与区域/表；使用多源时禁止同时传顶层单源选择器。逐源返回列画像、样例、覆盖和数据质量信息，并返回 `headerConsistency` 的列名集合、顺序、推断类型与表头推断模式是否一致，以及逐项差异。只报告不一致，不自动猜测列映射；发现差异时先询问或要求用户确认。

契约 DTO 位于 `OfficeCopilot.Host.Abstractions/Excel/ExcelAggregationContracts.cs`（现有单源请求约第 329 行）。

### 9.4 守恒

- 每个源各自守恒：输入行数 = 参与行数 + 被过滤行数（并报告过滤原因）；
- 总量守恒：结果的可加指标合计 = 各源合计之和；
- lookup：主表行数守恒（left join）或报告丢失行数（inner join）。

### 9.5 落点规则

- 结果默认写到**新表**（append），绝不写回数据源表，除非执行方式为 `modify_in_place`。
- 新表命名：用户指定，否则用 `汇总_<主题>`；与已有表重名时追加序号，不覆盖。

---

## 10. 技能与路由调整

### 10.1 移除 Excel 白名单拦截

- `SkillPlanSupport.GetSkillPlanViolationMessage` 对 Excel 工具不再拦截；技能的 `AllowedTools` 改作**推荐工具**，在提示词中排在前面。
- `AllowCodeExecution`、`AllowHighRiskTools` 对 Excel 不再作为放权依据（安全由写入级别闸门负责）。
- 保留 Word/PowerPoint 现有行为，不在本计划范围内。

### 10.2 技能重新定位

四档技能（read-analysis / live-result / structural-transform / bulk-format）改为**策略包**：每个包包含方法论提示、推荐工具、验证要求，不再包含权限。read-analysis 的提示词中删除"写工具故意不可用"之类与事实不符的表述。

### 10.3 追问继承

- 新增追问判定：消息很短（≤ 15 字）且不含新的数据对象，或包含"好的/继续/写进去/就这样/改成…"等延续词 → 判为追问。
- 追问时：继承上一轮的技能计划和执行方式（`TurnExecutionContract`），不重新打分。
- 判定逻辑放在 `SkillPlanningCoordinator`，上一轮的契约从会话状态中读取。

### 10.4 关闭无技能漏洞

`ShouldSkipSkillSelection` 为真且宿主为 Excel 时：如果是追问则走继承；否则回落到默认策略包（read-analysis）。由于白名单已经移除，回落不会限制能力，只确保有计划和验证。

### 10.5 `runtime.invoke_skill`

改为真正替换 `CurrentSkillPlan`（重新生成策略提示），返回新策略包的摘要。如果短期内来不及，至少删除"调用 invoke_skill 后重试"这一误导文案。

### 10.6 写入催促

`ShouldContinueForPendingMutation` 只在执行方式（预告或用户选择）≥ append 时触发；执行方式为 `result_only` 或尚未声明时不催促写入。

### 10.7 路由打分

- 短期：`HybridSkillScorer` 意图匹配从子串改为整词或分词匹配（英文按词边界，中文使用词表，排除"表"这类泛词）。
- 长期（S3）：结构化意图分类：由模型输出 `{ intent, objects, outputPreference }`，路由只决定策略包，执行方式由第 5–6 章决定。

---

## 11. 提示词与执行一致性

### 11.1 规则归类表（需在实施时逐条补全）

| 现有规则（来源） | 类别 | 是否已由运行时执行 | 调整 |
|---|---|---|---|
| 中间结果写 `_wip_`（ExcelAgent） | 策略 | 是（仅本会话登记的 WIP 才按 scratch 判定） | 保留为策略；安全由级别闸门负责，名称本身不构成豁免 |
| 只读档"写工具故意不可用"（SkillExecutionPromptBuilder） | 硬约束（伪） | 否（工具可见性由技能推荐与写入闸门分离） | **删除** |
| 覆盖原数据前确认（ExcelAgent） | 硬约束 | 部分（按工具确认） | 改为由级别闸门执行；提示词改为告知 |
| 路径四问 Q1–Q4（ExcelAgent） | 策略 | 否 | 保留；Q 中涉及"结果放哪"的部分改为引用执行方式声明 |
| 口径声明（ExcelBasisPolicy） | 策略 + 事实 | 否 | 保留；新增 sources/topology |
| 数据质量检查（ExcelDataQualityPolicy） | 策略 | 否 | 保留 |
| 验证换引擎（ExcelAgent） | 策略 | 部分（pending verification 催促） | 保留 |
| 公式兼容性（ExcelAgent） | 事实 + 策略 | 否 | 保留 |
| 白名单之外的工具不可用（违规报错） | 硬约束 | 是 | **移除**（Excel） |
| 执行方式上限 | 硬约束 | 新增 | 运行时执行；提示词只告知 |

### 11.2 提示词职责收敛

- `BuiltInHostPrompts.ExcelAgent` 作为 Excel 通用方法论的主要规则来源（中文）。
- `SkillExecutionPromptBuilder` 输出当前策略包/步骤、工具推荐和与任务相关的方法论；不得把推荐清单或技能标志描述成 Excel 权限。执行方式及用户选择上限只陈述运行时契约，不得声称某 Excel 工具因技能档位而不可用。
- 全局执行方式硬约束由 `DefaultContextBuilder` 注入；Excel 写入权限由写入级别闸门执行。
- 工具描述只写该工具做什么、参数含义，不写全局规则。

### 11.3 报错信息规范

所有拦截类报错使用统一格式：

```
[代码] EXECUTION_MODE_EXCEEDED
[原因] 本次调用将覆盖「销售明细」B2:F1200 中 5,995 个非空单元格（modify）
[约束] 用户本轮选择了"生成到新表"（上限 append）
[出路] 改为写入新工作表；或调用 runtime.ask_user_question 询问用户是否改为"直接改原表"
```

单元测试中断言：每种拦截报错都包含 `[出路]`，且出路中提到的工具在当前目录中存在。

---

## 12. 工具元数据修正清单

| 工具 | 现状 | 修正为 |
|---|---|---|
| `excel_capture_worksheet_screenshot` | IsReadOnly=false | MaxWriteEffect=none（截图不改工作簿；如实现中临时改了视图，需要在工具内恢复） |
| `excel_validate_power_query` | IsReadOnly=false | none（如果实现会创建临时查询，则为 scratch，并在工具内清理） |
| `excel_refresh_power_query` | IsReadOnly=false，RequiresConfirmation=false | MaxWriteEffect=modify；预检：输出表在基线中 → modify |
| `excel_cleanup_workspace` | 需要确认 | 只允许删除 WIP 登记表中的表 → scratch，免确认 |
| `excel_write_range` / `excel_set_range_formulas` | 非只读 | MaxWriteEffect=modify，按第 3.4 节预检 |
| `excel_set_range_format` | 非只读 | MaxWriteEffect=modify，格式规则：与基线 UsedRange 有交集即 modify |
| `excel_clear_range` | 非只读 | MaxWriteEffect=modify，按第 3.4 节预检 |
| `excel_execute_vba` | 高风险 | MaxWriteEffect=modify，按第 7 章处理 |
| `excel_create_worksheet`（或等价工具） | 非只读 | MaxWriteEffect=append；`_wip_` 名称 → scratch 并登记 |

实施时需要遍历 `OfficeCopilot.Host.Excel/Tools/**` 的所有工具，补全这张表，避免遗漏。

---

## 13. 可测性与评测

### 13.1 路由与契约测试表

- 新增数据驱动测试：`docs/excel-eval/routing-cases.json`，≥ 50 条，字段：

```json
{
  "id": "R-017",
  "history": ["汇总一下各月销售"],
  "prompt": "好的，写进去",
  "expectedStrategy": "live-result",
  "expectedFollowUp": true,
  "expectedModeCandidates": ["new_sheet_static", "new_sheet_formula"],
  "mustAsk": false
}
```

- 覆盖：纯读取、明确写新表、明确改原表、模糊写入、追问、包含"表/set/sum/计算"等陷阱词、多表、选区相关。

### 13.2 写入级别单元测试

针对 `ExcelWriteEffectEvaluator`（在宿主外实现纯逻辑部分，输入为基线元数据 + 目标区域 + CountA 结果）：

- 空白区域 / 交集为空 / 交集非空但全空 / 交集有值；
- 格式规则；ListObject 紧邻；透视表源；契约数据源；本轮新建表；用户自有 `_wip_` 表；
- 执行时复核升级。

### 13.3 多表评测夹具

- 同构月表 × 3 单独用于成功 union；另用独立工作簿放置列顺序不同、额外列的来源，避免把按契约必须拒绝 union 的输入混入成功任务。
- 订单与客户夹具分别覆盖 left lookup 未匹配行、重复键默认拒绝；既有订单/客户键集合任务覆盖两侧差异。
- 任务覆盖 union 总和、合并后一次求平均、画像表头差异、lookup 未匹配及重复键拒绝。评测夹具/任务结构校验不等同于真实模型通过率验收。

#### 13.3.1 真实 Excel 多表只读验收用例

使用隔离的评测夹具，不要对用户工作簿执行这些用例。若夹具尚未生成，从仓库根目录运行：

```powershell
.\OfficeCopilot.OpenXml.SmokeTest\bin\Debug\net472\OfficeCopilot.OpenXml.Word.SmokeTest.exe `
  --export-excel-eval-fixtures .\TestResults\ExcelEval-Contract-Validation
```

分别打开 `TestResults\ExcelEval-Contract-Validation` 下的指定 `.xlsx` 文件；每个用例结束确认工作簿未被修改。导出器不会覆盖已有文件；如需刷新夹具，应选用新的隔离输出目录，并同步更新任务集的 `fixtureDirectory`。

| 用例 | 夹具 | 可复制提示词 | 预期结果 |
|---|---|---|---|
| Union 总和 | `monthly_union.xlsx` | 把 `Jan!A1:B4`、`Feb!A1:B4`、`Mar!A1:B4` 作为三个明确来源，用 `excel_aggregate` 的 `sources[]` 按同一列结构纵向 union，`headerMode` 设为 `yes`，对 `Revenue` 求一次总和。只报告合并后的总销售额；不要逐月分别汇总后手工相加，也不要写回工作簿。 | 总销售额 `790`；工作簿不变。 |
| Union 平均值 | `monthly_union.xlsx` | 把 `Jan!A1:B4`、`Feb!A1:B4`、`Mar!A1:B4` 作为三个明确来源，用 `excel_aggregate` 的 `sources[]` 按同一列结构纵向 union，`headerMode` 设为 `yes`，对合并后的全部 `Revenue` 明细行计算一次 `average`。只报告总明细的平均销售额；不要先求每月平均再对三个平均值做无权平均，也不要写回工作簿。 | 平均销售额约 `87.7778`；工作簿不变。 |
| 表头结构差异 | `monthly_schema_mismatch.xlsx` | 用一次 `excel_profile_range` 的 `sources[]` 检查 `Jan!A1:B3`、`Feb!A1:B3`、`Mar!A1:C3`，`headerMode` 设为 `yes`。说明哪些来源的列顺序不同、哪张表多出什么列；不要猜测或静默映射列，也不要尝试合并结构不一致的来源，不要写回工作簿。 | 指出 `Feb` 列顺序不同、`Mar` 多出 `Channel`；工作簿不变。 |
| Left lookup 未匹配 | `orders_lookup.xlsx` | 以 `Orders!A1:C4` 为主表，用 `excel_aggregate` 的 `lookups[]` 左连接 `Customers!A1:B3`，`CustomerID` 对 `CustomerID`，取 `Name` 字段，`headerMode` 设为 `yes`，汇总 `Amount` 总额。报告总额和工具返回的未匹配主表行数；不要丢弃未匹配订单，也不要从数据行手工推断未匹配数，不要写回工作簿。 | 总额 `350`，未匹配主表行数 `1`；工作簿不变。 |
| 重复键拒绝 | `orders_lookup.xlsx` | 尝试以 `Orders!A1:C4` 为主表，左连接 `CustomersDuplicate!A1:B4`，`CustomerID` 对 `CustomerID` 并取 `Name` 字段。省略 `onDuplicateKey`，使用默认策略；如果工具报告维表键重复，应停止计算并说明重复键，不要改为取首条、去重或静默忽略冲突，也不要写回工作簿。 | 明确拒绝计算并报告重复键；不得生成看似成功的 lookup 结果，工作簿不变。 |

这组用例复核多表来源、union/lookup 口径、结构不兼容和重复键保护。union/lookup 若只生成对话中的虚拟结果、没有可供独立断言的实际目标范围，当前可信档位为 C 是已知验证边界，不等同于答案计算错误，也不能据此宣称已完成独立复核。若要验收提升后的可信档位，需另行实现并运行多源虚拟结果的独立复核；不得通过在用户工作簿创建临时结果来伪造只读验收。2026-10-08 用户确认上述五条用例均已在真实 Excel 手测，行为和结果符合预期。

#### 13.3.2 多源断言真实 Excel 集成验收

此用例会写入一个登记的 `_wip_` 结果表，只能在 `monthly_union.xlsx` 的一次性副本上执行，不得打开或修改原始评测夹具/用户工作簿。测试目标是验证 `excel_assert` 能以 Jan/Feb/Mar 三个 union 来源，在 Excel COM 环境中独立核对实际目标范围，并识别错误值后通过修正恢复。

**可复制提示词：**

> 只在当前评测工作簿副本中操作，不得改动 `Jan!A1:B4`、`Feb!A1:B4`、`Mar!A1:B4`。先用 `excel_aggregate` 对这三个明确来源做纵向 union，并对 `Revenue` 求和。新建并登记 `_wip_01_union_assert`，只在 A1:A2 写入表头 `Revenue` 和一个用于测试的错误值 `791`。随后调用 `excel_assert`：basis 必须声明 `topology=union`、`headerMode=yes` 和三个来源；断言类型为 scalar；source 对 `Revenue` 求 sum；target 指向 `_wip_01_union_assert!A1:A2` 的 `Revenue` sum；tolerance 为 0.01。第一次断言应失败，差异为 1。然后只将 `_wip_01_union_assert!A2` 修正为聚合得到的正确值 `790`，用完全相同的 basis 和断言再次验证，预期通过。不得更改来源表、不得跳过第一次失败或把失败报告成通过。最后回读三个来源范围和 `_wip_01_union_assert!A1:A2`，报告两次断言结果、实际验证后端/证据、可信档位及工作表清单；在我确认记录结果前不要清理测试 WIP 表。

**验收点：**第一次断言明确失败、第二次断言明确通过；通过回执的检查对象包含三个来源与目标范围；不对源表产生写入；结果表是唯一新增表。若 union `excel_assert` schema 不接受该形状、未能使用独立 Excel COM 复核，或首轮错误值被判通过，应停止后续修改并记录失败，不得宣称 S2-5 集成验收通过。完成并记录后，可另行使用 `excel_cleanup_workspace` 清理本会话登记的测试 WIP 表。

**用户实测记录：**union 结果为 790；第一次用 791 断言，COM 独立复核发现差值 1 并判失败；只将目标修正为 790 后，完全相同的 union basis / scalar sum 断言由 COM 判通过。两次断言均为 A 档，写入回执为 `scratch` 且成功；Jan/Feb/Mar 源范围回读未变，`_wip_01_union_assert!A1:A2` 为 Revenue / 790，测试 WIP 按要求保留。第二次实测的最终页脚为 **A 档：全量独立复核通过**，覆盖 1 项通过断言、10 行；检查对象包含工作表清单、三个来源范围和 WIP 目标范围。前置只读 `excel_aggregate` 自身仍是 C 档（无持久结果范围可供独立复核），这是正确保留的局部状态；最终回合 A 来自之后匹配的独立 COM 断言，不是把聚合工具自身的 C 改写成通过。第一次 A 档失败表示高置信度检出错误值，并不代表该次断言通过。`cellCountsAvailable=false` 是为避免额外逐格读取而未采集分类计数，不影响本次完整行覆盖证据。

账本现仅允许完整、未验证、单指标且无筛选/分组/lookup 的 union 聚合，被之后独立、非抽样且来源范围、alias、headerMode、指标及函数完全匹配的 scalar `excel_assert` 覆盖；已通过的 inline 聚合验证及口径不匹配的后续断言不会被覆盖。`VerificationLedgerTests` 与 `TrustFooterBuilderTests` 定向套件 **58/58 通过**；重建后的 Excel add-in/Broker 真实复测确认最终页脚为 A，S2-5 端到端验收完成。`_wip_01_union_assert` 尚未清理。

#### 13.3.3 多源只读 union 独立复核

此用例只读，不创建工作表、不写入结果、不清理现有 `_wip_01_union_assert`。使用 `monthly_union.xlsx` 的评测副本，对明确来源 `Jan!A1:B4`、`Feb!A1:B4`、`Mar!A1:B4` 执行一次纵向 union，`headerMode=yes`，只计算单一 `Revenue sum`；不加筛选、分组或 lookup。

**可复制提示词：**

> 只读分析当前评测工作簿，不得创建、删除或写入任何工作表，也不得修改任何单元格。将 `Jan!A1:B4`、`Feb!A1:B4`、`Mar!A1:B4` 作为明确来源，以 `headerMode=yes` 纵向 union，对 `Revenue` 做一次全局 sum；不要逐月先算再手工相加，不要添加筛选、分组或 lookup。报告总和，并报告工具验证回执的 tier、passed、producer/backend、independent、sampled、来源覆盖行数，以及本次是否发生写入。不得调用 `excel_assert`、不得创建临时目标表；若 COM 独立复核无法完整执行，保留实际 C 档及原因，不要补造验证。

**验收点：**总和为 `790`；完整 union 结果经 COM 对三个来源重新计算，`verification.tier=A`、`passed=true`、`independent=true`、`sampled=false`，source coverage 为 `9/9` 行。`cellCountsAvailable=false` 可接受，不要求额外逐格采集分类数。工作簿工作表清单和来源区域不变，已有 `_wip_01_union_assert` 仍保留。输出数值正确但回执仍为 C、COM 未实际执行、来源覆盖不完整或出现任意写入时，均不算本用例通过。

**实现状态：**首版只开放无筛选/分组/lookup、单一 `sum` 的 union；复用 `VerifyAggregateResult` 与 `ExcelAggregateVerificationService`，使用 COM 对已解析的全部来源范围独立重算。其它函数、来源形状和证据不足时仍保留 C。相关回归 **138/138 通过**，解决方案、Excel add-in 与 Broker 已重建。

**用户实测记录（2026-10-08）：**Revenue union sum 得到 790；自动验证回执 A、passed、COM、independent、非抽样，完整扫描 Jan/Feb/Mar 共 9 行。COM 独立重算 790，单值断言和 `aggregate_scalar_shape` 两项均通过，diffCount=0。整个回合未写入、未创建或删除工作表；已有 `_wip_01_union_assert` 按用户选择保留。`cellCountsAvailable=false` 符合预期；画像提示 `staleCalculation`，但来源为静态数值、无公式，不影响本次计算。**尚未覆盖的边界：**此数据集文本数字和错误值均为 0，因此本次没有实测 COM 与托管 sum 在文本数字/错误值分类上的一致性。

### 13.4 评测运行器调整

- `ExcelEvalRunner` 增加"不钉死技能"模式（`SkillId` 为空时走真实路由）；至少 30% 的任务在此模式下运行。
- 新增一组"模糊指令"任务，提示词按真实用户的写法（短、口语化）。

### 13.5 Trace 指标

每轮记录：策略包、是否追问继承、执行方式（来源：用户选择 / 原话 / 推断 / 继承）、是否询问、每次写入调用的预检级别与复核级别、是否超出预告、拦截代码。

评测汇总指标：

- 只读回合意外改写率（须同时报告完成工作簿不变核验的覆盖率）—— 目标 0；
- 询问率（明确任务 ≤ 5%，真实歧义任务 ≥ 80%）；口语化路由任务单独报告，不计入真实歧义 cohort；
- 模型预告超出率与用户选择执行契约越界尝试率，按有效写入预检调用计数；这两项反映被检测到的预检/拦截，不代表不合规写入已执行；
- 多表任务守恒通过率。

当前尚未实现所有写入回合的“已提交改动超过用户期望”误写率；`write_effect.evaluated` 记录的是执行契约上限与宿主预检判定，不能单独证明写入已提交。当前 Eval 报告将该项显式显示为 N/A，直到任务逐项声明可接受的变更范围并能对最终工作簿改动作范围核对。

---

## 14. 分阶段实施计划

依赖关系：

```
S0 快速修复 ──────────────┐
M  工作簿地图与基线 ──────┼─▶ S1 写入级别闸门 + 执行方式 ──▶ S2 多表 ──▶ S3 路由重做与评测
                          │
（S0 与 M 可并行）         ┘
```

### S0 快速修复（低风险，立即可做）

| 编号 | 内容 | 涉及文件 | 验收标准 |
|---|---|---|---|
| S0-1 | 修正工具元数据（第 12 章中不依赖级别闸门的部分：screenshot、validate_power_query 改为只读；refresh_power_query 改为需要确认） | `OfficeCopilot.Host.Excel/Tools/**` | 只读档调用截图不触发确认；刷新 PQ 触发确认 |
| S0-2 | 删除或修正 `invoke_skill` 误导文案 | `SkillPlanSupport.cs`（约第 235 行） | 拦截报错中不再出现"调用 invoke_skill 后重试" |
| S0-3 | 追问继承 + 关闭无技能漏洞 | `SkillPlanningCoordinator.cs`、`KeywordSkillPlanner.cs`（第 539 行）、`BrokerTurnExecutor.cs`（第 1307 行） | "好的，写进去"沿用上一轮技能；Excel 下不再出现无技能计划 |
| S0-4 | 确认框展示宿主的 OperationSummary，参数 JSON 折叠 | `DefaultToolDispatcher.cs`（约第 752、825 行） | 确认框首行为人类可读摘要 |
| S0-5 | 意图匹配改为整词匹配 | `HybridSkillScorer.cs`（第 472–505 行） | `reset`、`summary` 不再命中 set/sum；"表"不再单独触发表格意图 |
| S0-6 | 路由测试表首版（≥ 30 条）+ 测试 | 新增 `docs/excel-eval/routing-cases.json`、测试工程 | 测试通过；作为后续改动的回归基准 |

### M 工作簿地图与基线

| 编号 | 内容 | 涉及文件 | 验收标准 |
|---|---|---|---|
| M-1 | 地图 DTO | `HostContracts.cs`、`HostContextSnapshotDto.cs`、`ExcelWorksheetContracts.cs` | 序列化往返测试 |
| M-2 | 地图构建（含跨表推断） | `ExcelHostAdapter.cs`、`ExcelWorkbookMapInference.cs` | 50 张表的工作簿构建 < 2 秒；表头相似组与共享列候选推断正确 |
| M-3 | 缓存与事件失效 | Excel 加载项（ThisAddIn 事件订阅）、新增 `ExcelWorkbookMapCache` | 修改单元格只重建对应表 |
| M-4 | 上下文注入（保留现有近似摘要限制） | `DefaultContextBuilder.cs`（约第 260 行） | 快照中可见表清单与同构组；不要求精确 token 计数或成本优化 |
| M-5 | `excel_list_worksheets` 增加 detail 参数 / 新增 `excel_describe_workbook` | `Tools/` | 返回完整地图 |
| M-6 | 每轮基线快照 + 会话 WIP 登记表 | 新增 `ExcelTurnBaseline`、`ExcelWipRegistry` | 本轮新建表在下一轮进入基线；用户自有 `_wip_` 表不在登记表中 |

### S1 写入级别闸门 + 执行方式（核心）

| 编号 | 内容 | 涉及文件 | 验收标准 |
|---|---|---|---|
| S1-1 | `MaxWriteEffect` 元数据 + 推导 IsReadOnly | `ToolDefinition.cs`、`ToolSafetyPolicy.cs`、所有 Excel 工具 | 所有 Excel 工具都声明了上限 |
| S1-2 | `ExcelWriteEffectEvaluator`（判定算法 + 影响摘要） | 新增于 `OfficeCopilot.Host.Excel` | 第 13.2 节单元测试全部通过 |
| S1-3 | HostBridge `PreflightToolCall` 请求 + 执行时复核 | `HostBridge.Contracts`、`DefaultHostBridgeRequestHandler.cs` | 预检后修改目标区域，执行返回 `WRITE_EFFECT_ESCALATED` |
| S1-4 | 统一确认门（按级别，第 4.1 节；F4 只对 append 生效） | `DefaultToolDispatcher.cs`、`DefaultHostBridgeRequestHandler.cs`、`StagedWriteConfirmationPolicy.cs` | 两侧不再各自判断；确认矩阵测试通过 |
| S1-5 | 只在 modify 时备份 | `DefaultHostBridgeRequestHandler.cs` | append 写入不产生备份文件 |
| S1-6 | `runtime.declare_execution_mode` + `TurnExecutionContract` + 面板执行方式条 | `DefaultToolDispatcher.cs`、turn state、UI | 未声明时写入被拦截并给出出路；预告显示在面板上 |
| S1-7 | 多候选/口径澄清 → 询问卡片；用户选择 = 硬上限；追问继承；本会话记住并可清除；超时保守处理 | `DefaultToolDispatcher.cs`、会话状态、Panel | 选"生成到新表"后覆盖原表的调用被拦截；口径答复返回模型，取消必需澄清会阻止本轮写入 |
| S1-8 | 超出预告 → 强制确认并标注 | `DefaultToolDispatcher.cs` | AcceptEdits 下超出预告仍弹确认 |
| S1-9 | VBA：声明目标 + 静态检查 + `ProtectWorksheetsExcept` + 执行后核对 | `excel_execute_vba` 工具、`StagedWriteConfirmationPolicy.cs`、`ExcelStagedWorksheetProtection.cs` | 声明外写入被保护阻止或被核对发现并报 `VBA_SCOPE_VIOLATION` |
| S1-10 | 移除 Excel 白名单拦截；技能改为策略包；写入催促按执行方式触发 | `SkillPlanSupport.cs`、`OfficeSkillCatalog.cs`、`SkillExecutionPromptBuilder.cs` | read-analysis 下调用写入工具不被白名单拦截，而由级别闸门处理 |
| S1-11 | 提示词收敛（第 11 章） | `BuiltInHostPrompts.cs`、`SkillExecutionPromptBuilder.cs` | 规则归类表全部落地；报错格式测试通过 |
| S1-12 | `_wip_` 登记表接入创建、cleanup 与 scratch 判定 | `excel_create_worksheet`、`excel_execute_vba`、`excel_cleanup_workspace` | 用户自有或未登记的 `_wip_` 表不会进入清理目标；WIP 创建/清理按 scratch 处理 |
| S1-13 | 精确单格文本写入回读纳入验证账本 | `VerificationLedger.cs` | 同一工作簿/工作表/单元格的完整 1×1 回读值匹配时为 B 级；不匹配、失败或截断仍为 C 级，且回读绑定具体写入产出项 |
| S1-14 | 单一运行时验证状态及检查对象归属 | `VerificationLedger.cs`、`TrustFooterBuilder.cs`、`BrokerTurnExecutor.cs`、Panel | 模型自拟档位不与运行时回执并列；成功只读检查不误报“无产出项”C；多对象显示各自工作表/范围及关联步骤 |

### S2 多表

| 编号 | 内容 | 涉及文件 | 验收标准 |
|---|---|---|---|
| S2-1 | `basis.sources/topology/keys/join` 契约 | `BuiltInHostPrompts.cs`（ExcelBasisPolicy） | 多表任务的口径声明包含 sources |
| S2-2 | `excel_aggregate.sources[]` + `__source` | `ExcelAggregationContracts.cs`、聚合实现 | union 求和与逐表求和一致 |
| S2-3 | `excel_aggregate.lookups[]` + 键唯一性检查 | 同上 | 重复键默认报错；报告未匹配行数 |
| S2-4 | `excel_profile_range` 多区域 + 表头一致性报告 | 画像工具 | 列顺序不同、多列的情况被报告 |
| S2-5 | `excel_assert` 多源守恒 | 断言工具 | 总量守恒断言可用 |
| S2-6 | 多表评测夹具与任务 | `docs/excel-eval/` | 第 13.3 节任务通过率 ≥ 90% |

### S3 路由重做与评测

| 编号 | 内容 | 验收标准 |
|---|---|---|
| S3-1 | 结构化意图分类替代关键词打分 | 路由测试表 ≥ 50 条，准确率 ≥ 95% |
| S3-2 | 评测运行器"不钉死技能"模式 + 模糊指令任务集 | ≥ 30% 的任务走真实路由 |
| S3-3 | Trace 指标与评测汇总报表 | 误写率、询问率、超出预告率可在报告中看到 |

---

## 15. 进度追踪

| 编号 | 状态 | 负责人 | 备注 |
|---|---|---|---|
| S0-1 | 完成 | | 修正截图、Power Query 校验和刷新元数据；校验清理临时查询失败时显式返回错误。 |
| S0-2 | 完成 | | 删除 `runtime.invoke_skill` 可更换当前权限的误导说法。 |
| S0-3 | 完成 | | Excel 请求未显式指定技能时会建立适用的技能计划；追问按会话元数据继承上轮技能及执行方式。2026-10-08 真实 Excel 手测：首轮计算 `Orders!A1:C3001` 的 Amount 总和并准备新表写入，未显式指定技能，系统选择 `structural-transform` 并以 COM 全量复核得到 450315（A，覆盖 3000 行）；用户回复“好的，写进去”后 Broker 明确继续上一轮计划，成功声明 `new_sheet_static`、按 append 新建并写入 `S0_FollowUp_Result!A1:B2`，回读值与清单均符合预期，源表未改动。末尾额外生成的 `excel_assert` 因 target 使用单格 `B2` 却继承 source measure 列名 `Amount` 而失败；该断言形状错误导致最终回合 C，不是数据差异，也不改变本项跨轮计划继承验收结论。最终可信页脚仍如实保留 C；未将失败断言记作通过或 A 档。 |
| S0-4 | 完成 | | Broker 确认摘要置顶显示；完整参数 JSON 折叠查看，仍保留短参数摘要兼容路径；增加长参数透传测试。 |
| S0-5 | 完成 | | 英文意图词按词边界匹配，移除单字“表”的泛命中；已有回归测试。 |
| S0-6 | 完成 | | Planning 测试含 64 条中英文数据驱动路由用例，64/64 通过；用例与分类器测试同处于测试工程，未另建 JSON 路由文件。 |
| M-1 | 完成 | | 当前工作簿地图 DTO 已包含工作表名/CodeName、UsedRange、表头列、相似组、共享列候选和检查错误；结构化地图经 `HostContextSnapshot` 与 HostBridge 传到 Agent。摘要缺失时 `DefaultContextBuilder` 与 `runtime.get_host_context` 可从 DTO 渲染地图。DataContract/System.Text.Json 往返、HostBridge 传输和提示词渲染测试通过。2026-10-08 用户决定只验收并收敛当前地图能力，不扩展 ListObjects、PivotTables 或工作簿级属性；这些列为未来可选范围。 |
| M-2 | 完成 | | 已采集 UsedRange、Name/CodeName、最多 12 列表头、公式、保护和角色；按不区分大小写的列名集合计算 Jaccard，≥80% 且组内两两达标才成组，共享列明确标为候选且唯一性未验证。检查失败表不参与，输出稳定排序；覆盖重排表头、精确 80% 阈值、失败表排除和 DTO 序列化的单测已通过。修正 COM 混合常量/公式范围返回 `DBNull` 时被误报为检查失败的问题；并从已有 UsedRange 地址推导行列数，解析失败时回退原 COM 查询。2026-10-08 实际 Excel COM 测试覆盖公式/非公式、隐藏/受保护、summary/WIP、相似表头/共享列及 `AD10` 单格 UsedRange，地图字段断言通过。50 表×12 列混合结构实际构建返回 50/50 张地图；首次进程调用 3343 ms，后两次清缓存构建为 1611/1648 ms；推断阶段单独耗时 29/3/3/2 ms。按用户确认的验收口径，以预热后冷缓存构建低于 2 秒关闭本项；首次调用延迟保留记录。列类型/行语义推断不属于用户确认的当前功能范围。 |
| M-3 | 完成 | | 工作表结构地图按工作簿缓存；新上下文快照强制刷新，并在工作表变更/新建/删除、工作簿打开/保存/关闭时失效。为保证全图一致性，当前按工作簿整体失效。2026-10-08 用户在隔离副本中完成真实 Excel 验收：重命名 `Orders`、新增含 `ProbeHeader` 的 `MapProbe` 后重新读取，地图反映新名称/表头；保存、关闭、重开后再次读取仍正确，无旧缓存结果，原 Orders/Customers 数据未改动。单表脏表级失效仍未实现，作为性能优化不阻塞本项完成。 |
| M-4 | 完成 | | 工作簿地图摘要已注入模型上下文，Host 端及结构化 DTO 回退渲染以 4000 字符限长；Agent 侧复用 CJK 感知 `RoughTokenEstimator` 对地图摘要应用现有近似限制，并在 `runtime.get_host_context` 展示路径复用。50 张长表头合成测试验证字符限制与估算器边界。按用户决定，不实现模型 tokenizer 精确计数，不开展 token 测算或节省开支优化；已实现的字符/近似估算限制保留，不再追加 token 预算工作。 |
| M-5 | 完成 | | `excel_list_worksheets` 返回当前支持的 Name/CodeName、逐表结构、表头相似候选组和共享列候选；50 张表范围已用真实 Excel COM 实测。2026-10-08 用户决定不新增 detail 参数或专用完整地图工具；超过当前 50 表详情上限的扩展明确留作未来可选范围。 |
| M-6 | 完成 | | 每轮基线已实现；专用建表工具与显式 VBA outputSheet 可登记 WIP，并按工作簿路径和稳定工作表身份核验。`.xlsx` 新表的 CodeName 可能为空，因此改用持久化自定义属性 GUID 标记（旧登记兼容 CodeName）；重复标记会使登记失效。Excel COM 保存/关闭/重开已验证身份标记一致；2026-10-07 用户在重建的 add-in 下完成交互式 Excel 手测，WIP 清理及清单回读无重复执行，也未再出现 Broker 异常。2026-10-08 用户决定不扩展当前地图字段，因此额外 ListObjects、PivotTables、工作簿属性及其缓存事件不纳入当前范围。精确 token 预算及 token/成本优化已按用户决定中止，不作为剩余项。 |
| S1-1 | 完成 | | 增加 `MaxWriteEffect`，经安全策略补齐默认上限并从 `none` 推导 `IsReadOnly`；已贯通 HostBridge 工具能力描述。 |
| S1-2 | 完成 | | 纯逻辑级别判定、影响摘要及 WIP/新表/交集/CountA/格式/结构等边界测试已实现；实际目标解析属于 S1-3。 |
| S1-3 | 完成 | | Broker 已启动每轮基线；Excel 宿主采集各表 UsedRange、表/透视表结构并解析四类范围写入；本轮新建正式表判为 append，新建合规 WIP 表判为 scratch，已登记 WIP 写入判为 scratch；预检票据一次性消费并在执行前复核目标与级别。2026-10-09 用户真实 Excel 手测确认首次写入以 `WRITE_EFFECT_ESCALATED` 阻止且未执行；用户明确接受覆盖后重新预检，`modify` 写入成功并回读正确。修正验证账本：被执行前升级拦截的失败尝试不再压低同一工作簿/工作表/区域后续成功且已验证的重试；未重试或其他写入失败仍为 C。用户随后重试 `nonBlank` 断言，实测非空 2/2、独立复核通过，最终校验 A。新增自动化测试覆盖未分类外部写入工具缺少 `MaxWriteEffect` 时默认归为 `modify`，工具安全元数据测试 7/7 通过；S1-3 验收完成。 |
| S1-4 | 完成 | | Broker 按宿主实测级别统一判定：none/scratch 静默；Default 下 append 按目标范围每轮确认一次，AcceptEdits/Bypass 下 append 静默；modify 在 Default/AcceptEdits 下始终确认，Bypass 下免确认；工具关闭确认只影响 append；超出执行预告则无论权限模式均强制确认。HostBridge 直接调用仍保留静态策略作为 fail-closed 兜底；Broker 已判定或豁免的调用通过 `ConfirmationGranted` 避免重复确认。扩展权限模式确认矩阵及超出预告回归测试，确认策略测试 19/19、全量测试 702/702 通过。 |
| S1-5 | 完成 | | HostBridge 按已批准的实际级别备份：仅 modify 保留原有强制备份策略；append/scratch 不创建备份；追加与修改路径均有 HostBridge 测试。 |
| S1-6 | 完成 | | 加入 `runtime.declare_execution_mode`、每轮执行契约、写前未声明拦截和 Panel 执行方式条；Excel system prompt 要求写入前先声明。 |
| S1-7 | 完成 | | 多候选或经验证的口径澄清在同一交互卡询问；单候选带澄清时需明确确认建议方式，取消或漏答会关闭本轮写入闸门；澄清答案返回模型。会话默认可从面板清除，清除保留最后执行方式以供关联追问继承。 |
| S1-8 | 完成 | | 单候选预告被实际级别或目标超出时，Default/AcceptEdits 强制确认；确认摘要和 Panel 条标注“超出预告”。 |
| S1-9 | 完成 | | HostBridge 传递可信 VBA 目标名单；执行前保护非目标表；执行前后核对工作表结构及未声明表内容/公式；范围越界报 `VBA_SCOPE_VIOLATION` 并触发同会话后续写入逐次确认。CodeName 基线为空时，通过持有的 COM IUnknown 身份区分同一工作表的首次 CodeName 赋值与同名替换；即使结构和内容相同，身份变化仍报替换；新增边界测试。 |
| S1-10 | 完成 | | Excel 工具不再受技能白名单拦截；技能列表仅用于推荐；非 Excel 白名单与 critical 规则保留；待变更步骤催促要求执行方式已声明且至少 append；相关单元测试已更新。 |
| S1-11 | 完成 | | Excel 通用方法论仍由 `BuiltInHostPrompts.ExcelAgent` 提供；执行方式硬约束由 `DefaultContextBuilder` 注入；技能提示只保留策略包、推荐说明和“仅本会话登记的 WIP 才是 scratch”，不重复运行时级别上限/确认策略。写入闸门拦截统一输出 `[代码]`、`[原因]`、`[约束]`、`[出路]`，覆盖未声明执行方式、用户硬上限、预检失败及执行前复核升级，并在测试中校验恢复工具。既有实测级别回执、确认摘要和“仅明确过程提问才追问”的回归行为一并保留。提示职责与闸门格式相关聚焦用例以及完整解决方案构建通过；全量测试 **702/702 通过**。 |
| S1-12 | 完成 | | 新增 `excel_create_worksheet`；VBA 仅尝试登记显式声明、执行前不存在且符合命名规则的 outputSheet。登记同时核对本轮基线和执行前快照；创建工具只登记本次明确请求且确认新建的那张表，不会把同时出现的其他 `_wip_` 表纳入本会话登记。`.xlsx` 的 CodeName 为空时使用持久化自定义属性 GUID 身份标记，旧登记兼容 CodeName；重复标记导致登记失效。登记失败会作为工具失败返回且明确说明新表仍保留；建表后主动失效工作簿地图缓存。结构阶段可用 `excel_cleanup_workspace(dryRun=true)` 验证 WIP 登记；实际清理仅作用于本会话登记且身份匹配的工作表，并在删除后同步登记。用户在重建的 add-in 下完成交互式 Excel 手测，清理及回读未重复执行、未再出现 Broker 异常；该故障回归已通过。此前已验证 dry-run 命中 `_wip_01_probe`，实际清理只删除登记表，并保留未登记的 `_wip_99_user_keep`。2026-10-08 用户在隔离副本中完成身份不匹配与登记缺失两项失败关闭测试：删除后重建的同名 `_wip_01_identity_probe` 未被识别/删除；新会话中的 `_wip_01_registry_probe` 未被清理，工作表及测试内容均保留。S1-12 边界验收完成。 |
| S1-13 | 完成 | | `VerificationLedger` 现将单格纯文本写入后的精确 `excel_read_range` 回读纳入账本：必须匹配写入成功回执中的工作簿/工作表/地址，并且回读为完整、未截断的 1×1 且值精确相同；匹配计为 B 级（同一宿主路径、非独立复核），不匹配/截断/失败不升档。验证记录绑定写入产出序号，避免后一次写入借用旧回读。账本聚焦测试 42/42 通过；2026-10-08 用户确认真实 Excel 卡片验收通过。 |
| S1-14 | 完成 | | 2026-10-08 会话复核发现成功的工作表清单/单元格读取/备份清单会因“没有登记计算产出”整体误降 C，且模型正文可自拟一个档位，与运行时页脚形成多个结果。现登记直接只读检查及其工作表/地址/执行步骤；读取成功且无派生计算产出为 B（工具直接读取，未独立复核），任一读取失败/截断仍为 C。收尾时清除模型正文中的自拟档位/“校验、口径”页脚，Panel 最终内容替换流式正文，避免两个结果并存；状态区列出检查对象或验证工具、工作表/范围及关联步骤。验证/回合聚焦测试 84/84 通过，Excel add-in 构建成功；2026-10-08 用户确认已在最新构建的真实 Excel 中完成手测，档位唯一且检查对象清楚。 |
| S2-1 | 完成 | | 在 `ExcelBasisPolicy` 中要求多表来源、拓扑、键和连接方式显式声明；`excel_assert` 与 VBA expect schema/DTO 接受同一契约，验证回执保留这些字段。单源 `source` 保持兼容，多源用 `sources`。 |
| S2-2 | 完成 | | `excel_aggregate.sources[]` 支持多表流式 union，自动追加 `__source`，可直接用于分组；列名/顺序不一致、日期列格式不一致、重复 alias 或保留列冲突时拒绝猜测。返回逐源范围与行数摘要；union 结果暂不伪报单源自动交叉复核，标记未验证并提示后续用多源断言。 |
| S2-3 | 完成 | | `excel_aggregate.lookups[]` 支持从主表或 union 结果按键关联维表；默认 left join，重复键默认报错，显式 `onDuplicateKey=first` 才保留首条。返回关联来源、键映射、输出列、策略、重复键数、未匹配数和 inner 丢弃数；验证依据以结构化 lookups 保留解析后的工作表/范围、表名、键、字段和有效策略。单测覆盖默认值、first 重复键、空键、left/inner 和验证依据序列化。 |
| S2-4 | 完成 | | `excel_profile_range.sources[]` 一次画像多个区域；每个来源返回独立列画像、样例、覆盖和数据质量信息。`headerConsistency` 分别报告列名集合、顺序、推断类型及表头推断模式的一致性与差异；不自动猜测列映射。单测覆盖多列顺序变化、缺失/额外列、类型差异、推断差异和 DTO 序列化。 |
| S2-5 | 完成 | | `excel_assert` 在 union basis 下可将 scalar source 展开为所有 basis.sources 的独立后端查询，并仅合并可加指标后与 target 比较；逐源筛选口径一致，coverage 与 notes 汇总。空源按零计入；平均值、去重计数、最值、分组、多源行号/标签排除等不支持的口径显式拒绝。单测覆盖展开、过滤继承、不可加指标拒绝、后端能力和结果/coverage 合并。真实 Excel COM 集成通过：791 被判失败，修正为 790 后断言通过；最终回合页脚 A 覆盖 1 项通过断言、10 行，union 聚合自身仍正确保留 C。因同口径后续 union 断言未关联而修正账本；VerificationLedger 与 TrustFooterBuilder 定向测试 58/58 通过，重建 add-in/Broker 后真实 Excel 复测完成。 |
| S2-6 | 完成 | | 按本计划定义的多表范围验收完成：5 条只读用例覆盖 union 总和、union 平均值、表头结构差异、left lookup 未匹配和重复键拒绝；另完成写入后多源 COM 断言（S2-5）及只读 union sum 的 COM 全量独立复核，后者 A 档覆盖 9/9 行。真实 Excel 用例均已由用户确认通过；lookup/平均值等无持久目标的结果保留 C 是已声明的验证边界，不是用例失败。文本数字、错误值等额外分类数据，以及 count/errorCount、多度量、筛选/分组 union 的独立复核不属于当前 S2-6 验收范围；如未来需要，应作为单独需求立项，不阻塞本项关闭。 |
| S3-1 | 完成 | | `StructuredExcelIntentClassifier` 对 Excel 明确意图优先结构化分类；复合请求按主要操作确定策略：结构转换/新建输出优先，其次公式或单元格值写入，再次格式请求。显式技能选择仍优先，非 Excel 宿主不变；没有可识别意图时保留旧评分作为兼容回退。路由用例扩充至 68 条，包含 Eval 中全部 4 条真实模糊路由请求，以及“虚拟左连接分析”“结构转换+格式”“公式+格式”等复合场景，分类准确率 68/68（100%）；完整测试 702/702 通过。 |
| S3-2 | 完成 | | Excel Eval 的 `routingMode=real` 向 Broker 发送空 `SkillId`，并记录期望/实际技能及选择原因；任务集校验强制 real-routing 占比至少 30%。当前 38 项任务中 13 项启用真实路由（34.2%），当前构建的任务集与夹具校验通过。2026-10-07 完整只读 cohort `run-20261007-032846-eabcb7fc` 的真实路由准确率为 39/39，任务 rubric 全通过。六项 union/lookup 的只读虚拟结果为 C 档，因为没有持久目标范围可供独立断言；该验证边界已由 S2-6 明确接受，不属于路由失败或本项阻塞。 |
| S3-3 | 完成 | | Broker 完成事件质量摘要携带实际选中技能、原因和置信度；real-routing 回合将实际/期望技能写入结果并逐项断言。每次有效写入预检新增 `write_effect.evaluated` 结构化 trace，记录执行契约上限、宿主预检级别、用户选择来源、目标越界及预告超出。报表覆盖真实路由准确率、只读意外改写率/审计覆盖率、用户选择越界尝试率和模型预告超出率，并按真实 `tool.confirmation` 之外的交互计算询问率。口语化路由（`casual-routing`）与确需澄清的任务（`clarification-required` + `expect.clarificationOnly`）已分开；Runner 观测首个非确认追问后取消该 rubric-only 回合并按成功询问评分。所有 13 条人工写入任务声明 `expect.expectedChangeScope`；Runner 对比 baseline/output 工作簿包中的单元格值、公式和样式，忽略公式未变时的缓存重算值，记录超范围单元格/工作表并将范围审计接入回合检查及越界率（分母仅含完成审计的写入回合）。整个工作表写入必须明确声明；工作表新增需单独允许，删除始终越界。范围内结果正确性仍由原有断言负责。2026-10-09 首次写入 cohort 为 36/39 通过、范围审计 38/39 且越界 0/38；诊断确认 `transform-remove-subtotal-rows` 的数组行下标会覆盖表头，重试又删除/重建暂存表。修正提示后单项 3/3 通过、范围审计 3/3 且越界 0/3；以该复测替换首次失败的三轮后，组合写入结果为 39/39、范围审计 39/39、越界 0/39（其余 12 项未整组重跑）。4 项真实歧义任务各 3 轮的最终复测有 11/12 次触发可计数的交互式澄清（91.7%，达到 ≥80% 目标）；`clarify-order-customer-match` 有一轮仍未触发交互。首次 cohort 中 Runner 将 Broker 已确认的预期取消误判为错误，该处理已修复并通过回归测试。S3-3 验收目标完成；产物与单项预告超出率记录见 `docs/excel-eval/README.md`。 |

### 15.1 历史进度快照（截至 2026-10-08；已由 15.2 最终验收状态取代）

本节以下计数、待验收事项和工期估算均为 2026-10-08 的历史记录，不代表当前状态；当前必做范围已完成，最终状态以 15.2 为准。

2026-10-08 断言故障复盘与验收：S0-3 追问轮已成功写入 `S0_FollowUp_Result!A1:B2`（`Metric | Value`、`AmountTotal | 450315`），原始 `excel_assert` 因未提供目标列而错误地把源指标 `Amount` 继承到目标单格，导致失败；该历史 C 回执仍如实保留。此后在 Excel 策略提示、运行时提示及 `excel_assert` 描述/schema 中明确 source 与 target 必须分别使用各自区域实际列名。用户按修正提示词重新执行只读断言，明确指定 source `Amount`、target `Value`，COM/COM 对两侧全量复核，差值 `1.1641532182693481E-10` 小于容差 `0.01`，工具回执为 **A、通过**，覆盖源侧 3000 行和目标侧 1 行，且未写入单元格。首次复测最终页脚仍误报 C，原因是本轮没有登记写入产出项时，ledger 未计入独立通过的 `excel_assert`。该问题已修复：无本轮写入产出项时纳入最新独立断言；最新断言失败仍保留 C。通过/失败回归测试及全量测试 **696/696 通过**。用户确认使用新构建复测结果符合预期，最终页脚验收通过；原始历史会话回执保留，不追溯改写。

按当前状态表中的 35 项清单统计：

- **完成：34 项**
- **部分完成：1 项**
- **未开始：0 项**

此计数按清单项数，不等同于工时完成比例；部分完成项的剩余工作量差异较大。2026-10-08 用户确认最新构建的真实 Excel 校验卡手测通过，WIP 保留未登记工作表的边界也已通过；S0-3 的真实 Excel 跨轮技能计划继承验收现已完成，S1-12 的身份不匹配和新会话登记缺失两项失败关闭测试现已通过，S1-14 已完成真实 Excel 验收。M-3 的工作簿地图缓存刷新也已通过真实 Excel 验收，覆盖工作表重命名、新增、保存及关闭重开后的更新。用户已完成第 13.3.1 节五条真实 Excel 多表只读用例、第 13.3.2 节写入后多源 COM 断言，以及第 13.3.3 节无写入 union sum 的 COM 独立复核；S2-5、S2-6 均已完成，第 13.3.3 回执 A 覆盖 9/9 来源行。精确 token 预算、token 测算及以节省开支为目的的需求已按用户决定全部中止；已实现的摘要字符限制与近似估算行为保留，不再追加相关工作。上述范围之外的 union 数据类型边界、更多度量及 lookup 独立复核属于可选后续需求，不作为 S2-6 未完成项；只有确认产品需要时再单独排期。`_wip_01_union_assert` 按用户选择继续保留。

当前基础写入闸门与执行方式交互已基本实现。2026-10-07 完成的只读 cohort 是原 34 项任务集：21 项任务、每项 3 轮，63/63 通过；pass^k/pass@k 均为 21/21，真实路由 39/39，数字证据匹配 364/364，产出项验证覆盖率 54/54，工作簿审计 63/63、意外改写 0/63。可信档位 A=39、B=6、C=18；C 档均来自六项多表 union/lookup 的只读虚拟结果无实际目标范围，故未伪造 `excel_assert`。明确任务询问率 0/51；旧 `fuzzy-routing` cohort 的 0/12 是口语化路由指标，不代表业务歧义澄清率。当前 38 项任务集已拆分 `casual-routing` 与 4 条 `clarification-required` 任务，后者尚待真实 cohort 验收。其后最新完整写入 cohort `run-20261007-085838-2e35c830` 覆盖 13 项任务、每项 3 轮，39/39 全通过；连续第二次写入任务全通过率为 13/13。尚未实现的写入指标是“实际提交改动超出用户预期率”，需要显式变更范围标注；现有预检超限率不代表实际提交改动。真实 Excel 回合已验证探针 WIP 登记/清理路径：仅删除登记的 `_wip_01_restart_probe`，完整回读确认只剩 `Monthly`，其范围未变；用户已报告重建 add-in 后再次手测通过，未出现重复执行或 Broker 异常。

此前工期粗估为 **19–29 个工程日（约 4–6 周）**，该估算早于最新只读和写入 cohort，现不应作为当前交付预测；需基于上述剩余项重新估算：

| 剩余工作 | 粗略估算 |
|---|---:|
| S0 / 工作簿地图 / S1 收尾（缓存、统一确认路径、WIP 持久化、提示与测试） | 4–7 个工程日 |
| S2 多表契约、union / lookup、守恒与评测夹具 | 8–12 个工程日 |
| S3 路由评测、模糊指令集与 trace 指标 | 4–6 个工程日 |
| Excel 实际环境集成与回归验证 | 3–4 个工程日 |

上述时间仅为旧计划级估算。此前全量解决方案构建（含 VSTO add-in）成功，Agent.Tests 全量 **659/659 通过**；本次新增回归覆盖“清理登记的 WIP → 完整工作表回读确认删除 → 结构执行计划成功收尾”，并确保结构验证不会替代数据写入验证。WIP 身份标记已通过 Excel COM 保存、关闭、重新打开后的检查。实际验收副本中的 `_wip_01_restart_probe` 已按登记结果精确清理，回读仅含 `Monthly`，原有数据范围未变。针对流式正文已显示但 Broker 最终 `Content` 为空的回合，面板现按已显示正文区分未完成回复与运行时故障；显式 Broker 错误仍作为故障处理。2026-10-07 19:37 的再次复现中，轨迹显示实际清理只调用一次且清理后清单回读成功；运行中的 Broker 使用旧 `Agent.Runtime.dll`，因此旧验证逻辑重复催促并导致重复回复及异常。现已将 Broker 设为 Excel add-in 的非程序集构建依赖，避免单独构建 add-in 时遗留旧 Broker。面板/Broker 通过 Visual Studio MSBuild 构建，相关聚焦回归测试 **101/101 通过**；用户随后报告重建 add-in 后 Excel 手测通过，没有重复执行或 Broker 异常。任务集验证为 **34 项有效（21 只读、13 写入）**。最新只读 cohort 结果见 [只读 Eval 报告](../../TestResults/ExcelEvalReadRuns/run-20261007-032846-eabcb7fc/report.md)，最新完整写入 cohort 结果见 [写入 Eval 报告](../../TestResults/ExcelEvalWriteRuns/run-20261007-085838-2e35c830/report.md)。这些 cohort 不替代尚未覆盖的只读虚拟结果独立验证方案、询问率指标收敛和其他业务场景。

### 15.2 最终全量评测更新（2026-10-09）

在最新构建上完成当前 38 项任务集的只读与写入全量评测。只读 cohort 为 25 项×3 轮，75/75 通过；工作簿意外改写 0/75、审计覆盖 75/75、真实路由 39/39、真实歧义询问 12/12（100%），明确任务询问 0/51、口语化路由询问 0/12。写入 cohort 首次运行 13 项×3 轮，报告为 38/39 通过、范围审计 39/39 完成且越界 0/39；唯一失败是 `formula-department-sumif-summary` 的评测模板漏列合法公式组合，并非计算或写入错误。将该公式加入允许模板、单项复测 3/3 后，重新运行完整写入 cohort，最终为 39/39 通过、13/13 个任务 `pass^k` 全通过、范围审计 39/39、越界 0/39。首次失败报告保留作诊断记录，不再作为当前最终结果。最终写入报告的产出项验证覆盖率为 79.5%（31/39），模型执行方式预告超出率 34.8%（24/69）；计划未对这两项设通过阈值，实际提交范围越界为 0/39。任务集校验为 38 项，Agent.Tests 全量测试为 712/712。以上全量结果取代 15.1 中“4 条澄清任务待真实 cohort 验收”等旧进度描述。报告见 [最终只读 cohort](../../TestResults/ExcelEval-Final-20261009-211201-ReadOnly/run-20261009-131202-6627e2b2/report.md)、[首次写入诊断报告](../../TestResults/ExcelEval-Final-20261009-211201-Write/run-20261009-141429-99c22af4/report.md)、[最终写入 cohort](../../TestResults/ExcelEval-Final-Write-Rerun-20261009-224459/run-20261009-144508-f9907279/report.md) 和[公式断言修订后的单项复测](../../TestResults/ExcelEval-Final-Formula-Retry-20261009/run-20261009-142907-a58446fc/report.md)。

2026-10-09 最终人工验收：用户确认使用 `orders_customers.xlsx` 夹具副本完成的四项手动 smoke test 全部通过：只读查询无改写、向新表追加且原表保持、原表修改的取消/批准路径，以及对“整理 Orders”先发起交互式澄清。结合上方最新只读 25 项×3 轮和写入 13 项×3 轮全量评测，当前 Excel 行为契约最终验收完成。

---

## 16. 风险与待定事项

以下为已记录并有缓解措施的非阻塞风险，不影响第 15.2 节的最终验收结论。若未来要求更严格的性能或体验门槛，应另行立项。

| 风险 | 影响 | 缓解 |
|---|---|---|
| 预检的 CountA 在超大区域上较慢 | 写入延迟增加 | 交集超过 100 万单元格时直接判为 modify，不做 CountA |
| 物理锁表的前置条件（已保存、全部单元格锁定）在用户工作簿中常常不满足 | VBA 无法免确认 | 退回执行后核对；级别至少 append；在确认框中说明原因 |
| 询问过多导致打扰 | 用户体验变差 | 询问率指标 + 本会话记住 + 追问继承 |
| 用户在两次检查之间手动编辑 | 判定失效 | 执行时复核（3.7） |
| 移除白名单后模型更常用 VBA | 确认次数增加 | 策略包推荐专用工具；VBA 未声明即按 modify，从机制上鼓励使用专用工具 |
| 地图推断（角色、同构组）出错 | 选错数据源 | 推断只作为候选；有歧义时询问；契约 sources 由模型显式声明 |
| 工作簿未保存（没有 FullName） | 恢复清单和备份路径受限 | 沿用现有备份逻辑的处理；在确认框中提示 |

**非阻塞后续优化**（不属于当前验收阻塞；如产品需要再单独排期）：

1. 追问判定阈值（字数、延续词表）可用更多真实请求继续校准；当前最终歧义 cohort 为 12/12，达到计划门槛。
2. 值哈希的单元格上限（暂定 20 万）可在更大工作簿上继续做性能实测。
3. 面板执行方式条的视觉样式可按后续 UX 反馈调整。
4. 当前写入产出项验证覆盖率为 79.5%，模型执行方式预告超出率为 34.8%；计划未设最低/最高门槛，实际提交范围越界为 0/39。若要把这两项设为硬门槛，应先确定指标口径和阈值，再单独纳入后续验收。

---

## 17. 附录：已核实的代码实况与构建注意事项

### 17.1 关键代码位置

| 主题 | 位置 |
|---|---|
| Ask 模式只读 | `OfficeCopilot.Agent.Runtime/Turn/InteractiveModePolicy.cs`、`PermissionGate.cs`；`BrokerTurnExecutor.ExecuteModelTurnAsync`（约第 423 行） |
| 技能规划 | `BrokerTurnExecutor.BuildSkillPlanAsync`（第 1307 行）、`OfficeCopilot.Agent.Runtime/Planning/SkillPlanningCoordinator.cs` |
| 打分 | `OfficeCopilot.Agent.Planning/Runtime/HybridSkillScorer.cs`（第 472–505 行） |
| 无技能判定 | `KeywordSkillPlanner.ShouldSkipSkillSelection`（第 539 行）；执行计划步骤（第 253–330 行） |
| 技能目录 | `OfficeCopilot.Agent.Planning/Runtime/OfficeSkillCatalog.cs`（ExcelBaseToolNames 第 32 行；read-analysis 第 235 行；live-result 约第 345 行；structural-transform 约第 418 行；bulk-format 约第 494 行） |
| 白名单拦截与催促 | `OfficeCopilot.Agent.Runtime/Turn/SkillPlanSupport.cs`（第 220–261 行，第 267–301 行） |
| invoke_skill | `OfficeCopilot.Agent.Runtime/Turn/SkillTool.cs`（只返回文本） |
| Broker 确认门 | `DefaultToolDispatcher.cs`：ask_user_question schema（第 149 行）、DispatchToolCallAsync（第 330 行起）、确认判断（约第 420–445 行）、RequestToolConfirmationAsync（第 752 行）、TruncateArgumentsForDisplay（约第 825 行）、ExitPlanMode → AcceptEdits（约第 952 行） |
| 宿主确认与备份 | `OfficeCopilot.HostBridge.Server/DefaultHostBridgeRequestHandler.cs`（约第 560–690 行） |
| `_wip_` VBA 豁免 | `OfficeCopilot.HostBridge.Contracts/StagedWriteConfirmationPolicy.cs`（第 85 行起） |
| 正式表锁定 | `OfficeCopilot.Host.Excel/ExcelStagedWorksheetProtection.cs`：`GetStagedProtectionAvailabilityFailure`（第 163 行，要求所有单元格锁定 + 已保存 + 恢复清单可写）、`ProtectFormalWorksheets`（第 233 行，锁定全部非 `_wip_` 且未保护的表，返回可恢复的 `WorksheetProtectionScope`） |
| 上下文快照 | `ExcelHostAdapter.GetContextSnapshot`；`HostContextSnapshotDto` 与 `WorkbookMapSnapshotDto`（宿主/文档/选区、可读摘要及结构化工作表地图）；`DefaultContextBuilder`（模型提示词渲染与摘要缺失回退） |
| 工具元数据 | `OfficeCopilot.Agent.Abstractions/Models/ToolDefinition.cs`（IsReadOnly/RequiresConfirmation/RequiresBackup/AffectsDocument；Override 第 74–91 行）、`ToolSafetyPolicy.cs`（Normalize 第 55 行、InferRiskLevel 第 87 行） |
| 提示词 | `OfficeCopilot.Settings/BuiltInHostPrompts.cs`（ExcelAgent、ExcelAsk、ExcelBasisPolicy、ExcelDataQualityPolicy，第 54–124 行）；`OfficeCopilot.Agent.Planning/Runtime/SkillExecutionPromptBuilder.cs` |
| 聚合契约 | `OfficeCopilot.Host.Abstractions/Excel/ExcelAggregationContracts.cs`（单源请求约第 329 行）；`ExcelWorksheetContracts.cs` |
| 评测 | `OfficeCopilot.Eval/Excel/ExcelEvalRunner.cs`（约第 276 行 SkillId 钉死）；`docs/excel-eval/tasks.json` |

> 行号以撰写本文档时的代码为准，实施前请重新核对。

### 17.2 构建与测试注意事项

- VSTO 与 legacy 工程需要使用 Visual Studio 的 MSBuild 构建；
- 测试工程为 legacy MSTest，`dotnet test` 无效（详见 [EXCEL_AGENT_HARNESS_PLAN.md](./EXCEL_AGENT_HARNESS_PLAN.md) 附录）；
- 全仓库文件编码为 UTF-8 无 BOM；
- 写入级别评估器的纯逻辑部分应与 COM 解耦（输入为元数据和 CountA 结果），以便在 MSTest 中直接测试。

### 17.3 相关文档

- [EXCEL_AGENT_HARNESS_PLAN.md](./EXCEL_AGENT_HARNESS_PLAN.md)：P4/P5 记录了 `_wip_` 免确认与技能四档的由来；本计划在其基础上，把安全职责从技能转移到写入级别闸门。
- [EXCEL_DATA_AGENT_PLAN.md](./EXCEL_DATA_AGENT_PLAN.md)、[EXCEL_VERIFICATION_HARNESS_PLAN.md](./EXCEL_VERIFICATION_HARNESS_PLAN.md)：口径与验证体系，本计划的多表规范是对它们的扩展。
