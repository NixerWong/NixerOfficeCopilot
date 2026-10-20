# Excel Eval 任务集

本目录定义 Excel 分析评测任务。标准答案与工作簿夹具由
`OfficeCopilot.OpenXml.SmokeTest` 生成；答案通过 Open XML 独立读取，不依赖被测 Excel 分析代码。

## 生成夹具

从仓库根目录运行：

```powershell
.\OfficeCopilot.OpenXml.SmokeTest\bin\Debug\net472\OfficeCopilot.OpenXml.Word.SmokeTest.exe `
  --export-excel-eval-fixtures .\TestResults\ExcelEval-Contract-Validation
```

导出器不会覆盖或补写已有目录中的文件；旧任务集生成的目录也不会被增量修改。当前任务集的 `fixtureDirectory` 已指向上述隔离目录；如需另选目录，请同步更新该字段。每个 fixture 会生成同名 `.xlsx` 和 `.answers.json` 文件。

## 任务定义

`tasks.json` 的 `fixtureDirectory` 是相对于 `tasks.json` 所在目录解析的路径；每个任务通过 `fixture` 字段引用目录内的一对夹具文件。`runsPerTask` 指定每条任务的重复运行次数。

每条任务包含唯一 `id`、期望技能 `skill`、自然语言 `prompt`、`executionMode`、标签和 `expect` rubric。`routingMode` 默认为 `pinned`；设为 `real` 时 Runner 将 `SkillId` 留空，由产品路由器选择技能，`skill` 仅用于任务分类/期望结果，不会传给运行时钉住技能。含有 real-routing 任务的任务集必须至少有 30% 的任务启用该模式。询问率分组标签彼此独立：`casual-routing` 仅表示口语化/简短措辞；`clarification-required` 与 `expect.clarificationOnly: true` 配对，表示任务必须先追问。此类任务在观测到首个非确认交互后由 Runner 取消本回合，并按“已正确追问”评分，不将取消伪装成完整任务成功。答案路径从对应 `.answers.json` 文件的 `answers` 成员开始，例如：

- `answers.totalRevenue2025`：读取一个字段。
- `answers.dept_2025[*].revenue`：从数组中投影每项的 `revenue`。
- `answers.hiddenRows.length`：读取数组长度。

答案路径仅供 rubric 使用，不是用户提示词的一部分。Rubric 的可执行判定包括数字及容差、百分比精度、必含/任选文本、工作簿断言、工具轨迹和最低可信档位。工作簿断言类型由其 `type` 指定，例如 `formulaRange`、`rangeValuesEqual`、`rangeSumEquals`、`columnSumEquals`、`sheetRowCount`、`valueCount`、`keySetEquals`、`numberFormat`、`errorCellsRetained` 和 `boldRange`。

## 执行边界

- `automatic-read-only` 只用于只读分析；`workbookUnchanged: true` 要求任务结束时工作簿保持不变。
- 写入或执行 VBA 的任务使用 `manual-confirmation`。`workbookUnchanged: false` 表示预期工作簿会被修改；`cellValuesUnchanged: true` 则允许格式发生变化，但要求单元格底层值不变。
- 每条 `manual-confirmation` 任务必须声明 `expect.expectedChangeScope`：用 `{ "sheet": "Sales", "range": "G2:H10001" }` 限定单元格范围，或用 `{ "sheet": "_wip_01_output", "wholeSheet": true, "allowWorksheetAddition": true }` 声明允许新增并整体写入的输出表。删除工作表始终记为越界。Runner 将保存前后的工作簿包进行比较，审计单元格值、公式和样式，忽略公式未变时的缓存重算值；`result.json` 记录变更/越界单元格与工作表计数，`report.md` 按已完成范围审计的写入回合计算提交越界率。此审计衡量变更位置，不替代范围内结果正确性断言。
- 普通只读 Runner 不会执行或批准人工确认任务。显式测试写入模式仅用于 Eval：Runner 为每次运行创建 fixture 副本，将副本写入 `TestResults` 输出目录，以 Agent 模式运行并对输出工作簿执行实际断言；不得把这个选项用于用户工作簿。
- `routingMode: "real"` 的回合将空 `SkillId` 传给 Broker；`trace.jsonl` 的完成事件与 `result.json` 的质量摘要记录实际选中技能、选择原因和置信度，并加入 `skill-routing` 断言对比期望技能。批次 `report.md` 汇总真实路由技能准确率；未产生技能选择的 real 回合计为失败，未运行 real 任务时显示 N/A。仅校验任务集不代表已获得真实路由结果。
- 每次有效写入影响预检都会在 `trace.jsonl` 记录 `write_effect.evaluated`，包含执行契约上限、宿主预检级别、用户选择来源、目标越界和预告超出标志；`result.json` 另记录写入调用计数。报告中的用户选择越界率和模型预告超出率按各自对应的写入预检调用计算，它们衡量越界尝试，不代表写入已执行，也不等同于所有已提交写入的误写率。
- `report.md` 的只读误写率以确实完成 `workbookUnchanged: true` 核验的只读回合为分母，审计覆盖率单独列出；询问率只计非 `tool.confirmation` 的交互请求，分别报告无特殊标签的明确任务、`casual-routing` 口语化路由任务和 `clarification-required` 真实歧义任务，只统计至少观察到一个模型轮次的回合。缺少可计算样本时显示 N/A。
- `report.md` 的“已提交写入超出用户预期范围率”以完成实际工作簿范围审计的人工写入回合为分母；没有审计结果的中断回合不伪装成零越界，也不计入该指标。
- 写入测试只会自动批准任务 `expect.confirmationTools` 中逐项列出的 `tool.confirmation` 请求，并以 `allow-once` 应答；`trace.requiredToolsAny` 仅用于验收工具轨迹，不作为授权白名单。其他工具确认和用户交互仍会取消并判失败。
- `scenario.faultInjection` 只允许 Runner 明确支持的受控注入：首次成功写入 `_wip_01_summary` 后篡改一个汇总值，或首次成功写入 `_wip_01_unmatched` 后删除一条订单。注入在 Broker 发出写入完成事件后，由 Runner 的 Excel STA 线程在后续验证调用前执行；事件处理会等待注入完成。注入只作用于本次运行的隔离副本。Rubric 同时要求观察到注入、注入后的失败断言以及之后成功的 `excel_assert`；未知注入类型不会执行。
- 2026-10-05 恢复验收：`recovery-subtotal-first-attempt-corrupt` 与 `recovery-keyset-first-attempt-corrupt` 各 3 轮，均 3/3 通过、A 档 3/3、产出覆盖率 100%。小计场景只纠正被注入 +1 的 B2；键集合场景在重建后重复行数断言，并使用包含表头的范围比较 OrderID 集合。VBA 离线预检同时拒绝将保留字 `Sub` 用作变量名，避免该语法错误进入 Excel COM。
- 2026-10-05 完整写入验收：最新 batch `run-20261005-151738-a760d6a6` 覆盖 13 项任务、每项 3 轮，39/39 回合及工作簿 rubric 通过，产出验证覆盖率 39/39、数字证据匹配 248/248。可信档位为 A=27、B=12、C=0；39 个回合的页脚与结构化档位一致。小计清理与故障恢复提示均已修正，前者定向 3/3 A，后者定向 3/3 A 且确实触发注入后重试。范围地址证据采用独立类型：仅从工具结果中的合法 Excel 范围推导行跨度，且只支持明确的含表头行数/范围跨度声明，不作为普通数值或计算操作数。历史未匹配数字人工抽查 3 个实例：两个重复的 `151` 可由 `A1:C151` 佐证，现由类型化范围跨度规则匹配；`45365` 无工具来源，拒绝正确。该小样本跨旧批次且含重复实例，不代表当前误报率；数字匹配率也不能替代溯源误报率。
- Turn 汇总会将写入后的验证按产出工作表和声明范围关联，即使该验证属于后续阶段；分阶段写入屏障仍要求验证明确关联当前 mutation step，避免用最终汇总验证提前放行中间写入。VBA 产出范围从唯一的 `declaredWriteTargets` 声明读取，范围不匹配时不会计为覆盖。

多表评测夹具将兼容的三张月表与故意不兼容的表头夹具分开：前者覆盖 union 总和/平均，后者覆盖多区域画像中的列顺序和额外列差异；订单/客户夹具分别覆盖 left lookup 未匹配行和默认重复键拒绝。已有 `orders_customers` 任务继续覆盖两侧键集合差异。union 平均任务验证对合并后全体明细一次求平均，不允许平均各表平均值。

2026-10-07 `multitable-union-average` 首次真实运行因 `runtime.declare_execution_mode` 的 JSON Schema 层级错误在模型调用前被拒绝：澄清对象的 `required` 被放进 `properties`，而不是对象 schema。修复 schema 并区分只读 union 分析与持久化表合并后，该任务真实路由 3/3 通过，均选择 `read-analysis`、答案数值和工作簿未改写断言通过。邻近回归 batch `run-20261007-010722-ad9ce2bf` 仍 3/3 rubric 通过；可信档位为 C（两轮各有 1 个未匹配数字，一轮无未匹配数字），此任务 rubric 未要求最低可信档位，需作为独立验证质量问题继续跟踪。

同日 `fuzzy-cross-sheet-key-differences` 暴露只读计划重复要求最终验证的问题：`excel_compare_key_sets` 已在同一调用中给出通过的独立验证证据，执行计划仍把它留在待验证状态。现允许无待执行写入时的只读检查调用同时完成最终验证步骤；写入后的定向验证屏障不变。键集合结果新增工具直接计算的 `matchedCount` 与 `totalUnmatchedCount`，使模型可能补充的匹配/总差异数字有直接证据。任务 rubric 接受实际工作表名 `Orders` / `Customers`。当时的定向 batch `run-20261007-010536-f1e08b92` 三轮 3/3 通过、A 档 3/3、数字证据匹配 19/19、工作簿未改写 3/3。

任务集当前包含 38 条定义：25 条只读自动任务，以及 13 条写入任务；其中 13 条启用真实技能路由，占任务总数约 34%。口语化路由任务覆盖部门汇总、跨表差异、union 和 lookup；另有 4 条 `clarification-required` 任务专门测量真正信息不足时是否先追问。普通只读命令执行 25 条只读任务；显式写入测试模式可执行全部 13 条写入任务，包括 2 条受控故障恢复任务。

2026-10-07 最新完整只读 cohort：[`run-20261007-032846-eabcb7fc/report.md`](../../TestResults/ExcelEvalReadRuns/run-20261007-032846-eabcb7fc/report.md)（[`summary.json`](../../TestResults/ExcelEvalReadRuns/run-20261007-032846-eabcb7fc/summary.json)）。21 项任务各运行 3 次，共 63/63 通过；pass^k 与 pass@k 均为 21/21。真实路由准确率 39/39，回答数字证据匹配 364/364，产出项验证覆盖率为 54/54，63/63 回合完成工作簿未改写审计且意外改写为 0。可信档位为 A=39、B=6、C=18。六项多表 union/lookup 任务的 18 个回合均为 C 档：答案与任务 rubric 通过，但只读虚拟结果没有可供 `excel_assert` 核验的实际输出范围；按当前策略不伪造断言，因此不能把它们视为已获独立复核。

同一历史报告的明确任务询问率为 0/51；旧 `fuzzy-routing` cohort 询问率为 0/12。该 cohort 的提示词简短口语化，但包含可执行的工作表、键或计算口径，测的是路由而非业务歧义，因此不应与 ≥80% 的澄清目标比较。当前任务集已将此类标签改为 `casual-routing`，并新增 4 条 `clarification-required` 任务；新标签下的询问率须由后续真实 Eval cohort 验收，不能用历史 0/12 结果代替。

2026-10-07 最新完整写入 cohort：[`run-20261007-085838-2e35c830/report.md`](../../TestResults/ExcelEvalWriteRuns/run-20261007-085838-2e35c830/report.md) 覆盖 13 项任务、每项 3 轮，39/39 回合通过，pass^k 与 pass@k 均为 13/13，静默失败为 0/39；这是连续第二次完整写入 cohort 全部通过。年度合计任务的断言提示已明确：对 `Monthly!O2:O501` 这类不含表头的单列地址使用 `headerMode=no`，列名 `A` 是相对于该传入区域的列名，不是工作表列 `O`；source measure 和 target 都采用该区域内的列名。定向复测该任务 3/3 通过。

## Runner（V8-3）

先构建解决方案，然后验证任务集与本地夹具：

```powershell
.\OfficeCopilot.Eval\bin\Debug\OfficeCopilot.Eval.exe validate `
  --task-set .\docs\excel-eval\tasks.json
```

