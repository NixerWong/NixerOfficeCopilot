# 多语言（i18n）方案与开发步骤（Blueprint）

> 目标：整个产品（三个 VSTO 加载项 + 设置窗体 + 任务窗格面板 + Broker 用户可见文本）支持 **English / 简体中文** 切换，并为以后增加其他语言（如 繁體中文、日本語）留好扩展点。入口为 [fmSysConfig](../OfficeCopilot.Settings.WinForms/fmSysConfig.cs) 的 `cbeLanguage` 下拉框。
>
> 原则：
> 1. **只有一个语言源**：一份设置 → 一个 `LocalizationService` → 所有 UI 层（WinForms / DevExpress / Ribbon / WebView2 / Broker）。
> 2. **自有文本不依赖线程 Culture**：自有字符串查找总是显式传入 Culture；只有 DevExpress 内置文本依赖 `CurrentUICulture`（见 §3.1）。
> 3. **只改 UI Culture，不改格式 Culture**：数字、日期、Excel 数据格式仍跟随 Office/Windows 的 `CurrentCulture`，避免影响计算与 VBA。
> 4. **面向用户的文本 ≠ 面向模型的文本**：系统提示词、工具描述、意图关键词不在本方案的翻译范围内（见 §2）。

---

## 0. 现状盘点

### 0.1 语言入口

| 位置 | 现状 |
|---|---|
| [fmSysConfig.Designer.cs](../OfficeCopilot.Settings.WinForms/fmSysConfig.Designer.cs) | `grpLanguage` / `lbLanguage` / `cbeLanguage`（`ComboBoxEdit`，禁用文本输入）已布局，**无任何条目与逻辑** |
| [fmSysConfig.cs](../OfficeCopilot.Settings.WinForms/fmSysConfig.cs) | `WireEvents` / `BindGeneralSettings` / `ApplyGeneralSettings` 均未涉及语言 |
| [OfficeCopilotSettings.cs](../OfficeCopilot.Settings.Abstractions/OfficeCopilotSettings.cs) | 无语言字段；`ConfigurationVersion = 7` |
| 全解决方案 | 没有任何 `CurrentUICulture` 赋值、`ResourceManager.GetString`、`Localizable=true` 的窗体 |

### 0.2 DevExpress 本地化资源

- DevExpress 26.1.3 通过安装目录 `$(ProgramW6432)\DevExpress 26.1\Components\Bin\Framework` 引用（[Settings.WinForms](../OfficeCopilot.Settings.WinForms/OfficeCopilot.Settings.WinForms.csproj)、[Panel.WebView](../OfficeCopilot.Panel.WebView/OfficeCopilot.Panel.WebView.csproj)、[Slides.Rendering](../OfficeCopilot.Slides.Rendering/OfficeCopilot.Slides.Rendering.csproj)）。
- 安装目录只自带 `de` / `es` / `ja` 三种卫星程序集，构建时被 MSBuild 自动带进 `bin`；**`zh-Hans` 不在安装目录里**，是从 DevExpress Localization Service 单独下载的语言包。
- `zh-Hans` 目前**只存在于 `NixerExcelCopilot\bin\{Debug,Release}`**（126 个 `*.resources.dll`，含 Blazor / Dashboard / AspNetCore 等完全用不到的程序集），Word / PowerPoint 的 `bin` 里没有。没有任何 csproj / targets / 脚本负责复制它——是手工放入的，**不可复现**。
- [Stage-Package.ps1](../installer/Stage-Package.ps1) 把四个输出目录按相对路径合并，`installer\staging\zh-Hans` 因 Excel 输出而“碰巧”存在；`de` / `es` / `ja` 也被一并打包。
- 本机反射 `DevExpress.Data.v26.1.dll` 已确认可用的 API：`XtraLocalizer.QueryLocalizedString`（事件参数含 `Culture` / `StringIDType` / `ResourceStringID` / `Value`）、`XtraLocalizer.UserResourceManager`、`XtraLocalizer.HandleRequestsFromAllThreads`、`XtraLocalizer<T>.Active` / `SetActiveLocalizerProvider`。

### 0.3 硬编码中文规模（字符串字面量，排除测试 / Eval / 注释）

| 工程 | 文件 | 字面量 | 主要性质 |
|---|---:|---:|---|
| OfficeCopilot.Host.Excel | 80 | 1724 | 工具结果 / 校验信息（**面向模型为主**） |
| OfficeCopilot.Settings.WinForms | 24 | 773 | **设置窗体 UI**（`fmConfig` 174 + Designer 71 …） |
| OfficeCopilot.Host.PowerPoint | 24 | 587 | 工具结果 / Deck 说明（面向模型为主） |
| OfficeCopilot.Agent.Runtime | 35 | 523 | 混合：`TrustFooterBuilder`（用户可见）、`DefaultContextBuilder`（面向模型） |
| OfficeCopilot.Agent.Planning | 5 | 337 | **意图关键词**（匹配用户输入，禁止翻译） |
| OfficeCopilot.Panel.WebView | 21 | 269 | **面板 UI** + 附件提示 |
| OfficeCopilot.Settings | 4 | 134 | `BuiltInHostPrompts`（面向模型）+ `OfficeCopilotSettingsValidator`（用户可见） |
| OfficeCopilot.Slides.* | 19 | 189 | 预览窗体 / 渲染诊断 |
| Nixer{Excel,Word,Powerpoint}Copilot | 10 | 102 | **Ribbon、About、启动异常提示**（三份同构） |
| OfficeCopilot.Panel.WebAssets | 16 | ~70 | **前端 JS 文案**（按钮 title、状态、确认卡片） |
| 其余工程 | — | ~70 | 零散异常信息 |

结论：真正需要翻译的 **用户可见 UI 文本约 1300–1500 条**，集中在 Settings.WinForms、Panel.WebView、WebAssets、三个 VSTO 外壳和 Runtime 的少数文件；Host.Excel / Host.PowerPoint 的 2300+ 条大部分是喂给模型的，不在首批范围。

### 0.4 已有的“好习惯”（可直接复用）

