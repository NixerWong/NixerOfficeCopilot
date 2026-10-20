# Excel Agent 可信执行改造方案（Harness）

> 用途：**实施计划**。解决 Excel Copilot「执行不受约束、验证不闭环、失败不可见、改动无回归信号」的问题。
> 状态：历史方案；P0–P5 已完成，P6 已由 [EXCEL_VERIFICATION_HARNESS_PLAN.md](EXCEL_VERIFICATION_HARNESS_PLAN.md) 的 V8 方案取代。当前进度以新方案为准。制定日期：2026-09-09。
>
> 与 [EXCEL_DATA_AGENT_PLAN.md](EXCEL_DATA_AGENT_PLAN.md) 的关系：那份解决**能力缺失**（Step 1 的聚合/画像/对账工具已落地），
> 本份解决**能力可信**。两份互补，不重叠。
>
> 相关文档：[CODE_MAP.md](CODE_MAP.md) · [ARCHITECTURE.md](ARCHITECTURE.md) · [REMEDIATION_PLAN.md](REMEDIATION_PLAN.md)

---

## 目录

1. [诊断结论](#1-诊断结论)
2. [设计原则](#2-设计原则)
3. [阶段总览](#3-阶段总览)
4. [P0 度量与回归底座](#p0-度量与回归底座)
5. [P1 运行时闸门修复](#p1-运行时闸门修复)
6. [P2 守恒验证闭环](#p2-守恒验证闭环)
7. [P3 VBA 四件套](#p3-vba-四件套)
8. [P4 分段纪律与确认门](#p4-分段纪律与确认门)
9. [P5 技能分档与提示词](#p5-技能分档与提示词)
10. [P6 Eval 集](#p6-eval-集)
11. [进度追踪](#11-进度追踪)
12. [附录 A：已核实的代码实况](#附录-a已核实的代码实况)

---

## 1. 诊断结论

Step 1 的工具（`excel_aggregate` / `excel_profile_range` / `excel_reconcile` / `excel_cleanup_workspace`）**已经存在**，
提示词决策树也已写入 `BuiltInHostPrompts.ExcelAgent`。所以「Agent 能力不好」**不是缺工具**，而是下面九条（制定本方案时的诊断）：

| # | 问题 | 证据 | 危害 |
|---|---|---|---|
| D1 | **Agent 循环没有轮次上限** | `maxToolRoundCount` 传入 `ExecuteModelTurnAsync` 后**函数体内零引用** | 分页爬全表不触发任何闸门，直至烧穿上下文 |
| D2 | **验证重试机制是死代码** | `CanRecoverFromVerificationFailure` / `RewindForVerificationFailure` / `RegisterVerificationFailure` / `BuildVerificationFailureSystemPrompt` / `BuildAutomaticVerificationArgumentsJson` **全部零调用点** | 验证失败后不回退重做，只把文本丢给模型自由发挥 |
| D3 | **工具「成功」信号不可信** | `expect` 校验失败仍走 `CreateSuccess`（`ExcelExecuteVbaTool`） | `Success=true` → 重置失败计数 → 标记执行步完成，错误被吞 |
| D4 | **约束默认方向是放行** | 白名单为空→全放行；`GetCapabilitiesAsync` 失败→**静默降级为空能力集** | 最需要约束时约束消失；宿主不可用时模型凭空编造 |
| D5 | **可观测性形同虚设** | `SkillPlanSupport.WriteDiagnosticLog` **方法体为空** | 无结构化 trace，无法回放、无法统计、无法归因 |
| D6 | **缺少真实 Office 端到端测试** | 当前有 422 个自动化测试及回合级 scripted-model 场景，但没有覆盖真实 Excel / COM / 加载项 / Broker / 模型联动的端到端验证 | 自动化仍不能替代真实 Excel 集成验收，V8 runner 负责补齐 |
| D7 | **失败原因被错误归因，模型并会把错误结论传给用户** | `CreateFriendlyVbaException` 把通用错误码 `0x800A03EC` 一律映射为「VBA 信任未启用」 | 2026-09-09 实测会话中，真因是注入代码编译失败，模型据此写了两次「被信任中心拦截」，并写进最终回答，把用户引向错误的排查方向 |
| D8 | **验证工具本身验不了东西** | `excel_get_range_style` 返回的是**命名样式**（「常规」），不含填充色；且 `StyleName` 被序列化成 `System.__ComObject` | 模型调它验证标红，拿到无关信息，于是**凭执行时的返回值声称成功** |
| D9 | **原生路径的能力缺口与 VBA 硬门叠加成死锁** | ① `excel_aggregate.derivedGroupBy` 只实现 `firstCharacter`，日期取月/季/年这类基本派生做不了；② `read-analysis` 的白名单和提示词都给了 `excel_execute_vba` 并教模型做 `_wip_` 分段计算，但该技能 `AllowHighRiskTools=false`，而硬门是 `AllowHighRiskTools && AllowVbaFallback`，工具**必被拦截**；③ 拦截文案写「ask the user to switch to a skill」，把出路指向用户 | 2026-09-10 实测：原生聚合表达不了分组 → 转 VBA → 被拦 → 文案不给自救路径 → **基本任务无法完成**。提示词承诺的工具与运行时闸门直接自相矛盾 |

**D6 是根因中的根因**：没有回归信号，任何提示词/策略改动都只能靠人在 Excel 里手点验证。

**D9 是「原则正确、落地半截」的典型**：§2.3 定的分档依据是「是否触碰既有数据」，而 `read-analysis` 只写了 `AllowVbaFallback` 没写 `AllowHighRiskTools`，等于把自己的白名单作废了；同时原生侧的能力又不足以补位，两头落空。

---

## 2. 设计原则

### 2.1 三条执行原则

1. **产物形态决定路径**，不是数据规模决定路径。
2. **执行引擎与验证引擎必须不同源** —— VBA 写的用 `excel_aggregate` 验，不用 VBA 也不用公式验。
3. **守恒量全量验，映射关系抽样验** —— sum/count/distinct 是 O(1) 聚合，永远全量；逐行比对才抽样，且必须分层。

### 2.2 路径决策树（每一"段"问一次，不是每个任务问一次）

```
Q1 需要落进工作簿吗？
   否 → excel_aggregate，结束（零副作用，结果即权威）

Q2 是"改变表的形状"还是"算一个值"？
   改形状 / 批量格式 → excel_execute_vba（首选，走四件套）
   算值 → Q3

Q3 结果需要随源数据自动更新吗？
   要 → 公式 / 数据透视表
   不要 → Q4

Q4 数据量级？
   中小 → 公式（写入后 aggregate 守恒校验）
   大到公式或 aggregate 跑不动 → excel_execute_vba（调 Excel 内核，走四件套）
```

一个任务内**必然混用引擎**，这是正常形态。

### 2.3 VBA 的定位：首选，不是回退

`AllowVbaFallback` 这个命名本身就是错的。"回退"意味着模型在**理解最差、上下文最脏、已烧掉多轮**时才拿到最高破坏力的工具。

VBA 应在任务开始时基于"这是结构性变换"被**主动选中**。它不可替代的地方：

- **调用 Excel 内核的唯一通道**：`RemoveDuplicates` / `AdvancedFilter` / `TextToColumns` / `Sort` / `PivotTable` 是原生 C++ 实现，零跨进程数据搬运；`excel_aggregate` 要把 10 万行整个搬进 .NET
- **一次性写入大区域**：`Range("A1:H100000").Value = arr` 一次 COM 往返写 80 万格
- **公式彻底做不到的**：批量格式化、合并单元格拆解、按分组插小计行、一维↔二维互转、跨簿搬运

代价是每次 VBA 调用必须自带**四件套**：不自证（换引擎验）、金丝雀（先跑样本）、注入前 lint、可中断可回滚。

### 2.4 中间表是"分段的物化边界"

它不在路径树上，是另一根轴。**先分段（轴 A），再对每段选引擎（轴 B）。**

三条切分判据（缺一不切）：

| 判据 | 理由 |
|---|---|
| **引擎切换处** | 跨引擎交接只能通过工作簿物化——`excel_aggregate` 读的是工作簿，VBA 的内存数组它读不到。**"验证换引擎"本身就要求中间表存在** |
| **守恒式变化处** | 一段一个守恒断言。段内塞多个操作，行数对不上时**无法归因到哪一步** |
| **不可逆操作前** | 删行/去重/覆盖前把前一状态留在 `_wip_` 里 = 免费的细粒度回滚点，比整簿备份好用（不用关文件重开） |

**不切**的情形：单一操作、纯只读、纯格式化、< 200 行且逻辑简单。判据：**只有一个守恒式的任务不需要中间表。**

### 2.5 确认门集中在 commit 点

`excel_execute_vba` 是 `RequiresConfirmation=true`。6 段任务 = 弹 6 次确认框 → 用户点到第三次就不看了 → **确认门的实际安全价值降到零**。

正确结构：`_wip_` 段静默执行，只有 commit（落回正式表/改原表）弹一次**内容具体**的确认。

> 判据：`outputSheet` 以 `_wip_` 开头 → 免确认；写入原表或正式表 → 强制确认 + 强制备份。

这条同时是**激励设计**：分段的路径更顺畅（不弹窗），不分段的要过闸门。模型会自然倾向分段，不必靠提示词反复叮嘱。

---

## 3. 阶段总览

| 阶段 | 内容 | 解决 | 依赖 | 可独立验证 |
|---|---|---|---|---|
| **P0** | 结构化 trace + 回放 harness | D5、D6 | 无 | ✅ 单测 |
| **P1** | 轮次上限、验证重试接回、能力失败不静默 | D1、D2、D4 | P0（用 harness 验） | ✅ 单测 |
| **P2** | `expect.conserve` 守恒断言 + 失败即失败 + 分层抽样 | D3 | 无 | ✅ 单测 + 手测 |
| **P3** | VBA 护栏：金丝雀 / 看门狗（lint 已取消） | — | P2 | ✅ 单测 + 手测 |
| **P4** | `_wip_` 免确认、`_wip_NN_` 命名、commit 强确认 | — | P3 | 手测 |
| **P5** | 技能按产物形态分档 + 提示词决策树重写 | — | P1–P4 | Eval |
| **P6** | Eval 集与指标 | — | P0–P5 | — |

**P0 必须先做。** 它是后面所有阶段的验收手段；跳过 P0 直接改 P1–P5，等于继续盲改。

**P2 与 P1 相互独立，可并行**（P1 在 Runtime，P2 在 Host.Excel）。

---

## P0 度量与回归底座

### P0-1 结构化 Trace

**问题**：`SkillPlanSupport.WriteDiagnosticLog` 方法体为空，三类诊断（host-context / tool-catalog / execution-plan）全部丢弃。

**改动**

| 文件 | 动作 |
|---|---|
| `Agent.Runtime/Turn/SkillPlanSupport.cs` | `WriteDiagnosticLog` 落地为写 JSONL |
| 新增 `Agent.Runtime/Diagnostics/TurnTraceWriter.cs` | 每回合一文件：`%LOCALAPPDATA%\NixerOfficeCopilot\Traces\{yyyyMMdd}\{conversationId}_{messageId}.jsonl` |
| `Agent.Runtime/BrokerTurnExecutor.cs` | 在工具调用前后写 `tool_call` 事件 |

**事件格式**（一行一个 JSON，UTF-8 无 BOM）

```jsonc
{"seq":3,"kind":"tool_call","tool":"excel_read_range","argsHash":"a1b2c3d4",
 "args":{"address":"A1:A50"},"ok":true,"errCode":"","ms":812,
 "resultChars":4210,"estTokens":1052,
 "skill":"read-analysis","step":"inspect","round":2}
```

必须包含的字段：`round` / `skill`+`step` / `argsHash` / `resultChars` / `errCode`。
这是后续所有度量与 replay 的唯一数据源。

**验收**
- 跑一次真实 Excel 会话，`Traces/` 下生成 JSONL，每次工具调用一行
- 字段齐全，中文不乱码（UTF-8 无 BOM）
- 写失败不影响主流程（`catch` 内静默，但只在此处允许）

---

### P0-2 确定性回放 Harness

**问题**：无 `IModelClient` 假实现，`BrokerTurnExecutor` 完整回合零覆盖。

**新增文件**（均在 `OfficeCopilot.Agent.Tests/Harness/`）

| 文件 | 职责 |
|---|---|
| `ScriptedModelClient.cs` | 实现 `IModelClient` / `IModelInvoker`，按脚本吐固定的 tool_calls 序列与文本；记录每轮收到的 system prompt 与 tools 集合供断言 |
| `FakeHostToolInvoker.cs` | 实现 `IHostToolInvoker`，用内存伪工作簿（列名/行数/若干单元格）响应 `excel_*`；支持注入失败与 `expect` 不满足 |
| `TurnHarness.cs` | 组装 `BrokerTurnExecutor` + 上面两个假件，跑一个完整回合，返回 `TurnExecutionSummary` + 工具调用轨迹 |

**场景用例**（`OfficeCopilot.Agent.Tests/Scenarios/TurnLoopScenarioTests.cs`）

| 用例 | 断言 | 状态 |
|---|---|---|
| `ToolTrail_ExecutesInScriptedOrderThenAnswers` | 工具按脚本顺序落到宜主，末轮无工具调用即收尾 | ✅ |
| `ToolFailure_ErrorMessageReachesModelContext` | 工具失败时，`ErrorMessage` 与备份提示**同时**进入模型上下文 | ✅ 回归 D7 |
| `RoundLimit_StopsToolCallsAndReportsMaxToolRounds` | 达上限后不再发起工具调用，`FinishReason == "max_tool_rounds"` | ✅ 回归 D1 |
| `RoundLimit_FinalTurnHasNoToolsAndDemandsSummary` | 收尾轮下发工具集为空，且 system 消息要求基于已有证据总结 | ✅ |
| `AskMode_ExposesReadOnlyToolsOnly` | Ask 模式下发的工具集无任何 `IsReadOnly=false` 工具 | ✅ |
| `ConsecutiveFailures_StopTurnAndReportFailure` | 连续失败达上限立即停手，`Success == false` | ✅ |
| `VbaExpectViolation_FailsTheToolCall` | `expect` 不满足时 `outcome.Success == false`，且失败原因不被结构化内容挤掉 | ✅ 已补（落为 `ToolFailure_StructuredContentDoesNotSuppressErrorMessage`） |
| `VerificationFailure_RewindsToMutation` | 触发 rewind，重跑 mutation 步，次数 ≤ `MaxVerificationRetryCount` | ✅ 已补（拆为重试与超限两个用例） |
| `CapabilitiesFailure_DoesNotSilentlyDegrade` | 回合以显式错误结束，而非空工具集继续（harness 已备 `CapabilitiesThrow` 开关） | ✅ 已补（`ErrorCode=host.capabilities_unavailable`，不再调模型） |

**已验收**：126/126 全绿，纯内存、不触网不触 COM、秒级完成。

> ⚠️ 实测记录：`packages.config` 工程不继承传递引用。因 `BrokerTurnExecutor` 构造函数可选参数的类型需要在编译期可见，
> 测试工程必须显式添加 `HostBridge.Client` / `Memory` / `Mcp` 三个 `ProjectReference`。
> 无工具调用的普通收尾，`FinishReason` 是 `"stop"`（来自 `DefaultModelInvoker`），不是 `"completed"`。

> ⚠️ `OfficeCopilot.Agent.Tests` 是 legacy packages.config MSTest 工程，必须用 `vstest.console.exe` 打已构建的 DLL，`dotnet test` 无效。见 [CODE_MAP.md §7](CODE_MAP.md)。

---

## P1 运行时闸门修复

### P1-1 `maxToolRoundCount` 生效

**问题（D1）**：`ExecuteModelTurnAsync(..., int maxToolRoundCount)` 的参数在函数体内零引用。
默认 `AgentRuntimeOptions.MaxToolRoundCount = 100` 形同虚设，`SKILL.md` 里的 `maxToolRoundCount: 5/20` 全是装饰。

**改法**

- 在 `while(true)` 循环末尾 `round++` 之后判断 `round >= effectiveMax` 即终止
- `effectiveMax = min(maxToolRoundCount, skillPlan.MaxToolRoundCount > 0 ? skillPlan.MaxToolRoundCount : int.MaxValue)`
- 终止时不是硬切，而是**追加一条 system 消息要求模型基于已有证据总结**（复用 `LoopGuardPolicy.BuildSystemPrompt` 的模式），`FinishReason = "max_tool_rounds"`

**文件**：`Agent.Runtime/BrokerTurnExecutor.cs`

**验收**：`PagingCrawl_StoppedByRoundLimit` 通过；`RuntimeTrace` 打印 `effectiveMax` 来源。

---

### P1-2 验证重试接回主循环 ✅ 已完成

**问题（D2）**：五个方法零调用点。

**落地情况 ✅**

1. **接入点在工具失败分支内**（`ExecuteToolCallAsync` 返回后、`consecutiveFailures` 硬停判断前）：
   `CanRecoverFromVerificationFailure` 且 `RewindForVerificationFailure()` 真的回退成功 →
   `RegisterVerificationFailure()` + 追加 `BuildVerificationFailureSystemPrompt`；
   重试预算用尽后改发 `BuildVerificationRetryLimitSystemPrompt`，明确禁止再试。

2. **先回退再记账，不是先记账再回退**。`VerificationToolNames` 里包含 `excel_read_range`
   这类日常读取工具，一次普通读失败不该烧掉验证重试预算。
   `RewindForVerificationFailure` 找不到「verify 步 + 前一个 mutation 步」时返回 false，
   此时不消耗任何预算，走普通失败路径。

3. **工具自证失败同样算验证失败**。P2-2 让 `expect`/`conserve` 不过时报
   `excel.execute_vba_expectation_failed`，但它来自 `excel_execute_vba`——一个**写入**工具。
   若只看 `IsVerificationTool`，「工具自己发现结果不对」反而拿不到重做机会，
   恰好把最可靠的失败信号排除在外。因此新增 `IsSelfVerificationFailureCode`。

4. **受控重试不计入 `consecutiveFailures`**（回退分支里自减一次）。否则两次验证失败
   就会撞上连续失败硬停，重试预算永远用不到第二次。

5. **回退/超限各落一条 `verification_retry` 轨迹**，否则回放时只能看到一次普通工具失败。

**文件**：
- `Agent.Runtime/BrokerTurnExecutor.cs`（接入点 + `WriteVerificationRetryTrace`）
- `Agent.Runtime/Turn/SkillPlanSupport.cs`（`IsSelfVerificationFailureCode` / 带 errorCode 的重载 / `HasExhaustedVerificationRetries`）
- `Agent.Tests/Harness/TurnHarness.cs`（`WithSkill` 注入测试专用技能，不依赖真实技能目录的措辞）
- `Agent.Tests/Scenarios/TurnLoopScenarioTests.cs`（新增 2 个用例）

**改法**：在 `ExecuteToolCallAsync` 返回后判断——

```
若 outcome 失败 且 SkillPlanSupport.CanRecoverFromVerificationFailure(skillPlan, outcome.ToolName)：
    skillPlan.RegisterVerificationFailure()
    skillPlan.RewindForVerificationFailure()      // 撤销 verify + 前一个 mutation 的完成标记
    追加 BuildVerificationFailureSystemPrompt(...) 到 modelMessages
    不计入 consecutiveFailures（这是受控重试，不是失控）
    continue
若已达重试上限：
    追加 BuildVerificationRetryLimitSystemPrompt(...)，禁止再试
```

**文件**：`Agent.Runtime/BrokerTurnExecutor.cs`

**验收**：`VerificationFailure_RewindsToMutation` 通过。✅ （实现为 `VerificationFailure_RewindsToMutationAndDemandsRetry`
与 `VerificationFailure_StopsRetryingAfterBudgetExhausted` 两个用例）

---

### P1-3 `pending verification` 死循环防护 ✅ 已完成

**问题**：`ShouldContinueForPendingVerification` 为真时 `continue`，若模型持续不调用验证工具，`CompletedExecutionStepCount` 不变 → **无限循环**。

**落地情况 ✅**

1. **这两条 `continue` 不走 `round++`**，所以 P1-1 的轮次上限根本拦不到它们。
   需要独立的 `pendingMutationNudges` / `pendingVerificationNudges` 计数，各上限 2 次。

2. **超限后放行收尾，但不当成功**：`FinishReason = "pending_step_unresolved"`、`Success = false`。
   静默放行会让未完成的写入/验证步骤悄悄消失，而这正是 D3 的同款毛病。

3. **补充文案需要补发一条 content delta**。此时正文已经流式发给用户，
   只改 `TurnExecutionSummary.Content` 的话面板上永远看不到这句提醒。

**文件**：`Agent.Runtime/BrokerTurnExecutor.cs`、`Agent.Tests/Scenarios/TurnLoopScenarioTests.cs`（新增 1 个用例）

---

### P1-4 能力获取失败不再静默降级

**问题（D4）**：`GetCapabilitiesAsync` catch 后返回空 `HostBridgeCapabilities`，本轮无任何 Excel 工具，模型只能凭空编造，用户看不出异常。

**改法**：失败时不返回空集，而是让本回合以显式错误结束：
`ErrorCode = "host.capabilities_unavailable"`，正文告知用户"无法连接 Excel 宿主，请重启加载项"。

**文件**：`Agent.Runtime/BrokerTurnExecutor.cs`

**验收**：`CapabilitiesFailure_DoesNotSilentlyDegrade` 通过。

---

## P2 守恒验证闭环

### P2-1 `expect.conserve` 守恒断言

**现状**：`ExcelVbaExpectation` 只有 `MinRows` / `MaxRows` / `Columns` —— **只能验形状，验不了守恒**。

**扩展后的契约**

```jsonc
"expect": {
  "minRows": 1,
  "maxRows": 100,
  "columns": ["部门", "金额合计"],
  "conserve": [
    {
      "function": "sum",              // sum | count | countDistinct | min | max
      "column": "金额合计",            // outputSheet 上的列
      "compareTo": {                  // 与源区域的同类聚合比对
        "worksheetName": "销售明细",
        "tableName": "",
        "address": "",
        "function": "sum",
        "column": "金额"
      },
      "tolerance": 0.01
    },
    {
      "function": "count",
      "column": "部门",
      "expectedValue": 8              // 或直接给定值（来自前置画像的 distinctCount）
    }
  ]
}
```

**改动**

| 文件 | 动作 |
|---|---|
| `Host.Abstractions/Excel/ExcelVbaStageContracts.cs` | 新增 `ExcelVbaConserveRule` + `ExcelVbaExpectation.Conserve` |
| `Host.Excel/ExcelHostAdapter.Facade.cs` | `EvaluateStageExpectations` 增加守恒规则求值，复用 `_aggregationService` |
| `Host.Excel/Tools/Write/ExcelExecuteVbaTool.cs` | `InputSchemaJson` 补 `conserve`；`BuildContent` 输出守恒对比明细 |

> ⚠️ `DataContractJsonSerializer` 不走构造函数，新增集合属性必须在 `[OnDeserializing]` 里初始化，否则为 `null`。见现有 `ExcelVbaExpectation.Initialize` 的注释。

**验收**：手测中给出错误的 `conserve`，工具返回实际值与期望值的差额，模型能据此自纠。

**落地情况 ✅**：与上述契约一致，另有三点实现决定：

- 两边的聚合都走 `_aggregationService.Aggregate`（无 `groupBy` 的全局聚合），
  而不是从 `OutputProfile` 里读 `Sum`/`DistinctCount`。理由是画像的数值统计只覆盖部分函数，
  用同一条聚合路径求值才能保证「输出表的 sum」和「明细表的 sum」语义严格一致。
- `tolerance` 不给或非正数时按 `1e-6` 处理。浮点求和不存在「精确相等」，
  容差 0 只会制造假失败；金额比对提示词里建议给 `0.01`。
- 结果新增 `conserveChecks`，**通过的比对也回传实际数字**。
  只回一句「期望校验：通过」是 D8 同类问题——让模型产生「已验证」的错觉却看不到验证了什么。

---

### P2-2 `expect` 不满足 → `CreateFailure`

**问题（D3）**：期望校验失败仍返回 `CreateSuccess`。

**改法**：`ExcelExecuteVbaTool.ExecuteAsync` 中，若 `result.ExpectationsPassed == false`，改走
`CreateFailure(request, <失败摘要>, "excel.execute_vba_expectation_failed", content: <完整 BuildContent 文本>)`。

保留完整 `content` 很重要——模型需要看到实际画像才能自纠，只给 `ErrorMessage` 不够。

**注意**：`BrokerTurnExecutor.BuildToolMessageContent` 目前**优先返回 `StructuredContentJson`**，失败时会把 `ErrorMessage` 挤掉。需一并修正为失败时拼接 `Content + ErrorMessage`。

**文件**：`Host.Excel/Tools/Write/ExcelExecuteVbaTool.cs`、`Agent.Runtime/BrokerTurnExecutor.cs`

**验收**：`VbaExpectViolation_FailsAndRetries` 通过。

---

### P2-3 `excel_reconcile` 分层抽样

**现状**：`sampleCount` 默认 5，但选样策略未定义（随机抽 5 行是自我安慰）。

**改法**：内置分层选样，固定抽这 5 类：

1. 最大分组（数据量大，最可能截断）
2. 最小分组 / 单元素分组（边界）
3. 含空值或错误值的分组（`profile` 的 `errorCount` 已知）
4. 首行与末行所属分组（off-by-one 经典位置）
5. `topValues` 中频次最高的取值

**文件**：`Host.Excel/ExcelHostAdapter`（`Reconcile` 实现）

**验收**：返回的 `Samples` 带 `SelectionReason` 字段，说明每条为何被抽中。

---

### P2-4 `excel_get_range_style` 修成可验证的格式回读 ✅ 已完成

**问题（D8）**：它返回的是单元格的**命名样式**（「常规」），不含 `Interior.Color`、字体、边框；
`StyleName` 直接 `Convert.ToString(comObject)` 得到 `System.__ComObject`。它在技能的 `VerificationToolNames` 里，
却无法验证任何格式结果——这比没有验证工具更危险，因为它让模型产生「已验证」的错觉。

**改法**：`StyleName` 取 `style.Name`；返回值增加实际生效的 `FillColor`（#RRGGBB）/ `FontColor` / `Bold` / `HasBorder`，
并在区域内格式不一致时明确返回 `mixed` 而不是静默取首格。

**落地情况 ✅**

1. **回读实际格式**，不再只回读命名样式：`FillColor` / `FontColor` / `FontName` / `FontSize` /
   `Bold` / `Italic` / `Underline` / `NumberFormat` / `HorizontalAlignment` / `VerticalAlignment` / `Borders`，
   外加 `CellCount`。命名样式保留但降级为附属信息，且 `StyleName` 改走 `style.Name`。

2. **mixed 是一等返回值，不是空值**。Excel 对区域内取值不一致的属性一律返回 `null`。
   如果把它折叠成 `false`／空字符串，模型会得出「没有加粗」「没有填充色」这类**方向相反**的结论。
   因此全部回读字段用字符串承载三态：具体值 / `mixed` / `unknown`，
   并额外回传 `MixedProperties` 列出所有不可下结论的字段，文案直接给出「缩到单个单元格再读一次」的动作。

3. **无填充必须靠 `ColorIndex` 判定**：`Interior.Color` 在无填充时返回白色 `16777215`，
   只看 `Color` 会把「没设过」误报成「设成白色」。先查 `ColorIndex == xlNone` 才输出 `none`。

4. **提示词补上回读义务**：凡技能允许 `excel_set_range_format`，提示词明确
   「调用不抛异常只证明调用成功，不证明单元格长对了」，要求写完格式后回读比对，
   并说明 `mixed` 的含义是「验证不充分」而非「未设置」。

**文件**：
- `Host.Abstractions/Excel/ExcelRangeContracts.cs`（`ExcelRangeStyleResult` 扩展 13 个字段）
- `Host.Excel/Analysis/ExcelStyleReadback.cs`（新增，纯计算翻译层）
- `Host.Excel/ExcelRangeService.cs`（重写 `GetRangeStyle`，拆出 `ReadNamedStyle` / `ReadEffectiveFormat` / `ReadBorderSummary`）
- `Host.Excel/Tools/Data/ExcelGetRangeStyleTool.cs`（描述 + 文案）
- `Agent.Planning/Runtime/SkillExecutionPromptBuilder.cs`（格式回读义务）
- `Agent.Tests/Excel/ExcelStyleReadbackTests.cs`（新增 9 个用例）

---

### P2-5 派生分组补齐 + 解除 read-analysis 的能力死锁 ✅ 已完成

**问题（D9）**：见 §1。原生路径表达不了，代码路径被拦，拦截文案又不给自救出口，三者叠加成死锁。

**改法（三处同时改，缺一仍是死锁）**：

1. **补齐原生能力** —— `ExcelAggregateDerivedGroup` 增加 `size` 字段；
   `transform` 从 1 个扩到 10 个：`firstCharacter` / `prefix`(size) /
   `year` / `quarter` / `month` / `monthName` / `yearMonth` / `day` / `dayOfWeek` / `numberBucket`(size)。
   日期类 transform 校验源列必须是日期格式列（`ColumnIsDateFormatted` 早已存在），否则报错并说明原因。
   同时修掉一个隐蔽 bug：原先 `ApplyDerivedGroupTransform` 只拿到**格式化后的文本**，
   现在改成接收**原始 Value2 + 是否日期列**，日期派生才能走 `DateTime.FromOADate`
   （并防御 Excel 1900 闰年 bug：序列号 <1 或 >2958465 直接返回空）。

2. **修掉技能自相矛盾** —— `read-analysis` 补上 `AllowHighRiskTools = true`。
   依据是 §2.3：分档看**是否触碰既有数据**。该技能已在提示词里禁掉
   `excel_write_range` / `excel_set_range_formulas` / `excel_clear_range` / `excel_set_range_format`，
   并强制 VBA 只能写 `_wip_` 新表，产物形态本就是「只读分析 + 可弃中间表」。

3. **拦截文案给出可执行出路** —— `GetSkillPlanViolationMessage` 改为三选一的有序指引：
   ① 先用原生工具重新表达（明确列出 derivedGroupBy 的全部 transform）；
   ② 调 `runtime.invoke_skill` 换到有代码执行权的技能后重试；
   ③ 都不行才向用户说明被拦原因。并显式禁止「在自己手里就有 `runtime.invoke_skill` 时让用户去切技能」。

**文件**：
- `Host.Abstractions/Excel/ExcelAggregationContracts.cs`（`Size` 字段）
- `Host.Excel/Analysis/ExcelAggregationEngine.cs`（`DerivedGroupTransforms` + 重写 `ApplyDerivedGroupTransform`）
- `Host.Excel/Tools/Data/ExcelAggregateTool.cs`（schema enum + 描述）
- `Agent.Planning/Runtime/OfficeSkillCatalog.cs`（`AllowHighRiskTools`）
- `Agent.Planning/Runtime/SkillExecutionPromptBuilder.cs`（transform 清单写进提示词，并要求「先查清单再下结论」）
- `Agent.Runtime/Turn/SkillPlanSupport.cs`（拦截文案）

**留下的问题**：`AllowsCodeExecution = AllowHighRiskTools && AllowVbaFallback` 这个双标志与运算本身就是陷阱——
两个语义重叠的标志，漏配任意一个都导致白名单静默失效且无任何告警。P5-2 合并为单一
`AllowCodeExecution` 时一并消除。

---

## P3 VBA 四件套

### P3-1 安全静态检查器（lint）—— ❌ 已取消

**取消决定（用户）**：VBA 静态检查没有必要，不做了。

**取消理由（补记）**：这一项与已落地的三道防线严重重叠，且性价比更差。
金丝雀 probe（P3-2）在真实样本上跑一遍，直接观察**行为**；`expect.conserve`（P2-1）
用另一条引擎核对守恒量；看门狗（P3-3）兜住死循环。三者拦的都是「代码实际做了什么」，
而 lint 拦的是「代码文本长什么样」——正则永远会有误报，而**误报的代价是模型被挡在
门外无路可走，也就是 D9 死锁的成因**。L1/L7（隐式引用）的价值最高，但它已经在
P4-1 的确认豁免判定里以「判不出来就要求确认」的**保守方向**实现了：判不准时
退化为多弹一次窗，而不是拒绝执行。L8（下划线标识符）是我们自己注入代码的 bug，
已在 P3-0 直接修好，不需要一个检查器长期站岗。

原方案（L1–L8 规则表）保留在 git 历史中，如果将来 VBA 失败模式出现新的聚集，
再按实测数据重新引入单条规则，而不是一次性上八条。

---

### P3-2 金丝雀执行（probe）

**思路**：不复制整个工作簿（太贵），而是**先在样本上跑**。

```
1. 取源表前 probeRows 行 → 复制到 _wip_probe
2. 同一段 VBA 在 _wip_probe 上跑
3. expect 守恒校验通过？
4. 通过 → 对全量跑，并删除 _wip_probe
   不通过 → 返回实际值供自纠，保留 _wip_probe 供检查
```

**契约扩展**

```jsonc
{
  "probe": {
    "rows": 200,                    // 样本行数，0/缺省 = 不做金丝雀
    "sourceWorksheetName": "销售明细"
  }
}
```

**改动**：`ExcelVbaStageContracts.cs`（新增 `ExcelVbaProbeOptions`）、`ExcelHostAdapter.Facade.cs`（`ExecuteVbaStage` 两段式执行）、`ExcelExecuteVbaTool.cs`（schema + 结果展示）。

**验收**：手测 1000 行表，故意写错列名的 VBA，probe 阶段即被拦下，原表未被触碰。

---

### P3-3 执行看门狗

**现状**：`ExcelVbaService.ExecuteVba` 通过 `_application.Run(...)` **同步阻塞**，无超时无取消。
已有的缓解只有 `Application.EnableCancelKey = 2`（Ctrl+Break 变成可捕获的 Err 18）。

**改法**：`ToolDefinition.TimeoutSeconds` 已存在（非只读默认 120s，见 `ToolSafetyPolicy.Normalize`），
但 VBA 执行路径未使用。在 `ExecuteVba` 外层加超时监视：超时后向 Excel 发送中断信号，
并返回 `excel.execute_vba_timeout`，提示模型把任务拆成更小的步骤。

> 已有的 Err 18 分支已经给出了正确文案（"如果是因为运行时间过长，请把任务拆成更小的步骤后重试"），复用它。

**验收（2026-09-13 已手测通过）**：`timeoutSeconds=15` 的有界循环在 **15,164 ms** 被掐表中断（`timedOut=true`），
返回 `excel.execute_vba_timeout`，`NixerLog "自然结束"` 未出现，宿主 `IsAvailable=true`，Sheet1 完好，无需杀进程。

> ⚠️ **实测澄清两点**：
> 1. 看门狗是**外部掐断**而非代码自觉退出——中断发生在任意一条语句之间，宏不会走 `End Sub`，也不会执行中断点之后的清理逻辑。因此**中断 ≠ 回滚**：若循环体在逐行写单元格，掐断前写入的内容会原样留在表里，得到一个残缺结果。这正是「分步写 `_wip_NN_*` + 每步守恒断言」的硬依据：中断时坏的是中间表，正式表不受污染。
> 2. 紧凑空循环**可以被中断**（手动 Ctrl+Break 多数同样有效）；真正难以中断的是阻塞在对话框、外部进程调用或 `DoEvents` 之外的系统调用上的代码。原「Ctrl+Break 通常无效」的说法过于绝对，已更正。

---

## P4 分段纪律与确认门

### P4-1 `_wip_` 免确认 / 正式表强确认

**现状**：`ExcelExecuteVbaTool.Definition` 是 `static readonly`，`RequiresConfirmation = true` 恒定。
`DefaultHostBridgeRequestHandler.PrepareToolExecution` 按 `definition.RequiresConfirmation` 决定是否弹窗。

**难点**：确认判定当前**基于工具定义**，不基于**调用参数**。需要参数级判定。

**改法**（参考 `DefaultToolDispatcher` 对 `runtime.powershell` 的处理——它已经有"按 command 内容做 per-call 判定"的先例）：

- 在 `PrepareToolExecution` 之前插入一个**参数级确认豁免**判定
- 判据：`excel_execute_vba` 且 `outputSheet` 以 `_wip_` 开头且**代码中无对非 `_wip_` 表的写操作** → 豁免确认
- 其余情况维持强确认，且确认文案改为具体描述（"即将把 `_wip_03_报表` 的 42 行写入新表 `季度报表`，原始数据不改动"）

**备份不豁免**：`RequiresBackup` 由 `ToolSafetyPolicy.Normalize` 对所有非只读工具自动置 `true`，保持不变。

**文件**：`HostBridge.Server/DefaultHostBridgeRequestHandler.cs`、`Host.Excel/Tools/Write/ExcelExecuteVbaTool.cs`

---

### P4-2 `_wip_NN_描述` 命名规范

**改法**：`ExecuteVbaStage` 校验 `outputSheet`——若以 `_wip_` 开头但不符合 `_wip_\d{2}_.+` 格式，
返回可自纠的错误。序号递增便于顺序审计与清理。

**文件**：`Host.Excel/ExcelHostAdapter.Facade.cs`

---

## P5 技能分档与提示词

### P5-1 按产物形态分档

**现状**：`OfficeSkillCatalog` 只有 `read-analysis`(read) 与 `transform`(write) 两档，
隐含按"是否允许 VBA"切分（`AllowVbaFallback`），**与用户意图正交**，导致技能命中后白名单与真实需求对不上。

**改为四档**（下表为**实际落地结果**，与本节初稿有一处刻意偏离，见其后说明）：

| 技能 | Category | 首选工具 | `AllowCodeExecution` | `MaxToolRoundCount` |
|---|---|---|---|---|
| `read-analysis` | read | `excel_profile_range` / `excel_aggregate` / `excel_reconcile` / `excel_execute_vba` | **true** | 12 |
| `live-result` | write | `excel_set_range_formulas` / `excel_validate_range_formulas` | false | 12 |
| `structural-transform` | write | **`excel_execute_vba`（preferred 首位）** | true | 16 |
| `bulk-format` | write | `excel_get_range_style` / `excel_set_range_format` / `excel_execute_vba` | true | 12 |

三个写档统一 `FallbackSkillNames = ["read-analysis"]`、`RequireVerification = true`、
`RequiresBackup = true`、`AllowHighRiskTools = true`。`transform` 档已删除。

**偏离说明**：初稿写的是 `readonly-analysis` + `AllowCodeExecution = false`，落地时改成
保留 `read-analysis` 之名并**保留代码执行权**，原因有二：

1. 名字：`read-analysis` 是 `AgentRuntimeOptions.DefaultSkillName`，也已被持久化进用户设置，
   改名会让存量配置指向一个不存在的技能。
2. 权限：初稿早于 P2-5。按 §2.1 原则①，分档依据是**产物形态**——`read-analysis` 的产物是
   「只读结论 + 可弃的 `_wip_` 中间表」，它从不触碰既有数据，因此没有理由收回代码执行权。
   收回它就是 D9 死锁的直接成因。白名单里同时**排除** `excel_write_range` /
   `excel_set_range_formulas` / `excel_clear_range`，保证"能算、不能改"。

每档的 `VerificationToolNames` 自然填对，执行计划的 `verify` 步才有确定的工具。

### P5-2 `AllowVbaFallback` → `AllowCodeExecution`

纯改名 + 语义澄清：表达的是"这档任务本来就要写代码"，不是"允许降级"。
涉及 `SkillDefinition`、`RuntimeSkillPlan`、`SkillPlanSupport.AllowsCodeExecution`、`BuiltInSkillLoader`（YAML key `allow-vba-fallback` 需保留向后兼容别名）。

**已落地**：同时消除了双标志陷阱——旧实现要求 `AllowHighRiskTools && AllowVbaFallback`
同时成立，两个语义重叠的标志漏配任一个都会让白名单静默失效且无告警。现在
`AllowsCodeExecution` 只看 `AllowCodeExecution` 一个标志；`AllowHighRiskTools` 保留它自己
的独立含义（可回滚的破坏性写入）。YAML 侧读取时 `allow-code-execution || allow-vba-fallback`，
写入时只输出 `allow-code-execution`。

### P5-3 `ExcelAgent` 提示词重写

删掉"VBA 是最后手段"的暗示，改成：

1. 路径四问（§2.2）
2. 分段三判据 + 每段一个守恒式 + commit 单独成段（§2.4）
3. 验证换引擎 + 守恒全量/映射抽样（§2.1）
4. 保留已有的公式兼容性约束（动态数组函数）

**文件**：`OfficeCopilot.Settings/BuiltInHostPrompts.cs`

---

## P6 Eval 集

20–30 条真实任务，每条配**程序化 rubric**（不用 LLM 判分）：

```yaml
- id: excel-groupby-1000
  fixture: sales_1k.xlsx
  prompt: "按部门汇总销售额，并给出占比"
  assert:
    trace: { read_range_calls: 0, total_rounds: "<=6" }
    answer_numbers_match: aggregate_baseline    # 与独立计算的基线比对
    workbook_unchanged: true
```

**六个指标**：任务成功率 / 轮次中位数 / token 中位数 / 越权工具调用率 / **验证覆盖率**（含 mutation 的回合中执行了 verify 的比例）/ **静默失败率**（`Success=true` 但断言不过）。

---

## 11. 进度追踪

| 阶段 | 项 | 状态 | 备注 |
|---|---|---|---|
| P0 | P0-1 结构化 trace | ✅ 已完成 | 新增 `Agent.Runtime/Diagnostics/TurnTraceWriter.cs`；`WriteDiagnosticLog` 已落地；工具调用写 `tool_call` 事件 |
| P0 | P0-2 回放 harness | ✅ 已完成 | 8 个循环场景 + 8 个聚合引擎用例 + 3 个契约反序列化用例；测试工程现引用 Host.Excel/Host.Abstractions（仅用纯计算类型，不碰 COM） |
| P1 | P1-1 轮次上限生效 | ✅ 已完成 | `ResolveEffectiveMaxToolRounds` + `ExecuteRoundLimitCompletionAsync`，`FinishReason="max_tool_rounds"` |
| P1 | P1-2 验证重试接回 | ✅ 已完成 | 先 rewind 成功才消耗重试预算；`expectation_failed` 等工具自证失败同走此路径；受控重试不计入 `consecutiveFailures` |
| P1 | P1-3 pending 死循环防护 | ✅ 已完成 | 催促各限 2 次；超限收尾并回 `pending_step_unresolved` + `Success=false`，补发 content delta |
| P1 | P1-4 能力失败不静默 | ✅ 已完成 | `GetCapabilitiesAsync` 失败返回 null，`PreparedTurnContext.CapabilitiesUnavailable` 直接终止回合，`ErrorCode=host.capabilities_unavailable`，**不再调模型** |
| P2 | P2-1 `expect.conserve` | ✅ 已完成 | `ExcelVbaConserveRule`/`ExcelVbaConserveSource` 落地；两边走同一条聚合路径；新增 `conserveChecks` 回传实际数字 |
| P2 | P2-2 expect 失败即失败 | ✅ 已完成 | `ExpectationsPassed=false` → `CreateFailure`（保留完整 content + structured）；`BuildToolMessageContent` 不再用 structured JSON 挤掉 ErrorMessage |
| P2 | P2-3 分层抽样 | ✅ 已完成 | `PickStratifiedSamples` 先占满必检层（最大额/负值/零值/最小额/首末组）再用均匀覆盖补齐；`ExcelReconcileSample.SelectionReason` 回传抽中理由 |
| P2 | P2-4 `excel_get_range_style` 可验证化 | ✅ 已完成 | 回读实际填充色/字体/数字格式/边框；区域内不一致显式返回 `mixed` 并列入 `MixedProperties`，不再折叠成"未设置" |
| P2 | P2-5 派生分组补齐 + 解除 read-analysis 死锁 | ✅ 已完成 | 2026-09-10 实测发现（D9）。derivedGroupBy 扩到 10 种 transform；`read-analysis` 补 `AllowHighRiskTools`；拦截文案给出 `runtime.invoke_skill` 出路 |
| P3 | P3-0 VBA 注入代码下划线标识符 + 错误归因 | ✅ 已完成 | 2026-09-09 实测发现，见附录 B |
| P3 | P3-1 VBA lint | ❌ 已取消 | 用户决定不做静态检查：真正的护栏是金丝雀 + expect + 看门狗（拦的是行为），lint 拦的是文本，误报会再次制造 D9 式死锁 |
| P3 | P3-2 金丝雀 probe | ✅ 已完成 | `ExcelStagingConventions.TryRedirectToProbe` + `CreateSampleWorksheet`；样本上跑通并满足（去掉行数下限的）expect 才动全量；失败保留 `_wip_probe_*` 供排查 |
| P3 | P3-3 看门狗 | ✅ 已完成 | `StartExecutionWatchdog` 到点 Ctrl+Break（注入代码已设 `EnableCancelKey=2`）；COM/TargetInvocation 两个 catch 均先判 `timedOut`，避免复现 D7 误归因。**2026-09-13 已手测验证**：15s 看门狗 → 15,164 ms 掐表中断，无「信任中心」误报 |
| P4 | P4-1 `_wip_` 免确认 | ✅ 已完成 | `StagedWriteConfirmationPolicy.IsExemptStagedVbaWrite` 接入 `PrepareToolExecution`；只写 `_wip_` 才豁免，`RequiresBackup` 不豁免 |
| P4 | P4-2 `_wip_NN_` 命名 | ✅ 已完成 | `GetStagedNameViolation` 在执行前拒绝不合规的 `outputSheet`，错误码 `excel.execute_vba_invalid_stage_name` |
| P5 | P5-1 四档技能 | ✅ 已完成 | `read-analysis` / `live-result` / `structural-transform` / `bulk-format`，`transform` 已删除；**偏离原方案**：`read-analysis` 保留代码执行权（见下） |
| P5 | P5-2 改名 `AllowCodeExecution` | ✅ 已完成 | 双标志合一，`AllowsCodeExecution` 只看单一标志；YAML `allow-vba-fallback` 仅保留读侧兼容 |
| P5 | P5-3 提示词重写 | ✅ 已完成 | 四阶段结构：先量规模 → 路径四问 → 分段纪律 → 验证纪律；删掉"VBA 是最后手段"的表述 |
| P6 | Eval 集 | 🔄 已由新方案取代 | 夹具、Runner 与指标改由 `EXCEL_VERIFICATION_HARNESS_PLAN.md` 的 V8 管理 |

### 11.1 历史状态（截至 2026-09-14）

当时全解决方案编译干净，`OfficeCopilot.Agent.Tests` **162/162 通过**。
截至该日 P0–P5 已落地，P3-1 取消；§11.3 记录的四项手工验证已全部通过。之后的测试数和 P6 状态以新方案为准。

### 11.2 剩余工作

P6 Eval 集已由 [EXCEL_VERIFICATION_HARNESS_PLAN.md](EXCEL_VERIFICATION_HARNESS_PLAN.md) 的 V8 取代；夹具、Runner、指标定义及当前未完成的真实 Excel 验收均在该方案维护，避免两份并行清单产生冲突。

### 11.3 手工验证（2026-09-14 全部通过）

> 四项均在真实 Excel 中验证通过：P3-3（2026-09-13），P3-2 / P4-1 / P4-2（2026-09-14）。

| 项 | 操作 | 期望 |
|---|---|---|
| P3-3 看门狗 ✅ | 用「有界但超时」的循环测（`Do While Timer - t0 < 300` + `timeoutSeconds: 15`）。模型会拒绝故意死循环，不能作为测入口 | 15,164 ms 掐表中断（`timedOut=true`），返回 `excel.execute_vba_timeout`；`NixerLog "自然结束"` 未出现；宿主 `IsAvailable=true`、Sheet1 完好、临时模块已清理；无「信任中心」误报（D7 回归通过） |
| P3-2 金丝雀 ✅ | 在千行表上跑一段引用了错误列名的 VBA | 在 `_wip_probe_*` 上失败并保留探针表，原表未被触碰 |
| P4-1 免确认 ✅ | 先输出到 `_wip_01_清洗`，再 commit 到正式表 | 前者无弹窗，后者弹窗 |
| P4-2 命名 ✅ | 让 `outputSheet` 传 `临时表` | 执行前即拒绝，错误码 `excel.execute_vba_invalid_stage_name` |

---

## 附录 A：已核实的代码实况

> 2026-09-09 逐一读码核实，用于纠正旧文档中的过时判断。

| 结论 | 证据 |
|---|---|
| ✅ Step 1 工具**已落地** | `ExcelAggregateTool` / `ExcelProfileRangeTool` / `ExcelReconcileTool` / `ExcelCleanupWorkspaceTool` 均存在并已注册进技能白名单 |
| ✅ 备份**已经会阻断** | `ToolSafetyPolicy.Normalize` 对所有非只读工具置 `RequiresBackup=true`；`PrepareToolExecution` 在备份失败时返回 `tool.backup_failed` 并拒绝执行。**旧文档 §1.5「备份失败不阻断」已过时** |
| ✅ 还原能力**已存在** | `ExcelListBackupsTool` / `ExcelRestoreBackupTool`，还原生成 `{原名}_还原_{时分秒}.xlsx` 不覆盖原文件。**旧文档「备份只写不读」已过时** |
| ✅ VBA 语法预检**已存在** | `VbaSyntaxValidator`（ANTLR，离线，无 COM），在 `ExecuteVba` 开头调用 |
| ✅ VBA 日志**已存在** | `NixerLog` 注入 + `__NIXER_VBA_LOG__` 分隔回传，上限 200 条 |
| ✅ Ctrl+Break 保护**已存在** | `Application.EnableCancelKey = 2` → Err 18 可捕获，不弹 VBA 调试器 |
| ❌ VBA **无超时无取消** | `_application.Run(...)` 同步阻塞，仅靠用户手动 Ctrl+Break |
| ❌ `maxToolRoundCount` **在模型循环中零引用** | `BrokerTurnExecutor.ExecuteModelTurnAsync` 签名收了参数但函数体不用 |
| ❌ 验证重试**五个方法零调用点** | `CanRecoverFromVerificationFailure` / `RewindForVerificationFailure` / `RegisterVerificationFailure` / `BuildVerificationFailureSystemPrompt` / `BuildAutomaticVerificationArgumentsJson` |
| ❌ `WriteDiagnosticLog` **方法体为空** | `SkillPlanSupport.cs` 末尾 |
| ❌ `expect` 失败仍 `CreateSuccess` | `ExcelExecuteVbaTool.ExecuteAsync` |
| ❌ 工具参数**零 schema 校验** | `InputSchemaJson` 只用于下发给模型；dispatcher 侧只做 `NormalizeArgumentsJson`（trim + 空则 `{}`） |
| 🟡 测试**缺少真实 Office 端到端覆盖** | 目前 422 个自动化测试含回合级 scripted-model 场景；仍待真实 Excel / COM / 加载项 / Broker / 模型联动验证，见 V8 Runner |

### 构建与测试提醒

```powershell
# 全量构建（VSTO 工程必须用 VS MSBuild）
$msb = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
& $msb 'NixerOfficeCopilot.sln' /nologo /v:m /p:Configuration=Debug /m

# 测试（legacy packages.config MSTest，dotnet test 无效）
$vstest = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -find '**\vstest.console.exe' | Select-Object -First 1
& $vstest 'OfficeCopilot.Agent.Tests\bin\Debug\OfficeCopilot.Agent.Tests.dll' /Logger:console`;verbosity=minimal
```

⚠️ **Broker rebuild trap**：改动 Runtime/Persistence/Broker 后必须确认
`OfficeCopilot.Agent.Broker\bin\Debug\net472\OfficeCopilot.Agent.Runtime.dll` 时间戳新于源码，否则 Excel 跑的是旧代码。