在交互式 Windows 桌面会话中运行一条只读任务（按 `runsPerTask` 重复）：

```powershell
.\OfficeCopilot.Eval\bin\Debug\OfficeCopilot.Eval.exe run `
  --task-set .\docs\excel-eval\tasks.json `
  --task analysis-total-sales-2025 `
  --output-directory .\TestResults\ExcelEvalRuns
```

要运行全部自动只读任务，显式使用 `--all-read-only`；这会按当前配置启动 63 个回合。可用 `--fixture-directory <path>` 覆盖夹具目录、`--timeout-seconds <1-3600>` 设置每回合超时。默认超时为 600 秒。

Runner 从 `%APPDATA%\NixerOfficeCopilot\settings.json` 读取模型配置，优先使用已启用的 `DefaultModelProviderId`；若默认项无效，则回退到 ID 最小的已启用模型。未配置有效 endpoint 或 model ID 时，会在启动 Excel 前明确失败。模型 API key 只随 Broker 请求使用，不写入 Eval 的 `summary.json` 或事件 trace。

需要与先前基线比较时，可增加 `--baseline-summary <summary.json>`，指向前一次运行目录中的 `summary.json`。比较前会校验任务集、任务 cohort、每任务运行次数及工作簿/答案夹具指纹；不匹配时会在启动 Excel 前拒绝比较。

Runner 启动一个独立的可见 Excel COM 实例，打开每次运行的夹具副本，然后按该 Excel 进程 ID 从 `%LOCALAPPDATA%\NixerOfficeCopilot\BrokerEndpoints` 发现加载项发布的 Broker 管道端点。已经手动打开的另一个 Excel 实例不能代替 Runner 所启动进程的端点。加载项与 Copilot 面板必须已启用；若该 Excel 进程注册了多个窗口端点，Runner 会停止并要求通过 `--broker-pipe <name>` 明确选择，绝不自行猜测管道名。

Excel 加载项启动时会预热隐藏任务窗格；首次打开工作簿时也会触发一次预热，以覆盖 Excel 启动时尚无活动工作簿的情况。若 Runner 报告 60 秒内没有 Broker endpoint，先检查 Excel COM 加载项是否启用，以及 `%LOCALAPPDATA%\NixerOfficeCopilot\logs\application.log` 中的任务窗格初始化错误。超时后 Runner 会关闭自己创建的 Excel 实例；用于探测端点的 `endpoint-bootstrap.xlsx` 是临时夹具副本，不是原始夹具。

每条运行会保存 `input.xlsx`、`workbook.xlsx`、`answer.txt`、`trace.jsonl` 和 `result.json`；整个批次另有 `summary.json` 与 `report.md`。普通只读任务使用 `InteractiveMode=ask` 和只读工作簿；未声明的交互会取消回合并判失败。写入测试必须显式传入 `--allow-workbook-write-tests`；每次仍只操作 Runner 复制到本次 run 目录的夹具副本，使用可写 Excel 和 `InteractiveMode=agent`，并通过公式、值、格式、行数、key-set 等工作簿断言验收。自动批准次数记录在 `result.json`；原始 fixture 不会被打开写入。

批量执行时每条任务使用独立 Excel COM 实例，同一任务的重复回合共用该实例；任务间关闭并重新启动 Excel，隔离 VBA / COM 状态。`summary.json` 记录进程 ID 列表。

显式运行所有无故障注入写入任务（使用任务集配置的每任务重复次数）：

```powershell
.\OfficeCopilot.Eval\bin\Debug\OfficeCopilot.Eval.exe run `
  --task-set .\docs\excel-eval\tasks.json `
  --all-manual-confirmation `
  --allow-workbook-write-tests `
  --output-directory .\TestResults\ExcelEvalWriteRuns