- `cbeTrustMode`、`cbeTranscriptLogMode`、`cbeRuntimeLogLevel` 均按 **索引** 映射枚举，切换语言不影响已保存的设置值。
- 全仓库只有 [ExcelEvalRunner.cs](../OfficeCopilot.Eval/Excel/ExcelEvalRunner.cs) 用 `case "全量独立复核通过":` 比较显示文本——它依赖 `TrustFooterBuilder` 的中文输出，Phase 5 需同步处理。
- 面板与前端之间已有统一消息通道 [PanelWebViewBridge.cs](../OfficeCopilot.Panel.WebView/Presentation/PanelWebViewBridge.cs)（`PostWebMessageAsJson`），可直接承载语言包下发。
- Panel → Broker 的 [AgentBrokerTurnRequest](../OfficeCopilot.Agent.Client/AgentBrokerTurnRequest.cs) 已有 `Metadata` 字典，可零协议破坏地携带 `uiCulture`。

---

## 1. 目标行为

| 项 | 行为 |
|---|---|
| 可选语言 | `跟随 Office`（auto）/ `English` / `简体中文`；列表由 `SupportedLanguages` 注册表生成，新增语言不改 UI 代码 |
| 下拉显示 | 每种语言用**其自身语言**显示（English、简体中文、日本語），与当前 UI 语言无关，避免用户切错后看不懂 |
| 存储 | `OfficeCopilotSettings.UiLanguage`，取值 `"auto"` / `"en"` / `"zh-Hans"`（BCP-47，与 DevExpress 卫星目录名一致） |
| auto 解析 | Office UI 语言（`LanguageSettings.LanguageID[msoLanguageIDUI]`）→ 映射到已支持语言；`zh-CN`/`zh-SG`/`zh-Hans*` → `zh-Hans`；未支持的语言 → `en` |
| 回退链 | 缺失翻译的 key 回退到中性资源（English），绝不显示 key 名或空串 |
| 生效时机 | 见 §5：MVP 为“保存后提示重启 Office 完全生效”，Ribbon / 面板 / 设置窗体自身在当前进程内即时刷新 |
| 不变的部分 | `CurrentCulture`（数字/日期格式）、会话历史内容、用户写的提示词与 Skill、模型输出语言（由模型跟随用户输入，见 §4.9） |

---

## 2. 文本分类与范围

每一条硬编码文本按下表归类，**只有 A/B/C 类进资源文件**。

| 类 | 定义 | 例子 | 处理 |
|---|---|---|---|
| **A. 静态 UI** | 窗体/控件/Ribbon 的 Caption、Text、Tooltip、列头 | `grpLanguage.Text = "语言"`、`btnTogglePane.Label = "显示面板"` | 资源化，`ApplyLocalization()` 统一赋值 |
| **B. 动态 UI 消息** | MessageBox、状态栏、校验错误、面板提示、前端按钮状态 | `"打开模型管理失败：" + ex.Message`、`'执行中'` | 资源化，带占位符 `{0}` |
| **C. 运行时用户可见文本** | Broker/Runtime 生成并展示给用户的文字 | `TrustFooterBuilder` 校验页脚、工具显示名 `DisplayName`、ask_user 默认按钮 | 资源化，按每轮请求携带的 `uiCulture` 解析 |
| **D. 面向模型的文本** | 系统提示词、工具 description、工具结果、上下文注入 | `BuiltInHostPrompts`、`DefaultContextBuilder`、Host.Excel 工具返回信息 | **本方案不翻译**；只加“回答语言”策略（§4.9） |
| **E. 输入匹配关键词** | 用于匹配用户输入的词表/正则 | `StructuredExcelIntentClassifier`、`OfficeSkillCatalog`、“记住/记录下来” | **禁止翻译**；以后另立“多语言输入理解”专题补充英文关键词 |
| **F. 日志 / 诊断 / 注释** | `ILogger`、Trace、异常内部信息、代码注释 | `StartupTimingLog` | 不翻译（日志保持稳定以便检索） |

> 判定口诀：**“用户的眼睛会看到，并且它不会被送进模型或拿去做匹配”** → A/B/C；否则 D/E/F。
> 边界情况：TrustFooter 既展示给用户，又被 `TrustFooterBuilder` 的正则从模型输出中剥离、被 Eval 解析——属于 C，但正则需同时识别所有语言（§4.8）。

---

## 3. 关键技术约束

### 3.1 VSTO 进程内的 UI Culture

- 三个加载项都运行在 Office 进程的**主 STA 线程**上，该线程与 Office 本身、其他厂商的加载项共享。`Thread.CurrentThread.CurrentUICulture` 是线程级状态，在 .NET Framework 中修改它**是否会泄漏到其他加载项（它们各自在独立 AppDomain）需要实测**——这是本方案的头号风险，放在 Phase 0 Spike S1 验证。
- 因此设计上分两层：
  - **自有文本**：`LocalizationService` 一律调用 `ResourceManager.GetString(key, explicitCulture)`，完全不依赖线程 Culture，任何情况下都正确。
  - **DevExpress 内置文本**（右键菜单、Find 面板、XtraMessageBox 按钮、日期选择器…）：DevExpress 的 ResX 本地化器按**查找时**的 `CurrentUICulture` 加载卫星程序集，所以必须选定一种策略（S1 结果决定）：
    - **方案 A（首选，标准做法）**：在 `ThisAddIn_Startup` / Ribbon 构造时（任何 DevExpress 控件创建前）设置 UI 线程的 `CurrentUICulture`；同时 `CultureInfo.DefaultThreadCurrentUICulture`（只影响本 AppDomain 的新线程）。前提：S1 证明不会污染其他加载项或 Office 的托管 UI。
    - **方案 B（隔离做法）**：不改线程 Culture；挂 `XtraLocalizer.QueryLocalizedString` + `HandleRequestsFromAllThreads`，在回调里按 `StringIDType` 所在程序集、`ResourceStringID`，用显式 Culture 从卫星资源取值并写入 `e.Value`。S2 需验证能否通用地定位每个 DevExpress 程序集的资源基名。
  - **不采用**：在每个事件入口临时切换再还原 Culture（DevExpress 很多文本在消息循环中延迟取，比如弹出菜单，会出现中英混杂）。
