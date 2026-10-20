# Excel 分析可信验证改造方案（Verification Harness）

> 用途：**实施计划**。在 [EXCEL_AGENT_HARNESS_PLAN.md](EXCEL_AGENT_HARNESS_PLAN.md)（P0–P5 已落地）的基础上，
> 把「验证」从「模型自己写代码自证」升级为「模型只声明断言、宿主用受信引擎执行」，并补齐口径、数字溯源、数据陷阱与 Eval。
> 状态（2026-10-05 更新）：最新真实 Excel batch `run-20261005-050814-2943154f` 在行范围端点与上下文预算告警抽取修复后的当前构建上 36/36 回合通过；12 个自动只读任务的 pass^k 与 pass@k 均为 12/12（100%），另有 13 条人工确认任务按设计跳过。验证覆盖率 36/36（100%），回答数字证据匹配 274/274（100%），A 档 rubric 失败与静默失败均为 0/36；可信档位 A 为 36/36。相较基线 `run-20261005-041813-fdfd69dc`，pass^k、pass@k、覆盖率、证据匹配率及 rubric 指标均无百分点变化；数字实例数由 282 降至 274，当前批次没有未匹配数字。历史 trace 全表复核发现行范围端点和上下文预算告警两类抽取误报并补上回归测试；相关测试 58/58 通过，Broker 已重建且 Runtime DLL 与测试输出一致。
> 写入类真实 Excel batch `run-20261005-075607-2525daa4` 另有 11 个手动确认任务，每个 3 轮，33/33 回合通过；pass^k 与 pass@k 均为 11/11（100%）。两个依赖尚未实现故障注入的恢复任务按设计跳过。该报告可信档位为 A=1、B=6、C=26，产出项验证覆盖率为 41.7%（10/24），因此此处 100% 表示任务级工作簿 rubric 通过，不代表每轮回答均达 A 级受信验证。交互竞态已修复，pending 响应在事件发送前注册；`TurnLoopScenarioTests` 19/19 通过。
> 2026-10-05 将信任覆盖验收扩展到全部 11 个非恢复写入任务（每项 3 轮，分项运行）：33/33 回合及工作簿 rubric 全部通过，账本产出项覆盖 50/50，可信档位 A=21、B=12、C=0。公式与结构变换任务均通过受信断言达 A；4 个格式任务均通过精确样式回读达 B（写入与回读处于同一 Excel 链路，保守封顶 B）。格式账本将 `excel_set_range_format` 作为产出，仅在同工作表、同区域、所有可读请求属性匹配的 `excel_get_range_style` 后计入覆盖；当前不可由样式工具回读的 wrapText/shrinkToFit 不会被误报成已验证。提交阶段的 VBA 写入也必须显式声明 `outputSheet/expect`。随后两个故障注入恢复任务完成真实 Excel/模型联动验收：小计修复 batch `run-20261005-103114-fc932eb0` 与订单键集修复 batch `run-20261005-103642-456f60e5` 均为 3/3 通过、可信档位 A=3/3、产出覆盖率 100%。VBE 语法错误现场追溯到预检漏过将保留字 `Sub` 用作变量名；离线预检已补充拒绝该类声明的回归检查。Agent 测试现为 476/476。
> 自动化与代码实现状态见第 17 节。OfficeCopilot.Agent.Tests 已用当前源码重建并全量通过 487/487（2026-10-06）；这关闭了 Agent 自动化单测未全量运行的缺口，但不替代 Excel COM/模型联动验收。主要未完成项：V7-2 真实 trace 误报观察；在后续当前版本 trace 中继续监测溯源误报率。当前只读及写入 batch 均没有未匹配数字，因此不能据此计算当前溯源误报率（N/A）。旧阶段测试数是各阶段快照，不代表当前全量测试数。
> 最新完整写入 cohort `run-20261005-151738-a760d6a6` 按当前 13 项任务定义各运行 3 轮：39/39 回合通过、产出验证覆盖 39/39、数字证据匹配 248/248；可信档位 A=27、B=12、C=0。39 个回合的用户可见信任页脚与结构化 `QualitySummary.TrustTier` 全部一致。此前唯一 C 的小计清理任务已修正提示中的 VBA 数组计数器要求，并省略 `moduleName` 使用唯一临时模块名；定向 3/3 均 A，完整 cohort 中该任务三轮均一次 VBA 成功。故障恢复任务也明确禁止文本键的 `count` 守恒设置，修正后定向 3/3 均观察到注入、失败断言和成功重试。对旧批次 3 个未匹配数字实例进行人工抽查：两个重复的 `151` 均可由工具结果中的 `A1:C151` 佐证，现以隔离的范围跨度证据类型匹配，只能用于明确的含表头总行数/范围跨度声明；日期序列值 `45365` 无工具来源，拒绝正确。该小样本包含重复运行且跨旧提示，不能视为当前溯源误报率估计。数字匹配率不是溯源误报率，V7-2 观察期也尚未完成。
> 2026-10-06 对用户生成的桌面测试工作簿 `data.xlsx` 做只读产品测试：独立读取全部 1,000 行并确认 32 个姓氏的最高均值为韩姓（165090 / 28 = 5896.071428...）。用户复跑时人数合计 1000 的守恒断言通过，但 `excel_aggregate` 仍为 C；原因是此前只核对汇总总数，无法验证截断结果中的逐组值与排名。现增加独立分组复核：对单字段 raw/firstCharacter/prefix 分组、无额外行筛选及 sum/count/average 度量，COM 全量重算候选组并验证返回组值和 top-k 顺序；受组数与计算量安全上限限制，其他形状继续 fail-closed。对同一工作簿以自动表头、前 10 组重跑 Host 聚合入口，32/32 组复算；分组与排名断言通过，Tier A、独立、覆盖 1,000 行，TOP-10 首组仍为韩。全量 Agent 测试 487/487；Excel 加载项重建且内置 Host.Excel DLL 与当前 Host.Excel 构建一致。用户随后实际复跑仍为 C，原因与修复见下一条记录。
> 2026-10-06 08:19 用户通过实际加载项复跑仍显示 C。最新回合 trace 确认 `excel_aggregate` 请求省略可选 `filters` 字段（即无筛选）；`DataContractJsonSerializer` 将该字段留为 null，而 `TryResolveSupportedShape` 错误地把 null 判为不支持，导致退回人数守恒检查、跳过平均值独立复核。现已将 null 视为无筛选，并新增使用实际工具 JSON serializer 的回归测试；相关测试 7/7 通过，Excel 加载项已重建。用户随后在修复构建上复跑，结果见下一条。
> 2026-10-06 08:28 用户对同一 `data.xlsx` 与相同口径通过修复后的 Excel 加载项复跑，实际页脚确认为 A：Excel 原生引擎独立重算全部 32 个分组，逐组平均值与人数差异均为 0（容差 1e-6），分组数与排名核对通过；21 项断言通过，覆盖源数据 1,000 行。全程只读，未修改工作簿或生成中间工作表。该实测关闭了无筛选请求因 `filters=null` 导致跳过平均值独立复核的问题。
> 2026-10-06 用户按生日月份汇总及单独复核六月时再次得到 C。原因是自动分组复核此前只支持 raw/firstCharacter/prefix，拒绝 `month` 日期变换和额外筛选。现已加入月份键重建、Excel `MONTH` 原生条件公式，以及严格限定在同一日期列 `contains "-MM-"` 的筛选等价复核；文本日期、无效日期、其他筛选仍 fail-closed。月份形状、筛选语义、COM 条件公式及边界值定向测试 39/39 通过；测试程序集与 Excel 加载项均重建，三个输出位置的 Host.Excel DLL SHA-256 一致。真实 Excel 重跑尚待用户确认，不能据此预先宣称该用例已达 A。
> 2026-10-06 08:48 同一月份聚合已由 COM 对 12 组完成独立复算，25 项断言通过；但最终回答仍为 C，因为回答提到差额 `1214.094223046` 来自 `runtime.calculate`，对应回合 trace 实际只有 `excel_profile_range` 与 `excel_aggregate`，没有计算工具调用。C 是正确的 fail-closed 结果，不把文字声称当作工具证据。现收紧数字订正提示，并在第一次订正仍产生未溯源数字时最多再请求一次有工具权限的订正；仍无成功工具证据则保持 C。新增回归场景覆盖「先声称已计算但未调用 → 再实际调用 runtime.calculate」；相关运行时、证据账本、Excel 月份复核定向测试 83/83 通过。Excel 加载项及 Agent.Broker 已重建，Broker 输出的 Runtime DLL 与源码构建 SHA-256 一致。真实 Excel/Broker 回合复跑待用户确认。
> 2026-10-06 09:05 用户的再次核验回复仍显示 C：更正正文两次复述了未溯源的 `1,214`，即使语义是在说明删除，该字面数字仍被最终回答提取器识别；该回合 trace 仍无 `runtime.calculate` 调用。因此 C 仍符合当前字面证据规则。现进一步要求更正文本在没有成功工具证据前不得复述未溯源数字（即使在引用、否认或说明删除时），应称“该差额”；新增不复述原数字的明确撤回回归测试。相关 84/84 定向测试通过；用户关闭 Excel 后 Broker 已成功重建，输出 Runtime DLL 与源码构建 SHA-256 一致。真实 Excel/Broker 回合复跑待用户确认。
> 2026-10-06 在数字匹配器中加入受限的减法派生证据：回答须给出完整等式；两个精确操作数必须出自同一条用户/工具消息；宿主用 decimal 重算且结果完全一致后，派生结果才可匹配。跨消息操作数、无来源操作数或错误等式仍判未溯源。数字订正提示已改为优先接受这种可验证等式，不再强制为了简单减法调用 `runtime.calculate`；其他派生运算仍走原计算工具。数字匹配、证据账本、回合场景与计算工具相关定向测试 47/47 通过；用户保存关闭 Excel 后，解决方案及 Broker 已重建，全量 Agent 测试 502/502 通过，真实 Excel 差额场景复跑待确认。
> 2026-10-06 09:32 用户按生日年份汇总平均月薪再次得到 C。代码核查确认聚合引擎支持 `year`，但独立复核此前只支持 `month`，因此无法重建年份分组键并退回 C。现加入原生日期值的年份键重建，以及 COM `YEAR()` 分组条件，对完整年份组独立重算平均值、人数和排名；文本/无效日期及额外筛选仍 fail-closed。年份、月份、COM 条件及聚合服务定向测试 42/42 通过；完整解决方案构建，全量 Agent 测试 505/505 通过；Excel 加载项 Host.Excel 与源码构建、Broker Runtime 与源码构建的 DLL 哈希分别一致。真实工作簿重跑尚待确认。
> 2026-10-06 09:39 用户指出不应为每种日期粒度各自重做验证方案。现将日期分组复核统一到同一条流程，覆盖聚合引擎当前支持的 `year/quarter/month/monthName/yearMonth/day/dayOfWeek`：统一从原生日期值重建分组键，再转为 COM 日期条件公式全组重算；月名和星期名映射回数值日期部分，避免依赖 Excel 显示语言。保留现有筛选限制；未新增 hour/minute/second 聚合变换。日期服务、COM 条件及后端路由定向测试 44/44 通过；完整解决方案重建，全量 Agent 测试 507/507 通过；Excel Host 与加载项副本哈希一致，Broker Runtime 副本检查通过。真实工作簿重跑尚待确认。
> 2026-10-06 用户按 `性别=女`、姓名首字符分组统计人数仍得到 C。根因是独立聚合复核不支持常规源筛选。现复用聚合引擎的筛选编译与行判断，仅接受 COM 条件可等价表达的文本 `eq`；COM 侧使用显式文本等值条件（`ISTEXT` 加等值比较），避免 Excel 将数字/日期文本隐式转型。数值/日期字面量及其他运算符继续 fail-closed。筛选列参与分组键发现，且原筛选条件与组条件一并进入每个 COM 重算查询；读取与条件比较预算同步计入新增列/条件。聚合复核定向测试 16/16、COM 条件定向测试 49/49、全量 Agent 测试 512/512 通过；完整解决方案构建成功，Excel 加载项中的 Host.Excel 与源码 DLL SHA-256 一致。真实工作簿复跑待用户确认。
> 2026-10-06 用户指出不能为每种筛选和分组组合分别开发验证逻辑。现将独立聚合复核扩展为组合式路径：复用聚合引擎的筛选解析/行判断，并将已支持的 `eq/ne/contains/startsWith/in/gt/ge/lt/le/isEmpty/notEmpty/isNativeNumber/isTextNumber` 编译为 COM 条件；按源列类型验证可等价范围，日期格式文本筛选与现有日期分组变换组合处理，`numberBucket` 用观测到的桶边界编译成左闭右开数值条件。筛选、分组条件均进入每组 COM 重算，成本预算计入额外条件。混合或不兼容类型、桶标签碰撞、超预算，以及多分组维度/不支持度量等仍 fail-closed；不为单个问题增加专用分支。复核/公式/COM 后端定向测试 55/55、完整解决方案构建及全量 Agent 测试 518/518 通过。尚未在用户实际工作簿中复跑“姓名包含虎、按性别统计”；不能预先宣称产品回合已达 A。
> 2026-10-06 11:10 用户的“姓名以张开头，取最高月薪并回查记录”回合中，VBA 极值复核通过但 `excel_aggregate` 仍为 C。根因是自动复核只接受单字段分组及 `sum/count/average`，无法处理无分组全局汇总和 `max`。现加入全局标量聚合复核：扫描筛选/度量列并按数据类型编译同一套 COM 条件，逐度量核对全局输出；筛选后的 `min/max` 使用带 `ISNUMBER` 掩码的 Excel `AGGREGATE` 公式，极值列含文本、错误或日期值时保守拒绝，避免 Excel 与托管极值语义不一致。筛选条件数及极值公式额外比较计入预算。COM/标量聚合定向测试 59/59、完整解决方案构建及全量 Agent 测试 522/522 通过。尚未在用户工作簿中复跑此场景；产品回合是否升为 A 待真实 Excel 确认。
> 2026-10-06 11:32 用户决定暂停实际 Excel 工作簿只读复跑；相关产品场景仍记为待确认，不视为已验收。本阶段不再安排工作簿只读实测。
> 2026-10-06 11:37 用户通过显式 `--allow-workbook-write-tests` 运行 `formula-sales-tax-inclusive` 写入 Eval。Runner 对每轮创建独立夹具副本；3/3 轮通过，可信档位 A=3/3，产出验证覆盖 3/3。报告记录公式模板与写入区域断言通过；第 1 轮的 `excel_assert` 独立核对 10,000 行，差异为 0。该结果验证的是 Eval 夹具副本，不是用户原工作簿。
> 2026-10-06 用户反馈女性姓氏分析回答因 `22、25、28` 被降为 C。对应 trace 中 `excel_aggregate` 成功返回全部 32 个分组并按平均月薪排序；三个数字只出现在 Markdown 表格的“排名”列，是连续行序号而非业务指标，属于数字提取误报。现仅在表头明确为“排名/名次/位次/rank/ranking”且数据行序号从 1 连续递增时排除该列的结构性序号；非连续排名和其他列中的指标数字仍照常追溯。新增回归覆盖连续序号排除、非连续排名保留、指标数字继续匹配；数字提取与匹配定向测试 25/25、Agent 全量测试 534/534 通过。Agent.Tests 与 Agent.Broker 均通过 Visual Studio .NET Framework MSBuild 重建，Broker 输出的 Runtime DLL 与源码构建 SHA-256 一致。之前用 .NET SDK 的 MSBuild 重建失败是因为该工程依赖 Excel COM；已改用 Visual Studio 完整 MSBuild 验证。
> 2026-10-06 13:55 用户复跑女性姓氏均值分析仍为 C。最新 trace 表明，全局 `count(性别)` 的独立复核通过；失败在分组 `average(月薪)` 的 COM 独立重算，双重文本筛选（`性别=女` + 姓名首字符）使 `SUMPRODUCT` 将已带 `IFERROR` 的条件再次嵌套包装，Excel 返回 `#VALUE!`。现统一避免重复错误包装，并用于筛选和/或组合条件下的 sum、average 分母、count、min/max 公式。以纯内存 Excel 工作簿复现修复前双条件失败、修复后分组 sum=13,000、count=2、average=6,500；定向公式/复核测试通过，全量 Agent 测试 536/536。Excel 加载项已用 Visual Studio .NET Framework MSBuild 重建，输出 Host.Excel DLL 与源码 SHA-256 一致；未读取或修改用户工作簿，真实回合重跑待确认。
>
> 与旧方案的关系：旧方案解决「执行不受约束、失败不可见」，本方案解决「**验证本身是否可信**」。
> 旧方案 P6（Eval 集）由本方案 V8 取代。
>
> 相关文档：[CODE_MAP.md](CODE_MAP.md) · [ARCHITECTURE.md](ARCHITECTURE.md) · [EXCEL_DATA_AGENT_PLAN.md](EXCEL_DATA_AGENT_PLAN.md)

---

## 目录

