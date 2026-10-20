# 三个 Office Copilot 会话历史隔离方案

> 状态：**已实施（代码已改，编译与相关单元测试通过）；§11.2 手工验证、§4.5 SQLite busy timeout 核实尚未完成**（2026-10-15）
> 范围：Word / Excel / PowerPoint 三个加载项的历史会话（对话记录、索引、旧版列表）及其直接相关的本地数据目录。
> 关联文档：[ARCHITECTURE.md](./ARCHITECTURE.md) §3（三进程模型）、§6 P0-4 / P0-5（本轮不处理的发布与依赖项）。

---

## 1. 目标与约束

### 1.1 目标

1. **库层面隔离**：`OfficeCopilot.Agent.Persistence` 自身保证不同宿主的会话互不可见、互不读写，调用方无法通过 ID 越界访问。
2. **文件系统层面隔离**：每个宿主的会话文件、索引、WebView2 缓存位于各自的目录，互不混放，删除或清理一个宿主不影响其他宿主。
3. **适应统一打包**：三个加载项共用一套程序文件（同一份 `OfficeCopilot.Agent.Broker.exe`、DLL、DevExpress 程序集），以缩小安装包体积。

### 1.2 关键约束

统一打包的目的是**尽可能共用相同文件**。因此：

- **隔离不能靠安装目录分开**。三个宿主装在同一个目录，程序文件是同一份，不能在安装目录里为每个宿主放一份副本。
- **隔离必须发生在运行期**：同一个 Broker 可执行文件，由宿主启动参数 `--hostApplicationName` 决定它读写哪一个数据子目录。
- **安装包内不包含任何用户数据**。会话、索引、WebView2 缓存全部在首次运行时按用户创建，位于 `%APPDATA%` / `%LOCALAPPDATA%`，不写入 `Program Files` 或加载项目录。

这条约束决定了本方案的核心做法：**用宿主名作为运行期数据目录的一段路径**，而不是复制程序文件。安装包体积不变。

---

## 2. 现状（已核对的事实）

| 项 | 现状 | 位置 |
|---|---|---|
| 会话根目录 | `%APPDATA%\NixerOfficeCopilot\sessions\<yyyyMMdd>\<id>.jsonl`，三个宿主共用 | `JsonlPaths.DefaultRootDirectory`、`JsonlPaths.SessionsDirectory` |
| 索引 | `%APPDATA%\NixerOfficeCopilot\sessions\index.sqlite`，`conv_id` 为全局主键，`host_app` 仅作为普通列 | `SqliteConversationIndex` |
| 库接口 | `IConversationStore` 的 `GetOrCreateAsync` / `ListAsync` / `DeleteAsync` 都不带宿主参数 | `IConversationStore.cs` |
| 列表过滤 | 只在 Runtime 层做，且 `ListAsync(take)` 先截取再过滤，Word 可能被 Excel 的会话挤出列表 | `AgentRuntimeService.ListConversationsAsync` |
| 读取校验 | `LoadConversation` 不检查宿主，知道 ID 即可读取其他宿主的会话 | `AgentRuntimeService` |
| 旧版列表 | `%LOCALAPPDATA%\NixerOfficeCopilot\ChatSessions\sessions.json`，无宿主字段，合并时不过滤，三个宿主都能看到 | `LegacyChatSessionHistory` |
| Broker 进程 | **每个 Office 窗口一个 Broker 进程**，管道名 `officecopilot-agent-broker-{pid}-{windowKey}`。同一宿主开多个窗口，会有多个进程并发访问同一目录与索引 | `BrokerRuntimeHost`、`ARCHITECTURE.md` §3 |
| 宿主名来源 | `CopilotTaskPaneManagerBase` 统一传入 `hostAdapter.HostApplicationName`，取值为 `Word` / `Excel` / `PowerPoint`（常量在 `HostApplicationNames`） | `CopilotTaskPaneManagerBase.cs` ~299 |
| 宿主名到 Broker | 通过 `--hostApplicationName` 传入，`BrokerApplication` 写入 `AgentRuntimeOptions.HostApplicationName` | `BrokerRuntimeHost.BuildBrokerArguments`、`BrokerApplication` |
| WebView2 缓存 | `%LOCALAPPDATA%\NixerOfficeCopilot\NixerExcelCopilot\WebView2`，**写死为 Excel 名称**，三个宿主共用 | `PanelWebViewBridge.GetUserDataFolder()` |
| 其他同根数据 | 计划文件 `%APPDATA%\NixerOfficeCopilot\plans`、记忆 `%APPDATA%\NixerOfficeCopilot\memory`、转录与诊断日志位于 `%LOCALAPPDATA%` 下 | `PlanFileManager`、`FileSystemMemoryStore`、`AgentRuntimeTranscriptLogger`、`TurnTraceWriter` |

