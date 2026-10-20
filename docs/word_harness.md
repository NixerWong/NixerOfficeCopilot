# Word Harness：排版任务收敛与批量排版

> 起因：2026-10-20 会话 `42c22481-31be-4f43-bfb9-b0e7735b0d4b`（“帮我按照论文格式，把当前这篇论文规范一下。”），
> 一轮跑满 300 次工具调用仍未完成；用户回复“继续”后，模型又从第 1 段开始重新排版。
>
> 目标：让 Word 排版类任务**在 3~5 轮内收敛**，并且“继续”能接着上次的进度做，而不是从头再来。
> 收敛要靠 harness 约束（提示词分流、批量工具、跨轮进度、停滞检测），不能只靠模型自觉。

---

## 1. 事故复盘

### 1.1 第一轮的 300 次调用花在哪

每轮只有 1 次工具调用，下表的 “r” 即工具调用轮次。

| 遍数 | 轮次 | 做了什么 | 问题 |
|---|---|---|---|
| 第 1 遍 | r5–r70 | `word_select_document` + `word_insert_text(replaceSelection)` 把全文替换成标题，再逐段 `insert_paragraph` + `insert_text` 重写全文 | 用户在 ask_user 中选的“允许改写”只是让它修正几处错乱段落，模型却重写了全部 33 段，原有格式全部丢失；标题和作者简介之间漏了换段，粘成一段（读回结果：段落 1 = 标题+作者简介） |
| 第 2 遍 | r71–r166 | 每段都执行 `read_paragraphs(maxCount=0)` → `select_paragraph(i)` → `insert_text(replaceSelection, "第i段\n第i+1段")` | 想修复粘连，但替换文本里的换段让段号不断移位，越修越乱；每次都读全文（约 7.5K 字符），上下文膨胀到约 25 万 token |
| 第 3 遍 | r168–r296 | 用 `apply_style("Heading 1")` 报“带指定名称的项目不存在” → `list_styles` → 改用“标题 1/2/3”逐段套；全选设字体；再对每段分别 `select` + `format_selection` + `set_paragraph_format` | 一次只能改一段：32 段 × 3 次 ≈ 100 轮 |
| 收尾 | r297–r300 | 又开始往文末插段落 | 达到 300 轮上限，被强制停止 |

收尾时，模型在不提供工具的总结轮里三次都把工具调用写成了正文（DSML 标记），最终只输出了固定提示：
“已达到本回合工具调用轮次上限……已完成的写入保留在工作区，可回复‘继续’接着完成。”

### 1.2 “继续”为什么从头开始

“继续”那一轮的 `BuildModelMessages` 只有 5 条消息、1699 字符：

```
[0] system  （Word 提示词）
[1] user    帮我按照论文格式，把当前这篇论文规范一下。
[2] assistant 我先读取当前文档，了解它的结构和内容。
[3] assistant 已达到本回合工具调用轮次上限……可回复“继续”接着完成。
[4] user    继续
```

跨轮回放只保留 user/assistant 文本，下面这些都没有带过去：

- 上一轮的 600 条工具调用和结果；
- 用户在 ask_user 中的三个选择（允许改写 / 暂不添加参考文献 / 保留身份证占位）；
- 已完成到哪一步。

模型因此重新执行 `read_paragraphs` → `apply_style("Title")`（又报错一次）→ 从第 1 段开始逐段排版。

### 1.3 其他异常

- **上下文压缩没起作用**：每一轮日志都显示超过阈值、触发了压缩（`Token usage (251735) exceeds trigger threshold (147849)`），
  但随后是 `compact pruned 0 messages`。同一条消息链，压缩用的估算约 25 万 token，`Round N starting` 日志里的 `tokenEstimate` 却是约 5 万，两处差了约 5 倍。
- **收尾提示语不适合 Word**：“已完成的写入保留在工作区”是 PPT 工作区的说法。

---

## 2. 根因