- `System.Windows.Forms.MessageBox` 的按钮文字跟随 Windows 语言，无法本地化。需要按钮也一致的场景改用 `XtraMessageBox`（受 DevExpress 本地化控制）。

### 3.2 Ribbon 早于 Startup 创建

VSTO 先调用 `CreateRibbonExtensibilityObject` 再触发 `ThisAddIn_Startup`；Ribbon Designer 的 `InitializeComponent` 会写入中文 Label。所以：

- 语言初始化入口 `LocalizationBootstrap.EnsureInitialized()` 必须**幂等**，并在 Ribbon 构造函数与 `ThisAddIn_Startup` 中都调用，先到先得。
- auto 模式在 Ribbon 阶段可能还拿不到 `Application.LanguageSettings`，退化为读注册表 `HKCU\Software\Microsoft\Office\16.0\Common\LanguageResources\UILanguageTag`，再退化为 `CultureInfo.InstalledUICulture`。
- Ribbon 在 `Load` 事件里调用 `ApplyLocalization()` 覆盖 Designer 的 Label；运行时修改 `RibbonButton.Label` 会自动刷新，天然支持即时切换。

### 3.3 多进程

| 进程 | 语言来源 |
|---|---|
| EXCEL / WINWORD / POWERPNT（各自一份 `LocalizationService`） | 共享的 settings JSON（[JsonSettingsStore](../OfficeCopilot.Settings/JsonSettingsStore.cs)） |
| Broker（每个 Office 窗口一个） | 启动参数 `--ui-culture` 作为默认值 + 每轮 `AgentBrokerTurnRequest.Metadata["uiCulture"]` 覆盖 |

- 在 Excel 里切换语言，Word/PowerPoint 进程只能在**下次启动**或检测到设置文件变化时生效。MVP 接受“其他宿主重启后生效”；Phase 7 可选加 `FileSystemWatcher`。
- Broker 是独立进程，可以放心设置自身的 `CurrentUICulture`，但因为同一 Broker 生命周期内语言可能变化，仍然以**每轮请求携带的值**为准。

### 3.4 布局

设置窗体大量使用绝对坐标（例如 `cbeLanguage.Location = (221, 26)`，宽 85），英文普遍比中文长 30%–100%。需要：

- Phase 0 Spike S3：用伪本地化（§7.3）跑一遍所有窗体截图，列出截断点。
- 修复手段优先级：控件 `AutoSize` / `AutoSizeInLayoutControl` → `LayoutControl` → 加宽固定宽度。**不**为每种语言维护独立布局（不启用 `Localizable=true` 的原因之一，见 §4.4）。

### 3.5 DevExpress 卫星程序集的版本

卫星程序集版本必须与主程序集**完全一致**（26.1.3.0）。DevExpress 升级时（对应 ARCHITECTURE §6.1 的 P0-5 版本漂移风险）必须同步更新语言包，否则静默回退为英文。

---

## 4. 总体设计

```mermaid
graph TB
    S[settings.json<br/>UiLanguage] --> LS[LocalizationService<br/>OfficeCopilot.Localization]
    O[Office UI 语言<br/>auto 模式] --> LS
    LS -->|显式 Culture 查找| R[(Strings.resx<br/>Strings.zh-Hans.resx<br/>…)]
    LS -->|LanguageChanged| F[设置窗体<br/>ApplyLocalization]
    LS -->|LanguageChanged| RB[Ribbon ×3]
    LS -->|LanguageChanged| P[CopilotPanelControl]
    P -->|panel.locale 消息<br/>Web.* 子集| JS[WebAssets i18n.js]
    P -->|Metadata uiCulture| B[Broker / Runtime<br/>C 类文本]
    LS -->|方案 A 或 B| DX[DevExpress 内置文本<br/>zh-Hans 卫星程序集]
```

### 4.1 新工程 `OfficeCopilot.Localization`

- **netstandard2.0、SDK 格式、无任何依赖**，可被 net472 的 VSTO / WinForms / Broker / Runtime 全部引用。
- 内容：

| 类型 | 职责 |
|---|---|
| `SupportedLanguages` | 语言注册表：`Code`（`en` / `zh-Hans`）、`NativeName`（English / 简体中文）、`Culture`。新增语言只改这里 + 加 resx |
| `UiLanguageResolver` | `"auto"` + Office/Windows UI 语言 → 已支持语言；处理 `zh-CN`→`zh-Hans`、`zh-TW`→（未来 `zh-Hant`，现回退 `en`） |
| `LocalizationService`（静态门面 `L`） | `CurrentLanguage`、`Culture`、`Get(key)`、`Format(key, args)`、`GetForCulture(key, culture)`（Broker 用）、`event LanguageChanged`、`GetAll(prefix)`（给前端导出子集） |
| `Resources\Strings.resx` | 中性资源 = **English**，`PublicResXFileCodeGenerator` 生成强类型 `Strings.Settings_SysConfig_grpLanguage_Text` 等属性 |
| `Resources\Strings.zh-Hans.resx` | 简体中文 → 输出 `zh-Hans\OfficeCopilot.Localization.resources.dll` |
| `[assembly: NeutralResourcesLanguage("en")]` | 中性资源即英文，避免英文环境下多一次卫星探测 |

> 为什么集中到一个工程而不是每个工程各自一份 resx：
> 1. 部署时每种语言**只多一个**自有卫星 dll，和 DevExpress 的 `zh-Hans` 放同一目录；
> 2. 翻译人员只面对一组文件，便于做 key 一致性测试；
> 3. 三个 VSTO 外壳的 Ribbon/About 文案相同，可共享 key（用 `{0}` 代入宿主名）。
>
> 代价是 UI 工程都要引用它——它没有依赖，可以接受。资源按 key 前缀分区，文件过大时可拆为 `Strings.Settings.resx` / `Strings.Panel.resx` / `Strings.Runtime.resx` / `Strings.Web.resx` 多个 ResourceManager，但对外仍是一个门面。

### 4.2 Key 命名规范