### 2.1 现状的风险归纳

- **库层面**：任何调用方只要拿到 ID 就能跨宿主读写删除，隔离完全依赖 UI 过滤。
- **并发层面**：同一宿主多窗口时多个 Broker 写同一个 `index.sqlite`；索引写失败只记日志不抛异常，索引可能与文件漂移。
- **列表层面**：`take` 先于过滤，宿主数量一多，列表就会缺项，且不报错。
- **缓存层面**：WebView2 共用一个用户数据目录，并且目录名与 Excel 绑定，与宿主无关的 Word/PowerPoint 也写进 `NixerExcelCopilot` 下。

---

## 3. 目标目录布局

```text
%APPDATA%\NixerOfficeCopilot\
    sessions\                       ← 旧版共享数据（保留，不再被读取，见 §8）
    Word\
        sessions\
            <yyyyMMdd>\<id>.jsonl
            index.sqlite
    Excel\
        sessions\
            <yyyyMMdd>\<id>.jsonl
            index.sqlite
    PowerPoint\
        sessions\
            <yyyyMMdd>\<id>.jsonl
            index.sqlite
    _unscoped\                      ← 宿主名为空时的兜底（见 §4.4）
        sessions\...

%LOCALAPPDATA%\NixerOfficeCopilot\
    Word\WebView2\
    Excel\WebView2\
    PowerPoint\WebView2\
    BrokerEndpoints\                ← 已按进程唯一命名，无需改动
    ChatSessions\sessions.json      ← 旧版列表，只读（见 §8）
```

设计理由：

- 宿主名作为**第一层**目录，三个宿主的全部会话数据在一个子树内，删除或备份一个宿主只需处理一个目录。
- `sessions` 保留在宿主目录下，`JsonlPaths` 的内部布局（日期子目录 + `index.sqlite`）不变，只是根目录变了。
- 每个宿主有独立的 `index.sqlite`，`conv_id` 主键的冲突范围缩小到单个宿主。

---

## 4. 库层面设计（`OfficeCopilot.Agent.Persistence`）

### 4.1 接口保持不变

`IConversationStore` 不增加宿主参数。隔离通过**构造时确定作用域**实现，所有方法自动落在作用域内：

- `GetOrCreateAsync(id)`：只能看到本作用域的文件，同一 ID 在另一作用域会新建一个独立会话。
- `ListAsync(take)`：只扫描本作用域的目录和索引，`take` 不再被其他宿主挤占。
- `DeleteAsync(id)`：只能删除本作用域的文件。
- `AppendMessageAsync` / `SaveAsync`：只写本作用域的路径。

调用方（Runtime、Broker）只需在创建时传入宿主名，业务代码不需要改动。

### 4.2 构造函数

```csharp
// 示意，非最终签名
public JsonlConversationStore(string rootDirectory, string hostScope)
```

- 库内部计算实际根目录：`Path.Combine(rootDirectory, hostScope)`。**由库计算而不是由调用方拼接**，调用方无法忘记加宿主段。
- 无参构造 `JsonlConversationStore()` 与单参构造 `JsonlConversationStore(string rootDirectory)` 保留给测试和兼容用途，并在文档中标注为"无作用域"。生产代码路径全部改用带作用域的构造。
- `hostScope` 必须通过校验：仅允许 `[A-Za-z0-9_-]{1,32}`，拒绝路径分隔符、`..`、空白和盘符。校验失败直接抛 `ArgumentException`。

### 4.3 索引