| # | 根因 | 位置 |
|---|---|---|
| R1 | Word 提示词只适合“新写文档”：第一阶段规定“用 `word_insert_text` 写正文”，没有“排版现有文档”的分支，模型就把现有文档重写了一遍 | [BuiltInHostPrompts.cs](../OfficeCopilot.Settings/BuiltInHostPrompts.cs) `WordAgent` |
| R2 | 提示词写死英文样式名 `"Title"` / `"Heading 1"`，在中文版 Word 中样式名叫“标题”“标题 1”，所以报错 | 同上；[WordHostAdapter.cs](../OfficeCopilot.Host.Word/WordHostAdapter.cs) `selection.set_Style(ref styleName)` 只接受本地化名称 |
| R3 | 工具粒度太细：29 个 `word_*` 工具都只能处理当前选区，改一段需要 2~3 次调用，每次都要让模型完整推理一轮 | `OfficeCopilot.Host.Word/Tools` |
| R4 | 按段号编辑却没有校验：替换文本中的 `\n` 会增加段落，后续段号整体移位 | `word_select_paragraph` + `word_insert_text` |
| R5 | 达到轮次上限时，如果模型总结失败，就没有任何进度摘要被保存下来 | [BrokerTurnExecutor.cs](../OfficeCopilot.Agent.Runtime/BrokerTurnExecutor.cs) `StreamNoToolSummaryAsync` |
| R6 | 跨轮次只回放文本：PPT 有 [PriorTurnDeckWorkspaceCarryOver](../OfficeCopilot.Agent.Runtime/Turn/PriorTurnDeckWorkspaceCarryOver.cs) 把 deckId 带到下一轮，Word 没有类似机制 | `OfficeCopilot.Agent.Runtime/Turn` |
| R7 | 压缩触发了却没有裁剪任何消息，两处 token 估算口径不一致 | `BrokerTurnExecutor` 压缩逻辑 |
| R8 | 没有停滞检测：同一工具加同类参数重复上百次、反复读全文，都不会触发任何干预 | `BrokerTurnExecutor` 主循环 |

---

## 3. 方案

### 3.1 提示词分流（解决 R1、R2）

`WordAgent` 开头先判断任务类型：

- **新写 / 续写内容**：保留现有的三阶段流程（先写内容，再排版，最后做内联强调）。
- **排版 / 规范现有文档**（关键词：排版、格式、规范、调整样式、按××格式）：
  1. 用 `word_read_paragraphs` 读一次全文，判断每段的角色（标题、一级标题、二级标题、正文、摘要、关键词等）。
  2. 调用一次 `word_format_paragraphs` 完成整篇排版（见 §3.2）。
  3. 用 `word_read_outline` 回读大纲做验证，对跳过或失败的段落最多补救一次。
  4. **禁止**：先全选再 `replaceSelection`（会清空整篇文档）；为了排版而重写段落文字；逐段调用 `select_paragraph` + `format_selection`。
  5. 确实需要修改文字时（例如修正粘连段落），只改涉及的段落，**从后往前改**，避免段号移位；改完读回一次再排版。
- 样式统一写成内置样式 ID（`Title`、`Heading1`、`Normal`），不再写本地化名称。

### 3.2 批量排版工具 `word_format_paragraphs`（解决 R2、R3、R4）

**原理**：模型读一次全文、给每段分好角色后，一次调用把整张清单交给工具，由工具在 Word 进程里逐段处理，中间不再经过模型。

```json
word_format_paragraphs({
  "roles": {
    "title": { "builtinStyle": "Title",    "fontName": "黑体", "size": 18, "alignment": "center" },
    "h1":    { "builtinStyle": "Heading1", "fontName": "黑体", "size": 14 },
    "h2":    { "builtinStyle": "Heading2", "fontName": "楷体", "size": 12 },
    "body":  { "builtinStyle": "Normal",   "fontName": "宋体", "size": 12, "firstLineIndentChars": 2, "lineSpacing": 20 }
  },
  "paragraphs": [
    { "index": 1,  "role": "title", "expect": "铁路财务共享系统" },
    { "index": 5,  "role": "h1",    "expect": "一、引言" },
    { "index": 14, "role": "h2",    "expect": "（一）实现合同" }
  ],
  "defaultRole": "body",
  "updateStyleDefinitions": false
})
```