```
<区域>.<组件>.<元素>[.<属性>]
Common.Button.OK                      ← 跨窗体共享
Common.Error.OpenFailed               = "Failed to open {0}: {1}"
Ribbon.Group.Caption                  = "{0} Copilot"          （{0}=Excel/Word/PowerPoint）
Ribbon.btnModelConfig.Label           = "Models"
Settings.SysConfig.grpLanguage.Text   = "Language"
Settings.SysConfig.cbeTrustMode.Items = "Ask for confirmation|Always allow"   ← 列表项用 | 分隔，保持与枚举索引同序
Panel.Toolbar.btnNewChat.Caption
Web.ToolCall.State.Running            = "Running"
Runtime.TrustFooter.GradeA            = "Grade A: fully verified ({0})"
```

- 元素名直接用控件 `Name`，便于对照 Designer；带格式的文本必须用 `{n}` 占位，**禁止字符串拼接句子**（语序因语言而异）。
- `Web.*` 前缀的 key 会整体下发给前端。

### 4.3 设置与 `cbeLanguage`

1. [OfficeCopilotSettings](../OfficeCopilot.Settings.Abstractions/OfficeCopilotSettings.cs) 增加 `public string UiLanguage { get; set; }`，默认值见 §9 决策 Q1；`ConfigurationVersion` 7 → 8；`OfficeCopilotSettingsFactory.Normalize` 把空值/未知值归一为默认值。
2. [fmSysConfig](../OfficeCopilot.Settings.WinForms/fmSysConfig.cs)：
   - `WireEvents` 中用 `SupportedLanguages` 填充 `cbeLanguage`（条目为 `{Code, NativeName}` 对象，显示 `NativeName`），并挂 `EditValueChanged += GeneralSetting_Changed`。
   - `BindGeneralSettings` / `ApplyGeneralSettings` 读写 `UiLanguage`。
   - 保存后若语言变化：调用 `LocalizationService.SetLanguage(code)` → 触发 `LanguageChanged`；本窗体立即 `ApplyLocalization()`；弹一次提示“部分界面需重启 Office 后完全生效”（XtraMessageBox，用**新语言**显示）。
3. 现有中文下拉项（`"禁用", "仅元数据"…`、`"提示确认", "始终放行"`）改为从资源读取，**仍按索引映射**，保存值不受语言影响。

### 4.4 WinForms / DevExpress 窗体：代码驱动的 `ApplyLocalization()`

**不使用** Visual Studio 的 `Localizable = true`，原因：

- 它为每种语言生成一份包含**布局属性**的 `.resx`，13+ 个窗体 × N 种语言，改布局要改 N 份；
- Designer 对 DevExpress 复杂控件（BarManager、LayoutControl、Grid 列）的本地化序列化容易产生大 diff，且与现有 `fmXxx.resx` 冲突；
- 无法运行时切换（需要重建窗体）。

**采用**：每个窗体/用户控件实现

```csharp
internal interface ILocalizable
{
    void ApplyLocalization();
}

public partial class fmSysConfig : HostAwareForm, ILocalizable
{
    public void ApplyLocalization()
    {
        Text = L.Get("Settings.SysConfig.Text");
        grpLanguage.Text = L.Get("Settings.SysConfig.grpLanguage.Text");
        lbLanguage.Text = L.Get("Settings.SysConfig.lbLanguage.Text");
        ceAutoCompactEnabled.Properties.Caption = L.Get("Settings.SysConfig.ceAutoCompactEnabled.Caption");
        // ComboBox 条目：保持 SelectedIndex，替换文本
        LocalizationUi.ReplaceItems(cbeTrustMode, "Settings.SysConfig.cbeTrustMode.Items");
        // ...
    }
}
```

- 在构造函数 `InitializeComponent()` 之后调用一次；订阅 `LocalizationService.LanguageChanged`（窗体关闭时退订）。
- `HostAwareForm` 基类统一做订阅/退订，子类只写 `ApplyLocalization()`。
- Designer 里的中文**保留不动**（设计时可读），运行时被覆盖；等全部窗体迁移完成后，可选择把 Designer 文本批量改为英文。
- 提供一个**开发期辅助工具**（Phase 1 交付）：遍历窗体控件树，导出 `Name → Text/Caption/Hint` 为 resx 草稿，减少手工抄写 773 条。

### 4.5 DevExpress 内置文本与卫星程序集部署