1. [背景与诊断](#1-背景与诊断)
2. [已确定的设计决策](#2-已确定的设计决策)
3. [总体架构](#3-总体架构)（含 [3.2 代码落点约定](#32-代码落点约定)）
4. [线程模型与 COM 安全规则](#4-线程模型与-com-安全规则)
5. [阶段总览](#5-阶段总览)
6. [V0 COM 调用底座](#v0-com-调用底座)
7. [V1 托管后端：列投影 + 分块读取](#v1-托管后端列投影--分块读取)
8. [V2 COM 后端：Excel 内核标量指纹](#v2-com-后端excel-内核标量指纹)
9. [V3 验证框架：断言契约、路由与报告](#v3-验证框架断言契约路由与报告)
10. [V4 口径与可信度页脚](#v4-口径与可信度页脚)
11. [V5 最终回答数字溯源](#v5-最终回答数字溯源)
12. [V6 数据语义陷阱](#v6-数据语义陷阱)
13. [V7 安全加固与遗留清理](#v7-安全加固与遗留清理)
14. [V8 Eval 集](#v8-eval-集)
15. [性能目标](#15-性能目标)
16. [风险与待定事项](#16-风险与待定事项)
17. [进度追踪](#17-进度追踪)
18. [附录 A：已核实的代码实况](#附录-a已核实的代码实况)

---

## 1. 背景与诊断

旧方案落地后，执行链路已有闸门（轮次上限、验证重试、金丝雀、看门狗、`_wip_` 分段）。
下表记录制定本计划时识别的问题；当前完成状态以第 17 节为准。

| # | 问题 | 证据 | 危害 |
|---|---|---|---|
| E1 | **守恒断言无视截断（V1-4 已修复）** | 改造前：`ReadRaw` 超过 `MaxCellCount = 1,000,000` 即截断并置 `IsTruncated`；`ComputeScalarAggregate` 不检查该标志；`Rows.Count == 0` 时直接返回 `0` | 两边同时截断 → **假通过**，后半数据从未被校验；一边截断 → 假失败，引导模型去修不存在的错 |
| E2 | **验证代码与计算代码同源** | 现有路径允许「VBA 算、公式验」或反之，两者都由模型编写 | 口径误解（错列、漏筛选、错分组）在两边**同向出错**，验证必然通过 |
| E3 | **最终回答的数字无人核对** | 所有验证都针对工作簿状态；回答正文中的数字未与工具结果比对 | 四舍五入错、张冠李戴、凭空占比直接交付给用户 |
| E4 | **口径不可见** | 计算口径只存在于模型的思考中 | 用户无法发现「按下单日期还是发货日期」这类误解 |
| E5 | **数据语义陷阱未处理** | 画像不识别夹在明细中的合计行、隐藏/筛选行、文本型数字、多行表头 | 源数据本身就重复计数时，守恒断言**无法**发现 |
| E6 | **`_wip_` 免确认可被绕过** | `StagedWriteConfirmationPolicy` 只识别 `Set x = Worksheets("…")`；`With Worksheets("原表")` + `.Range(..).Value=`、CodeName（`Sheet1.Range`）、索引（`Worksheets(1)`）均被误判为只写中间表，且无测试覆盖 | 「改原表必须经用户确认」的承诺不成立（备份仍在，损失可恢复） |
| E7 | **真实 Excel busy HRESULT 尚未验证** | Excel dispatcher 已有模拟 HRESULT 自动测试；按讨论跳过真实 Excel 编辑态手测，模拟测试不能证明真实对象模型的 HRESULT 行为 | 用户处于单元格编辑态或有对话框时的实际重试行为仍未端到端验证，详见 V0 |
| E8 | **工具参数零 schema 校验** | `NormalizeArgumentsJson` 只做 trim；各工具用 `ReadInt(args, "x", default)` 静默兜底 | 模型传错类型时工具静默使用默认值，结果「成功」但不是用户要的 |
| E9 | **Eval 卡在夹具上** | 旧方案 P6 写「不打算凭空造夹具」 | 没有回归信号，任何改动的效果都无法量化 |

---

## 2. 已确定的设计决策

以下决策已在 2026-10-04 的讨论中确定，实施时不再重新论证。

| # | 决策 | 理由 |
|---|---|---|
| K1 | **模型只写断言，验证由宿主执行** | 声明式断言只有几个字段，出错空间远小于另写一段公式或 VBA；执行者是有单测的宿主代码 |
| K2 | **受信引擎 = 一层框架 + 两个计算后端**：COM 后端（Excel 内核）与托管后端（.NET） | 框架只负责契约、翻译、比对、覆盖判定，可完全单测；算术尽量交给 Excel 内核，不自己重造 |
| K3 | **标量指纹走 COM，分组 / 去重计数 / 集合差异走托管** | COM 的 `SUM`/`COUNT` 等是单遍 O(n) 原生实现，主线程只占用毫秒级；分组与集合运算放在 COM 会把 CPU 压在 Excel 主线程上 |
| K4 | **独立性规则：验证后端 ≠ 结果的生产者** | `excel_aggregate`（托管）的结果由 COM 复核；VBA / 公式 / 模型产出的结果，两个后端都可以复核 |
| K5 | **截断即失败** | 校验路径上任何不完整覆盖都返回「无法验证」，绝不给出一个看起来成立的数字 |
| K6 | **最低支持 Excel 2016** | 不依赖 `UNIQUE` / `FILTER` / 动态数组 / `Evaluate` 返回数组 |
| K7 | **可信度分 A/B/C 三档**，回答末尾由**运行时**（而非模型）追加一行校验状态 + 口径 | 页脚由确定性代码生成，模型无法美化 |
| K8 | **数字溯源**：回答中找不到出处的数字 → 要求模型订正一次；仍对不上则在页脚标注 | 正文已流式输出，订正以追加「更正」段的形式呈现，不撤回 |
| K9 | **SQLite 只读计算路径不纳入本方案** | 部署成本（x86/x64 原生依赖）与收益不匹配，后续按需另立 |
| K10 | **Eval 夹具用 C# 生成**（复用 `OfficeCopilot.OpenXml.Excel`），标准答案由生成器独立计算 | 不引入 Python；生成器不得引用产品侧聚合代码，保证答案独立 |

### 2.1 三档可信度定义

| 档 | 条件（全部满足） | 页脚文案示例 |
|---|---|---|
| **A级** | 本回合每个产出项都被宿主断言覆盖；验证后端与生产者独立；覆盖全量；全部通过；回答数字全部溯源 | `校验：A级：全量独立复核通过（3 项断言通过，覆盖 52,310 行）` |
| **B级** | 有验证，但存在以下任一：仅模型编写的公式 / VBA 回读；只有抽样覆盖；只能用同源后端复核；有数字经订正后才溯源 | `校验：B级：已复核，但仍有局限（分组明细只做了抽样复核）` |
| **C级** | 存在产出项没有任何验证，或断言无法执行（截断、超时、表达不了），或本轮自动追溯未能找到部分结论数字的直接证据 | `校验：C级：尚未完成有效验证 —— 有部分结论数字未能找到直接证据` |

回合档位 = 所有产出项中**最低**的一档。最终回答同时显示 A/B/C 级别和对应含义短语，避免只给用户一个无法解释的字母。

**文案记录（2026-10-06）**：用户可见状态暂用「A级：全量独立复核通过」「B级：已复核，但仍有局限」「C级：尚未完成有效验证」及对应原因。C 级遇到数字证据未匹配时，补充说明「有部分结论数字未能找到直接证据」并列出数字；这表示本轮自动追溯未能匹配证据，不代表工具结果中客观不存在该数字。后续再根据用户理解情况优化措辞。

**复现记录（2026-10-06）**：对桌面测试工作簿只读按「性别=女、姓名首字符」分组重算，共 32 组，`count(姓名)` 为 9–27，未出现 22、25、28。若这三个数表示该口径下的分组人数，则当前工作簿数据不支持；本次未取得原回合工具 trace，尚不能判断是否存在自动追溯漏匹配。

### 2.2 错误类型与对应防线

| 错误类型 | 例子 | 本方案的防线 |
|---|---|---|
| ① 口径错 | 错列、漏筛选、错分组 | V4：口径结构化并展示给用户 |
| ② 实现错 | 循环边界、空值、类型转换 | V3：独立后端守恒断言 |
| ③ 数据本身有问题 | 合计行、隐藏行、文本型数字 | V6：画像主动标记 |
| ④ 执行不完整 | 截断、超时、写了一半 | V1 / V3：截断即失败、覆盖报告 |
| ⑤ 转述错 | 回答数字与工具结果对不上 | V5：数字溯源 |

---

## 3. 总体架构

```
              ┌────────────────────── Agent.Runtime（Broker 进程）──────────────────────┐
              │  BrokerTurnExecutor                                                     │
              │   ├─ VerificationLedger   收集本回合各工具回传的 verification 元数据      │
              │   ├─ EvidenceLedger       收集本回合及历史 tool_result 中的数字           │
              │   ├─ runtime.calculate    确定性算术，结果计入证据                         │
              │   └─ 收尾：NumberGrounding → 订正一次 → TrustFooter（A/B/C + 口径）        │
              └──────────────────────────────▲──────────────────────────────────────────┘
                                             │ 工具结果中的 verification / basis 结构化字段
              ┌──────────────────────────────┴───── Host.Excel（Excel 进程内）──────────┐
              │  工具层：excel_assert（新）/ excel_execute_vba.expect / excel_reconcile /  │
              │          excel_aggregate（自动 COM 复核）/ excel_profile_range（陷阱标记） │
              │                         │                                               │
              │  验证框架（纯计算，可单测）                                               │
              │   ├─ AssertionContracts     断言与口径契约                                │
              │   ├─ VerificationPlanner    按函数、成本、独立性选择后端                    │
              │   ├─ Comparator             容差比对、差异明细                              │
              │   └─ VerificationReport     结论 + 覆盖 + 档位                             │
              │                         │                                               │
              │  IExcelCalcBackend ─────┼──────────────┐                                │
              │   ├─ ComCalcBackend     │              │ ManagedCalcBackend             │
              │   │   WorksheetFunction │              │   ExcelColumnReader（列投影 +   │
              │   │   标量 / 质量指纹    │              │   分块）+ 流式累加器             │
              │   └─ FakeCalcBackend（测试）           │                                │
              │                         │                                               │
              │  ExcelComDispatcher：忙碌重试 / 单次调用时长约束                          │
              └─────────────────────────────────────────────────────────────────────────┘
```

### 3.1 分层职责

| 层 | 职责 | 是否碰 COM | 测试方式 |
|---|---|---|---|
| 跨进程契约（`HostBridge.Contracts`） | 工具结果中 `verification` 字段的字段名与取值常量（宿主写、运行时读） | 否 | 往返契约测 |
| 宿主契约层（`Host.Abstractions/Excel`） | 断言、口径、覆盖报告、计算查询 DTO（只在宿主内使用） | 否 | 反序列化单测 |
| COM 底座（`Host.Excel/Com`） | 调度器、错误分类、列读取器；验证框架与普通工具共用 | 是 | 纯函数单测 + 手测 |
| 纯计算（`Host.Excel/Analysis`） | 现有聚合引擎 + 新增流式累加器 | 否 | 纯单测 |
| 框架层（`Host.Excel/Verification`） | 路由、翻译（断言 → 后端查询）、比对、档位判定 | 否 | 纯单测 + `FakeCalcBackend` |
| 后端层（`Host.Excel/Verification/Backends`） | 真正取数与计算 | 是 | 少量真实 Excel 手测 / 集成测 |
| 运行时（`Agent.Runtime/Verification`） | 汇总档位、数字溯源、页脚 | 否 | `TurnHarness` 场景测 |

### 3.2 代码落点约定

仓库没有单独的编码规范文档，现有约定分散在 [ARCHITECTURE.md](ARCHITECTURE.md) §2.2–2.3（L0–L5 分层、目录职责、UTF-8 无 BOM）、[CODE_MAP.md](CODE_MAP.md) §2/§7（工程清单、构建与测试）、`Directory.Build.props`（C# 10、`Nullable` 关闭）、`Directory.Packages.props`（仅对 SDK 格式工程生效的集中版本管理），以及代码中的既成做法。本方案在此基础上约定如下。

#### 3.2.1 Host.Excel 目录与 COM 边界

| 目录 | 放什么 | 可否调 COM | 命名空间 |
|---|---|---|---|
| `Analysis/` | 既有约定：纯计算（`ExcelAggregationEngine`、`ExcelTabularSnapshot` 等）。新增 `Analysis/Accumulators/` | **否** | `OfficeCopilot.Host.Excel.Analysis` |
| `Com/`（新） | COM 基础设施：`ExcelComDispatcher`、`ExcelComErrorClassifier`、`ExcelColumnReader` | 是 | `OfficeCopilot.Host.Excel.Com` |
| `Verification/`（新） | `VerificationPlanner`、`Comparator`、`VerificationEngine`、`DataQualityInspector` | **否**，只经 `IExcelCalcBackend` 取数 | `OfficeCopilot.Host.Excel.Verification` |
| `Verification/Backends/` | `IExcelCalcBackend`；`Com/` 子目录放 COM 后端，`Managed/` 子目录放托管后端（组合 `Com/ExcelColumnReader` 与 `Analysis/Accumulators`） | 仅 `Backends/Com/` | `OfficeCopilot.Host.Excel.Verification.Backends` |
| `Tools/Data/` | `ExcelAssertTool` 等新工具 | 经宿主门面 | `OfficeCopilot.Host.Excel.Tools`（既有做法：`Tools/*` 子目录不进命名空间） |

- `ExcelColumnReader` 放 `Com/` 而不是 `Verification/Backends/Managed/`：它通过 COM 读 `Value2`，且 `excel_aggregate` / `excel_profile_range` 也要用，不属于验证框架。
- `Backends/Com/` 里的纯函数（如 `SumIfsCriteriaBuilder`）可以放在该目录，但本身不得调 COM，以便单测。
- **门禁**：`Analysis/` 与 `Verification/`（不含 `Backends/Com/`）中不得出现 `Microsoft.Office.Interop.Excel`。每个阶段收尾时用下面的命令检查，结果必须为空：

```powershell
Get-ChildItem OfficeCopilot.Host.Excel\Analysis, OfficeCopilot.Host.Excel\Verification -Recurse -Filter *.cs |
  Where-Object { $_.FullName -notmatch '\\Backends\\Com\\' } |
  Select-String -Pattern 'Microsoft\.Office\.Interop\.Excel'
```

#### 3.2.2 跨进程契约

- `Agent.Runtime`（Broker 进程）**不引用** `Host.Abstractions`；V4 起运行时只从 `StructuredContentJson` 读取跨进程契约字段，不反序列化宿主 DTO，也不依赖宿主实现程序集。
- 运行时需要读取的字段，其**字段名与取值常量**统一定义在 `HostBridge.Contracts`（netstandard2.0，两个进程都能引用）的 `ToolResultVerificationFields`。做法与 `HostBridgeErrorCodes` / `HostBridgeOperationNames` 相同：只放常量，不放 DTO。原因是 Contracts 目前没有 JSON 依赖，而两侧序列化器不同（宿主用 `DataContractJsonSerializer`，运行时用 `System.Text.Json`）。
  - 宿主侧 DTO 写成 `[DataMember(Name = ToolResultVerificationFields.Tier)]`；
  - 运行时用 `JsonDocument` 按同一组常量读取；
  - `Agent.Tests/HostBridge/ToolResultVerificationContractTests.cs` 做往返测试：宿主 DTO 序列化后，运行时解析器能读出相同的值。
- `Host.Abstractions` 与 `Host.Excel` 各新增一条对 `HostBridge.Contracts` 的 `ProjectReference`（旧格式工程不会传递引用，两处都要加）。
- `Host.Abstractions/Excel` 只放宿主内部使用的 DTO（断言、口径、覆盖、计算查询），不重复定义上述常量。
- `_wip_` 前缀统一由 `HostBridge.Contracts/StagedWriteConfirmationPolicy.WipPrefix` 定义；V7-1 已将 `ExcelVbaStageContracts`、`ExcelStagingConventions` 与 Facade 改为引用该常量。

#### 3.2.3 工程格式

- `Host.Excel`、`Host.Abstractions`、`Agent.Tests` 是旧格式（legacy）csproj：**每个新增的 `.cs` 都要手动加 `<Compile Include>`**。漏加时文件不参与编译，测试也不会被发现。每个阶段的验收都要核对：新增的测试方法出现在 vstest 的结果中。
- 本方案**不引入新的 NuGet 包**（schema 校验器与 `runtime.calculate` 均自研），避免旧格式工程的 `packages.config` 与 SDK 工程的集中版本管理之间出现版本漂移。
- 旧格式迁移到 SDK 格式（ARCHITECTURE 的 E3）不在本方案范围内。

#### 3.2.4 测试落点

`Agent.Tests` 按源码位置组织目录，本方案沿用：

| 被测代码 | 测试目录 |
|---|---|
| `Host.Excel/Analysis`、`Host.Excel/Com` 中的纯函数 | `Agent.Tests/Excel/` |
| `Host.Excel/Verification` | `Agent.Tests/Excel/Verification/` |
| `Agent.Runtime/Verification` | `Agent.Tests/Verification/`（新） |
| `Agent.Runtime/Turn` | `Agent.Tests/Turn/` |
| `HostBridge.*` | `Agent.Tests/HostBridge/` |
| 回合循环场景 | `Agent.Tests/Scenarios/`，复用 `Agent.Tests/Harness/` 中的测试替身 |

#### 3.2.5 是否新建工程

| 候选 | 结论 | 理由 |
|---|---|---|
| 验证框架独立成工程 | **不建** | 只被 `Host.Excel` 使用；`Analysis/` 已是同类先例；测试工程已引用 `Host.Excel`；拆出去会多一个要随加载项部署的 DLL，还要先决定工程格式。等 Word / PowerPoint 需要复用时再拆 |
| Eval 夹具生成器 | **不建**，并入 `OfficeCopilot.OpenXml.SmokeTest` | 该工程已有 `--export-attachment-fixtures` 的同类先例，已引用 `OpenXml.Excel`，且是 SDK 格式的 net472 控制台 |
| Eval Runner | **新建 1 个** `OfficeCopilot.Eval`（保留仓库当前传统 csproj 格式，net472 控制台） | 要驱动 Broker 管道协议与 Excel 进程，放不进测试工程或加载项；用 PowerShell 处理 JSON 管道协议太脆弱。传统格式可正常构建；本方案不要求为它迁移到 SDK 格式 |

- `OfficeCopilot.Eval` 只能引用 `Agent.Client`（传输）与 `OpenXml.Excel`（读取结果工作簿）。**不得引用** `Host.Excel` 或 `Agent.Runtime`：rubric 必须与被测系统的验证、溯源代码相互独立，否则两者的错误会相关。
- 保持传统格式时，项目引用与运行时依赖须通过实际构建验证；若 SDK 风格被引用工程的 NuGet 依赖未自动复制，按仓库传统工程做法显式配置，不因此引入不必要的新包。
- 夹具生成器与 Runner 之间只通过文件（`*.xlsx`、`*.answers.json`、任务 JSON）交换数据，不共享代码。
- 新建目录或工程时，同步更新 [CODE_MAP.md](CODE_MAP.md) §2/§3 与 [ARCHITECTURE.md](ARCHITECTURE.md) §2.3，并把工程加入 `NixerOfficeCopilot.sln`（`build.ps1` 构建整个解决方案，无需另行登记）。

---

## 4. 线程模型与 COM 安全规则

### 4.1 现状（已核实）

- `NamedPipeHostBridgeServer` 全程 `ConfigureAwait(false)`，工具调用运行在**线程池线程**。
- 线程池线程访问 Excel 对象时，COM 将调用封送到 Excel **STA 主线程**排队执行，调用方同步等待。
- VBA 看门狗依赖这一点：`Application.Run` 阻塞的是线程池线程，计时器线程仍能发出 Ctrl+Break。

### 4.2 风险与规则

| 风险 | 规则 |
|---|---|
| **死锁**：UI 线程同步等待工具结果 | **R1**：任何代码不得在 Excel UI 线程上同步等待（`.Result` / `.Wait()`）HostBridge 或工具调用 |
| **主线程长时间占用** | **R2**：单次 COM 调用的预期耗时须 ≤ 200 ms。读取按块（≤ 200,000 格/次）；分组计算不放在 COM 上 |
| **计算量失控** | **R3**：禁止 O(n²) 的 COM 表达式（如 `SUMPRODUCT(1/COUNTIF(rng,rng))`）。带条件的 COM 调用须满足 `条件数 × 行数 ≤ 20,000,000`，否则转托管 |
| **Excel 忙碌** | **R4**：Excel 工具调用经 `ExcelComDispatcher`；仅对只读工具遇到的 `RPC_E_CALL_REJECTED (0x80010001)` / `RPC_E_SERVERCALL_RETRYLATER (0x8001010A)` / `VBA_E_IGNORE (0x800AC472)` 做有限次退避重试。这是工具级策略，不声称包裹每一个低层 COM 调用 |
| **强制重算** | **R5**：验证路径禁止调用 `Calculate` / `CalculateFull`。只读取 `Application.Calculation` 与 `CalculationState`，异常时在报告里标注 |
| **COM 调用无法取消** | **R6**：调用方以 `TimeoutSeconds` 放弃等待并返回超时错误；被放弃的调用仍会在主线程上跑完，所以 R2/R3 才是根本约束 |

这些规则写入 [CODE_MAP.md](CODE_MAP.md) 的 Excel 宿主章节，并在 code review 中检查。

---

## 5. 阶段总览

| 阶段 | 内容 | 解决 | 依赖 | 验收方式 |
|---|---|---|---|---|
| **V0** | `ExcelComDispatcher`：忙碌重试、错误码、调用时长埋点 | E7 | 无 | 单测；真实 Excel 忙碌态需经 HostBridge 诊断调用验证，不依赖 Panel 输入焦点 |
| **V1** | 托管后端：列投影 + 分块读取 + 流式累加 + 截断即失败；`excel_aggregate` / `excel_profile_range` 迁移 | E1 | 无 | 单测 + 手测 |
| **V2** | COM 后端：标量函数、质量指纹、重算状态；`IExcelCalcBackend` + `FakeCalcBackend` | E1、E5 前置 | V0 | 单测 + 手测 |
| **V3** | 验证框架：断言契约、路由、比对、报告；新工具 `excel_assert`；`expect.conserve` / `excel_reconcile` 迁移；`excel_aggregate` 自动 COM 复核 | E1、E2 | V1、V2 | 单测 + 场景测 + 手测 |
| **V4** | 口径契约 + 运行时 `VerificationLedger` + A/B/C 页脚 | E4 | V3 | 场景测 |
| **V5** | 数字溯源：`EvidenceLedger`、`runtime.calculate`、匹配器、订正一次 + 标注 | E3 | 无（页脚部分依赖 V4） | 单测 + 场景测 |
| **V6** | 数据语义陷阱：画像标记 + 提示词规则 | E5 | V1、V2 | 单测 + 手测 |
| **V7** | `_wip_` 运行时保护、参数 schema 校验、死代码与旧文档清理 | E6、E8 | 无 | 单测 + 手测 |
| **V8** | Eval：C# 夹具生成器、rubric、真实 Excel runner、指标 | E9 | 夹具生成器无依赖；rubric 用到 V4/V5 输出 | 跑通全量 Eval |

**建议顺序**：V0 → V1 ∥ V7 ∥ V8（夹具生成器）→ V2 → V3 → V4 ∥ V5 → V6 → V8（runner + 指标）。

V8 的夹具生成器建议**尽早**开始：它不依赖任何产品改动，越早有回归基线，后续每一阶段的效果越能量化。

---

## V0 COM 调用底座

### V0-1 `ExcelComDispatcher`

**问题（E7）**：用户正在编辑单元格或开着对话框时，COM 调用被拒绝，工具直接抛异常。

**设计**

V0-1 采用宿主调用策略而不是替每个 COM 调用点包一层 dispatcher：`DefaultHostBridgeRequestHandler` 在工具调用前查找可选的 `IHostCallPolicy`；仅 Excel 宿主在 `RegisterHostServices` 注册 `ExcelComDispatcher`。未注册策略的 Word / PowerPoint 等宿主保持原有调用路径。

- Excel 宿主注册服务时若当前尚无 `SynchronizationContext`，会在 UI 线程建立 WinForms 上下文；Dispatcher 捕获该上下文与线程，工具调用从其他线程进入时封送到 Excel UI 线程。Excel COM 工具当前同步执行后返回已完成的 `Task`。
- 将 `0x80010001`、`0x8001010A`、`0x800AC472` 映射为 `excel.host_busy`。工具 catch 块把原始异常传给 `ExcelToolBase.CreateFailure`，避免只传 `ex.Message` 后丢失 HRESULT。
- 只对 `ToolDefinition.IsReadOnly == true` 的工具重试；写入 / VBA 工具绝不做整体重试，以免 COM 在部分副作用后拒绝调用造成重复写入。
- 退避序列为 `50, 100, 200, 400, 800, 1600 ms`，即最多 7 次调用；只有识别到 busy HRESULT 或 `excel.host_busy` 才重试，其他错误原样返回 / 抛出。
- 每次调用记录耗时；超过 200 ms 写 `Trace` 警告。忙碌耗尽后返回 `excel.host_busy` 与可操作的中文提示。

**文件**

| 文件 | 动作 |
|---|---|
| `Host.Excel/Com/ExcelComDispatcher.cs` | 新增 |
| `Host.Excel/Com/ExcelComErrorClassifier.cs` | 新增，纯函数：HRESULT → 可重试 / 忙碌 / 其他 |
| `Agent.Abstractions/Runtime/IHostCallPolicy.cs` | 新增可选服务接口 |
| `HostBridge.Server/DefaultHostBridgeRequestHandler.cs` | 调用工具时套用 `IHostCallPolicy` |
| `NixerExcelCopilot/Composition/ExcelCopilotTaskPaneManager.cs` | 注册服务 |
| `Host.Excel/Tools/Common/ExcelToolBase.cs` + Excel 工具 catch 块 | 保留原始异常并映射 busy 错误 |
| `Agent.Tests/Excel/ExcelComDispatcherTests.cs` | 覆盖 HRESULT、错误映射、只读重试 / 写入不重试与退避上限 |

**验收**
- 自动：三种 HRESULT 分类、异常包装、工具层错误映射、只读重试 / 写入不重试、退避上限通过单测。
- 不要求通过 Panel 手测 F2 编辑状态：将焦点移到 Panel 输入框会结束 Excel 单元格编辑，无法从该输入框同时发起测试请求。
- 若需验证真实 Excel 对象模型实际返回的 busy HRESULT，后续应由独立诊断客户端在 Excel 保持编辑状态时直接经 HostBridge 发送 `host.invoke_tool`，再确认 `excel.host_busy` 与退避行为；该诊断客户端尚未实现，不能以当前单测替代真实 HRESULT 验证。

---

## V1 托管后端：列投影 + 分块读取

### V1-1 `ExcelColumnReader`

**改造前的问题（E1）**：`ReadRaw` 一次性读取整块区域，超过 100 万格即截断；守恒断言通常只用 1–2 列，却读了全部列。

**设计**

```csharp
internal sealed class ExcelColumnReadRequest
{
    public IList<string> Columns;          // 只读这些列（按表头名）
    public string HeaderMode;              // 复用现有表头推断
    public int ChunkCellBudget = 200000;   // 单次 Value2 读取的格数上限
}

internal interface IColumnChunkSink
{
    void Begin(ExcelColumnReadLayout layout);                    // 投影列、日期标志、数据行数
    void Accept(int firstRowIndex, object[][] columns); // 零起始数据行偏移；按投影列顺序提供列数组
    void End();
}
```

- `ExcelAggregationService` 继续负责解析 worksheet / address / ListObject，并把已解析的 `Range` 交给 reader；不复制一份 `ResolveSource`。因此 reader 请求、布局和 sink 是 Host.Excel 内部实现，不新增公共 `ExcelSourceRef`。
- 读取首行以构造列名，复用 `ExcelAggregationEngine` 的表头规则；自动推断只依赖首行是否全为非空文本，现有引擎对数据体的采样只影响解释文案，不影响是否判为表头。
- 按列名定位列索引；找不到列即报错，并列出可用列名。
- 每块读取 `rowsPerChunk = ChunkCellBudget / 实际读取列数` 行；连续列合并成一个 `Value2` 区域读取，稀疏列按连续段读取，避免逐列 COM 往返及无谓读入中间列。
- `ExcelComDispatcher` 当前在只读工具调用层处理 busy 异常，不逐块封装 COM 调用；每块以 cell budget 限制读取量。因 busy 导致工具级重试时，调用方必须为新一轮读取创建全新的 sink。
- 通用 reader 的读取上限为 `总行数 × 实际读取列数 ≤ 50,000,000`；超出时**明确失败**（`excel.source_too_large`），而不是截断。V1-4 的三个分析工具选择读取全部源列以保持既有整行空白判定，因此这些工具按全部源列计算上限。

### V1-2 流式累加器

先把 `ExcelAggregationEngine` 中 `Accept` / `Render` 一类的累加逻辑抽为可跨块复用的纯计算累加器，并用现有聚合/对账结果做回归。此阶段不直接实现 `IColumnChunkSink`：读块与累加状态的编排、以及工具迁移统一放在 V1-4，避免为未接入的各工具提前复制读取流程。

| 累加器 | 内存 |
|---|---|
| `ScalarAccumulator`（sum / count / countA / min / max / nonNumeric / errors / blanks） | O(1) |
| `GroupAccumulator`（按 key 的上述统计，支持派生分组） | O(组数) |
| `DistinctAccumulator` | O(不同值个数) |
| `KeySetAccumulator`（两侧 key 集合差异） | O(不同 key 个数) |

`Aggregate` 的分组计算和 `Reconcile` 的 key/total 累加复用这些实现；原快照入口与 V1-4 的流式入口共用累加状态。`excel_profile_range` 只保留请求数量的样本行，其余画像统计走分块读取与累加。排序检查属于 V3 `sorted` 断言；比较类型与空值规则届时一起定义，不在 V1 提前实现。

### V1-3 覆盖报告

V1 托管画像、聚合与对账都返回：

```csharp
public sealed class ExcelCoverageReport
{
    public int TotalRows;        // 数据体总行数，包含空白行
    public int ScannedRows;      // 实际扫描行数；必须 == TotalRows，否则计算失败
    public int NonNumericCount;  // 实际读取列中的非原生数值，包含文本数字和 Excel 错误
    public int TextNumberCount;  // NonNumericCount 中可解析为数字的文本
    public int ErrorCount;       // NonNumericCount 中的 Excel 错误
    public int BlankCount;       // 实际读取列中的空单元格
    public string Backend;       // "managed"
    public bool? CellCountsAvailable; // false 表示未枚举；null 表示旧报告未提供该字段
}
```

`excel_profile_range` 与 `excel_aggregate` 各返回一个报告；`excel_reconcile` 分别返回 `detailCoverage` 和 `summaryCoverage`。单元格计数按每个输入实际读取的列统计，每列只计一次；数据行不因空白而扣除。文本数字、错误值分别包含在 `NonNumericCount` 内，并在各自字段中单独给出。V1-4 为保持旧行为，三个分析工具都读取全部源列，并以“只有整行所有源列都为空才跳过”判定空行；因此报告计数和 50,000,000 格上限都覆盖全部源列，列投影的性能收益相应受限。行数覆盖不完整时以 `excel.incomplete_coverage` 拒绝返回计算结果。守恒路径 `ComputeScalarAggregate` 也携带覆盖信息，空数据不再作为数值 0 成功返回。

COM 后端可能直接对整段区域调用 Excel 原生函数而不枚举单元格；此时 `CellCountsAvailable=false`，分类计数不得解释为零值或「无异常」，并须在结果 `Notes` 明示。

### V1-4 工具迁移

- `excel_aggregate`、`excel_profile_range`、`excel_reconcile` 已改用 `ExcelColumnReader` 和流式 sink；对外参数不变，画像只保留请求样本行。
- 聚合与对账继续用全部源列判定整行是否为空，保留“只有整行所有源列都为空才跳过”的既有语义；连续源列块合并读取。此兼容选择意味着暂不获得只读少数分析列的投影收益。
- 已删除聚合服务的 `ReadRaw` 整表读取、100 万格/256 列截断路径；`IsTruncated` 字段保留但恒为 `false`（兼容契约），超出 reader 的 50,000,000 格上限时明确返回 `excel.source_too_large`。
- `ComputeScalarAggregate` 已返回覆盖报告；没有可参与计算的数据行时明确失败，不再把空输出当作 0。
- 验证：Host.Excel 与 Agent.Tests 构建通过，Agent.Tests **287 项全部通过**；尚未进行真实 Excel 大表端到端手测。

### V1-5（可选）回合内读取缓存

同一回合中「画像 → 聚合 → 断言」常读同一批列。可按 `(工作簿, 工作表, 地址, 列)` 缓存列数据，监听 `Application.SheetChange` 使相关缓存失效。**先不做**，等 V8 的 trace 证明重复读取确实是瓶颈再做。

**文件**

| 文件 | 动作 |
|---|---|
| `Host.Excel/Com/ExcelColumnReader.cs` | 新增 reader、内部 request/layout/sink；复用调用方解析好的区域；按连续列区域分块读取并校验 COM 返回矩阵尺寸 |
| `Host.Excel/Tools/Common/ExcelToolBase.cs` | 将 `excel.source_too_large` 映射为明确工具错误 |
| `Host.Excel/Analysis/Accumulators/*.cs` | 新增 4 个纯计算累加器 |
| `Host.Excel/Analysis/ExcelAggregationEngine.cs` | 快照与流式路径共用分组、画像、对账累加状态及覆盖统计 |
| `Host.Excel/ExcelAggregationService.cs` | 三个分析工具使用列读取器、流式 sink 并拒绝不完整覆盖；删除旧整表截断路径 |
| `Host.Excel/ExcelHostAdapter.Facade.cs` | `ComputeScalarAggregate` 返回覆盖信息；空数据不作为 0 通过 |
| `Host.Abstractions/Excel/ExcelCoverageContracts.cs` | 新增覆盖报告契约；V1-1 不新增公共 source contract |
| `Agent.Tests/Excel/ExcelColumnReaderTests.cs` | 新增：列解析、chunk cell budget、50M 边界、矩阵顺序/尺寸校验与错误码 |
| `Agent.Tests/Excel/ColumnAccumulatorTests.cs` | 新增：标量统计、distinct、跨多次 Accept 的分组、key 大小写合并及聚合/对账回归 |
| `Agent.Tests/Excel/ExcelCoverageReportTests.cs` | 新增：覆盖计数、文本数字/错误/空值及不完整扫描失败 |
| `Agent.Tests/Excel/ExcelAggregationStreamingSinkTests.cs` | 新增：画像、跨块聚合、对账 sink 与整行空白语义 |

**验收**
- 单测：列名解析与去重、缺失列提示、200,000 cell budget、1 / 7 / 1000 行分块无遗漏、50M 投影单元格边界与错误码。
- 累加器回归：数字/文本数字/错误/空值统计，分次累加，聚合结果与对账大小写及总计语义不变。
- 覆盖报告：各来源报告完整数据行数；对账分别报告明细和汇总；计数只覆盖实际读取列，不完整扫描明确失败。
- 手测（未执行）：20 列 × 20 万行的表，对单列求和不再截断，结果与 Excel 状态栏的求和一致；由于兼容整行判空，会读取全部 20 列。
- 回归：`ExcelAggregationEngineTests` 全部通过。

---

## V2 COM 后端：Excel 内核标量指纹

### V2-1 `IExcelCalcBackend`

已定义通用来源、计算查询、筛选、结果与成本估算契约。`ExcelSourceRef` 复用 worksheet/address/table 定位字段；`CalcCostEstimate` 由调用方给出数据行数、读取列数和投影总格数，供后端 `CanEvaluate` 判断是否适合执行。

```csharp
public interface IExcelCalcBackend
{
    string Name { get; }  // "com" | "managed"
    bool CanEvaluate(CalcQuery query, CalcCostEstimate cost);
    CalcResult Evaluate(CalcQuery query);
}

public sealed class CalcQuery
{
    public ExcelSourceRef Source;
    public string Function;              // sum | count | countA | min | max | countDistinct | ...
    public IList<string> Columns;        // 多列时为各列之和（宽表转长表的守恒）
    public IList<CalcFilter> Filters;    // eq / ne / gt / ge / lt / le / blank / notBlank / contains
    public string GroupByColumn;         // 为空表示标量
}

public sealed class CalcResult
{
    public double? Scalar;
    public IDictionary<string, double> Groups;
    public ExcelCoverageReport Coverage;
    public IList<string> Notes;          // 例如「源表为手动计算模式，数值可能未重算」
}
```

### V2-2 `ComCalcBackend` 支持范围

| 查询 | COM 实现 | 备注 |
|---|---|---|
| 标量 sum / count / countA / min / max，无筛选 | `WorksheetFunction.Sum / Count / CountA / Min / Max` | 区域为单列数据体（不含表头）；多列 sum 逐列执行后相加 |
| 带筛选的 sum | `SUMIFS` 或 `SUMPRODUCT` 条件公式 | 按条件类型选择等价公式；文本/日期及集合谓词使用公式条件，要求源列类型兼容、条件转义/长度及 R3 预算通过 |
| 带筛选的 count | `SUMPRODUCT` 条件公式 | 保持 `COUNT` 的数值计数语义；不使用 `COUNTIFS`，以免把文本值计为数值；须满足 R3 |
| 多列之和 | 对每列分别 `Sum` 后相加 | |
| 错误值个数 | `Evaluate("SUMPRODUCT(--ISERROR(<地址>))")` | 地址字符串须 ≤ 255 字符 |
| 通用分组查询 / 去重计数 / 集合差异 / 有序检查 | **不支持**，`CanEvaluate` 返回 false | 聚合验证器通过逐组 COM 标量查询独立复算，并把工具筛选编译为类型明确的公式谓词（如 `contains` → `textContains`）；直接传入通用 `CalcQuery.contains` 仍不支持，类型不兼容时拒绝验证 |

**`SUMIFS` 条件构造的坑**

- 文本 key 中的 `*` `?` `~` 必须用 `~` 转义，否则按通配符匹配。
- 条件字符串超过 255 字符时 `CanEvaluate` 返回 false。
- 数字条件的小数点受 Excel 区域设置影响（如德语环境用逗号）。构造时读取 `Application.International[xlDecimalSeparator]`，或改用 `"=" & 单元格引用` 的方式避免字面量。**实现时以真实多语言 Excel 手测为准。**
- `SUMIFS` 会把文本 `"1"` 与数字 `1` 视为相等，与托管后端的语义不同；比对出现差异时，报告里需给出这一可能原因。
- `CanEvaluate` 要求估算数据格数不超过 100,000（为 R2 的 100,000 行单列基准保留余量）；条件计算另须满足 R3 的 `条件数 × 行数 ≤ 20,000,000`。不满足时交给托管后端。

### V2-3 数据质量指纹（供 V6 使用）

对指定数据列，用几次 O(n) 调用得到：

| 指纹 | 计算 | 含义 |
|---|---|---|
| 非数值个数 | `COUNTA − COUNT − 错误值个数` | 非空、非数值且非错误值；包含文本型数字和逻辑值，不能据此区分两者 |
| 嵌套小计信号 | 无错误且无活动筛选时比较 `SUM(rng)` 与 `SUBTOTAL(9, rng)` | 差异仅是小计信号，不证明具体公式行；存在错误值时跳过，活动筛选时保留差值但不作归因 |
| 手动隐藏行 | 比较 `SUBTOTAL(3, rng)` 与 `SUBTOTAL(103, rng)` | 差值表示所选列中可能有手动隐藏的非空行；筛选隐藏行由 Excel 的 subtotal 行为忽略 |
| 筛选状态 | 分别记录 `Worksheet.AutoFilterMode` / `FilterMode` | 前者表示筛选箭头，后者表示当前有活动筛选，二者含义不同 |
| 重算状态 | 记录 `Application.Calculation`、`CalculationState` | 手动计算模式或状态不是 `xlDone` 时标记结果可能过期；指纹不会主动重算 |

实现边界：COM 指纹每次仅接受一个数据列、最多 100,000 行，且公式区域地址最多 255 字符；超限明确失败，不静默截断。COM 结果仍需在真实 Excel 2016 / Microsoft 365 中验证。

**V2-3 验收**
- 纯逻辑单测覆盖计数一致性、浮点差异容差以及活动筛选/缺失值时的小计状态；不模拟或声称验证 Excel COM 行为。
- 手测（Excel 2016 与 Microsoft 365 各一次）尚未执行：确认含文本型数字、错误值、`SUBTOTAL` 行、手动隐藏行及筛选状态的指纹结果，并测量 10 万行单列调用耗时。`SUM` 与 `SUBTOTAL` 差异只是信号，不可作为公式逐行识别或无条件重复计数的证明。

### V2-4 `FakeCalcBackend`

测试工程内的内存 `IExcelCalcBackend`，按 `CalcQuery` 在给定的二维数据矩阵上执行 `sum`、`count`、`countA`、`min`、`max`、`countDistinct`、`errorCount`，支持筛选和单列分组，并返回完整覆盖报告。可注入异常（包括 COM busy HRESULT）和结果 `Notes`，供后续框架层与 `TurnHarness` 场景测试使用；目前只作为可复用 fixture，尚未接入未实现的 V3 框架。

Fake 沿用托管聚合语义：`count` 统计非空值；`sum` / `min` / `max` 接受可解析的数字文本。数字矩阵值应使用 Value2 常见的 `double`，`int` 按现有 `ExcelValueSemantics` 解释为 Excel 错误值。它不模拟 Excel COM、公式或重算行为。

**文件**

| 文件 | 动作 |
|---|---|
| `Host.Abstractions/Excel/ExcelCalcContracts.cs` | 新增 `ExcelSourceRef` / `CalcQuery` / `CalcFilter` / `CalcCostEstimate` / `CalcResult` |
| `Host.Excel/Verification/Backends/IExcelCalcBackend.cs` | 新增 |
| `Host.Excel/Verification/Backends/Com/ComCalcBackend.cs` | 新增 |
| `Host.Excel/Verification/Backends/Com/SumIfsCriteriaBuilder.cs` | 新增，纯函数（转义、长度、区域设置） |
| `Host.Excel/Verification/Backends/Com/DataQualityFingerprint.cs` | 新增 |
| `Agent.Tests/Excel/Verification/DataQualityFingerprintTests.cs` | 新增，非数值计数一致性、差异容差及筛选下的未知状态 |
| `Agent.Tests/Excel/Verification/FakeCalcBackend.cs` | 新增，内存后端、筛选/分组、覆盖信息和异常/Notes 注入 |
| `Agent.Tests/Excel/Verification/FakeCalcBackendTests.cs` | 新增，聚合结果、成本估算、覆盖、筛选及注入行为 |
| `Agent.Tests/Excel/Verification/SumIfsCriteriaBuilderTests.cs` | 新增 |
| `Agent.Tests/Excel/Verification/ComCalcBackendTests.cs` | 新增：支持矩阵、成本估算及 R3 上限 |
| `Agent.Tests/Excel/ExcelCalcContractsTests.cs` | 新增：查询与结果契约的 DataContract 往返 |

**V2-4 验收**
- 纯逻辑测试覆盖聚合、筛选、分组、完整覆盖报告、成本估算拒绝，以及注入 Notes 和异常的行为；测试通过，不需要 Excel 手测。

### V2-5 托管计算后端

生产 `ManagedCalcBackend` 使用 `ExcelColumnReader` 按投影列分块读取，并复用 `ExcelAggregationEngine` 的流式累加器；不把整块数据装入快照。支持标量 sum / count / countA / countDistinct / average / min / max / errorCount、筛选、多列 sum 和单列分组。`countA` 按 Value2 可观察到的非空语义处理；公式返回空字符串与真实空白无法仅凭 Value2 区分。

`CanEvaluate` 校验查询形状和投影成本，硬上限与 `ExcelColumnReader` 同为 50,000,000 格。多列求和可用；分组查询只支持单个度量列，结果超过后端可表达范围时明确失败，不静默截断。托管计算覆盖所有数据行，按实际投影列给出分类计数。

**文件**

| 文件 | 动作 |
|---|---|
| `Host.Excel/Verification/Backends/Managed/ManagedCalcBackend.cs` | 新增，流式读取并映射 `CalcQuery` 到共享累加器 |
| `Host.Excel/Analysis/Accumulators/ScalarAccumulator.cs`、`Analysis/ExcelAggregationEngine.cs` | 支持托管 `errorCount` 聚合 |
| `Agent.Tests/Excel/Verification/ManagedCalcBackendTests.cs` | 新增查询成本、分块顺序/完整性、筛选、分组、错误值计数及平均值测试 |

**V2-5 验收**
- 纯逻辑流式 sink 与路由能力测试通过；真实 Excel COM 源解析及 Value2 分块集成尚未手测，但不阻塞 V3 开发。

---

## V3 验证框架：断言契约、路由与报告

### V3-1 断言契约

统一现有 `expect.conserve` 与 `excel_reconcile` 的表达，形成一套声明式词汇（约 8 个原语）：

| 原语 `kind` | 含义 | 典型用途 |
|---|---|---|
| `scalar` | 目标的某个聚合 == 源的某个聚合（或指定值） | 汇总前后 `sum(金额)` 守恒；去重后 `count` == 源 `countDistinct(key)` |
| `groupMatch` | 目标表每组的值 == 源表按 key 聚合后的值，**全组比对** | 分组汇总、透视结果、公式汇总表 |
| `keySet` | 目标与源的 key 集合差异（缺失 / 多余 / 交集个数） | 跨表匹配、拆表、合并 |
| `sorted` | 目标列有序 | 排序结果 |
| `rowCount` | `scalar` 的特例：行数 | 筛选、拆分 |
| `nonBlank` | 多列非空格数之和 | 宽表转长表 |

```jsonc
{
  "basis": { /* 口径，见 V4 */ },
  "assertions": [
    {
      "id": "total",
      "kind": "scalar",
      "target": { "worksheetName": "_wip_02_汇总", "column": "金额合计", "function": "sum" },
      "source": { "worksheetName": "销售明细", "column": "金额", "function": "sum",
                  "filters": [{ "column": "年份", "operator": "eq", "value": "2025" }] },
      "tolerance": 0.01
    },
    {
      "id": "byDept",
      "kind": "groupMatch",
      "target": { "worksheetName": "_wip_02_汇总", "keyColumn": "部门", "valueColumn": "金额合计" },
      "source": { "worksheetName": "销售明细", "keyColumn": "部门", "valueColumn": "金额", "function": "sum",
                  "filters": [{ "column": "年份", "operator": "eq", "value": "2025" }] },
      "tolerance": 0.01
    },
    {
      "id": "dedup",
      "kind": "rowCount",
      "target": { "worksheetName": "_wip_01_去重" },
      "source": { "worksheetName": "客户", "column": "客户编号", "function": "countDistinct" }
    }
  ]
}
```

`groupMatch` 一律全组比对，不抽样；只有逐行映射（例如 10 万行派生列）才使用 `excel_reconcile` 现有的分层抽样，并在报告中标为「抽样」（最高 B 档）。

契约以 DataContract DTO 表达 `{ basis, assertions }`，涵盖目标/源数据引用、表头模式、筛选、聚合字段、期望值、容差及排序方向；集合在 JSON 反序列化时初始化。DTO 只固定线格式，按 `kind` 的必填字段和互斥约束由 V3-2 planner 校验。

**文件**

| 文件 | 动作 |
|---|---|
| `Host.Abstractions/Excel/ExcelAssertionContracts.cs` | 新增验证计划、口径、度量、断言及断言侧 DTO |
| `Agent.Tests/Excel/ExcelAssertionContractsTests.cs` | 新增 JSON 往返和省略字段默认初始化测试 |

### V3-2 `VerificationPlanner` 路由

对每条断言的每一侧生成 `CalcQuery`，按以下顺序选择后端：

1. **独立性**：若该侧数据的生产者是托管后端（例如 `excel_aggregate` 的结果），则只能用 COM 后端复核。
2. **能力**：COM `CanEvaluate` 为 false 时（通用分组查询、去重计数、集合差异、类型不兼容的筛选或超过成本预算）用托管。
3. **默认**：标量优先 COM，其余托管。

若独立性要求与能力冲突（托管生产、但只有托管能算），则仍执行但标记 `independent = false`，该项最高 B 档。

**生产者如何得知**：被验证的表若是 `excel_aggregate` 写出的（当前不写表，仅返回结果），或是对 `excel_aggregate` 返回值的复核（V3-5），框架内部直接知道生产者；其他情况（VBA、公式、写入工具）一律视为「模型产出」，两个后端都可用。

**V3-2 实现边界**：

- `VerificationPlanner` 按断言类型生成独立的 target/source `CalcQuery`，并将 `basis.headerMode` 传递到成本估算和后端读取；成本估算由调用方提供，缺失时明确拒绝，不在 planner 中猜测工作簿行数。
- source 侧继承 `basis.source`；若 source 侧没有填写筛选条件，则继承 `basis.filters`。source 侧显式提供筛选时，以该列表替代 basis 列表；target 不继承 basis source 或 filters。
- `rowCount` target 是结构性数据行数，不接受筛选；`groupMatch`、`keySet` 不接受 `expectedValue`；缺失字段、互斥字段及后端不支持的查询都会明确报错。`keySet` 是严格相等断言，不相等时失败。
- `CalcResult` 用 `Keys` 表示 key-set、用 nullable `IsSorted` 表示排序结果；`rowCount` 的覆盖计数注明未读取单元格值。
- 托管 `sorted` 忽略空白，只对原生数值给出 true/false；文本、布尔值、错误值或其他不能可靠比较的值返回未知，避免用 .NET 字符串顺序冒充 Excel 区域排序语义。
- Fake 后端覆盖 rowCount / nonBlank / keySet / sorted 结果形状，供后续纯逻辑验证测试使用。

### V3-3 `Comparator` 与 `VerificationReport`

```csharp
public sealed class VerificationReport
{
    public bool Passed;
    public string Tier;                       // A | B | C（单项档位）
    public IList<AssertionOutcome> Items;     // 每条断言：期望值、实际值、差额、后端、是否独立、覆盖
    public IList<string> Diffs;               // groupMatch / keySet 的差异明细（最多 20 条 + 总数）
    public IList<string> Notes;               // 重算状态、类型语义差异等
}
```

- 容差：未给出或 ≤ 0 时取 `1e-6`（沿用 P2-1 的决定）。
- 任何一侧覆盖不完整（`ScannedRows < TotalRows`）、后端超时或忙碌 → 该项 `Passed = false`、`Tier = C`，原因写入 `Notes`。
- **通过的项也回传实际数字**（沿用 P2-1「不要只回一句通过」的决定）。

**V3-3 实现边界**：

- `Comparator` 接收每条断言的计划、target/source 结果及执行异常；空断言集、缺结果、无效/不完整覆盖、COM busy、超时和其他执行异常均不能通过，归为 C 档并写明原因。
- `scalar` / `rowCount` / `nonBlank` 比较 target 与 source 或期望值；普通 `groupMatch` 对两侧 key 并集逐组比较，key 使用 `Ordinal` 精确匹配；`keySet` 按 `Ordinal` 精确比较集合，区分缺失和多余 key。专用 `excel_compare_key_sets` 使用同一键值语义，但将双向差异作为成功报告的结果，并分别返回差异数和有界样例；不改变 `keySet` 相等断言的语义。V3-5 的 `excel_reconcile` 将原有大小写不敏感字典传入 Comparator，以维持该工具既有 key 语义；差异明细总计最多保留 20 条，同时保留完整 `diffCount`，并在 `Notes` 中提示截断。
- `sorted` 的未知结果不能通过；可判定的未排序结果是有把握的失败，不降为 C。Tier 表示证据可信度而非通过状态，因此完整且独立的确定性失败仍可为 A 档，非独立结果最高 B 档。
- `ExcelVerificationReport` / `ExcelAssertionOutcome` 为 DataContract DTO；数值结果回传期望值、实际值、差值和容差，分组/集合回传数量、后端、独立性与两侧覆盖。
- `keySetDifference` 仅供 `excel_compare_key_sets` 内部使用：完整扫描两侧后按 `Ordinal` 排序取最多 10 个确定性样例（默认 3 个，取首/中/末）；空单元格和空字符串按 keySet 查询既有规则排除，仅含空格的字符串仍参与比较；键值不忽略大小写或修剪空格。
- Agent.Tests 覆盖数值容差、分组全量差异、key-set、排序未知、覆盖失败、busy/超时、差异上限及报告 JSON 往返。

### V3-4 新工具 `excel_assert`

- 只读工具，`IsReadOnly = true`，不需确认，不需备份。
- 输入为 V3-1 的 `{ basis, assertions }`。
- 使用场景：公式写入后（`live-result`）、`excel_write_range` 后、对已有报表做核对、只读分析的结论复核。
- `VerificationEngine` 先按实际 source 区域、表头模式和查询投影列数估算成本，再调用 Planner 路由 target/source 后端，执行后交给 Comparator；成本估算不猜测行数。
- 注册到 Excel 数据工具目录，并加入四类 Excel 技能的允许、首选和验证工具列表。
- 断言不通过时返回 `excel.assertion_failed`、完整可读报告和 structured JSON；执行异常由 Comparator 作为不可验证结果报告。COM busy 会保留 busy 错误码，以便 Excel UI 线程调度器对只读操作退避重试。
- `excel.assertion_failed` 已纳入自我验证失败恢复判定；通过结构化执行计划的 Excel 验证步骤允许回到 mutation 步骤重试。
- Agent.Tests 覆盖工具注册与 schema、四类技能可见性、恢复错误码和既有 Planner/Comparator 行为。真实 Excel COM / Value2 集成仍需后续在 Excel 环境验证。

### V3-5 现有工具迁移

| 工具 | 迁移方式 |
|---|---|
| `excel_execute_vba` 的 `expect.conserve` | 每条旧 `conserve` 规则映射为统一的 `scalar` / `rowCount` 断言，继续接受旧字段；新增 `expect.assertions` 直接接受 V3-1 断言。阶段结果写入 `verification`，并保留兼容的 `conserveChecks` / `expectationFailures` |
| `excel_execute_vba` 的 `probe` | 金丝雀阶段同样走统一框架；source 指向抽样源表，输出指向 probe 输出表。固定 expected-value 规则不会误套到抽样数据，source 侧可计算断言仍可执行 |
| `excel_reconcile` | 复用现有流式累加器已计算的两侧 totals / groups，由 `Comparator` 做总量和全分组比较；保留原有大小写不敏感分组语义、缺失/多余兼容字段和分层抽样，不重新扫描工作簿 |
| `excel_aggregate` | 对未截断结果，将已返回 `sum` / 有列 `count` 分组值汇总为期望值，再由 COM 优先独立计算原始源区域（沿用源、筛选和表头口径）；聚合 `count` 按非空计数并映射为 COM `counta` 复核，普通计算查询的 `count` 仍保持 Excel `COUNT` 数值语义。复核结果写入工具结果的 `verification` 字段。COM 不支持的查询可退回托管后端并降为非独立；截断结果、`count(*)`（聚合会先跳过空白行）及其他函数明确标记未完整复核。其他 COM 与托管数值语义差异通过差异和 coverage notes 暴露 |

V3-5 纯逻辑和契约测试已覆盖期望反序列化、VBA schema JSON、表头模式传递，以及 reconcile 累加器结果进入统一 Comparator。全量 `build.ps1 -Test`：343/343 通过。真实 Excel COM / Value2 与 Excel 版本、区域设置相关的集成仍未手测。

### V3-6 工具结果中的 `verification` 字段

所有带验证语义的工具，在 `StructuredContentJson` 中统一输出 `verification` 摘要，并把原始逐断言结果放入 `details`，避免 V4 Ledger 为每种工具维护不同解析路径：

```jsonc
"verification": {
  "tier": "A",
  "passed": true,
  "assertionCount": 3,
  "coveredRows": 52310,       // 按不同 coverage 对象汇总 scannedRows；同一对象只计一次
  "independent": true,
  "sampled": false,
  "producer": "vba",          // vba | formula | managed | model | unknown
  "basis": { /* 口径，若有 */ },
  "notes": [],
  "details": {
    "items": [],
    "diffs": [],
    "diffCount": 0
  }
}
```

`coveredRows` 表示本次比较各不同 coverage 实例的已扫描行数总和，不是去重后的源工作表行数；跨断言重复读取同一范围会重复计入。`sampled` 描述 `details` 所代表的验证证据是否抽样；保留采样信息但同时做了全量比较的工具应为 `false`。未知产出来源使用 `unknown`，不得推断为模型产出。

这是 V4 页脚与 V8 指标的唯一数据源。字段名和 producer 值的常量定义在 `HostBridge.Contracts/ToolResultVerificationFields.cs`；`Host.Abstractions` 的 DataContract 与 `Host.Excel` 工具输出共用该契约。

V3-6 已实现：`excel_assert` 返回 `{ verification: ... }`；VBA 阶段、`excel_aggregate` 和 `excel_reconcile` 在原工具结果中输出相同 envelope，同时将完整报告放在 `details`。VBA 的新字段采用规范名 `verification`；旧的 expectation、probe、coverage 和分组兼容结果继续保留。VBA 阶段若在完整报告生成前早退，工具层会补充 C 档失败 envelope，避免遗漏 `verification` 或把缺失输出画像视为通过。producer 分别记录为 `model`、`vba`、`managed`；`excel_reconcile` 来源无法确定时记录 `unknown`。覆盖摘要、basis、早退回退和各工具结果字段的 DataContract/纯逻辑测试通过；V3 实现阶段测试快照为 375/375。真实 Excel COM / Value2 集成仍未手测。

**文件**

| 文件 | 动作 |
|---|---|
| `Host.Abstractions/Excel/ExcelCalcContracts.cs` | 扩展排序方向、key-set 与排序结果形状 |
| `Host.Abstractions/Excel/ExcelAssertionContracts.cs` | 新增比对 outcome 与验证报告 DataContract DTO |
| `Host.Excel/Verification/VerificationPlanner.cs` | 新增断言校验、查询生成及后端路由 |
| `Host.Excel/Verification/Comparator.cs` | 新增比较、完整性判定、差异摘要与可信度分档 |
| `Host.Excel/Verification/VerificationEngine.cs` | 新增计划 → 真实成本估算 → 路由执行 → 比对 |
| `Host.Abstractions/Excel/IExcelToolHost.cs` | 增加 `VerifyAssertions` 宿主契约 |
| `Host.Excel/ExcelAggregationService.cs` | 从真实源区域布局与投影列估算查询成本 |
| `Host.Excel/Verification/Backends/Com/ComCalcBackend.cs`、`.../Managed/ManagedCalcBackend.cs` | 补齐 rowCount / nonBlank / keySet / sorted 的能力边界 |
| `Agent.Tests/Excel/Verification/VerificationPlannerTests.cs`、`ComparatorTests.cs`、`ManagedCalcBackendTests.cs`、`FakeCalcBackendTests.cs` | 覆盖路由、比对、形状校验、排序未知语义及 Fake 结果形状 |
| `HostBridge.Contracts/ToolResultVerificationFields.cs` | 新增：`verification` 字段名与取值常量 |
| `Host.Abstractions/*.csproj`、`Host.Excel/*.csproj` | 新增对 `HostBridge.Contracts` 的 `ProjectReference` |
| `Host.Abstractions/Excel/ExcelAssertionContracts.cs` | 新增统一 `ExcelVerificationResult` envelope、coverage 汇总及 `excel_assert` 结果 wrapper |
| `Host.Abstractions/Excel/ExcelAssertionContracts.cs` | 补充断言执行结果与验证报告 DTO（`[OnDeserializing]` 初始化集合；后续按 HostBridge 契约常量统一字段名） |
| `Host.Excel/Verification/Comparator.cs` | 新增 |
| `Host.Excel/Verification/VerificationEngine.cs` | 新增，组织规划 → 执行 → 比对 → 报告 |
| `Host.Excel/Tools/Data/ExcelAssertTool.cs` | 新增 |
| `Host.Excel/Tools/Registration/ExcelBuiltInToolCatalog.cs` | 注册 |
| `Host.Excel/ExcelHostAdapter.Facade.cs` | 初始化验证引擎并转发 `VerifyAssertions` |
| `Host.Excel/Tools/Data/ExcelAssertTool.cs`、`Tools/Registration/ExcelBuiltInToolCatalog.cs` | 新增只读工具并注册 |
| `Host.Excel/Tools/Write/ExcelExecuteVbaTool.cs` | schema 增加 `expect.assertions`；输出 `verification` |
| `Host.Excel/Tools/Data/ExcelReconcileTool.cs`、`ExcelAggregateTool.cs` | 输出 `verification` |
| `Agent.Tests/Excel/ExcelAssertionContractsTests.cs`、`ExcelVbaExpectationContractTests.cs` | 覆盖 envelope 元数据、JSON 往返及 VBA / aggregate / reconcile 的规范字段形状 |
| `Agent.Planning/Runtime/OfficeSkillCatalog.cs` | 各 Excel 技能加入 `excel_assert` |
| `Agent.Runtime/Turn/SkillPlanSupport.cs` | `excel.assertion_failed` 接入验证失败恢复 |
| `Agent.Tests/Excel/Verification/ExcelAssertToolTests.cs`、`Planning/OfficeSkillCatalogTests.cs`、`Turn/SkillPlanSupportTests.cs` | 覆盖工具注册、技能白名单与失败恢复识别 |
| `Agent.Planning/Runtime/SkillExecutionPromptBuilder.cs`、`Settings/BuiltInHostPrompts.cs` | 提示词：验证一律写断言，不再自写验证公式或 VBA |
| `Agent.Tests/Excel/Verification/VerificationPlannerTests.cs` | 新增：独立性、能力、成本路由 |
| `Agent.Tests/Excel/Verification/ComparatorTests.cs` | 新增：容差、差异明细、覆盖不全即失败 |
| `Agent.Tests/HostBridge/ToolResultVerificationContractTests.cs` | 新增：宿主 DTO 序列化 → 运行时按常量解析，往返一致 |
| `Agent.Tests/Scenarios/TurnLoopScenarioTests.cs` | 新增：断言失败触发验证重试 |

**验收**
- 单测：路由矩阵全覆盖；两侧都截断时判为失败（E1 回归）。
- 单测：验证工具进入只读目录，四类 Excel 技能均允许、首选并识别为验证工具；`excel.assertion_failed` 进入结构化执行计划的失败恢复路径。
- 手测：故意让 VBA 漏掉最后一组 → `groupMatch` 指出缺失的组名与差额。

---

## V4 口径与可信度页脚

### V4-1 口径契约 `basis`

```jsonc
"basis": {
  "source": { "worksheetName": "销售明细", "tableName": "", "address": "" },
  "filters": [{ "column": "年份", "operator": "eq", "value": "2025" }],
  "groupBy": ["部门"],
  "derivedGroupBy": [
    { "column": "生日", "transform": "month", "alias": "出生月" }
  ],
  "measures": [{ "column": "金额", "function": "sum" }],
  "note": "按下单日期统计；不含退货"
}
```

- 出现在 `excel_assert` 与 `excel_execute_vba.expect` 中；派生分组需保留源列、transform、alias 及适用时的 size。`excel_aggregate` 的请求本身即可推导出口径，由工具自动生成。
- 提示词规则：**计算前先确定口径；先查看相关表头/画像；若字段有多个合理候选（特别是多个日期字段），或筛选、排除、统计含义仍不唯一，先用 `runtime.ask_user_question` 给出基于实际候选的选项并等待确认。用户已明确口径时不重复询问。**
- V4-1 已实现：Excel Ask/Agent 默认提示词与技能执行提示都要求计算和验证使用同一口径；Excel 技能允许调用 `runtime.ask_user_question`。`excel_assert` 与 VBA 的声明方式、`excel_aggregate` 以请求参数作为口径来源也已写入提示词。普通与派生分组均纳入口径 DTO、工具 schema、自动生成的聚合 basis 与可信页脚；自定义提示词仍由用户自行维护，不会被默认提示词覆盖。

### V4-2 `VerificationLedger`（运行时）

- 位于 `Agent.Runtime/Verification/VerificationLedger.cs`，每回合一个实例。
- 每次 Excel 数据/写入类工具调用完成后，用 `JsonDocument` 按 `HostBridge.Contracts` 中的常量从 `StructuredContentJson` 读取 `verification` 字段并登记（运行时不引用 `Host.Abstractions`，见 §3.2.2）。
- 同时登记产出项：非只读 Excel 工具的调用按工具目录的 `IsReadOnly` 标志识别；失败写入也作为“结果可能不完整”的产出项并降为 C。`excel_aggregate` 的只读结果本身是产出项，必须由同次调用的 envelope 通过自验证。
- 普通直接读取（工作表清单、单元格/区域读取、备份/表格清单、范围信息）登记为只读检查对象，不冒充待验证的计算产出；读取成功且没有派生计算产出时为 B（工具直接读取、未独立复核），任一读取失败仍为 C。汇总必须列明检查对象，范围读取显示工作表与地址。
- 回合档位按 §2.1 计算：每个产出项取其之后（同次调用也可）最近一次覆盖它的验证；后续写入会形成新产出项，不继承先前验证。未覆盖、未通过或 envelope 不完整均为 C；仅抽样或非独立验证最高为 B。

> 当前 coverage DTO 不含工作表名，因此目标名从工具参数推导：`excel_assert` 读取每条断言的 `target.worksheetName`，`excel_reconcile` 读取 `summaryWorksheetName`，VBA 读取 `outputSheet`，写入工具读取其目标工作表。只按工作表名匹配，不追求单元格级精度；目标无法明确时不猜测，记为 C。上下文、样式读取/格式化和清理等非数据工具不触发数据可信页脚。

- V4-2 已实现：Ledger 在 `BrokerTurnExecutor` 的模型工具调用与显式工具调用路径各自维护，使用工具结果 envelope 常量解析，不跨回合复用。纯逻辑测试覆盖 A/B/C 分档、同表与异表匹配、最近验证失败、抽样/同源降级、失败写入、缺 envelope 与普通非 Excel 调用。

### V4-3 页脚 `TrustFooter`

- 在 `BrokerTurnExecutor` 完成本回合工具/待完成步骤处理后，由运行时从 Ledger 生成并写入 `TurnExecutionSummary.Content`。运行时移除模型正文中自拟的 A/B/C 档位或“校验/口径”页脚，最终面板内容以该结构化回执生成的唯一状态为准，避免模型先报一个档位、运行时再追加另一个档位。
- 仅在本回合调用过 Excel 数据/写入类工具时生成；闲聊、纯问答以及仅上下文/样式/维护操作不生成。
- 格式（用分隔线与正文隔开；直接只读操作展示“检查对象”，计算/写入操作展示口径和验证目标）：

```
—
校验：A级：全量独立复核通过（3 项断言通过，覆盖 52,310 行）
检查对象：验证 excel_assert → 汇总!B2（步骤：核对销售总额）
口径：销售明细（筛选 年份=2025），按 部门 汇总 sum(金额)
```

- 有多个口径时最多列 3 个，其余写「等 N 项」。
- 多个检查/产出对象时逐项列出工具对象/工作表范围，并标明关联执行步骤；列表限长时显式显示其余项数。
- 档位为 C 时说明原因（取第一个未验证产出项或第一个失败断言）。
- 同时写一条 `trust_footer` 轨迹事件（tier、产出项数、验证数）。
- V4-3 已实现：A/B/C 状态与口径由纯函数构造，最多列出 3 个口径；派生分组的来源列、转换方式、别名与适用参数会展示在口径中；Broker 从最终内容中移除模型自拟验证档位，Panel 完成事件以运行时最终内容替换流式正文，防止出现两个互相冲突的档位。直接只读检查显示实际对象并按 B 档说明未做独立复核；多对象按范围列举。新增回归覆盖只读工作簿检查、重复档位清除、失败/截断读取仍为 C；当前聚焦测试 84/84 通过。

**文件**

| 文件 | 动作 |
|---|---|
| `Agent.Runtime/Verification/VerificationLedger.cs` | 新增 |
| `Agent.Runtime/Verification/TrustFooterBuilder.cs` | 新增，纯函数 |
| `Agent.Runtime/BrokerTurnExecutor.cs` | 工具完成后登记；收尾时追加页脚 |
| `Settings/BuiltInHostPrompts.cs` | 口径先行、歧义先问 |
| `Agent.Tests/Excel/Verification/TrustFooterBuilderTests.cs` | 新增 |
| `Agent.Tests/Scenarios/TurnLoopScenarioTests.cs` | 新增：无验证 → C；全部断言通过 → A；仅抽样 → B |

**验收**：三个场景测通过；手测中页脚在面板上正确显示，且不被模型改写。

---

## V5 最终回答数字溯源

### V5-1 `EvidenceLedger`

- 数字来源（全部来自**完整**内容，而不是被 MicroCompact 截断后的模型上下文）：
  1. 本会话 `conversation.Messages` 中所有 `tool_result` 的 `Content` 与 `StructuredContentJson`（已核实：完整内容会被持久化）；
  2. 本会话的用户消息；
  3. `runtime.calculate` 的结果（见 V5-2）。
- 归一化：去千分位、全角转半角、`万` / `亿` 换算、百分号。保存为 `(值, 原始小数位数)`。
- 派生证据：完整答案中显式给出的简单减法，只有在两个操作数精确出现在同一条可信消息、且宿主 `decimal` 重算与等式结果完全相等时才入账；该证据保留来源消息 ID。暂不做自动配对、自然语言算式推断或多步推导。
- V5-1 已实现：`BrokerTurnExecutor` 从未压缩的完整 `conversation.Messages` 构造回合 ledger，仅收集用户消息与工具结果的 `Content` / `StructuredContentJson`；识别 `runtime.calculate` 来源，并以 `decimal` 保存归一化数值、原始小数位、百分比标记和消息来源。MicroCompact 摘要及 assistant 正文不作为证据；纯逻辑测试覆盖全角数字、千分位、百分比、万/亿、负数、溢出保护、结构化结果去重与来源筛选。V5-1 实现阶段测试快照为 375/375。

### V5-2 `runtime.calculate`

- 确定性四则运算工具：输入 `expression`（数字、`+ - * / ( )`、`round(x, n)`、`sum(...)`、`avg(...)`），用 `decimal` 计算。
- 用途：占比、增长率及不受限简单减法规则覆盖的**派生数字**，继续通过它计算；受限简单减法可按下文 V5-3 的宿主复算规则验证。
- **防洗白**：表达式中的每个操作数本身必须在证据中（允许小整数 0–100 作为常数，如 `* 100`）；否则返回结果但标注「操作数未溯源」，结果不计入证据。
- V5-2 已实现：`runtime.calculate` 注册为只读内置工具并对 Excel 技能开放；自研限长/限复杂度的递归下降解析器只接受明确的算术语法，不执行任意代码。采用 `decimal`，`round` 使用 midpoint away-from-zero 语义。Ledger 在回合开始时从完整消息初始化，并在工具调用后增量登记；无证据操作数的结果可返回给模型纠正，但标记为未溯源且不会进入证据 Ledger。测试覆盖运算优先级、括号、sum/avg/round、百分比转小数、未溯源隔离、除零/溢出/无效表达式及运行时调度；V5-2 实现阶段测试快照为 375/375。

### V5-3 数字抽取与匹配

**实现状态**：已完成抽取器、匹配器及纯逻辑自动化测试。匹配器接受直接数值、明确的百分比换算/百分比证据、绝对值规则，以及满足上述来源和精确重算条件的显式简单减法；百分比回答不会错误匹配到数值相同但未标百分比的普通数字。按回答展示精度进行 midpoint-away-from-zero 舍入，并输出匹配证据来源及未匹配数字。历史 trace 回放与根因核验已在 V5-5 展开；当前构建的误报率仍需后续真实 trace 观察。

**抽取**（从最终回答正文中）：
- 支持：`1,234.56`、`-5`、`12.3%`、`1.2万`、`3.5亿`、全角数字。
- 排除：代码块与行内代码；单元格地址（`A1`、`$B$2`、`A1:H100`）及带“数据行 / data rows / 第…行”语义的行范围端点；运行时自动追加的上下文预算告警（`[上下文用量已达 … tokens (…%)]`）；工作表名；日期与时间（`2025-01-01`、`2025年3月`、`3月`、`14:30`）；列表序号（行首 `1.` / `1)`）；独立的 0–10 整数（可配置）。

**匹配**：回答中的数 `n`（小数位数 `d`）与证据 `e` 满足任一即视为溯源：
- `round(e, d) == n`；
- 减法：答案显式包含 `a - b = c`，且 `a`、`b` 是同一条可信消息中的非百分比数值、宿主以 `decimal` 重算得到的值与 `c` 完全一致；不从任意数字中猜测操作数；
- 百分比：`n%` 且 `round(e × 100, d) == n`，或 `round(e, d) == n`（证据本身已是百分数）；
- `万` / `亿`：按单位换算后在相同精度下相等；
- 绝对值相等（回答写「减少 5」，证据为 `-5`）。

### V5-4 订正流程

**实现状态**：已接入 Excel 数据工具回合收尾。在最终回答出现未溯源数字时，追加 system 订正指令和 `**更正**` 流式分隔符；订正仍可调用工具（包括 `runtime.calculate`），工具结果继续进入同一回合的 EvidenceLedger。对简单减法，可改为给出完整等式，由宿主按 V5-3 规则验证；不必为同一减法额外调用工具。若首轮订正仍有未溯源数字，最多再给一次有界订正机会。订正后只接受有证据的数字；仍有缺口时页脚列出未溯源数字并降为 C，成功订正的答案最高为 B。非 Excel 数据回合不触发该流程。

1. 收尾时（页脚生成之前）运行匹配；全部溯源 → 直接生成页脚。
2. 有未溯源数字，且订正预算（每回合 1 次）未用：
   - 追加 system 消息：列出未溯源的数字及其上下文，要求直接数字提供出处；简单减法给出完整、未舍入等式供宿主重算；其他派生值通过工具取得证据；更正中不复述无证据数字；
   - 运行时先以 content delta 追加分隔符 `\n\n**更正**\n`，然后流式输出模型的新内容；
   - 本轮允许调用工具，计入现有的轮次上限。
3. 订正后再次匹配；仍未溯源 → 在页脚追加一行 `未溯源数字：12.7%、3,412（未能在用户消息或工具结果中找到出处）`，档位降为 C。
4. 订正成功（数字经订正后溯源）→ 档位最高 B。

**自动化验证**：场景测试覆盖已有证据时不重复订正、未溯源回答调用 `runtime.calculate` 后成功纠正并将 A 限制为 B、简单减法等式独立复算成功、一次订正仍失败时保留未溯源数字并降为 C。V5 实现阶段全量测试快照为 388/388；当时全量测试为 433/433。

### V5-5 误报控制

- 抽取与匹配规则全部是纯函数，配一组从真实 trace 中收集的正 / 反例做单测。
- V8 的指标中加入「溯源误报率」（人工核对后确认正确却被判为未溯源的比例），目标 ≤ 5%；超过则先调抽取规则，不放宽匹配。

#### 真实 trace 人工标注步骤

这里的指标只回答一个问题：**被系统标成“未溯源”的实际数字声明中，有多少其实能在本回合允许的来源里找到有效证据？** 它不等于数字正确率，也不检查“已匹配数字是否匹配到了语义无关的证据”。

1. 固定当前构建 / 版本和 batch；只从有 `UnmatchedAnswerNumberCount > 0` 的 run 取候选。先看 `answer.txt` 的“未溯源数字”页脚，再用同目录 `trace.jsonl` 复核；`result.json` / batch summary 的匹配计数用于定位，不足以单独完成标注。
2. 标注单位是**一次数字声明的出现位置**，不是数字字符串或 trace 行。同一 trace 中的两个 `151`，或不同 run 中重复出现的 `20.0%`，都分别记录；把旧审查 CSV 的 `UnmatchedText` 拆成每个数字实例一行，并保留数字前后的原文上下文。
3. 对每个实例检查最终回答的具体声明、用户输入，以及 `trace.jsonl` 中成功的 `tool_call.completed` 结果（`Success=true`）。直接证据须支持相同的量、字段 / 指标、单位、筛选和口径；派生数还须有成功的 `runtime.calculate` 记录，且计算操作数本身有来源。失败工具调用、assistant 自述、仅凭打开工作簿后人工看到但 trace 中未读取的数据，都不算本指标的 trace 来源。
4. 按下表标注，并记录可复核的 trace 事件 / 工具名、证据摘录和理由。证据相同但语义不同不算支持；百分数维度、显示精度和范围跨度按当前匹配规则判断。`A1:C151` 只能支持明确的范围跨度或含表头总行数声明，不能支持金额或任意“151”。

| 标注 | 判定 | 误报率处理 |
|---|---|---|
| `SOURCE_FOUND` | 这是回答中的真实数字声明，且允许的用户 / 成功工具证据确实支持它；系统却报未溯源 | 计入误报分子和分母 |
| `NO_SOURCE` | 这是回答中的真实数字声明，但本回合没有符合策略的来源；它未必数值错误，只是无法由 trace 证明 | 只计入分母 |
| `NOT_A_NUMERIC_CLAIM` | 不是回答声明，例如上下文告警、单元格地址或不应抽取的范围端点 | 单列为抽取错误，不进入来源误报率 |
| `UNCERTAIN` | trace 缺失 / 截断，或来源语义无法判定 | 暂不进入分子或分母，补证或复核 |

**计算**：`SOURCE_FOUND / (SOURCE_FOUND + NO_SOURCE)`。同时报告两个原始计数；分母为 0 时写 `N/A`，不能写 0%。用于声称“当前版本误报率”的记录必须来自同一构建；跨构建历史记录只用于发现回归案例，不能合并估算当前值。

**标注记录建议字段**：`Build`、`Batch`、`Task`、`Run`、`Trace`、`Occurrence`、`UnmatchedValue`、`AnswerContext`、`Label`、`EvidenceEvent/Tool`、`EvidenceExcerpt`、`Rationale`、`Reviewer`。一条 trace 可展开为多行；这样可以从历史 `V5-5-number-provenance-review.csv` 复核每一个数字，而不把一整串未匹配值当成一个样本。

此流程**不测量已匹配数字的语义精确率**。若要检查“系统找到了数字相同、但字段或口径不相干的证据”，需另建 matched-claim 审查集，逐条记录回答声明与所匹配证据的对应关系；现有 `result.json` 只保存匹配计数，没有逐数字的匹配证据对。

**文件**

| 文件 | 动作 |
|---|---|
| `Agent.Runtime/Verification/EvidenceLedger.cs` | 新增 |
| `Agent.Runtime/Verification/NumericValueParser.cs` | 新增：共享数字归一化 |
| `Agent.Runtime/Verification/NumberExtractor.cs` | 新增，纯函数 |
| `Agent.Runtime/Verification/NumberMatcher.cs` | 新增，纯函数 |
| `Agent.Runtime/Turn/CalculateTool.cs` | 新增 `runtime.calculate` |
| `Agent.Runtime/BrokerTurnExecutor.cs` | 收尾时溯源 → 订正 → 页脚 |
| `Settings/BuiltInHostPrompts.cs` | 派生数字必须用 `runtime.calculate` |
| `Agent.Tests/Verification/EvidenceLedgerTests.cs` | 新增：完整工具结果及数字归一化 |
| `Agent.Tests/Verification/NumberExtractorTests.cs`、`NumberMatcherTests.cs` | 新增 |
| `Agent.Tests/Turn/CalculateToolTests.cs` | 新增 |
| `Agent.Tests/Scenarios/TurnLoopScenarioTests.cs` | 新增：编造数字 → 订正一次；仍不对 → 页脚标注 |

**自动化验证**：覆盖标准/全角/中文单位与紧邻中文文本、代码/地址/日期/列表号排除、可配置小整数过滤、原文索引上下文、精度舍入、百分比维度约束与换算、绝对值匹配及未匹配报告。V5 实现阶段全量测试快照为 388/388；当时全量测试为 433/433。

**历史 trace 逐项核验（2026-10-05）**：对既有 `V5-5-number-provenance-review.csv` 中 8 个历史 batch 的 32 条审查记录逐个检查 answer 与成功的 `tool_call.completed` 结果；记录的 `Unmatched` 合计为 48 个数字实例。按当时的混合口径，19 个告警无效：其中 15 个实际数字声明（`20.0%`、`20.01%`、`20.00%`、`19.98%`）由旧版 `runtime.calculate` 算出，但旧版结果文本漏掉 `%` 维度；另外 4 个不是回答声明，而是 1 个 `200001` 工作表区域/数据行范围端点和 3 个运行时上下文预算告警（`192585`、`200000`、`96%`）。前者已修复百分数维度，后者已补行范围与上下文告警掩码。其余 29 个实际数字声明按当前规则应继续拒绝：没有可接受的工具证据、依赖未溯源操作数，或是没有经 `runtime.calculate` 留证的派生 / 估算（其中 `50%` 可由 5,000/10,000 行数推算，但 trace 没有记录该计算）。这不等于 29 个数字必然错误，而是不能通过当前溯源策略验收。历史 `19/48`（39.6%）包含抽取噪声，与上述新定义的来源误报率分母不同，只能描述该混合版本历史审查集，不能当作当前指标或直接与未来指标比较。

**回放与剩余验收**：已有 10 条 trace-derived matcher 回归样例，覆盖百分数维度、未请求推算、行范围端点、隐藏行数和类别计数；另为运行时上下文预算告警添加抽取器回归测试。抽取、匹配、计算器、回合场景和提示策略的定向测试共 58/58 通过。只读 Eval `run-20261005-050814-2943154f` 为 36/36、数字证据 274/274 匹配；最新完整写入 cohort `run-20261005-151738-a760d6a6` 为 39/39、数字证据 248/248 匹配；两批均没有未匹配数字，因此当前构建的来源误报率为 N/A。历史混合版本审查的 19/48 不作为当前版本指标。后续若出现未匹配数字，按上述步骤逐实例标注；没有候选时继续记 N/A，而不是记 0%。目标 ≤ 5%。

---

## V6 数据语义陷阱

### V6-1 画像标记

`excel_profile_range` 结果新增 `dataQualityFlags`：

| 标记 | 检测方式 | 后端 |
|---|---|---|
| `subtotalFormulaRows` | 扫描受限数值列中的 `SUBTOTAL` 公式行；`SUM − SUBTOTAL(9)` 差异只作为补充风险信号 | COM |
| `subtotalLabelRows` | 数据行文本以「小计 / 合计 / 总计」结尾，或为 `total` / `subtotal` / `grand total` 标签 | 托管 |
| `hiddenOrFilteredRows` | `SUBTOTAL(3)` 与 `SUBTOTAL(103)` 差异或存在活动筛选 | COM |
| `textNumbers` | 至少一半非空值可解析为数字的列中，统计文本型数字 | 托管 |
| `mergedCells` | `Range.MergeCells` 为 `true` 或 `DBNull`（混合） | COM |
| `suspiciousHeader` | 原始表头空白/重复；或首条数据行文本占比过半且后续行数值占比过半 | 托管 |
| `staleCalculation` | 手动计算模式或计算未完成 | COM |

每个标记带影响说明、建议处理方式与 `sampleRows`；可定位的行标记最多列 10 行。工作簿状态类信号没有可定位行时，样本数组为空。COM 指纹最多检查 100,000 行、255 字符地址及最多 8 个非空列；超限或列数受限会写入 `dataQualityNotes`，不把未检查误报成无风险。`subtotalFormulaRows` 和 `hiddenOrFilteredRows` 的当前证据可能没有逐行样本，后续 V8 真实 Excel 夹具验收仍需核对。

**实现状态**：V6-1 的流式托管检测、有限 COM 指纹、契约兼容、工具说明及纯逻辑测试已完成；真实 Excel COM 与 V8 陷阱夹具验收待 V8 runner 验证。

### V6-2 提示词规则

- 画像出现任一标记时，模型**必须**在口径中写明处理方式（例如「排除第 120、245 行的小计行」），或先询问用户。
- `subtotalFormulaRows` / `subtotalLabelRows` 存在时，禁止直接对整列求和。
- `hiddenOrFilteredRows` 存在时，必须说明结果是「全部行」还是「可见行」。
- `dataQualityNotes` 中的未检查/未知状态必须披露；不能把未检查视为没有风险。样本被截断时，不得仅排除已显示样本并声称完整排除。
- 文本型数字、合并单元格、可疑表头及过期计算标记需给出相应影响与处理方式；不确定时询问，且不得未经用户请求修改源数据或强制重算。
- 用 `excel_assert` 验证排除口径时，在对应的 `target` / `source` 侧分别设置 `excludeRows` 或 `excludeLabelRows`；行号是该侧工作表的绝对行号，不得跨表复用。

### V6-3 断言侧联动

`scalar` / `groupMatch` 的 `target` / `source` 各自新增可选 `excludeRows`（该侧工作表的绝对行号，不含表头）或 `excludeLabelRows: true`。侧级配置避免把一张表的行号误用到另一张表。`excludeLabelRows` 扫描该侧完整行中的所有列，与 V6-1 标签匹配规则一致；两种选项可叠加。

含排除条件的查询强制走托管后端，避免 COM 汇总公式无法准确表达离散行排除。覆盖报告仍统计完整扫描区；有效聚合跳过排除行并在结果 Notes 中报告数量。超出该侧数据区的显式行号、重复/非正数行号、非 `scalar` / `groupMatch` 断言及超过 10,000 个行号都会明确失败。

**文件**

| 文件 | 动作 |
|---|---|
| `Host.Abstractions/Excel/ExcelAggregationContracts.cs` | `ExcelProfileRangeResult.DataQualityFlags` / `DataQualityNotes` 及旧契约缺字段默认值 |
| `Host.Excel/Verification/DataQualityInspector.cs` | 新增：托管标记、行样本与有界指纹聚合；V2-3 接口尚无质量指纹成员，因此由画像服务复用现有 COM 指纹读取器 |
| `Host.Excel/Com/ExcelColumnReader.cs`、`ExcelAggregationService.cs` | 保留原始表头/源行号并流式采集小计标签、文本型数字与表头风险 |
| `Host.Excel/Verification/Backends/Com/DataQualityFingerprint.cs` | 支持公式小计行样本及合并单元格状态 |
| `Host.Excel/Tools/Data/ExcelProfileRangeTool.cs` | 展示标记、影响、建议与检查范围说明 |
| `Settings/BuiltInHostPrompts.cs` | Ask / Agent 两种 Excel 默认提示词共用 V6-2 质量口径规则 |
| `Host.Abstractions/Excel/ExcelAssertionContracts.cs`、`ExcelCalcContracts.cs` | 添加侧级断言/查询排除选项 |
| `Host.Excel/Verification/VerificationPlanner.cs`、`Backends/Managed/ManagedCalcBackend.cs` | 侧级校验、行范围检查、全列小计标签检测与托管排除执行 |
| `Host.Excel/Verification/Backends/Com/ComCalcBackend.cs` | 明确拒绝不能精确支持的行排除查询，交由规划器路由托管后端 |
| `Agent.Tests/Excel/Verification/DataQualityInspectorTests.cs` | 新增：托管标记、阈值、样本上限与误报边界 |
| `Agent.Tests/Excel/Verification/ManagedCalcRowExclusionTests.cs`、`VerificationPlannerTests.cs`、`ComCalcBackendTests.cs` | 覆盖行号范围、标签排除、侧级隔离及后端路由 |
| `Agent.Tests/Excel/ExcelAssertionContractsTests.cs`、`Verification/ExcelAssertToolTests.cs` | 覆盖契约往返和输入 schema |

**自动验证**：质量标记纯逻辑、公式行识别、最多 10 行样本、旧 DataContract 字段缺省与默认提示词规则测试已覆盖；当时全量测试 433/433。真实 Excel COM / V8 陷阱夹具尚未运行，不宣称集成验收完成。

### V6-3 断言侧排除行

`scalar` / `groupMatch` 的 `target` / `source` 侧可分别提供 `excludeRows` 或 `excludeLabelRows`。行号按该侧数据区域对应工作表的绝对行号解释，不跨表复用。标签排除扫描整行所有列；排除查询路由到托管后端，COM 后端明确拒绝此形状。超出数据区的行号和不支持的断言类型会失败，不会静默忽略；覆盖仍统计完整扫描区，并在 Notes 报告实际排除行数。

**自动验证**：契约与工具 schema、Planner 的侧级隔离/后端路由、显式行与标签行求和、小计行排除、覆盖完整性和越界失败均有测试；当时全量测试 433/433。真实 Excel COM 行范围集成仍待 V8 runner 验收。

---

## V7 安全加固与遗留清理

### V7-1 `_wip_` 免确认的运行时保护（E6）

文本判定只决定**要不要弹确认**；是否真的只写中间表，由 Excel 的工作表保护在运行时兜底。

1. 只有文本判定与 Excel COM 执行前预检都通过时，`DefaultHostBridgeRequestHandler` 才在内部请求 metadata 中置 `stagedOnly = true`；用户传入的同名 metadata 会先被清除。
2. 宿主执行 `stagedOnly` 的 VBA 前：
   - 代码中含 `Unprotect` / `Protect` → 不允许免确认，返回需确认；
   - 对每张非 `_wip_` 工作表检查 `Cells.Locked` 必须为 `true`；发现未锁定或混合锁定的单元格时取消免确认，走常规用户确认，绝不临时改动单元格锁定状态；
   - 若有未受保护的正式工作表，工作簿必须已保存，且本地恢复记录可写；否则取消免确认并走常规用户确认；
   - 对所有**当前未受保护**的非 `_wip_` 工作表执行**无密码**保护（无密码，避免崩溃后用户被锁在外面），记录名单；
   - 执行 VBA；
   - `finally` 中只解除名单内工作表的保护。
3. VBA 因写入受保护表报 1004 → 返回 `excel.execute_vba_touched_formal_sheet`：「代码写入了非中间表，需要用户确认后再执行」。模型据此改为走确认路径。
4. 同时补强文本判定：`With <非 _wip_ 表>` 块、CodeName 引用（`SheetN.`）、索引引用（`Worksheets(数字)` / `Sheets(变量)`）一律判为不免确认；新建工作表必须先绑定变量并显式命名为 `_wip_` 才可写入，否则不免确认，并补对应单测。
5. 收拢 `_wip_` 前缀：在 `HostBridge.Contracts` 定义唯一常量，`ExcelVbaStageContracts`、`ExcelStagingConventions` 与 Facade 中的字面量改为引用它（见 §3.2.2）。

> ⚠️ 若 Excel 在执行中崩溃，名单内的表会保持无密码保护状态。恢复记录按工作簿完整路径保存在 LocalAppData；加载项启动及后续打开工作簿时会尝试自动解除。若自动恢复失败，加载项显示工作表名单和手动「审阅 → 撤销保护」指引。

### V7-2 工具参数 schema 校验（E8）

- 在 `DefaultToolDispatcher` 调用宿主工具之前，按 `InputSchemaJson` 校验参数。
- **自研最小子集校验器**：`type`、`required`、`enum`、`items`、`properties`、`minimum` / `maximum`。不引入 Newtonsoft.Json.Schema（AGPL / 商业授权）。
- 当前只记录不拦截：校验失败写 Runtime trace（路径、期望约束与实际类型/缺失状态），无效或不支持的 schema 单独记录并跳过；无论诊断结果如何都继续调用宿主工具。
- 观察 trace、确认误报为零后再启用拦截；启用后失败返回 `tool.invalid_arguments`，让模型按路径和约束自纠。
- 已覆盖嵌套 object/array、required、enum、minimum/maximum、非法 JSON，以及诊断不阻止宿主调用；真实 trace 误报观察期仍待完成。

### V7-3 清理

- ✅ 删除 `SkillPlanSupport.BuildAutomaticVerificationArgumentsJson`（确认运行时和测试均无调用点）。
- ✅ 将 [EXCEL_AGENT_HARNESS_PLAN.md](EXCEL_AGENT_HARNESS_PLAN.md) 标记为历史方案，注明 P0–P5 已完成、P6 由本方案 V8 取代；统一测试状态并改用 P6 标题引用，消除过期的章节编号依赖。

**文件**

| 文件 | 动作 |
|---|---|
| `HostBridge.Contracts/StagedWriteConfirmationPolicy.cs` | 补强文本判定；`_wip_` 前缀常量改为公开，作为唯一定义 |
| `Host.Abstractions/Excel/ExcelVbaStageContracts.cs`、`Host.Excel/Analysis/ExcelStagingConventions.cs` | 改为引用 Contracts 中的常量 |
| `HostBridge.Server/DefaultHostBridgeRequestHandler.cs` | 传递 `stagedOnly` |
| `Host.Excel/ExcelHostAdapter.Facade.cs`、`ExcelVbaService.cs`、`ExcelStagedWorksheetProtection.cs` | COM 预检、运行时保护与解除、原子恢复记录及启动恢复 |
| `NixerExcelCopilot/ThisAddIn.cs` | 加载项启动和打开工作簿时恢复遗留保护 |
| `Agent.Runtime/Turn/ToolArgumentValidator.cs` | 新增 |
| `Agent.Runtime/Turn/DefaultToolDispatcher.cs` | 接入校验 |
| `Agent.Runtime/Turn/SkillPlanSupport.cs` | 删除死代码 |
| `Agent.Tests/HostBridge/StagedWriteConfirmationPolicyTests.cs` | 覆盖 With / CodeName / 数字与变量索引 / Protect / Unprotect，并验证 WIP With 仍可豁免 |
| `Agent.Tests/Turn/ToolArgumentValidatorTests.cs` | 新增 |

**验收**
- 自动化：绕过写法及未命名/正式命名/先写后命名的新建工作表拒绝、WIP With 保持豁免、HostBridge 对可保护/不可保护工作簿分别设置 staged-only 标记或转确认；全量测试通过。
- 真实 Excel COM 的保护/恢复调用及崩溃恢复尚未端到端运行，待 V8 runner；本地测试不宣称 COM 集成验收完成。
- 手测：构造一段文本判定漏掉的写原表代码（例如通过变量间接引用）→ 被工作表保护拦下，返回 `excel.execute_vba_touched_formal_sheet`，原表未改动，执行后所有工作表的保护状态恢复原样。

---

## V8 Eval 集

### V8-1 夹具生成器

- **不新建工程**：在 `OfficeCopilot.OpenXml.SmokeTest` 中增加 `--export-excel-eval-fixtures <输出目录>` 子命令，与现有的 `--export-attachment-fixtures` 并列（见 §3.2.5）。生成代码放在该工程的 `ExcelEvalFixtures/` 目录，避免 `Program.cs` 继续膨胀。
- **不得引用** `Host.Excel` 或任何产品侧聚合代码：标准答案由生成器在内存中用 LINQ 独立计算。
- 每个夹具由「种子 + 规格」确定性生成，输出 `xxx.xlsx` + `xxx.answers.json`；与现有导出命令一样，拒绝覆盖已存在的文件。生成物输出到 `TestResults\ExcelEval\`（已被 `.gitignore` 忽略），不入库。
- 夹具包含 20 万行 × 12 列的大表；为避免通过 DOM 逐格写入造成高内存与低效率，夹具工程使用 `OpenXmlWriter` 实现流式 typed writer，支持数值、带日期格式的日期、故意的文本型数字、公式（含 `SUBTOTAL`）、合并单元格、隐藏行、自动筛选。无需仅为 Eval 扩大产品侧 `ExcelDocumentEngine` API。

```powershell
.\OfficeCopilot.OpenXml.SmokeTest\bin\Debug\net472\OfficeCopilot.OpenXml.Word.SmokeTest.exe `
  --export-excel-eval-fixtures .\TestResults\ExcelEval
```

**夹具清单（首批）**

| 夹具 | 规模 | 注入的陷阱 |
|---|---|---|
| `sales_clean` | 1 万行 × 8 列 | 无（基线） |
| `sales_large` | 20 万行 × 12 列 | 无（性能与截断回归） |
| `sales_subtotal` | 5000 行 | 每个部门后一行 `SUBTOTAL` 公式小计 + 末尾手写「合计」行 |
| `sales_dirty` | 5000 行 | 3% 文本型数字、1% `#N/A`、5% 空白、2 行隐藏 |
| `orders_customers` | 两表各 3000 行 | 跨表 key 有 5% 不匹配 |
| `wide_monthly` | 500 行 × 14 列 | 宽表（12 个月份列），用于宽转长 |
| `header_multirow` | 1000 行 | 两行表头 + 合并单元格 |

### V8-2 任务与 rubric

任务集是一个 JSON 文件（与 [attachment-eval](attachment-eval/README.md) 的 `manifest.json` 保持同一风格；不用 YAML，免得 Runner 再引入 YamlDotNet），**只用程序化判定**：

```jsonc
{
  "tasks": [
    {
      "id": "groupby-dept-2025",
      "fixture": "sales_clean",
      "prompt": "统计 2025 年各部门的销售额和占比",
      "skill": "read-analysis",
      "expect": {
        "answerNumbers": { "from": "answers.dept_2025", "tolerance": 0.01, "percentDecimals": 1 },
        "workbookUnchanged": true,
        "trace": { "maxRounds": 8, "forbidTools": ["excel_read_range"] },  // 不得翻页爬表
        "trustTier": { "min": "A" }
      }
    },
    {
      "id": "subtotal-trap",
      "fixture": "sales_subtotal",
      "prompt": "各部门销售额合计是多少",
      "expect": {
        "answerNumbers": { "from": "answers.dept_total_excluding_subtotals" },
        "answerMentionsAny": ["小计", "合计行"]
      }
    }
  ]
}
```

首批 25 条覆盖四类技能（`read-analysis` 12 条；`live-result` 4 条、`structural-transform` 5 条、`bulk-format` 4 条），另含陷阱 5 条、只读分析跨表任务 2 条及失败恢复 2 条（例如故意让第一次 VBA 写错）；公式和结构变换任务也提供额外跨表覆盖。写入任务只允许在显式 Eval 写入模式中对每次单独复制的夹具副本执行。

### V8-3 Runner

- `OfficeCopilot.Eval`（传统格式、net472 控制台；Excel 相关代码放 `Excel/` 子目录，为以后的 Word / PowerPoint Eval 留位置）。默认仍只自动运行只读任务；显式 `--allow-workbook-write-tests` 可在独立夹具副本上运行写入/VBA 验收，并执行实际工作簿断言。此开关只供 Eval，不改变产品权限策略。
- 流程：复制夹具 → 启动 Excel（带加载项）并打开 → 通过 Broker 客户端提交提示词 → 等待回合结束 → 收集 trace JSONL 与最终回答 → `SaveCopyAs` 结果工作簿 → 用 OpenXml 读取并按 rubric 判定。
- 每条任务跑 **k = 3** 次（可配置）。
- Runner 在交互式 Windows CI 会话中运行，不要求专用虚拟机；生成夹具无需 Excel，端到端 Eval 才启动真实 Excel 与加载项。
- 通过 `OfficeCopilot.Agent.Client` 提交回合并接收事件。`BrokerRuntimeHost` 将当前进程的 Broker 端点原子写入 `%LOCALAPPDATA%/NixerOfficeCopilot/BrokerEndpoints/`；Runner 按 Excel PID 与进程启动时间筛选，不复制管道命名规则。若同一 Excel 进程有多个任务窗格端点，可显式传 `--broker-pipe` 消除歧义。
- Runner 从共享 `settings.json` 解析默认启用模型，并把 `ModelProviderId` / `ModelOptions`（含 Excel Ask 系统提示词、超时、请求头和受控 transcript 选项）传给 Broker；未配置有效 endpoint / model ID 时在启动 Excel 前失败。每个 Eval 任务还要求 trace 至少观察到一个模型轮次，防止 Broker 的无模型运行时状态回退被误判为模型回答。
- Runner 在 STA 线程启动可见的 Excel COM 实例、打开 bootstrap 工作簿后发现端点；持续等待 Broker 时泵送 Windows 消息。每次运行单独复制夹具；只读任务以只读方式打开，显式写入测试则以可写方式打开该副本，完成后 `SaveCopyAs` 到结果目录。
- 每条任务使用独立 Excel COM 实例，同一任务的重复回合共用实例；跨任务重新启动以隔离长批次中的 VBA / COM 状态，`summary.json` 记录进程 ID 列表。
- Excel 加载项在启动时预热任务窗格，并在 `WorkbookOpen` 时再次请求预热，以覆盖启动预热早于首个活动工作簿的情况；预热异常写入 application log。此启动路径仍需通过真实 V8 Runner 验收。
- 只读回合使用 `InteractiveMode=ask` 且以只读方式打开工作簿。显式写入测试回合使用 `InteractiveMode=agent`，仅对 Runner 创建的输出目录副本授予写入能力；原始 fixture 不会在 Excel 中打开。该参数是显式 Eval 测试开关，不更改产品权限策略。
- Runner 将 Broker 事件追加为 `trace.jsonl`；`AgentBrokerEvent.Round` 记录实际模型轮次，避免用工具调用次数近似轮次。工作簿不变检查比较同一 Excel 实例执行任务前后的 `SaveCopyAs` 快照，并以 Open XML 包语义指纹忽略文档属性、计算链和窗口选择状态，避免把 Excel 首次打开时的文件规范化误判为任务写入。
- 写入测试仅在显式 `--allow-workbook-write-tests` 下，对任务 `expect.confirmationTools` 明确列出的 `tool.confirmation` 请求发送 `allow-once`；`trace.requiredToolsAny` 只做轨迹验收，不用于授权。其余确认与用户交互仍会取消并令任务失败，自动批准次数记入 `result.json`。
- 两个带 `scenario.faultInjection` 的恢复任务在 Runner 具备受控故障注入之前不得运行；批量写入测试会记录并跳过其 ID，单任务执行会在启动 Excel 前失败。其他写入任务要求显式 `--allow-workbook-write-tests`，并在只读基线之外对临时副本执行实际工作簿断言。

### V8-4 指标

| 指标 | 定义 |
|---|---|
| **pass^k** | 所有 k 次均已记录的任务中，k 次全部通过的任务比例（**稳定性的主指标**）；不完整任务不进入分母 |
| pass@k | 所有 k 次均已记录的任务中，至少一次通过的任务比例；不完整任务不进入分母 |
| 轮次中位数 / token 中位数 | 轮次来自 Broker trace；当前 token 指上下文预算估算，不是供应商账单 token；缺失观测不按 0 处理 |
| Runner 拦截的任务禁用工具尝试率 | 命中任务 `forbidTools` 的 `tool_call.started` 次数 / 全部 `tool_call.started` 次数；这不代表产品技能闸门拦截率；无调用时为 N/A |
| 验证覆盖率 | 有产出项且收到质量 telemetry 的回合中，所有产出项都有完整、适用的验证结果（不要求验证通过）的回合比例 |
| 回答数字证据匹配率 | 有数字匹配报告时，匹配数字数 /（匹配数字数 + 未匹配数字数）；不是误报率 |
| **验证假通过率** | A 档回合中 rubric 判失败的比例（**可信度的主指标**，目标 0）；无 A 档回合时为 N/A |
| 静默失败率 | Broker 回合成功的回合中，rubric 判失败的比例；无成功回合时为 N/A |
| 溯源误报率 | 见 V5-5 |
| 档位分布 | A / B / C 各占比 |

每次跑完生成一份 Markdown 报告。基线用 `--baseline-summary` 显式指定；报告只比较任务 cohort、每任务运行次数和输入指纹一致的运行。缺失上游指标以 N/A 表示。

Runner 每次完成一个 batch 后写出 `report.md`。基线通过 `--baseline-summary <summary.json>` 显式指定；比较前校验任务 cohort、每任务运行次数及 task-set、工作簿夹具、答案文件的组合指纹。没有基线时仍生成本次报告并标明未比较。Broker 的 `turn.completed` 事件现携带 V3 产出/覆盖计数、V5 匹配/未匹配计数及数字订正尝试/成功标记，Runner 将其保存在每次 run 的 summary 中并计算覆盖率与回答数字证据匹配率；旧 summary 或未产生相应报告的回合仍显示 N/A。匹配率不等同于误报率：V5-5 的「溯源误报率」仍需对真实 trace 做人工标注与核查。首份真实 batch 报告已生成，数据见下方最新 batch 记录。

**文件**

| 文件 | 动作 |
|---|---|
| `OfficeCopilot.OpenXml.SmokeTest/ExcelEvalFixtures/` | 新增：生成器、规格、标准答案计算 |
| `OfficeCopilot.OpenXml.SmokeTest/Program.cs` | 新增 `--export-excel-eval-fixtures` 子命令 |
| `OfficeCopilot.OpenXml.SmokeTest/ExcelEvalFixtures/FixtureWorkbookWriter.cs` | `OpenXmlWriter` 流式写入 typed cells、公式、日期格式及工作表元数据 |
| `OfficeCopilot.Eval/`（新工程） | Runner、rubric 判定、报告；加入 `NixerOfficeCopilot.sln` |
| `OfficeCopilot.Eval/Excel/ExcelEvalModelOptionsProvider.cs` | 从共享用户设置解析当前默认模型，并构造 Broker 回合使用的模型选项 |
| `OfficeCopilot.Eval/Excel/ExcelEvalMetricsReporter.cs` | 汇总 batch 指标、检查基线可比性并生成 Markdown 报告；缺失上游 telemetry 显示为 N/A |
| `OfficeCopilot.Agent.Client/AgentBrokerEndpointRegistry.cs` | 注册 / 查询当前进程的 Broker 管道端点，供同一用户会话中的 Eval Runner 发现 |
| `OfficeCopilot.Agent.Runtime/Turn/DefaultTurnEventEmitter.cs`、`OfficeCopilot.Agent.Client/AgentBrokerEvent.cs` | 在 Broker 事件中带上模型轮次与 V3/V5 回合质量摘要，供 Eval trace 计算轮次、验证覆盖及数字匹配指标 |
| `docs/excel-eval/tasks.json`、`docs/excel-eval/README.md` | 任务定义与运行说明（与 `docs/attachment-eval/` 并列） |
| `docs/CODE_MAP.md`、`docs/ARCHITECTURE.md` | 登记新工程与新目录 |

**验收**：25 条任务全部可运行；生成首份基线报告；在 V3 之前与之后各跑一次，以「验证假通过率」与 pass^k 的变化作为本方案的总体验收依据。

**当前状态**：行范围和上下文告警抽取修改后的真实只读 batch `run-20261005-050814-2943154f` 为 36/36；12 个自动只读任务均 3/3 通过，13 条人工确认任务按设计跳过。该批与基线 `run-20261005-041813-fdfd69dc` 对比，pass^k / pass@k 均为 12/12，验证覆盖率 36/36，数字证据 274/274，A 档 rubric 失败和静默失败均为 0/36；指标百分点变化均为 0.0。原始写入 batch `run-20261005-075607-2525daa4` 为 33/33，但其 A/B/C 分布与覆盖率基于旧账本及旧任务提示。随后 11 个非恢复写入任务按 ID 分项各运行 3 轮，总计 33/33 通过，账本产出项覆盖 50/50、可信档位 A=21/B=12/C=0；4 个格式任务的样式回读均通过并保守评级 B。此结果来自分项 batch 汇总，不应与旧单报告的产出项覆盖率直接比较。两个故障注入恢复任务现均完成真实 Excel/模型联动验收，各 3/3 通过、A 档 3/3、产出覆盖率 100%。交互竞态已修复；`TurnLoopScenarioTests` 19/19；OfficeCopilot.Agent.Tests 当前源码重建后 487/487 通过。VBA 离线预检补上了保留字变量名回归检查。最新只读和写入 batch 均无未匹配数字，溯源误报率仍为 N/A；V7-2 真实 trace 误报观察仍待持续完成。

---

## 15. 性能目标

| 场景 | 目标 |
|---|---|
| 单次 COM 调用（任意） | ≤ 200 ms（R2） |
| 10 万行单列 COM `Sum` | ≤ 50 ms |
| 10 万行 × 3 列托管读取 | ≤ 0.5 s |
| 一组典型断言（3–5 条，10 万行源表） | ≤ 2 s |
| 100 万行 × 2 列托管读取 | ≤ 5 s，且不截断 |
| 32 位 Excel 下单次验证的托管内存峰值 | ≤ 150 MB |

**当前结果**：V1、V2 的真实 Excel 性能压测尚未执行；上表是目标，不代表已经达到。具备 Excel 集成环境后，分别记录实际数据、Excel 版本、位数和区域设置。

---

## 16. 风险与待定事项

| # | 事项 | 处理 |
|---|---|---|
| Q1 | COM `SUMPRODUCT` / `NUMBERVALUE` 公式受 Excel 版本、函数参数分隔符及数字区域设置影响 | 代码显式使用 Excel 区域参数分隔符，并固定小数点/千位分隔符以匹配托管解析；仍需在 Excel 2016 / Microsoft 365 及中文、英文、德文区域设置下手测，公式失败时不得标为已验证 |
| Q2 | 混合错误值与文本型数字时，各分析入口可能采用不同求和语义 | `excel_aggregate.sum` 明确定义为计入原生数值和可解析文本数字、忽略错误值；COM 复核须保持相同语义，`excel_analyze_range` 的 Excel 原生 SUM 不可替代此路径；用类型筛选分别分析两类数值 |
| Q3 | 运行时保护使用无密码保护，崩溃后表保持受保护 | 启动时自动恢复；文档告知用户手动解除方法 |
| Q4 | 数字溯源误报 | 当前运行时代码会对符合条件的 Excel 回合自动尝试一次数字订正；混合历史构建的审查集误报比例为 19/48，不能代表当前运行时。当前真实 Eval 无未匹配数字，故当前误报率为 N/A；需继续人工标注同一构建的真实 trace，纯逻辑测试不能替代真实数据验收 |
| Q5 | Eval Runner 的交互式入口、端点发现与确认策略 | 首版在交互式 Windows CI 会话运行；复用 `Agent.Client`，通过显式配置 / 暴露取得 Broker 管道端点；只读任务自动跑，写入 / VBA 保留人工确认；不要求专用 VM。全自动写入另行设计隔离与审计 |
| Q6 | `ExcelComDispatcher` 对写入工具的整体重试安全性 | 首版写入 / VBA 一律不做整体重试；仅只读工具重试 |
| Q7 | 「产出项被哪次验证覆盖」只按表名匹配，精度有限 | 先按表名；若 V8 显示误判，再细化到区域 |
| Q8 | 宿主断言词汇表达不了的任务 | 如实降为 B / C 档，不为覆盖率扩张词汇；扩张需由 V8 数据证明必要 |

---

## 17. 进度追踪

| 阶段 | 项 | 状态 | 备注 |
|---|---|---|---|
| V0 | V0-1 `ExcelComDispatcher` | ✅ 代码接入与模拟 HRESULT 自动验证完成；真实 Excel busy HRESULT 验证按讨论跳过 | 解决方案构建通过，264 项测试通过；不阻塞后续阶段；只读工具重试，写入 / VBA 不重试 |
| V1 | V1-1 列读取器 | ✅ 完成并接入三个分析工具 | 分块读取、矩阵尺寸校验、50,000,000 格硬上限；真实 Excel 大表端到端测试尚未进行 |
| V1 | V1-2 流式累加器 | ✅ 完成 | 快照/流式路径共用聚合、画像和对账累加状态；流式 sink 有跨块回归测试 |
| V1 | V1-3 覆盖报告 | ✅ 完成 | 计数覆盖每次实际读取的全部源列；不完整扫描返回 `excel.incomplete_coverage` |
| V1 | V1-4 工具迁移 + 截断即失败 | ✅ 完成 | Agent.Tests 287 项通过；保留全源列整行判空语义；真实 Excel 大表端到端测试未做 |
| V1 | V1-5 回合内读取缓存 | ⏸ 暂缓 | 等 V8 数据 |
| V2 | V2-1 后端接口 | ✅ 完成 | 新增 `ExcelSourceRef`、`CalcQuery`、`CalcFilter`、`CalcCostEstimate`、`CalcResult` 与 `IExcelCalcBackend`；DataContract 往返测试通过；完整测试 288 项通过 |
| V2 | V2-2 COM 后端 | 🟡 实现与纯逻辑测试完成 | 支持标量、筛选求和/数值计数、多列求和及错误值计数；按 100,000 格和 R3 路由；Agent.Tests 300 项通过；真实 Excel 2016 / Microsoft 365 性能与区域设置手测未执行 |
| V2 | V2-3 质量指纹 | 🟡 代码实现与纯逻辑测试完成 | 新增单列 COM 指纹，限制 100,000 行/255 字符地址；覆盖非数值和错误值计数、手动隐藏行、筛选及计算状态；嵌套小计仅作为信号，错误值或活动筛选时不作归因；Agent.Tests 304 项通过；真实 Excel 2016 / Microsoft 365 指纹与性能手测未执行 |
| V2 | V2-4 `FakeCalcBackend` | ✅ 完成 | 新增测试工程内的可复用内存后端，支持查询、筛选、单列分组、覆盖报告及异常/Notes 注入；Agent.Tests 309 项通过；不涉及 Excel COM 手测 |
| V2 | V2-5 托管计算后端 | 🟡 代码实现与纯逻辑测试完成 | 通过分块读取与共享累加器支持筛选、分组、多列求和及分类覆盖；校验 50,000,000 格上限；Agent.Tests 316 项通过；真实 Excel COM 源解析/Value2 集成未手测 |
| V3 | V3-1 断言契约 | ✅ 完成 | 新增 `{ basis, assertions }` DataContract JSON DTO，覆盖 scalar / groupMatch / keySet / sorted / rowCount / nonBlank 所需字段；JSON 往返及省略集合默认值测试通过；种类专属字段校验留给 V3-2 |
| V3 | V3-2 路由 | ✅ 完成 | 新增 `VerificationPlanner`，按断言形状、后端能力、成本及生产者独立性生成 target/source 查询；校验 rowCount / groupMatch / keySet 输入边界；结果契约增加 `Keys` / `IsSorted`；托管排序对非原生数值保守返回未知；Agent.Tests 328 项通过。真实 Excel COM 与 Value2 集成仍未手测 |
| V3 | V3-3 比对与报告 | ✅ 完成 | 新增报告/outcome DTO 与 Comparator；实现数值容差、全组比较、key-set 差异、排序三态、覆盖完整性校验、COM busy/超时失败归档及最多 20 条差异明细（总数不截断）；Agent.Tests 338 项通过 |
| V3 | V3-4 `excel_assert` | ✅ 代码与纯逻辑测试完成 | 新增只读工具、宿主执行引擎与真实区域成本估算；四类技能允许/首选/验证名单及失败恢复路径接入；Agent.Tests 341 项通过；真实 Excel COM / Value2 集成未手测 |
| V3 | V3-5 现有工具迁移 | ✅ 代码与纯逻辑测试完成 | VBA conserve/assertions/probe、reconcile 全组比较、aggregate sum/有列 count 独立复核已接入；保留兼容字段与现有流式读取；count(*)、截断结果及非 sum/count 度量会明确标记未完整复核；343/343 测试通过；真实 Excel COM / Value2 集成未手测 |
| V3 | V3-6 `verification` 字段 | ✅ 代码与契约测试完成 | HostBridge 常量及统一 envelope 已用于 `excel_assert`、VBA、aggregate、reconcile；details 保留原始比较报告；覆盖量按 coverage 实例汇总；VBA 早退也生成 C 档失败 envelope；V3 阶段测试快照 375/375，当时全量 433/433；真实 Excel COM / Value2 集成未手测 |
| V3 | V3-7 双向键集合差异工具 | ✅ 代码、自动测试及当前真实 Eval 已验证 | 新增只读 `excel_compare_key_sets`，复用完整覆盖和信任 envelope；分别输出 target-only/source-only 数量及默认首/中/末有界样例；以 `verification.operation=keySetDifference` 区别差异计算和相等断言，页脚不会误称“断言通过”。接入后两项跨表任务均已 3/3 通过；最近一次全量测试快照 448/448（不含本轮改动） |
| V4 | V4-1 口径契约 | ✅ 默认提示词、技能提示与测试完成 | 明确 source/date field/filters/exclusions/groupBy/derivedGroupBy/measures；多个合理候选时要求用 `runtime.ask_user_question` 先确认；计算与验证沿用同一口径；聚合派生分组完整传递至 verification basis 与页脚 |
| V4 | V4-2 `VerificationLedger` | ✅ 回合级登记与纯逻辑测试完成 | 模型及显式工具路径共用每回合 Ledger；按 `verification` 常量解析 envelope；匹配最近的同表验证；缺目标、失败写入、失败/缺失/不完整验证保守降为 C |
| V4 | V4-3 页脚 | ✅ 运行时生成与场景测试完成 | 最终答复后追加 A/B/C 校验与结构化口径；派生分组也完整展示；流式正文仅追加 delta；写入 `trust_footer` 轨迹；无 Excel 数据调用时不显示；V4 阶段测试快照 375/375，当时全量 433/433 |
| V5 | V5-1 `EvidenceLedger` | ✅ 完成 | 从完整会话消息收集用户、工具结果及 `runtime.calculate` 数字；十进制归一化保留小数位和来源；忽略 assistant / compact 内容 |
| V5 | V5-2 `runtime.calculate` | ✅ 完成 | 只读 decimal 工具；限制语法/复杂度；逐项校验操作数溯源；未溯源结果不会进入 Ledger |
| V5 | V5-3 抽取与匹配 | ✅ 代码与纯逻辑测试完成 | 支持数字归一化、最终回答抽取、证据匹配和未匹配报告；合法工具范围地址另生成隔离的行跨度证据，仅匹配明确的含表头/范围跨度声明，不进入普通数字 Ledger 或 calculation 操数；V5 阶段测试快照 388/388，当时全量 433/433。真实 trace 误报率测量留待 V5-5 |
| V5 | V5-4 订正流程 | ✅ 一次订正与失败降级完成 | Excel 数据回合收尾时自动尝试一次数字订正；订正仍可调用工具并更新 EvidenceLedger；失败以未溯源列表和 C 档页脚收尾，成功订正封顶 B；V5 阶段测试快照 388/388，当时全量 433/433；真实 trace 运行效果仍待评估 |
| V5 | V5-5 误报控制 | 🟡 历史误报已分类，当前版本持续观察 | 对审查 CSV 的 32 条 trace / 48 个未匹配数字实例逐项核验：19 个历史误报（旧百分比维度缺失 15、行范围端点 1、上下文告警 3），其余 29 个按当前证据策略应拒绝。另对旧写入 batch 的 3 个未匹配实例抽查，两个重复的 `151` 有 `A1:C151` 范围证据，现由专用范围跨度规则支持；日期序列值 `45365` 无工具来源，拒绝正确。该小样本跨构建且有重复实例，不能作为误报率估计；历史样本的 19/48（39.6%）也不是当前指标。最新只读 Eval 为 36/36、274/274 匹配；最新写入 Eval 为 39/39、248/248 匹配，均无未匹配数字，因此当前误报率 N/A |
| V6 | V6-1 画像标记 | 🟡 实现与纯逻辑测试完成 | 新增七类 `dataQualityFlags`、有界 COM 指纹与显式检查范围说明；V6 阶段测试快照 424/424，当时全量 433/433；真实 Excel COM / V8 陷阱夹具集成验收待运行 |
| V6 | V6-2 提示词规则 | ✅ 默认提示词与测试完成 | Ask / Agent 均要求按标记说明口径、披露未检查状态、禁止只排除样例行或虚称可见行结果；并说明断言侧排除规则；V6 阶段测试快照 424/424，当时全量 433/433 |
| V6 | V6-3 断言排除行 | ✅ 契约、托管执行与测试完成 | `target` / `source` 侧分别支持工作表行号与标签行排除；有界范围校验、COM 拒绝并路由 managed、完整覆盖及结果 Notes；V6 阶段测试快照 424/424，当时全量 433/433；真实 Excel COM 集成待 V8 |
| V7 | V7-1 `_wip_` 运行时保护 | ✅ 实现与自动化测试完成 | COM 预检拒绝未锁定/混合锁定表并强制确认；执行前无密码保护、finally 按名单恢复、崩溃恢复记录；HostBridge 路由及静态策略测试通过；V7 阶段测试快照 424/424，当时全量 433/433；真实 Excel COM 验收待 V8 |
| V7 | V7-2 参数 schema 校验 | 🟡 日志模式实现与自动化测试完成 | 最小 schema 子集调用前诊断；日志模式不拦截宿主工具；V7 阶段测试快照 424/424，当时全量 433/433；真实 trace 误报观察期未完成，尚未启用拦截 |
| V7 | V7-3 清理 | ✅ 完成 | 删除零调用点的自动验证参数构造方法；将旧 Harness 方案标记为历史并链接到 V8 主计划 |
| V8 | V8-1 夹具生成器 | ✅ 已完成 | SmokeTest 流式导出 7 组 xlsx + 独立答案；确定性、结构、标准答案、拒绝覆盖已验证 |
| V8 | V8-2 任务与 rubric | ✅ 已完成 | 任务集 CLI 校验通过：25 条任务（12 条只读、13 条人工确认）；13 条人工任务逐条都有 `workbookUnchanged=false`、工作簿断言和必需写入工具约束，分为 4 条公式、5 条结构转换/恢复、4 条格式任务。因均会修改工作簿，按设计不由只读 Runner 自动执行 |
| V8 | V8-3 Runner | ✅ 只读、非恢复写入及两种恢复注入均端到端验收 | 只读 batch `run-20261005-050814-2943154f` 为 36/36；12 个自动任务均 3/3，13 条人工确认任务按设计跳过。Agent 测试当前为 487/487。11 个非恢复写入任务各 3/3 分项通过；两个恢复任务各 3/3、A 档 3/3、产出覆盖 100%。Runner 在 STA 线程对隔离副本注入错误，并验收注入后的失败断言及成功重验 |
| V8 | V8-4 指标与报告 | 🟡 只读基线和完整写入报告已完成；溯源误报率 N/A | 最新只读报告覆盖率 36/36、数字匹配 274/274、可信档位 A=36/36。最新完整写入 batch `run-20261005-151738-a760d6a6` 为 39/39、覆盖率 39/39、数字证据 248/248、A=27/B=12/C=0；结构化档位与页脚 39/39 一致。历史 C 对应任务和故障恢复任务的定向复测均为 3/3 A；当前误报率仍待有效人工标注样本 |

**最新全量 batch 结果**（batch 为 `run-20261005-050814-2943154f`；36/36 全部通过；基线为 `run-20261005-041813-fdfd69dc`）：

- `analysis-department-sales-2025`、`analysis-total-sales-2025`、`analysis-large-source-total`、`analysis-wide-monthly-unpivot`、`analysis-department-count`：本批均 3/3 通过。
- `trap-subtotal-double-count`、`trap-text-numbers-kept-separate`、`trap-dirty-column-quality-counts`、`trap-hidden-rows`、`trap-multirow-header`：本批均 3/3 通过。文本数字任务在提示调整后不再输出未请求的算术派生数；小计、质量计数和隐藏行的独立复核亦保持稳定。
- `cross-table-unmatched-counts`、`cross-table-unmatched-key-samples`：本批均 3/3 通过。键集合工具返回完整计数与有界确定性样例。

**最新完整写入 cohort**（batch 为 `run-20261005-151738-a760d6a6`；13 项任务各 3 轮）：39/39 回合和工作簿 rubric 通过，产出覆盖 39/39，数字证据匹配 248/248（100%）；可信档位 A=27、B=12、C=0。结构化质量摘要与信任页脚在全部 39 轮一致。小计清理任务定向复测 3/3 A，故障恢复任务定向复测 3/3 A 且包含注入和成功重试。两个历史 `151` 实例由工具范围 `A1:C151` 支持，现通过类型化行跨度证据匹配；`45365` 无来源，仍正确拒绝。数字匹配率不等于溯源误报率，旧样本不用于推算当前误报率。

---

## 18. 分阶段执行与验证检查点（V9）

### 目标与边界

复杂 Excel 任务按**可报告或会被后续步骤使用的逻辑产出**设置验证检查点，而不是每调用一个探查工具就增加一次验证。简单任务可只有一个产出和一个检查点；多个互相依赖的写入/转换阶段必须在阶段边界验证后，才能继续消费该阶段结果。回合结束时仍汇总所有产出的覆盖和最低可信档位。

本阶段不引入新的验证公式或更多 A/B/C 档位，也不把模型计划文本当作验证证据。现有工具 envelope、COM/托管独立计算、精确格式回读和 fail-closed 规则继续作为证据来源。

### 目标执行形态

| 任务形态 | 执行与验证顺序 |
|---|---|
| 单一只读结果 | 定位数据与口径 → 计算产出 → 该产出的内嵌独立验证或一个显式验证 → 汇总回答 |
| 多个只读结果 / 派生步骤 | 定位数据 → 生成可复用的阶段产出 → 在该产出被后续步骤使用前验证 → 继续计算 → 汇总时逐项确认覆盖 |
| 单个写入阶段 | 检查目标与基线 → 写入 → 对写入结果做适当回读/断言 → 汇总 |
| 多个写入或转换阶段 | 检查目标 → 阶段 1 写入 → 验证阶段 1 → 阶段 2 写入 → 验证阶段 2 → 汇总所有产出 |

普通读取、字段画像、定位等辅助调用不单独要求复核；若一个调用自身产生可报告结果（例如 `excel_aggregate`），仍要求其自身验证 envelope 或有明确关联的验证记录。阶段验证失败、缺失或范围不匹配时，不得把该阶段标为已验证，也不得以成功口吻继续依赖它。

### 实施阶段

| 阶段 | 实施内容 | 完成条件 |
|---|---|---|
| V9-0 方案与基线 | 明确“逻辑产出/阶段边界”定义；记录当前规划器通常将验证放在计划末尾、账本按产出项汇总但不按执行步骤关联的限制。 | 本节完成；不修改现有验证语义。 |
| V9-1 计划检查点 | 为执行步骤增加稳定的步骤标识及验证目标标识；写入技能若要求验证，则在每个写入阶段后插入一个针对该阶段的验证步骤。若模型在初始阶段完成后继续提出写入，运行时追加同样受验证约束的阶段，不要求事先猜测步骤数量。只读分析仍以产出自验证和最终显式检查为主，不为每个工具调用增设步骤。保留最终回合账本汇总，不重复要求无意义的末尾工具调用。 | 顺序为 mutation → 对应 verify → 下一 mutation；任意后续写入同样经过关联验证；简单单阶段任务仍只有一个阶段检查点。 |
| V9-2 运行时门控 | 把当前步骤标识传入写入产出/验证登记；只有成功的写入调用才完成 mutation 步骤，只有产生通过且关联到目标写入阶段的验证证据才完成该 verify 步骤。验证通过后若出现新的写入调用，先创建新的 mutation/verify 检查点；验证未通过时仍阻止下一次写入。只读分析继续使用工具内嵌验证及账本的产出覆盖规则。验证失败沿用有限重试；缺证据或耗尽重试时停止依赖该阶段并显式降级。 | 不相关的成功读取不能越过 mutation；无效/失败验证不能解除写入检查点；重试耗尽后不输出成功验证结论。 |
| V9-3 账本关联 | 将阶段标识写入产出项和验证记录。计划内阶段产出优先按明确的 `verifiesStepId` 关联，不再让同一工作表上后续无关验证覆盖它；保留 `excel_aggregate` 同调用内验证、格式回读精确地址匹配及现有未计划调用的兼容路径。回合档位仍按全部产出中最低有效档位汇总。 | 阶段 1 的验证不能覆盖阶段 2；没有匹配验证的产出继续为 C；现有聚合与格式规则不回退。 |
| V9-4 自动化与隔离验收 | 增加计划生成、顺序门控、验证失败/重试、阶段关联和账本覆盖回归测试；再用现有 Excel Eval 写入夹具副本验证至少一个多阶段场景。 | 定向测试通过；Eval 使用逐轮隔离副本；不写入用户工作簿。 |

### 验收标准

1. 每个要求验证的写入阶段后都有唯一关联检查点；不得先执行下一阶段写入，包括运行时追加的后续写入阶段。
2. 对计划生成的写入检查点，只有对目标写入阶段产生可解析且通过的验证证据，才能完成该检查点；工具调用成功本身不等于验证成功。
3. 验证失败可按既有预算重试；预算耗尽或证据不适用时保留未覆盖状态、降为 C，并说明原因。
4. 每个回合结束时仍按全部产出汇总；阶段覆盖不能只看最后一个结果或最后一次验证。
5. 一般只读辅助调用不新增冗余验证步骤；`excel_aggregate` 等自带验证的产出保持其同调用验证要求。
6. 既有 Agent 自动化测试全量通过；写入 Eval 仅作用于隔离 fixture 副本，不将结果解释为用户工作簿验收。

**进度（2026-10-06）**：V9-0 至 V9-4 已完成。已实现阶段标识、写入顺序门控、失败重试和账本关联；后续写入会动态追加检查点。Agent 全量测试 531/531 通过；`transform-wide-monthly-to-long` 隔离写入 Eval 3/3 通过。未测试或修改用户实际工作簿。

---

## 附录 A：已核实的代码实况

> 2026-10-04 读码核实。

| 结论 | 证据 |
|---|---|
| 托管分析不再一次性读表并截断；按块读取，全部源列的总格数超过 50,000,000 时明确失败 | `ExcelColumnReader`；`ExcelAggregationService` 三个分析工具设置 `IncludeAllColumns = true`，保留旧整行空白判定 |
| 守恒计算携带覆盖报告；没有可参与计算的数据行时失败而不是返回 0 | `ExcelHostAdapter.Facade.cs` 的 `ComputeScalarAggregate` |
| 金丝雀会把 `compareTo` 重定向到样本表 | `BuildProbeExpectation` 将 `CompareTo.WorksheetName` 改为 `_wip_probe_src`；写死期望值的规则在样本上跳过 |
| `_wip_` 免确认经过双重门控 | 共享文本策略排除 With 正式表、CodeName、索引及保护调用；HostBridge 通过 `ExcelComDispatcher` 在 UI 线程检查全表 `Cells.Locked`，不满足则转常规确认 |
| HostBridge 请求从管道异步进入，Excel COM 操作封送到 UI 线程 | `NamedPipeHostBridgeServer` 异步处理；`ExcelComDispatcher` 负责 Excel 工具与免确认预检 |
| VBA 看门狗由计时器线程发送 Ctrl+Break | `ExcelVbaService.StartExecutionWatchdog` |
| Excel 侧无 COM 忙碌重试 | 仅 `PowerPointComDispatcher` 处理 `RPC_E_CALL_REJECTED` |
| 工具结果的完整内容会持久化到会话 | `BrokerTurnExecutor`：`AddConversationMessage(conversation, "tool_result", ..., persistedToolContent, ..., outcome.StructuredContentJson)` |
| 回合收尾的接入点 | `BrokerTurnExecutor` 中 `if (!completion.HasToolCalls)` 分支，pending 检查之后的 `return` 之前 |
| `excel_aggregate` 已支持筛选、分组、10 种派生分组 | `ExcelAggregateRequest.Filters` / `GroupBy` / `DerivedGroupBy` |
| `ExcelToolBase` 无统一执行包装 | 各工具各自实现 `ExecuteAsync`；统一接入点只能放在 `DefaultHostBridgeRequestHandler` |
| 工程目标框架 | VSTO 与 Host 工程为 .NET Framework 4.7.2；Contracts / Planning / OpenXml 为 netstandard2.0 |
| 运行时不引用宿主契约，也从未解析工具结果的结构化字段 | `Agent.Runtime.csproj` 无 `Host.Abstractions` 引用；验证失败只经 `outcome.ErrorCode` 传递（`SkillPlanSupport.CanRecoverFromVerificationFailure`） |
| `HostBridge.Contracts` 无 JSON 依赖；两侧序列化器不同 | Contracts 只引用 BCL；宿主 DTO 用 `[DataContract]`，运行时用 `System.Text.Json` |
| `_wip_` 前缀统一定义 | `StagedWriteConfirmationPolicy.WipPrefix` 是唯一前缀常量；Host.Abstractions / Host.Excel 直接引用该 Contracts 项目 |
| `Host.Excel`、`Host.Abstractions`、`Agent.Tests` 为旧格式工程 | csproj 中逐个列出 `<Compile Include>`；`Directory.Packages.props` 对其不生效 |
| 已有离线夹具导出的先例 | `OfficeCopilot.OpenXml.SmokeTest` 的 `--export-attachment-fixtures`（SDK 格式 net472，已引用 `OpenXml.Excel`） |