```

要只运行一条写入任务，可用 `--task <id> --allow-workbook-write-tests` 替代批量选项。两个恢复任务可分别通过 `recovery-subtotal-first-attempt-corrupt` 和 `recovery-keyset-first-attempt-corrupt` 运行；写入测试不接受只读基线。未知 `faultInjection` 类型在批量模式跳过，在单项模式启动 Excel 前明确失败。

`workbookUnchanged` 比较同一 Excel 实例在任务开始前和结束后的 `SaveCopyAs` 快照，避免把 Excel 首次打开时的文件格式规范化误判为任务写入；指纹会忽略 Excel 保存时生成的 `revisionPtr` 文档标识及其他视图/计算元数据，但保留工作表数据变化的检测。Runner 还要求 trace 中至少出现一个模型轮次；Broker 若只返回运行时状态诊断而未调用模型，该回合会失败，不会被当成有效 Eval 结果。
若 `workbook-unchanged` 检查失败，Runner 会在该回合目录保留 `baseline.xlsx`，可与 `workbook.xlsx` 对比定位变化部件；检查通过时 baseline 会自动清理。聚合工具的 `count` 按非空单元格计数；其独立 COM 复核映射为 `counta`，不改变普通计算查询中 `count` 与 Excel `COUNT` 的数值计数语义。

报告目前统计 pass^k、pass@k、轮次中位数、上下文 token 估算中位数、命中任务 `forbidTools` 清单的工具尝试率、验证覆盖率、回答数字证据匹配率、A 档 rubric 失败率、静默失败率及可信档位分布。Runner 会对命中的调用发送取消请求；该指标不等同于产品技能闸门拦截率。token 是 Broker 的上下文预算估算，不是供应商账单 token。验证覆盖率或数字匹配率缺少对应 telemetry 时显示 N/A；溯源误报率仍需人工标注真实 trace，当前不由数字匹配率替代。

Runner 已实现只读任务使用的数字、文本、可信档位、轮次上限、禁用工具和工作簿不变判定；写入测试模式支持任务清单中声明的公式、数值、行数、键集合、格式和错误值断言。端到端运行需要本机 Excel、已安装并启用的加载项，以及可用的模型配置；仅验证任务集不需要启动 Excel。

2026-10-09 最新全量只读 cohort：25 项任务×3 轮，75/75 rubric 通过；工作簿意外改写 0/75，审计覆盖 75/75；真实路由 39/39；真实歧义任务交互式询问 12/12（100%），明确任务询问率 0/51，口语化路由询问率 0/12。详见[只读报告](../../TestResults/ExcelEval-Final-20261009-211201-ReadOnly/run-20261009-131202-6627e2b2/report.md)。

随后以修订后的任务集重新运行完整写入 cohort：13 项任务×3 轮，39/39 通过；范围审计 39/39 完成，实际越界 0/39。首次 38/39 的唯一失败是 `formula-department-sumif-summary` 公式白名单漏了相对部门引用 `J{row}` 与 `<=DATE(2025,12,31)` 的合法组合；部门数值、公式结果与写入范围均正确。补充该允许模板后再次全量运行，最终报告为 13/13 个任务 `pass^k` 全通过。详见[修订后最终写入报告](../../TestResults/ExcelEval-Final-Write-Rerun-20261009-224459/run-20261009-144508-f9907279/report.md)；首次报告保留为[诊断记录](../../TestResults/ExcelEval-Final-20261009-211201-Write/run-20261009-141429-99c22af4/report.md)。

最终写入报告的产出项验证覆盖率为 79.5%（31/39），模型执行方式预告超出率为 34.8%（24/69），实际提交范围越界为 0/39。前两项是持续观测指标，计划未设通过阈值。此前 `transform-remove-subtotal-rows` 数组下标覆盖表头、重试时替换工作表的问题已通过提示修订及全量 39/39 测试处理。代码构建及 Agent.Tests 全量测试为 712/712 通过。2026-10-09 用户另确认基于 `orders_customers.xlsx` 夹具副本的四项人工冒烟测试全部通过（只读、追加、修改确认/取消、歧义澄清）；当前计划范围最终验收完成。非阻塞后续优化及未设门槛指标见 [行为契约计划第 16 节](../EXCEL_BEHAVIOR_CONTRACT_PLAN.md)。