实现要点：

| 要点 | 做法 |
|---|---|
| 不依赖选区 | 直接操作 `doc.Paragraphs[i].Range`，不移动光标 |
| 中英文 Word 通用 | `builtinStyle` 映射到 `WdBuiltinStyle`（如 `wdStyleHeading1 = -2`），通过 `doc.Styles[(object)(int)wdStyle]` 取样式；仍兼容传本地化名称 |
| 防止段号错位 | 处理前核对段落开头文字是否与 `expect` 一致，不一致就跳过该段并在结果中报告 |
| 段号稳定 | 排版不增删段落，整批处理过程中段号不会变化 |
| 一步撤销 | 用 `Application.UndoRecord.StartCustomRecord("AI 排版")` / `EndCustomRecord()` 包住整批操作，失败时也要在 `finally` 中结束 |
| 性能 | 处理期间设 `ScreenUpdating = false`，在 `finally` 中恢复 |
| 先套样式后设格式 | 每段先套样式，再设直接格式（字体、字号、段落格式），避免样式把直接格式覆盖掉 |
| 未列出的段落 | 若指定了 `defaultRole`，就按它处理；表格内的段落、空段落默认跳过 |
| 返回结果 | 例如“成功 31 段，跳过 1 段（第 20 段开头不符：期望‘（四）’，实际‘财务人员’）”，并附各角色的段数统计 |

**可选：`updateStyleDefinitions = true`**：先修改“标题 1”“正文”等样式本身的字体、字号和行距，再给各段套样式，不在段落上设直接格式。
好处是格式集中由样式管理，导航窗格和自动目录都能正确识别层级，以后改一处就全篇生效。

**其他参数**（实现时新增）：

- `latinFontName`：西文和数字单独用的字体（如 Times New Roman）；`fontName` 同时设置中文和西文字体，`latinFontName` 再覆盖西文部分。
- `clearDirectFormatting = true`：先清掉目标段落已有的直接格式（段落格式 + 字符格式，句内加粗、标红也会被清掉），再套样式。适合前面已经被反复改乱的文档。
- 角色里还可设置 `bold`、`italic`、`color`、`lineSpacingRule`（single / 1.5 / double / atLeast / exactly / multiple）、`spaceBefore`、`spaceAfter`、`firstLineIndent`（磅）、`leftIndent`、`rightIndent`。
- 角色样式全部在修改前解析，任一样式找不到就整体报错、不做任何修改；单段出错只记入 skipped，不影响其他段落。

`word_apply_style` 也改为识别同一套内置样式别名（`Heading1`、`标题 1`、`Normal`、`正文` 等），在中文版 Word 中传英文名不再报错。

### 3.3 轮次上限收尾（解决 R5）

- 模型总结失败（DSML 泄漏重试仍失败、或内容为空）时，由程序根据本轮工具调用生成进度摘要：
  调用次数按工具分组、最后 N 次写操作及其参数摘要、ask_user 的问答，作为 assistant 消息持久化。
- 收尾提示语按宿主区分：Word / Excel 不再出现“工作区”的说法。

实现：`OfficeCopilot.Agent.Runtime\Turn\RoundLimitProgressSummary.cs`。数据来自本回合的 `toolCallTraces`（不受上下文压缩影响），写操作按工具目录的 `IsReadOnly` 判断；宿主名优先取 `AgentRuntimeOptions.HostApplicationName`，为空时取宿主快照。

### 3.4 Word 跨轮进度（解决 R6）