1. **语言包入库方式**（二选一，见 §9 Q3）：
   - 放入仓库 `third_party\DevExpress\26.1.3\zh-Hans\`，只保留实际引用的程序集（约 15 个：`Data`、`Data.Desktop`、`Utils`、`XtraEditors`、`XtraBars`、`XtraGrid`、`XtraLayout`、`XtraTreeList`、`XtraVerticalGrid`、`XtraRichEdit`、`RichEdit.Core`、`Office.Core`、`Printing.Core`、`XtraPrinting`、`Pdf/Docs/Spreadsheet.Core` 视引用而定）；
   - 或仿照 `$(DevExpressBin)`，新增 MSBuild 属性 `$(DevExpressLocalizationDir)` 指向机器上的语言包目录，缺失时给出 Warning。
2. 新增 `build\DevExpressLocalization.targets`，由三个 VSTO 加载项（以及需要 DevExpress UI 的其他输出）`Import`：`AfterBuild` 把 `<Culture>\DevExpress.*.resources.dll` 中**与当前 DevExpress 引用匹配**的文件复制到 `$(OutDir)<Culture>\`。语言列表来自一个共享属性 `$(OfficeCopilotUiCultures)`（初始 `zh-Hans`）。
3. 删除 Excel `bin` 里手工放入的 126 个文件，改由 targets 产出；[Stage-Package.ps1](../installer/Stage-Package.ps1) 增加校验：`OfficeCopilotUiCultures` 中每种语言目录都存在、且包含 `OfficeCopilot.Localization.resources.dll`。
4. 可选：不支持的 `de` / `es` / `ja` DevExpress 卫星目录在打包时剔除（减小体积，也避免将来声明支持日语前出现“半日语”界面）。
5. 方案 A / B 的代码都收敛在 [DevExpressSkinBootstrap](../OfficeCopilot.Settings.WinForms/DevExpressSkinBootstrap.cs) 旁新增的 `DevExpressLocalizationBootstrap.Apply(CultureInfo)` 中，三个 `ThisAddIn_Startup` 在 `DevExpressSkinBootstrap.Apply()` 前调用。

### 4.6 VSTO 外壳（Ribbon / About / 启动提示）

- 三个 `*CopilotRibbon` 的 `Load` 中调用 `ApplyLocalization()`，订阅 `LanguageChanged` 即时刷新；所有 `SetExcelWaitCursor("正在打开…")`、`MessageBox.Show("打开…失败")` 改为 `L.Format("Common.Status.Opening", L.Get("Ribbon.btnModelConfig.Label"))` 之类的共享 key。
- `AboutDialog` 三份同构，共用 `About.*` key。
- ARCHITECTURE R4/R5 已说明 `ThisAddIn` / Ribbon 三份复制有意保留；本方案只把**文本**集中到共享 key，不强行抽基类。可选地抽一个 `RibbonLocalizer.Apply(RibbonBase ribbon, string hostDisplayName)` 静态帮助方法按控件 `Name` 批量赋值，三份 Ribbon 各一行调用。

### 4.7 任务窗格面板（WinForms 部分 + WebView2 前端）

**WinForms 部分**（[CopilotPanelControl](../OfficeCopilot.Panel.WebView/CopilotPanelControl.cs) 及其 partial、`AskUserQuestionDialog`、`PlanApprovalDialog`、`PanelModelMenuPresenter`）：

- 工具栏 `BarButtonItem` / `BarSubItem` Caption、`txtChat` 占位符（`CopilotPanelControl.Placeholder.cs`）、附件/错误提示全部走 `L`。
- 面板实现 `ILocalizable`，`LanguageChanged` 时刷新工具栏并向前端推送新语言包。

**WebView2 前端**（[wwwroot/js](../OfficeCopilot.Panel.WebAssets/wwwroot/js)）：

- 新增 `js/i18n.js`：

```js
const dict = Object.create(null);
let lang = 'en';
export function setLocale(next, strings) { lang = next; Object.assign(dict, strings); document.documentElement.lang = next; applyStatic(); }
export function t(key, ...args) { const s = dict[key] ?? key; return s.replace(/\{(\d+)\}/g, (_, i) => args[i] ?? ''); }
function applyStatic() { document.querySelectorAll('[data-i18n-title]').forEach(el => { el.title = t(el.dataset.i18nTitle); el.setAttribute('aria-label', el.title); }); }
```

- 语言包来源：**由 C# 端下发**，消息类型 `panel.locale`，内容为 `LocalizationService.GetAll("Web.")` 在当前语言下（含回退）的字典。这样前端文案与 WinForms 文案**共用同一套 resx**，翻译人员无需维护第二种格式。
- 首次加载：在现有 ready 握手后立即发送一次；`main.js` 在收到语言包前渲染的内容使用 `t()` 的 key 回退（或短暂延迟首屏渲染）。
- 需要改造的文件：`markdown.js`、`tool-calls.js`、`tool-confirmation-ui.js`、`code-blocks.js`、`code-execution-state.js`、`scroll.js`、`clipboard.js`、`state.js`（`THINKING_ACTIVE_LABEL` 等常量改为函数）、`index.html`（`lang` 动态设置）。
- 已渲染的历史气泡中**由前端生成的标签**（如“已完成”“查看备份”）可通过 `data-i18n-*` 属性在切换时重刷；消息正文不变。

### 4.8 Broker / Runtime 的用户可见文本（C 类）

- Panel 在构造 `AgentBrokerTurnRequest` 时写入 `Metadata["uiCulture"] = L.CurrentLanguage.Code`；`BrokerRuntimeHost` 启动 Broker 时追加 `--ui-culture <code>` 作为默认值（用于非 turn 请求，如会话列表标题生成失败提示）。
- Runtime 侧引入 `IUserTextLocalizer`（实现即 `LocalizationService.GetForCulture`），通过 turn 上下文传递，**不读线程 Culture**。
- 需要改造的点：
  - [TrustFooterBuilder](../OfficeCopilot.Agent.Runtime/Verification/TrustFooterBuilder.cs)：页脚文字资源化；剥离用的正则改为同时匹配所有已支持语言（`校验|Verification`、`口径|Basis` …），否则切换语言后旧会话里的页脚剥不掉。
  - [ExcelEvalRunner](../OfficeCopilot.Eval/Excel/ExcelEvalRunner.cs) 解析等级的 `case "全量独立复核通过"` 改为解析**结构化等级**（A/B/C）而不是显示文本。
  - 工具 `DisplayName`（67 处，Host.Excel / Host.PowerPoint）：只用于确认卡片与工具调用卡片标题时资源化（key = `Tool.<toolName>.DisplayName`）；工具 `Description` 属 D 类不动。
  - 确认对话框 / ask_user 的默认按钮文案、Broker 返回给面板的错误提示（`HostBridgeErrorCodes` 对应的人类可读消息）。
  - [OfficeCopilotSettingsValidator](../OfficeCopilot.Settings/OfficeCopilotSettingsValidator.cs)（30 条，显示在设置窗体）。
- **原则**：Broker 发给面板的事件尽量携带“错误码 + 参数”，由面板在本地按当前语言格式化；只有确实只能在 Broker 生成的整段文字（如 TrustFooter）才在 Broker 端按 `uiCulture` 渲染。

### 4.9 面向模型的文本与“回答语言”策略（D/E 类）

- 系统提示词（[BuiltInHostPrompts](../OfficeCopilot.Settings/BuiltInHostPrompts.cs)，101 条）、工具 description、工具结果、上下文注入 **保持现状**。模型对中英混合提示的理解没有问题，翻译它们会改变已调优的 Agent 行为并使 harness 基线失效。
- 新增一条**短的**运行时系统指令（由 `DefaultContextBuilder` 注入）：
  `Reply in the same language as the user's latest message. If it is ambiguous, reply in {UiLanguageEnglishName}.`
  这样英文界面用户用英文提问得到英文回答，中文提问依然中文，不依赖翻译提示词。