- 索引文件随作用域走：`<root>\<hostScope>\sessions\index.sqlite`。
- 索引中的 `host_app` 列保留，并在写入时等于作用域名。它作为二次校验，用于排查历史数据。
- `ListAsync` 的 SQL 不需要 `WHERE host_app = ?`，因为索引本身已是单宿主的；如果将来有人误把两个作用域指向同一个目录，`host_app` 仍可用于发现问题。

### 4.4 空宿主名

Broker 启动参数缺失 `--hostApplicationName` 时（旧版 Panel、单元测试或未知宿主），有两个选择：

| 方案 | 行为 | 评价 |
|---|---|---|
| A. 落入 `_unscoped` | 可用，数据与三个宿主隔离，并打印警告日志 | **推荐**。不阻断对话，也不会混入宿主数据 |
| B. 拒绝启动 | 直接报错 | 会让旧版 Panel 与新版 Broker 的组合无法对话，风险过大 |

**不允许回落到根目录 `sessions\`**，否则又回到共享状态。

### 4.5 并发

- 同一宿主的多个窗口会有多个 Broker 进程共享一个作用域目录和索引。
- 现有实现每次操作都打开临时连接，依赖 SQLite 文件锁。**需要核实 `Microsoft.Data.Sqlite` 在当前连接串下的 busy timeout 默认值**；如果不足以覆盖多进程写入，应在连接上显式设置 busy timeout（`sqlite3_busy_timeout` 对应的等待时间）。
- 同一会话 ID 同时被两个窗口打开的情况，应在 Runtime 层避免（会话与窗口绑定），本方案不处理。
- 索引写失败被吞掉的问题保持不变，但 `RebuildFromDirectory` 会在下次启动时修复漂移。

### 4.6 测试要求（库层）

在 `JsonlConversationStoreTests` 中新增：

1. 两个作用域（`Word`、`Excel`）对同一 ID 调用 `GetOrCreateAsync`，各自得到独立文件，内容互不影响。
2. `ListAsync(take: 1)` 在 `Excel` 有更新会话时，仍能返回 `Word` 自己的会话（回归 `take` 先于过滤的问题）。
3. 在 `Word` 删除会话，`Excel` 中同 ID 会话不受影响。
4. 非法作用域（`..`、`a\b`、空串、超长）抛出 `ArgumentException`。
5. 索引文件位于各自作用域目录下，不出现在根目录。

---

## 5. Broker 与组合层

| 位置 | 改动 |
|---|---|
| `AgentRuntimeServiceCollectionExtensions.CreateConversationStore` | **已实施**：从 `options.HostApplicationName` 取作用域；为空或不合法时使用 `_unscoped` 并向 stderr 写警告；去掉无作用域的 `new JsonlConversationStore()` 分支 |
| `BrokerApplication` | 已有 `GetHostApplicationName`，无需改动；`runtimeOptions.HostApplicationName` 在服务构建前赋值 |
| `BrokerConfig` / `appsettings.json` | 不改。`jsonlRootDirectory` 仍是根目录，作用域段由库追加；自定义根目录也不会破坏隔离 |
| `AgentRuntimeService.ListConversationsAsync` | **决定不改**。库层作用域生效后，列表本身已只包含本宿主会话，`take` 截取发生在作用域内，原先的过滤顺序问题不再出现。过滤代码保留作为冗余防御 |

作用域名的校验与兜底集中在 `ConversationScope`（`OfficeCopilot.Agent.Persistence.Jsonl`），Runtime 与 Panel 共用同一套规则，避免两处各自实现。

注意：`appsettings.json` 与 Broker 可执行文件一样是共享的。不要为每个宿主放置单独的配置文件，否则会破坏"共用文件"的目标。

---

## 6. Panel 与 WebView2 层

### 6.1 WebView2 用户数据目录

- **已实施**：`PanelWebViewBridge.GetUserDataFolder(string hostApplicationName)` 返回 `%LOCALAPPDATA%\NixerOfficeCopilot\<Host>\WebView2`。
- 宿主名不在构造时可用（`HostAdapter` 在面板创建之后才赋值），因此 `PanelWebViewBridge` 接收 `Func<string>`，在 `EnsureReadyAsync` 时读取。`CopilotPanelControl.Placeholder` 同样在调用时读取。
- `OfficeCopilot.Panel.WebView` 新增对 `OfficeCopilot.Agent.Persistence` 的项目引用，以使用 `ConversationScope`。
- 前端不使用 `localStorage` / `IndexedDB`（已核对），因此共享的主要后果是 Cookie、缓存和 WebView2 进程组之间的耦合，而不是会话数据泄漏。
- 代价：三个 WebView2 缓存各占一份磁盘空间，首次启动会重新生成缓存。**这些都是运行期数据，不影响安装包体积**，符合统一打包的目标。

### 6.2 旧版 `LegacyChatSessionHistory`

- 该文件没有宿主字段，无法可靠地判断某条记录属于哪个宿主。
- **已实施**：`BrokerSessionRepository` 不再读取、合并或删除 `sessions.json`。Broker 不可用时面板不再回落到旧列表，直接显示空白新会话。
- `LegacyChatSessionHistory` 类及其 `Merge` / `Remove` 测试保留为未被调用的代码，待后续清理。

---

## 7. 其他同根数据（需决定是否按宿主划分）

| 数据 | 当前位置 | 建议 | 待决定 |
|---|---|---|---|
| 计划文件 `plans\` | `%APPDATA%\NixerOfficeCopilot\plans` | 与会话相关，**建议按宿主划分** | 是否跟随会话隔离 |
| 记忆 `memory\` | `%APPDATA%\NixerOfficeCopilot\memory` | 记忆可能是有意跨宿主共享的（如用户偏好）。**不建议默认隔离** | 需要产品确认 |
| 转录日志 / 诊断日志 | `%LOCALAPPDATA%` 下 | 只用于排障，共享可接受；但日志行中需带宿主名，便于区分 | 是否改为按宿主分目录 |
| Turn Trace | `TurnTraceWriter` 路径 | 同上 | 同上 |

本方案的必选范围是**会话历史**（JSONL、索引、旧版列表、WebView2 缓存）。其余项作为后续决定，不阻塞主方案。

---

## 8. 旧数据的处理

### 8.1 根目录的旧共享数据

`%APPDATA%\NixerOfficeCopilot\sessions\` 中已有的会话，由三个宿主混合写入，**无法从文件本身可靠地判断所属宿主**（不过每个 JSONL 首行的元数据中有 `hostApplicationName`，多数情况下可以判断）。

| 方案 | 做法 | 优点 | 缺点 |
|---|---|---|---|
| **A. 原地保留（推荐）** | 不读取、不删除，作为只读归档留在原处 | 零风险，不丢数据 | 旧会话在新版本中不可见 |
| B. 按元数据复制迁移 | 首次启动时，按首行 `hostApplicationName` 把文件**复制**到对应宿主目录，重建索引；无元数据的留在归档中 | 用户旧会话在对应宿主中可见 | 实现与测试量大，需要处理迁移中断；复制而非移动，磁盘占用翻倍 |
| C. 三个宿主都显示 | 保持现状 | 不需要改动 | 与隔离目标直接冲突 |

### 8.2 旧版 `sessions.json`

- 无宿主字段，不能按宿主拆分。
- 推荐：停止合并进列表，文件保留在原处。如需保留可见性，可仅在 Excel 中显示（Excel 是最早的宿主，但这需要产品决定）。

### 8.3 降级兼容

- 新版本写入的宿主目录，旧版本看不到，这是预期行为。
- 旧版本写入的根目录数据，新版本不读取（见 8.1 的方案 A）。

---

## 9. 打包与安装影响

| 项 | 影响 |
|---|---|
| 安装包内容 | **不变**。隔离只新增运行期目录，不新增任何程序文件 |
| 安装包体积 | **不变**。Broker、DLL、DevExpress 仍是同一份 |
| 安装目录写入 | **禁止**。任何会话、索引、缓存都不写入加载项目录或 `Program Files` |
| 首次运行 | 按宿主创建 `Word\`、`Excel\`、`PowerPoint\` 目录（由程序创建，安装包不携带） |
| 卸载 | Advanced Installer 默认不删除 `%APPDATA%` / `%LOCALAPPDATA%` 下的用户数据。这是合理默认，但需要在打包方案中明确 |
| 三个 `.vsto` 注册 | 与本方案无关，仍按各自的 `Addins` 键注册 |

这也是本方案的一个重要性质：**隔离在运行期完成，因此它与"一个安装包"的目标兼容**，不需要为隔离再生成三份程序。

> 注意：本方案不解决发布相关的遗留问题（P0-4 私钥与 ClickOnce 签名、P0-5 DevExpress 版本一致性），这些在 ARCHITECTURE.md 中已标注为本轮不处理。

---

## 10. 实施顺序

1. **库层**（已完成）：`JsonlConversationStore(root, hostScope)` 构造、`ConversationScope` 校验；`JsonlConversationStoreTests` 新增 §4.6 中的隔离、列表、删除、路径与非法作用域用例。
2. **组合层**（已完成）：`CreateConversationStore` 使用作用域。`ListConversationsAsync` 不改（见 §5）。
3. **WebView2**（已完成）：`GetUserDataFolder` 按宿主分目录，两处调用点都改为运行期读取宿主名。
4. **旧数据**（已完成，方案 A）：`BrokerSessionRepository` 不再合并或删除 `sessions.json`；根目录 `sessions\` 原地保留，不读取、不删除。
5. **验证**：编译 Persistence、Runtime、Panel.WebView、Agent.Tests 通过；`JsonlConversationStoreTests`（32 项）、`LegacyChatSessionHistoryTests` 与 `AgentRuntimeCompositionTests`（5 项）通过。§11.2 手工验证待做。

每一步都应单独通过相关测试，再进入下一步。

---

## 11. 验证计划

### 11.1 自动化

- `OfficeCopilot.Agent.Tests` 中 `Persistence` 命名空间的测试（§4.6）。
- `LegacyChatSessionHistoryTests`：确认按决定后的合并行为。
- `AgentRuntimeServiceCollectionExtensions` 相关测试：确认作用域从 `HostApplicationName` 正确传入。

### 11.2 手工验证

1. 启动 Word 与 Excel 各一个窗口，各自创建会话，确认：
   - 文件分别出现在 `%APPDATA%\NixerOfficeCopilot\Word\sessions\` 与 `...\Excel\sessions\`；
   - 根目录 `sessions\` 不再新增文件。
2. 在 Word 中看不到 Excel 的会话，反之亦然，即使会话数超过 `take` 上限。
3. 同一宿主开两个窗口，确认两个 Broker 的会话都能保存，索引无错误日志。
4. 检查 `%LOCALAPPDATA%\NixerOfficeCopilot\<Host>\WebView2` 按宿主生成。
5. 打包前后比较安装包内文件清单与大小，确认无增加。

---

## 12. 待确认事项

以下各项已按推荐方案实施，待用户确认；如需更改，告知即可。

1. **旧会话处理**：已采用方案 A（原地保留）。方案 B（按元数据复制迁移）未实施。
2. **旧版 `sessions.json`**：已停止合并，不在任何宿主中显示。
3. **记忆是否跨宿主共享**：是否保持共享（推荐），还是也按宿主隔离？
4. **计划文件与日志**：是否跟随会话按宿主划分？
5. **空宿主名**：采用 `_unscoped` 兜底（推荐），还是直接报错？

---

## 13. 风险

| 风险 | 等级 | 缓解 |
|---|---|---|
| 同宿主多 Broker 并发写同一索引 | 中 | 核实 SQLite busy timeout；索引失败不阻断，启动时重建 |
| 旧会话在新版本中不可见，用户误以为丢失 | 中 | 发布说明写明；保留原文件；可选迁移（§8.1-B） |
| 空宿主名导致数据落入 `_unscoped` | 低 | 记录警告日志；Panel 始终传入宿主名 |
| WebView2 按宿主分目录增加磁盘占用 | 低 | 仅运行期占用，不影响安装包 |
| 作用域校验遗漏导致路径穿越 | 中 | 白名单校验 `[A-Za-z0-9_-]{1,32}`，并加单元测试 |
| 误把三个宿主指向同一目录 | 低 | 索引中的 `host_app` 二次校验，便于排查 |