新增 `PriorTurnWordProgressCarryOver`（仿照 `PriorTurnDeckWorkspaceCarryOver`），从已持久化的上一轮消息中提取：

- `runtime.ask_user_question` 的问题和用户的选择；
- 写操作摘要（改过哪些段、套过哪些样式、批量排版的结果汇总）；
- 最后一次成功的写操作。

以 system 提示注入当前轮，并明确要求：“继续时先读回文档核对现状，从未完成处接着做，不要从头重排。”

### 3.5 压缩修复（解决 R7）

- 统一压缩判断和 `Round N starting` 日志所用的 token 估算口径。
- 查清 `compact pruned 0 messages` 的原因：确认是保护规则过严，还是可裁剪消息的判定有误。
- 旧的 `word_read_paragraphs` / `word_read_document` 全文结果只保留最近一次，更早的折叠成“已读取 N 段（已过期）”。

### 3.6 停滞检测（解决 R8）

在主循环中统计最近 K 轮的调用模式，满足任一条件时注入一次 system 提醒：

- 同一工具 + 同类参数（同一 argsHash 前缀或同一参数键集合）连续出现 ≥ 8 次 → 提示改用批量工具；
- 一轮内全文读取 ≥ 3 次 → 提示不要重复读全文，基于已有结果做事；
- 提醒后仍然重复同样的模式 → 提前进入收尾总结，不再耗到 300 轮。

---

## 4. 工时预估

按一名熟悉本项目的开发者估算（单位：人天，含单元测试和在 Word 中实测）：

| # | 改进项 | 对应根因 | 预估 |
|---|---|---|---|
| 1 | 提示词分流 | R1、R2 | 0.5 |
| 2 | 批量排版工具 `word_format_paragraphs` | R2、R3、R4 | 2 ~ 3 |
| 2b | 可选：改样式定义（`updateStyleDefinitions`） | — | +0.5 ~ 1 |
| 3 | 轮次上限收尾进度摘要 + 宿主化提示语 | R5 | 0.5 ~ 1 |
| 4 | Word 跨轮进度带入 | R6 | 1 |
| 5 | 压缩逻辑修复（通用运行时，需回归 PPT/Excel） | R7 | 1 ~ 2 |
| 6 | 停滞检测 | R8 | 0.5 ~ 1 |
| — | 端到端回归（用本次论文重跑 + PPT/Excel 主流程） | — | 0.5 |

**合计约 6.5 ~ 10 人天。** 第 5 项的根因已查清（见下文第二批状态）。

### 分批交付

- **第一批（1 + 2 + 3）：约 3 ~ 4.5 天**。直接解决“排版来回好几遍、停不下来”，收益最大。
  - 状态：代码与单元测试已完成，整个解决方案编译通过、测试全部通过；**尚未在真实 Word 中实测**（重点核对：中文版 Word 下内置样式索引、字符单位首行缩进、一步撤销）。
  - 10-20 实测（会话 `67861243`）：`word_format_paragraphs` 成功一次排完 40/45 段、无跳过，但随后又逐段 `format_selection` 重做了 18 段。原因与修复：
    1. 本机 `prompts\Word.agent.txt` 是 8-29 打开编辑器时播种的旧版提示词副本，优先级高于内置提示词，新提示词未生效。→ 播种/重置时写 `.seed` 哈希，未改过的副本始终跟随最新内置提示词（`HostSystemPromptStore`）；本机文件已备份为 `.bak` 后删除。没有 `.seed` 的旧副本仍按用户自定义处理，其他机器需手动清空。
    2. `word_read_paragraphs` 只返回文字，模型无法确认格式已生效。→ 批量结果在无跳过时明确提示“已完成，不要逐段重做”。
    3. `word_select_paragraph` 选区含段落标记，`replaceSelection` 会把它一起替换，导致与下一段粘连（第一次事故里标题与作者简介粘连也是这个原因），模型为拆分又循环了 5 次。→ 替换文本不以换行结尾时保留段落标记。