- 后续可选（独立专题，不在本蓝图内）：
  - `HostSystemPromptStore` 支持按语言的默认提示词文件（`excel.agent.en.md`）；
  - E 类意图关键词补充英文同义词（`StructuredExcelIntentClassifier`、`OfficeSkillCatalog`、“记住 / remember”）并配套 harness 用例。

---

## 5. 语言切换流程

```mermaid
sequenceDiagram
    participant U as 用户
    participant SC as fmSysConfig
    participant ST as JsonSettingsStore
    participant LS as LocalizationService
    participant RB as Ribbon
    participant PN as Panel(WinForms+JS)
    participant BR as Broker

    U->>SC: cbeLanguage 选 English
    SC->>ST: Save(UiLanguage="en")
    SC->>LS: SetLanguage("en")
    LS->>LS: 方案 A: 设置 UI 线程 CurrentUICulture
    LS-->>SC: LanguageChanged → ApplyLocalization()
    LS-->>RB: LanguageChanged → Label 刷新
    LS-->>PN: LanguageChanged → 工具栏刷新 + post panel.locale
    SC->>U: 提示“DevExpress 内置文本/其他 Office 程序重启后完全生效”
    PN->>BR: 下一轮 turn: Metadata.uiCulture="en"
```

| 界面 | MVP（Phase 1–4） | 完整版（Phase 7） |
|---|---|---|
| 当前进程的 Ribbon / 面板 / 打开中的设置窗体 | 即时 | 即时 |
| 其他已打开的设置窗体 | 下次打开 | 即时（统一订阅） |
| DevExpress 内置文本（已创建的控件） | 重启 Office | 新弹出的菜单/对话框即时，已缓存的需重启 |
| 其他 Office 程序（Word/PPT） | 重启后 | `FileSystemWatcher` 检测设置变化后即时 |
| Broker 文本 | 下一轮对话 | 下一轮对话 |

---

## 6. 分阶段实施计划

每个阶段可独立合并、独立验收；括号内为约略的字符串数量，便于估算工作量。

### Phase 0 · 技术验证（Spike，不合并到主干）

| ID | 验证内容 | 通过标准 |
|---|---|---|
| S1 | 在 Excel 中同时加载本加载项与另一个测试 VSTO 加载项，本加载项在 Startup 设置 `CurrentUICulture = zh-Hans`，观察另一个加载项与 Office 托管 UI 读取到的 Culture | 明确结论：方案 A 是否安全 → 决定 §3.1 走 A 还是 B |
| S2 | 方案 B 原型：`QueryLocalizedString` 回调里用显式 Culture 读取 `DevExpress.XtraEditors` / `XtraBars` / `XtraGrid` 卫星资源 | 不改线程 Culture 的情况下 Grid 右键菜单、XtraMessageBox 按钮显示中文 |
| S3 | 伪本地化截图：把 Settings 13 个窗体 + 面板的文本替换为加长 40% 的伪文本 | 输出截断清单，作为 Phase 3 的布局修复输入 |
| S4 | 确认 DevExpress 26.1.3 zh-Hans 语言包的最小必要程序集集合 | 精简后 Settings / Panel / Slides 所有窗体内置文本仍为中文 |

### Phase 1 · 基础设施（MVP 骨架）

