# Excel 数据处理能力改造方案

> 用途：**实施计划**。解决 Excel Copilot 在中大型数据表上"逐行读取、逐行验证"导致的低效与不可完成问题。
> 状态：设计已定稿，待实施。制定日期：2026-08-31。
>
> 相关文档：[CODE_MAP.md](CODE_MAP.md)（导航索引）· [ARCHITECTURE.md](ARCHITECTURE.md) · [REMEDIATION_PLAN.md](REMEDIATION_PLAN.md)

---

## 目录

1. [问题诊断](#1-问题诊断)
2. [设计原则](#2-设计原则)
3. [实施步骤总览](#3-实施步骤总览)
4. [Step 1：聚合与画像工具](#step-1聚合与画像工具)
5. [Step 2：备份补齐（承重墙）](#step-2备份补齐承重墙)
6. [Step 3：VBA 分步执行增强](#step-3vba-分步执行增强)
7. [Step 4：提示词决策树](#step-4提示词决策树)
8. [Step 5：Skill 两档分离](#step-5skill-两档分离)
9. [附录 A：被否决的方案](#附录-a被否决的方案)
10. [附录 B：实施注意事项](#附录-b实施注意事项)

---

## 1. 问题诊断

现象：1000 行的表提出"分类汇总"需求，模型逐行读取内容，而不是从表结构出发；验证环节同样退化为逐行核对，实际不可完成。

根因有四条，**都不是"模型笨"**：

### 1.1 工具目录里没有分组聚合能力

[ExcelBuiltInToolCatalog.cs](../OfficeCopilot.Host.Excel/Tools/Registration/ExcelBuiltInToolCatalog.cs) 注册的 42 个 `excel_*` 工具中，唯一的分析工具是 `excel_analyze_range`。而它在
[ExcelAnalysisService.cs](../OfficeCopilot.Host.Excel/ExcelAnalysisService.cs) 的 `NormalizeAnalyzeOperation` 只支持两种 operation：

- `aggregate`：对整个区域跑单个函数
- `lookup_by_extreme`：按极值查找

**没有 group by**。模型要做分类汇总，唯一可走的路就是把数据搬进上下文自己算。

### 1.2 读取工具的默认上限把模型逼成分页爬虫

| 位置 | 默认 maxRows |
|---|---:|
| [ExcelRangeService.cs](../OfficeCopilot.Host.Excel/ExcelRangeService.cs) L168 / L967 | 50 |
| [ExcelHostAdapter.cs](../OfficeCopilot.Host.Excel/ExcelHostAdapter.cs) L100 | 20 |
| [ExcelHostAdapter.cs](../OfficeCopilot.Host.Excel/ExcelHostAdapter.cs) L179 | 50 |
| [ExcelTableService.cs](../OfficeCopilot.Host.Excel/ExcelTableService.cs) L226 | 100 |

返回的还是 `Preview` 文本 + 一句"已截断：是"，没有任何替代路径的指引。1000 行 → 20+ 次工具调用 → 上下文膨胀 → 触发压缩 → 信息丢失。

### 1.3 提示词对数据规模零指导

[BuiltInHostPrompts.cs](../OfficeCopilot.Settings/BuiltInHostPrompts.cs) 的 `ExcelAgent` 只有两句话，核心指令是
"务必先调用 excel_* 工具读取真实数据再作判断" —— 这句话本身就在**鼓励**模型去读原始数据。
对比同文件里 `WordAgent` 那段带三阶段流程的提示词，Excel 侧几乎是裸奔。

### 1.4 验证工具本身就是 O(行×列) 的 COM 逐格调用

`ExcelAnalysisService.ValidateRangeFormulas` 用 `range.Cells[row, column]` 双重循环，上限 100 行。
每次 `Cells[r,c]` 都是一次跨进程 COM 往返 —— 这不只是"模型不该这么干"，是**宿主端实现本身就跑不动**。

### 1.5 安全网存在缺口

- [ExcelBackupService.cs](../OfficeCopilot.Host.Excel/ExcelBackupService.cs) 在工作簿未保存时返回 `Success=false`，
  但调用方**没有阻断工具执行** —— 在最需要备份的场景（新建未保存的簿）恰好裸奔。
- 全仓搜索 `Restore|Rollback|list_backups` 无任何还原实现。**备份只写不读**，用户想回滚只能自己去
  `%LOCALAPPDATA%\NixerOfficeCopilot\Backups\` 翻文件夹并靠时间戳猜。
- [ExcelVbaService.cs](../OfficeCopilot.Host.Excel/ExcelVbaService.cs) 的 `ExecuteVba` 通过
  `_application.Run(...)` 同步阻塞执行，**无超时、无取消**。模型写出死循环则 Excel 卡死，用户只能杀进程。

---

## 2. 设计原则

### 2.1 三条路径，按"用户要的产物"分叉

第一层分叉不是"VBA vs 公式"，而是用户要什么：

| 用户意图 | 产物 | 路径 |
|---|---|---|
| "这 1000 行按部门汇总是多少" | **一个答案**，不改工作簿 | `excel_aggregate` 内存计算，纯只读 |
| "在工作簿里做一张部门汇总表，源数据变了要跟着变" | **活的结果** | 写公式 / 透视表 |
| "把 A 列按分隔符拆成三列，空值行删掉" | **一次性结构改造** | VBA |

第一条当前完全缺失，而它是最高频的。补上它就消掉一大半路径之争。

> **关键提醒**：VBA 生成的汇总表是**静态死值快照**，源数据变化不会自动更新。
> Agent 在计划阶段必须主动声明或询问，这是用户最容易踩的坑。

### 2.2 风险轴是"对既有数据是否可逆"，不是"是否用了 VBA"

模型写代码的能力远强于逐步硬执行的能力，**不能因为安全就砍掉 VBA 这块最强能力**。
应该被约束的是"原地不可逆修改"，而不是 VBA 本身。

### 2.3 分步落中间表 = 可观测性，不是安全性

把大任务拆成多个阶段、每阶段结果写入新工作表，价值在三处：

- **对用户**：Excel 里实实在在多出 `_wip_01_清洗`、`_wip_02_汇总`，随时能点开看 —— 可审计的计算过程留痕。
  这比公式路径可信度还高，公式只能看到最终式子，中间推导是隐含的。
- **对模型**：单段代码短、出错定位准、失败只重做一节。
- **对上下文**：数据留在 Excel 里，模型只看画像 —— **直接解决 1.2 的上下文膨胀问题**。

安全性由备份负责，两件事不捆绑。因此：

- **不拆两个 VBA 工具**，只保留一个 `excel_execute_vba`
- 分步落中间表是**推荐工作法**（提示词规则 + 可选参数），**不是硬约束**
- 需要动原表时就动

### 2.4 备份是唯一承重墙，必须先修好

既然放弃了硬约束，安全担子全压在备份上。当前备份"失败不阻断 + 无还原能力"，
所以 Step 2 优先级仅次于 Step 1。

---

## 3. 实施步骤总览

| # | 内容 | 性质 | 依赖 | 建议顺序 |
|---|---|---|---|---|
| 1 | `excel_aggregate` / `excel_profile_range` / `excel_reconcile` | 新增能力 | 无 | 先做，见效最直接 |
| 2 | 备份失败即阻断 + 备份列表 + 面板还原按钮 | 修 bug + 补缺口 | 无（可与 1 并行） | 承重墙，紧随其后 |
| 3 | `excel_execute_vba` 分步/日志/看门狗增强 | 增强 | 2 | |
| 4 | Excel 提示词决策树 | 提示词 | 1、3 | |
| 5 | Skill 两档分离 | 权限隔离 | 1、3 | 最后 |

**Step 1 与 Step 2 相互独立，可并行。** Step 4 必须在 1、3 之后，否则会指向不存在的工具。

---

## Step 1：聚合与画像工具

### 目标

补齐"从结构出发"的能力，让模型一次调用就能拿到全表洞察，不必搬运数据。

### 1.1 `excel_profile_range` —— 表结构画像

**返回体量与行数无关**，恒定几百 token。

输入：
```jsonc
{
  "worksheetName": "Sheet1",     // 可选
  "address": "A1:H1001",         // 可选
  "tableName": "销售明细",        // 可选，与 address 二选一
  "hasHeaders": true,            // 可选，默认自动推断
  "topK": 10                     // 每列返回的高频取值数，默认 10
}
```

返回（每列一项）：

| 字段 | 说明 |
|---|---|
| `name` | 列名（无表头时用 A/B/C） |
| `inferredType` | `number` / `text` / `date` / `boolean` / `mixed` / `empty` |
| `nonEmptyCount` | 非空单元格数 |
| `distinctCount` | **去重数**（判断是否适合做分组键的关键指标） |
| `topValues` | Top-K 取值及频次 |
| `min` / `max` / `sum` / `average` | 仅数值列与日期列 |
| `errorCount` | Excel 错误值数量 |
| `sampleRows` | 整体前 5 行原样，帮助模型理解语义 |

### 1.2 `excel_aggregate` —— 分组聚合（内存计算）

**已定：在宿主端 .NET 内存里算，不改工作簿。**（写公式/建透视表的需求由 Step 3 或后续独立工具承担。）

输入：
```jsonc
{
  "source": { "tableName": "销售明细" },   // 或 { "worksheetName": ..., "address": ... }
  "groupBy": ["部门", "季度"],
  "measures": [
    { "column": "金额", "function": "sum",   "alias": "金额合计" },
    { "column": "订单号", "function": "count", "alias": "订单数" }
  ],
  "filter": [                              // 可选，AND 关系
    { "column": "状态", "operator": "eq", "value": "已完成" }
  ],
  "orderBy": [{ "alias": "金额合计", "descending": true }],
  "maxGroups": 200                         // 超出则截断并标注
}
```

支持的 `function`：`sum` / `count` / `countDistinct` / `average` / `min` / `max` / `first` / `last`。
支持的 `operator`：`eq` / `ne` / `gt` / `ge` / `lt` / `le` / `contains` / `startsWith` / `in` / `isEmpty` / `notEmpty`。

返回：汇总表（行数 = 分组数，与源数据行数无关）+ 总行数 / 被过滤行数 / 被跳过的脏值行数。

### 1.3 `excel_reconcile` —— 对账验证

**这是"验证不能逐行做"的答案。** 验证的正确姿势是总量对账 + 抽样，不是逐行比对。

输入：明细区 + 汇总区 + 对账列映射 + 抽样数（默认 5）。

返回**结论 + 差异项**，而非全量清单：

- 总量校验：`SUM(明细.金额)` vs `SUM(汇总.金额合计)`，差值与相对误差
- 分组数校验：明细的 `distinctCount(部门)` vs 汇总行数
- 抽样校验：随机 N 个分组，重算并比对，列出不一致项
- 结论：`passed` / `failed` + 人类可读说明

### 1.4 表范围识别（已定：两者都支持，需区别用户语义）

`source` 解析优先级：

1. 显式 `tableName` → 按 ListObject 查找
2. 显式 `address` → 直接用该区域
3. 都没给 → 探测顺序：
   a. 当前选区若在某 ListObject 内 → 用该 ListObject
   b. 当前选区若为多格区域 → 用选区
   c. 当前活动单元格的 `CurrentRegion` → 用推断的准表格

返回体中必须带 `sourceKind`（`listObject` / `explicitAddress` / `selection` / `currentRegion`）
与实际解析出的 `address`，让模型和用户都知道**到底在算哪一块**。
`currentRegion` 情况下额外返回 `headerInferred: true/false` 与推断依据，因为表头识别有歧义。

### 1.5 实现要点（必须遵守）

- **一次性 `range.Value2` 取整个二维数组到 .NET 内存**，绝不逐格 `range.Cells[r,c]`。
  1000×20 只需 1 次 COM 往返，几十毫秒；逐格则是 20000 次跨进程调用。
- 类型推断要处理 Excel 日期序列号（`Value2` 返回 double，需结合 `NumberFormat` 判断）。
- 脏值（文本混在数值列里）不抛异常，计入 `skippedRows` 并在返回体中报告。
- 新建 `ExcelAggregationService.cs`，不要塞进已经 987 行的 `ExcelAnalysisService.cs`。

### 1.6 涉及文件

| 文件 | 改动 |
|---|---|
| `OfficeCopilot.Host.Excel/ExcelAggregationService.cs` | **新建**，核心计算 |
| `OfficeCopilot.Host.Abstractions/HostContracts.cs` 或新建契约文件 | 新增 Request/Result DTO |
| `OfficeCopilot.Host.Excel/ExcelHostAdapter.Facade.cs` | 暴露三个新方法 |
| `OfficeCopilot.Host.Excel/Tools/Data/ExcelProfileRangeTool.cs` | **新建** |
| `OfficeCopilot.Host.Excel/Tools/Data/ExcelAggregateTool.cs` | **新建** |
| `OfficeCopilot.Host.Excel/Tools/Data/ExcelReconcileTool.cs` | **新建** |
| `OfficeCopilot.Host.Excel/Tools/Registration/ExcelBuiltInToolCatalog.cs` | 注册进 `CreateDataTools` |
| `OfficeCopilot.Host.Excel/OfficeCopilot.Host.Excel.csproj` | **手工添加 `<Compile Include>`**（legacy 格式，见附录 B） |

### 1.7 顺带：截断文案（已定：简单化处理，只改文案）

不做拒绝执行，只把 `IsTruncated` 时的提示改成带路径指引：

```
区域共 1000 行，已返回前 50 行。
如需统计、汇总或分组，请改用 excel_aggregate；如需了解表结构，请用 excel_profile_range。
不要分页遍历全表。
```

涉及 `ExcelReadRangeTool` / `ExcelReadWorksheetDataTool` / `ExcelReadTableDataTool` 的 `BuildContent`。

### 1.8 验收标准

- 1000 行 × 8 列的表，`excel_aggregate` 单次调用返回汇总结果，耗时 < 500ms
- `excel_profile_range` 返回内容 < 1500 token，且与源数据行数无关
- ListObject 与 CurrentRegion 两种来源均能正确识别，返回体中 `sourceKind` 准确

---

## Step 2：备份补齐（承重墙）

### 目标

放弃硬约束之后，备份成为唯一安全网。当前它"失败不阻断 + 无还原能力"，必须补齐。

### 2.1 备份失败即阻断（修 bug）

现状：`ExcelBackupService.CreateBackup` 在工作簿未保存时返回 `Success=false`，调用方忽略该结果继续执行。

改法：`RequiresBackup = true` 的工具，在备份失败时**拒绝执行**，返回明确错误：

```
无法创建备份：当前工作簿尚未保存到磁盘。
请先保存工作簿（Ctrl+S）后重试；或改用只读分析工具。
```

同时检查 [BackupPathManager.cs](../OfficeCopilot.Agent.Runtime/Turn/BackupPathManager.cs) 与
`DefaultHostBridgeRequestHandler` 中的备份调用链，确认阻断在正确的层级发生（应在宿主侧工具执行前）。

### 2.2 备份点元信息

现状文件名是 `{文档名}_{4位哈希}/{HHmmss}.xlsx`，用户面对一堆 `143022.xlsx` 认不出来。

在备份目录同级写一个 `index.jsonl`，每行一条：

```jsonc
{ "backupPath": "...", "createdAtUtc": "...", "toolName": "excel_execute_vba",
  "riskLevel": "critical", "conversationId": "...", "messageId": "...",
  "userIntent": "把 A 列按分隔符拆成三列" }
```

`userIntent` 取当前回合的用户消息摘要，这是让用户认出备份点的关键字段。

### 2.3 `excel_list_backups` 工具

列出当前工作簿的备份点（时间、触发工具、用户意图摘要），供 agent 在用户说"撤销刚才的操作"时定位。

### 2.4 面板"撤销本次修改"按钮（比工具更重要）

出事时用户是慌的，他要的是一个按钮，不是跟 agent 对话。

- 位置：[CopilotPanelControl.cs](../OfficeCopilot.Panel.WebView/CopilotPanelControl.cs) —— 在发生过写操作的助手消息卡片上显示
- 交互：点击 → 展示该备份点信息 → 二次确认 → 执行还原

**还原语义（重要）**：**另存为 `{原名}_还原_{HHmmss}.xlsx` 并打开**，不直接覆盖原文件。
理由：还原动作本身不应该是又一次不可逆操作。用户可以两个文件对比后自己决定用哪个。

### 2.5 备份粒度确认

`BackupPathManager` 当前语义是**每回合只备份一次**（metadata 里有值就跳过）。
配合 Step 3 的多阶段方案，跨多个 turn 的任务会产生多个备份点 —— 这是好事，等于自动 checkpoint，保持不变。
关键是 2.2 的元信息必须到位，否则多个备份点反而更难辨认。

### 2.6 涉及文件

| 文件 | 改动 |
|---|---|
| `OfficeCopilot.Host.Excel/ExcelBackupService.cs` | 写 `index.jsonl`；新增 `ListBackups` / `RestoreBackup` |
| `OfficeCopilot.Agent.Runtime/Turn/BackupPathManager.cs` | 备份失败的传播语义 |
| `OfficeCopilot.HostBridge.Server/DefaultHostBridgeRequestHandler.cs` | 备份失败时阻断工具执行 |
| `OfficeCopilot.Host.Excel/Tools/Data/ExcelListBackupsTool.cs` | **新建** |
| `OfficeCopilot.Panel.WebView/CopilotPanelControl.cs` + 前端 | 还原按钮 UI 与交互 |

### 2.7 验收标准

- 未保存的新建工作簿上调用 `excel_execute_vba`，被明确拒绝且提示保存
- 执行一次写操作后，面板出现还原入口；点击后生成 `_还原_` 副本并打开，原文件不受影响
- `excel_list_backups` 返回的条目能让用户凭 `userIntent` 认出是哪次操作

---

## Step 3：VBA 分步执行增强

### 目标

保留 VBA 的完整能力，通过**可观测性**而非**能力限制**来建立信任。

### 3.1 分步落中间表（推荐工作法，非硬约束）

`excel_execute_vba` 新增两个**可选**参数：

```jsonc
{
  "code": "...",
  "stageName": "02_按部门汇总",      // 可选
  "outputSheet": "_wip_02_汇总",     // 可选：本阶段结果写入的新表名
  "expect": {                        // 可选：执行后对 outputSheet 的断言
    "minRows": 10,
    "columns": ["部门", "金额合计"]
  }
}
```

行为：

- 提供了 `outputSheet` → 执行后**自动回读该表的画像**（复用 Step 1 的 `profile` 逻辑：行数、列名、前 5 行、数值列合计）作为工具返回值的一部分。模型不必再调一次读取工具。
- 提供了 `expect` → 不满足则工具返回失败，并回传**实际值**，让模型自己修正重试。
- 两者都没提供 → 行为与现在完全一致，向后兼容。

### 3.2 中间表生命周期

- 约定前缀 `_wip_`，创建时记账到会话状态
- 新增 `excel_cleanup_workspace` 工具：一键删除本次会话产生的所有 `_wip_*` 表
- 任务收尾时 agent 应主动给用户三选一：**保留（可审计）/ 合并到目标位置 / 清理**

> 中间表放在**同工作簿**（用户随手可见）而非独立工作簿。代价是会污染工作簿，因此收尾提示是必需的。

### 3.3 VBA 结构化日志回传

现状 `ExecuteVba` 只返回 `Success` / `Message` 两个 bit。

在 `BuildProtectedExecutionCode` 注入的模板中预置日志过程：

```vb
Public Sub NixerLog(ByVal msg As String)
    ' 写入隐藏表 __nixer_log__
End Sub
```

执行完毕由 .NET 侧读回并清理，结构化返回：处理行数、跳过行数、警告明细、耗时。

**价值**：让 VBA 从"下命令"变成"可对话的过程"。模型能根据"跳过了 37 行"这种反馈，
自己发现数据里有脏值并追问用户，而不是默默产出错误结果。

### 3.4 超时看门狗

现状 `_application.Run(...)` 同步阻塞、无超时、无取消。至少做到：

- 注入模板中设置 `Application.EnableCancelKey = xlErrorHandler`
- 模板中注入循环迭代计数上限守卫 + 定期 `DoEvents`
- 循环中检查一个 .NET 侧可置位的取消标志（用 `Names` 或临时文件传递）
- .NET 侧独立线程计时，超时后向用户提示"VBA 执行超过 N 秒，可按 Ctrl+Break 中断"

分步之后每段代码都短，卡死概率本身已大幅下降；此项是兜底。

### 3.5 顺带修复

[ExcelVbaService.cs](../OfficeCopilot.Host.Excel/ExcelVbaService.cs) 的 `OpenInVbaEditor` 中，
返回文案已被 ANSI 编码破坏成 `"????? VBA ??????"`，动到该文件时一并改回中文（UTF-8 无 BOM）。

### 3.6 验收标准

- 带 `outputSheet` 执行后，工具返回值直接包含输出表画像，模型无需追加读取调用
- `expect` 不满足时返回实际值，模型能据此自我修正
- 死循环 VBA 在超时后有明确用户提示，而非无声卡死
- `excel_cleanup_workspace` 能清干净本次会话的所有 `_wip_*` 表

---

## Step 4：提示词决策树

### 目标

改写 [BuiltInHostPrompts.cs](../OfficeCopilot.Settings/BuiltInHostPrompts.cs) 的 `ExcelAgent`，
参照同文件 `WordAgent` 的分阶段写法。当前两句话的版本反而在鼓励错误行为。

### 4.1 需要写入的规则

**阶段一 · 先量规模，不要先读数据**

- 用 `excel_list_tables` / `excel_get_range_info` 拿行列数
- 用 `excel_profile_range` 拿列结构、类型、去重数、Top-K
- **禁止**上来就 `excel_read_range` 拉数据

**阶段二 · 按规模与意图选路**

| 条件 | 路径 |
|---|---|
| 用户只要一个答案 | `excel_aggregate`，**不写任何东西到工作簿** |
| 用户要"活的"结果（源变则汇总变） | 写公式 / 透视表 |
| 批量、结构性、多步处理 | `excel_execute_vba`，优先分步落 `_wip_*` 中间表 |
| ≤ 200 行且需逐行判断 | 才允许直读全量 |
| > 200 行 | **禁止全量读取**，必须让 Excel / 宿主算 |

**阶段三 · 写入纪律**

- **公式必须先写单格验算，读回结果确认无误，再批量填充**（人类 Excel 高手的做法）
- 尽量分步落中间表不动原表；必要时才动原表，动之前说明影响范围
- **必须声明**：VBA 产出的是静态死值快照，源数据变化不会自动更新。
  用户需要自动更新时应改用公式或透视表。

**阶段四 · 验证纪律**

- 用 `excel_reconcile` 做总量对账 + 分组数校验 + 抽样比对
- **明令禁止逐行验证**
- 向用户报告验证结论，而非罗列数据

### 4.2 公式兼容性陷阱

`UNIQUE` / `FILTER` / `LET` / `XLOOKUP` / `SORT` 等动态数组函数在 Office 2019 及更早版本不存在，
模型很爱用。提示词中必须要求：先确认 Excel 版本，或默认只用 `SUMIFS` / `COUNTIFS` / `INDEX+MATCH` 等
传统函数。

### 4.3 涉及文件

`OfficeCopilot.Settings/BuiltInHostPrompts.cs` 的 `ExcelAgent` 常量（`ExcelAsk` 也需同步调整只读部分）。

---

## Step 5：Skill 两档分离

### 目标

用**能力剥夺**替代**行为劝导**。提示词说"优先用 X"是劝导，Skill 白名单是物理隔离，可靠一个量级。
复用现有的 [SkillPlanSupport](../OfficeCopilot.Agent.Runtime/Turn/SkillPlanSupport.cs) 白名单机制。

### 5.1 分档（简化为两档）

原方案三档过于复杂，且依赖 skill 路由精度。简化为：

| Skill | AllowedToolNames 含 | 触发 |
|---|---|---|
| `excel-analysis` | 只读工具 + `excel_aggregate` / `excel_profile_range` / `excel_reconcile`，**无任何写工具** | 用户在提问、查询、分析 |
| `excel-transform` | 上述 + 全部写工具 + `excel_execute_vba` | 用户要求修改文档 |

分档依据是 **"是否触碰既有数据"**，不是"是否用了 VBA"。VBA 在 `excel-transform` 里是一等公民。

### 5.2 路由可靠性的备选方案

Skill 分档依赖 [HybridSkillScorer](../OfficeCopilot.Agent.Planning/Runtime/HybridSkillScorer.cs) 的选择精度。
若实测路由不稳（该只读时选了 transform，或反之），**改为在面板上提供显式模式切换**
（分析 / 编辑），由用户决定。用户显式选择比模型推断更可控，且符合 Excel 用户的心智。

**此项待 Step 1–4 落地后，用真实模型实测再定。**

### 5.3 涉及文件

`OfficeCopilot.Agent.Planning/Runtime/OfficeSkillCatalog.cs` —— 修改 `CreateExcelReadAnalysisSkill`
并新增 transform skill；注意其中 `baseToolNames` 的基线工具列表也需同步补充新工具。

---

## 附录 A：被否决的方案

记录下来以免后续重复讨论。

### A.1 把 VBA 拆成两个工具（只写新表 / 可原地改）

**否决理由**：会无端损失模型最强的能力。模型写代码的能力远强于逐步硬执行的能力，
不应因安全考虑把 VBA 降级为受限工具。分步落中间表作为**推荐工作法**即可，不需要拆工具做硬约束。

### A.2 执行前后工作表快照指纹校验

**否决理由**：该机制的唯一用途是强制执行 A.1 的"只写新表"契约。A.1 被否决后它失去意义。
且它把**可观测性**和**安全性**错误地捆绑在一起 —— 分步的价值是可观测、可纠错、上下文不膨胀，
安全性应由备份负责，两者不该混为一谈。既然已有备份机制，就不必限制这么死。

### A.3 在 `SaveCopyAs` 临时副本、隐藏 Excel 实例中执行 VBA

**否决理由**：物理隔离最安全，但隐藏 Excel 实例的 COM 生命周期管理坑多，大工作簿复制开销明显，
且结果表 Copy 回主工作簿时的引用/格式/条件格式处理复杂。收益不抵成本。保留为远期选项。

### A.4 变更计划确认门（把逐工具确认改为一次性计划确认）

**暂缓，非否决**。当前 `PermissionGate` 逐 tool_call 弹窗，在数据处理任务中会弹 20 次，
用户疲劳后点"总是允许"，确认门形同虚设。正确做法是让模型先产出带**影响面量化**的结构化变更计划
（影响哪些区域、写入什么、多少格、是否可逆），一次性确认后 Apply 阶段不再逐个弹窗。

**暂缓理由**：改动最大，需动 Broker 协议与 [PlanModeTools.cs](../OfficeCopilot.Agent.Runtime/Turn/PlanModeTools.cs)。
先看 Step 1–5 的实际效果再决定是否投入。

### A.5 写工具的 `dryRun` 参数

**暂缓**。原设想是写工具支持 `dryRun: true`，返回"会影响哪些格、多少格当前非空会被覆盖、前 5 行试算结果"。
其核心价值（先小范围验证再放大）已由 Step 4 的"公式必须先写单格验算"规则和 Step 3 的
`expect` 断言覆盖。若实践中仍有需要再补。

---

## 附录 B：实施注意事项

### B.1 `OfficeCopilot.Host.Excel` 是 legacy csproj 格式

新增 `.cs` 文件**必须手工添加 `<Compile Include="..." />`** 到
[OfficeCopilot.Host.Excel.csproj](../OfficeCopilot.Host.Excel/OfficeCopilot.Host.Excel.csproj)，
不会自动包含。忘记会导致"文件存在但类型找不到"的诡异编译错误。

### B.2 构建必须用 VS MSBuild

`dotnet build` 在 VSTO 工程上会报 MSB4019/MSB4803。使用仓库根的 `build.ps1`：

```powershell
.\build.ps1 -Rebuild -Test
```

### B.3 Broker rebuild trap

`Agent.Broker` 是独立 exe，不是任何 VSTO 工程的依赖。若本方案改动了 `Agent.Runtime` /
`Agent.Planning`（Step 5 会改），必须确认
`Agent.Broker\bin\Debug\net472\OfficeCopilot.Agent.Runtime.dll` 时间戳新于源码，
否则 Excel 里跑的还是旧代码。`build.ps1` 已内置该校验。

### B.4 编码

所有新建与修改的文件一律 **UTF-8 无 BOM**。PowerShell 5.1 下：

- 写文件用 `[System.IO.File]::WriteAllText($p, $s, (New-Object System.Text.UTF8Encoding $false))`，
  **不要**用 `Set-Content -Encoding UTF8`（会写 BOM）
- 读文件用 `[System.IO.File]::ReadAllText()`，**不要**用无 `-Encoding` 的 `Get-Content`（按 ANSI 读会永久破坏 CJK）

### B.5 COM 对象释放

新增的 `ExcelAggregationService` 需遵循 `ExcelServiceBase` 既有的
`try/finally + ReleaseComObject` 模式。一次性 `Value2` 取数的写法本身就减少了 COM 对象数量，
但 `Worksheet` / `Range` / `ListObject` 引用仍需释放。

### B.6 测试

`OfficeCopilot.Agent.Tests` 是 legacy packages.config MSTest 工程，`dotnet test` 无效，需用 `vstest.console.exe`
（`build.ps1 -Test` 已封装）。

Step 1 的聚合/分组/类型推断逻辑应尽量做成**不依赖 COM 的纯函数**（输入 `object[,]`，输出汇总结果），
这样可以直接单测；COM 交互只负责取数与写回。这是本方案里唯一可被有效单测覆盖的部分，值得为此做分层。