- **第二批（4 + 5 + 6）：约 2.5 ~ 4 天**。属于运行时层面的通用加固，PPT 和 Excel 也会受益，但风险较高，需要更多回归测试。
  - 状态：代码与单元测试已完成，整个解决方案编译通过，967 个测试全部通过（4 个跳过）；**尚未在真实 Word 中实测**。
  - ④ `Turn\PriorTurnWordProgressCarryOver`：从最近 3 个回合的持久化工具消息中提取 ask_user 答复、最近一次有写操作的回合（写操作数量、分布、批量排版结果、最近 12 次写操作及其段号），在 `DefaultContextBuilder` 中以 system 提示注入。仅在出现 `word_` 工具时生效，不影响 PPT/Excel。
  - ⑤ 查明 `compact pruned 0 messages` 的原因：
    1. 压缩判断用的是持久化链，其中包含历史回合的 tool_call/tool_result，而这些消息根本不会回放给模型。事故中链估算 27 万 tokens，实际发送只有约 1.1 万（全程最高约 5 万）——是误报。
    2. `TurnSegmenter` 按用户消息分回合，当前回合（700+ 条）整体属于“最近回合”，中段为空，引擎直接返回 null，所以每轮都“触发但裁剪 0 条”。
    3. 隐患：压缩一旦成功，`BuildModelMessages` 重建时不含本回合工具消息，模型会丢失本回合全部进度并从头开始。

    修复（`Turn\InTurnContextMaintenance` + `BrokerTurnExecutor`）：自动压缩先用实际发送内容的估算门控，未达阈值不压缩；压缩后重建时接回本回合的工具调用/结果；token 估算统一计入工具调用参数；每轮发送前折叠本回合已过期的 Word 读取结果（同一读取工具后来又读过，且期间有写入或参数相同）。
  - ⑥ `Turn\WordStallDetector`：最近 30 次调用中同一 `word_` 工具 + 同一参数键集合 ≥ 8 次时提醒改用 `word_format_paragraphs`，提醒后同一模式再出现 6 次则提前收尾（复用 LoopGuard 收尾）；本回合全文读取（`word_read_document` / `word_read_paragraphs`）≥ 3 次时提醒，提醒后再读 2 次则收尾。

---

## 5. 验收标准

用本次事故中的那篇论文（`铁路财务共享系统合同智能审核优化研究.docx` 的原始版本）重跑“帮我按照论文格式，把当前这篇论文规范一下。”：

| 指标 | 事故时 | 目标 |
|---|---|---|
| 工具调用轮次 | 300（未完成） | ≤ 8 |
| 是否重写正文 | 全文重写 | 只修正用户同意修改的段落 |
| 中文版 Word 样式报错 | 2 次 | 0 |
| 撤销 | 数百次 Ctrl+Z | 一次撤销整批排版 |
| “继续”行为 | 从头重排 | 从未完成处接着做（人为把轮次上限调到 5 来测试） |
| 上下文峰值 | 约 25 万 token | < 3 万 token |

单元测试至少覆盖：

- `word_format_paragraphs` 的参数校验、`expect` 不一致时跳过、内置样式映射；
- 跨轮进度提取（ask_user 的选择、写操作摘要）；
- 停滞检测的触发条件和不误触发的情况；
- 收尾时模型总结失败的分支会生成程序摘要。

---

## 6. 诊断数据位置

| 数据 | 路径 |
|---|---|
| 每轮工具调用 trace | `%LOCALAPPDATA%\NixerOfficeCopilot\Traces\<yyyyMMdd>\<conversationId>_<messageId>.jsonl` |
| 会话持久化（含工具结果原文） | `%APPDATA%\NixerOfficeCopilot\Word\sessions\<yyyyMMdd>\<conversationId>.jsonl` |
| 运行时日志（BuildModelMessages、Compact、Round 统计） | `%LOCALAPPDATA%\NixerOfficeCopilot\logs\application.log` |