1. 新建 `OfficeCopilot.Localization`（netstandard2.0），加入 sln；实现 §4.1 的类型与空的 `Strings.resx` / `Strings.zh-Hans.resx`。
2. `OfficeCopilotSettings.UiLanguage` + 版本迁移 + Normalize。
3. `LocalizationBootstrap.EnsureInitialized()`：三个 `ThisAddIn_Startup` 与 Ribbon 构造函数调用；auto 模式解析 Office UI 语言。
4. `DevExpressLocalizationBootstrap`（按 S1/S2 结论实现方案 A 或 B）。
5. `build\DevExpressLocalization.targets` + 语言包入库/外部目录（§4.5）；三个加载项输出目录都产生 `zh-Hans\`；清理手工文件；`Stage-Package.ps1` 校验。
6. `fmSysConfig.cbeLanguage` 完整接线（§4.3），`fmSysConfig` 自身先完成 `ApplyLocalization()` 作为样板（~48 条）。
7. `HostAwareForm` 增加 `ILocalizable` 订阅/退订基础设施；开发期“控件文本导出 resx 草稿”工具。

**验收**：三个宿主中切换 English / 简体中文 / 跟随 Office，重启后 DevExpress 内置文本（编辑器右键菜单、Grid 列菜单）与 `fmSysConfig` 全部为所选语言；Word/PPT 的 `bin` 也有 `zh-Hans`；设置文件旧版本可正常升级。

### Phase 2 · VSTO 外壳（~100 条，三份共享约 40 个 key）

Ribbon ×3、AboutDialog ×3、ThisAddIn 启动/保护恢复提示 ×3。
**验收**：三宿主 Ribbon 在 `fmSysConfig` 保存后即时切换语言。

### Phase 3 · 设置窗体（~770 条）

顺序：`fmConfig`（174+71，最大）→ `fmModelConfig` / `fmModelEditor` → `fmMcpConfig` / `fmMcpEditor` → `fmSkillsConfig` / `fmSkillEditor` → `fmMemoryConfig` → `fmBuiltinToolConfig` / `fmToolEditor` → `GridContextMenuHelper` 等辅助类；同时处理 `OfficeCopilotSettingsValidator` 与 `HostApplicationNames.GetDisplayName`。按 S3 清单修复英文截断。
**验收**：英文界面下所有设置窗体无中文残留（§7.2 门禁）、无截断；切换语言后已保存配置不变。

### Phase 4 · 面板（~270 C# + ~70 JS）

`CopilotPanelControl`（含 AttachmentReferences / CodeExecution / ToolSafety / Placeholder partial）、`AskUserQuestionDialog`、`PlanApprovalDialog`、`PanelModelMenuPresenter`、`BrokerToPanelConverter` 中的可见文本；WebAssets `i18n.js` 与 `panel.locale` 消息；`index.html` 的 `lang`。
**验收**：英文界面下工具调用卡片、确认卡片、代码块按钮、思考折叠标签、滚动按钮均为英文；切换语言后面板无需重开即刷新。

### Phase 5 · Broker / Runtime 用户可见文本

`uiCulture` 透传（Metadata + 启动参数）；`IUserTextLocalizer`；`TrustFooterBuilder`（含双语剥离正则）；工具 `DisplayName`；Broker→Panel 错误码化；`ExcelEvalRunner` 改为解析结构化等级。
**验收**：英文界面下 TrustFooter、工具显示名为英文；中文会话历史中的旧页脚在英文界面下仍能被正确剥离；Eval 全绿。

### Phase 6 · 模型回答语言策略

`DefaultContextBuilder` 注入“回答语言”指令（§4.9）；用现有 harness（`docs\excel-eval`、`docs\harness-baseline`）跑中英两种界面语言的回归，确认中文用例行为不变。

### Phase 7 · 加固与扩展

- 即时切换补全（其他窗体订阅、`FileSystemWatcher` 跨宿主同步）。
- 伪本地化语言 `qps-ploc` 作为隐藏选项（仅 Debug 构建可见）。
- 翻译流程：resx ↔ XLIFF/Excel 导入导出脚本，交给翻译人员。
- “新增一种语言”检查清单验证（用 `ja` 走一遍，但不发布）：`SupportedLanguages` 加一行 → 加 `Strings.ja.resx` → `OfficeCopilotUiCultures` 加 `ja` → 准备 DevExpress `ja`（安装目录自带）→ 运行门禁测试。

---

## 7. 测试与质量门禁（加入 `OfficeCopilot.Agent.Tests`）

### 7.1 资源一致性测试

- 每种语言的 resx key 集合与中性资源**完全一致**（多出 = 废弃 key，缺少 = 漏翻）。
- 同一 key 在各语言中的占位符 `{n}` 集合一致；`|` 分隔的列表项数量一致（防止 ComboBox 索引错位）。
- `SupportedLanguages` 中每种语言都有对应 resx；`UiLanguageResolver` 对 `zh-CN`、`zh-SG`、`zh-TW`、`en-GB`、`ja-JP`、`fr-FR` 的映射结果符合 §1。

### 7.2 “禁止新增硬编码 UI 中文”门禁

- 扫描 **A/B/C 类工程**（Settings.WinForms、Panel.WebView、Panel.WebAssets、三个 VSTO 外壳）中非注释、非 `*.Designer.cs` 的 CJK 字符串字面量，与一份 allowlist（迁移期存量）比对；数量只能减少不能增加。迁移完成后 allowlist 清空。
- D/E/F 类工程不纳入扫描。

### 7.3 伪本地化

- `qps-ploc` 资源由构建脚本从中性资源自动生成：加 `[` `]` 包裹、元音加重音、长度 +40%。任何未包裹的文本 = 遗漏；任何被裁掉的 `]` = 截断。

### 7.4 回归

- 设置文件 v7 → v8 迁移测试；`UiLanguage` 空/非法值归一化测试。
- `TrustFooterBuilder` 双语剥离测试；`ExcelEvalRunner` 结构化等级解析测试。
- 现有 118+ 用例与 harness 基线在 `UiLanguage=zh-Hans` 下必须与改造前一致。

---

## 8. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| 修改 Office 主线程 UI Culture 影响其他加载项 | 其他厂商加载项语言异常，用户投诉难定位 | Phase 0 S1 实测；不安全则走方案 B；自有文本从设计上不依赖线程 Culture |
| DevExpress 升级后语言包版本不匹配 | 静默回退英文 | targets 中校验卫星程序集 `AssemblyVersion` 与主程序集一致，不一致给 Error；与 P0-5 一并治理 |
| 英文文本更长导致绝对布局截断 | 界面难看/文字不可读 | S3 伪本地化截图 + 门禁；优先 AutoSize / LayoutControl |
| 翻译 D 类提示词导致 Agent 行为漂移 | harness 回归失败 | 明确 D 类不翻译，只加“回答语言”指令 |
| TrustFooter 文本变化破坏剥离与 Eval | 页脚重复、Eval 误判 | 双语正则 + 结构化等级 |
| `fmConfig` 等大窗体迁移工作量大且易漏 | 中英混杂 | 控件文本导出工具 + §7.2 门禁 + 伪本地化 |
| 语言包授权 | DevExpress 本地化资源随应用分发属授权范围，但入库需团队确认 | §9 Q3 决策；或采用外部目录属性 |

---

## 9. 待决策问题

| # | 问题 | 选项 | 建议 |
|---|---|---|---|
| Q1 | `UiLanguage` 默认值 | `auto`（跟随 Office）/ `en` / `zh-Hans` | **`auto`**：中文 Office 用户升级后界面保持中文，英文 Office 用户自动英文；无法识别时回退 `en`（与 DevExpress 默认一致） |
| Q2 | 中性资源语言 | English / 简体中文 | **English**：符合 .NET 与 DevExpress 惯例，未来新增语言缺翻译时回退到英文比回退到中文更通用。代价：Phase 1 起需先把现有中文翻成英文 |
| Q3 | DevExpress zh-Hans 语言包来源 | 精简后入库 `third_party\` / 外部目录属性 `$(DevExpressLocalizationDir)` | 入库（只含约 15 个必要文件），构建可复现；若授权上有顾虑则用外部目录 |
| Q4 | DevExpress 文本策略 | 方案 A（改线程 UI Culture）/ 方案 B（QueryLocalizedString 隔离） | 由 Phase 0 S1/S2 结论决定；倾向 A（简单、DevExpress 官方路径），前提是 S1 证明无泄漏 |
| Q5 | 即时切换的程度 | MVP 重启生效 / 全量即时 | MVP 先“当前进程主要界面即时 + 提示重启”，Phase 7 再补全 |
| Q6 | 是否打包 DevExpress 的 de/es/ja | 保留 / 剔除 | 剔除，未声明支持的语言不发布 |

---

## 10. 工时预估

> 单位：人日（1 名熟悉本代码库的开发者，含自测，不含评审等待）。字符串迁移按“抽取 + 接线 + 英文初译 + 自测”约 120–180 条/人日估算，英文初译可借助 AI 生成后人工校对。

| 阶段 | 主要工作 | 乐观 | 悲观 | 说明 |
|---|---|---:|---:|---|
| Phase 0 · Spike | S1 线程 Culture 隔离 1–1.5 / S2 方案 B 原型 1–2 / S3 伪本地化截图 1 / S4 语言包精简 0.5 | 3 | 5 | S2 若 S1 证明方案 A 安全可跳过，省 1–2 天 |
| Phase 1 · 基础设施 | 新工程 + 服务 1.5 / 设置字段与迁移 0.5 / 启动引导与 auto 解析 1 / DevExpress 引导 0.5–1.5 / targets + 打包校验 1–1.5 / `fmSysConfig` 样板 1 / `ILocalizable` + 文本导出工具 1–1.5 | 7 | 9 | 方案 B 取上限 |
| Phase 2 · VSTO 外壳 | ~100 条，三宿主共享 key；三宿主各验证一遍 | 1.5 | 2 | |
| Phase 3 · 设置窗体 | ~770 条 / 11 个窗体 5–7；英文截断修复 2–3；校验器与显示名 1 | 8 | 11 | `fmConfig` 最重；若需改用 LayoutControl 重排可再 +2–3 |
| Phase 4 · 面板 | C# ~270 条 2–3；`i18n.js` + `panel.locale` + 即时刷新 2–3；JS ~70 条与回归 1 | 5 | 7 | |
| Phase 5 · Broker / Runtime | `uiCulture` 透传 1；TrustFooter 双语 + Eval 结构化 1.5–2；工具 DisplayName 67 处 1–1.5；错误码化 0.5–1.5 | 4 | 6 | |
| Phase 6 · 回答语言 | 指令注入 0.5；中英两套 harness 回归 1–2 | 1.5 | 2.5 | harness 跑批耗时视模型额度 |
| Phase 7 · 加固 | 全量即时切换 + 跨宿主同步 1.5–2；伪本地化构建 1；一致性/门禁测试 1–1.5；翻译导入导出脚本 0.5–1.5 | 4 | 6 | 可按需裁剪 |
| 翻译校对 | ~1400 条英文终稿校对（术语表统一） | 2 | 3 | 外包专业翻译则转为外部周期 |
| 联调与回归 | 三宿主 × 两语言冒烟、安装包验证 | 3 | 4 | |
| **合计** | | **39** | **56** | **约 8–11 周（单人）** |

**里程碑拆分：**

| 里程碑 | 包含 | 人日 | 单人日历 |
|---|---|---:|---|
| M1 · 可切换（MVP） | Phase 0–2 + `fmSysConfig` | 12–16 | 2.5–3.5 周 |
| M2 · 界面全量英文 | + Phase 3–4 + 翻译校对 + 回归 | 30–43 | 6–9 周 |
| M3 · 完整版 | + Phase 5–7 | 39–56 | 8–11 周 |

**并行：** Phase 1 完成后，Phase 3（设置窗体）与 Phase 4/5（面板 + Broker）互不依赖，2 人并行可把 M3 压缩到 **约 5–7 周**。

**主要不确定性：**

- S1 结论不利、必须走方案 B：+2–3 人日。
- 绝对布局截断严重、需把大窗体改为 `LayoutControl`：+3–5 人日。
- 迁移期间设置窗体/面板仍在频繁改动：合并冲突与重复抽取，建议迁移某窗体期间冻结其功能改动。

---

## 附录 A · 受影响文件清单（按阶段）

| 阶段 | 新增 | 修改 |
|---|---|---|
| 1 | `OfficeCopilot.Localization\*`、`build\DevExpressLocalization.targets`、`OfficeCopilot.Settings.WinForms\DevExpressLocalizationBootstrap.cs`、`ILocalizable` | `OfficeCopilotSettings.cs`、`OfficeCopilotSettingsFactory`、`JsonSettingsStore.cs`（迁移）、`fmSysConfig.cs`、`HostAwareForm.cs`、三个 `ThisAddIn.cs`、三个加载项 csproj、`Stage-Package.ps1`、`NixerOfficeCopilot.sln` |
| 2 | — | 三个 `*CopilotRibbon.cs`、三个 `AboutDialog.cs`、三个 `ThisAddIn.cs` |
| 3 | — | `OfficeCopilot.Settings.WinForms\fm*.cs`（11 个窗体）、`GridContextMenuHelper.cs`、`OfficeCopilotSettingsValidator.cs`、`HostApplicationNames` |
| 4 | `wwwroot\js\i18n.js` | `CopilotPanelControl*.cs`、`AskUserQuestionDialog.cs`、`PlanApprovalDialog.cs`、`PanelModelMenuPresenter.cs`、`PanelWebViewBridge.cs`、`BrokerToPanelConverter.cs`、`wwwroot\js\*.js`、`index.html` |
| 5 | `IUserTextLocalizer` | `AgentBrokerTurnRequest` 使用方、`BrokerRuntimeHost.cs`、`BrokerConfig.cs`、`TrustFooterBuilder.cs`、工具 `DisplayName`、`ExcelEvalRunner.cs` |
| 6 | — | `DefaultContextBuilder.cs` |

## 附录 B · 新增一种语言的步骤（目标状态）

1. `SupportedLanguages` 增加一项（`Code`、`NativeName`）。
2. 复制 `Strings.resx` 为 `Strings.<code>.resx` 并翻译；运行 §7.1 一致性测试。
3. `OfficeCopilotUiCultures` 增加 `<code>`；准备对应的 DevExpress 卫星程序集（`ja` / `de` / `es` 安装目录自带，其他语言从 DevExpress Localization Service 下载）。
4. `UiLanguageResolver` 增加 Office 语言映射（如 `ja-JP` → `ja`）。
5. 伪本地化与截图检查；按需补充 E 类关键词（独立专题）。
