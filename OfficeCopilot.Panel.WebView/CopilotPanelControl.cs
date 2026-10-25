using DevExpress.XtraBars;
using DevExpress.XtraEditors.Controls;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Diagnostics;
using OfficeCopilot.Diagnostics.Logging;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Panel.WebAssets;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using OfficeCopilot.Panel.Abstractions;
using OfficeCopilot.Settings.WinForms;
using OfficeCopilot.Panel.WebView.Models;
using OfficeCopilot.Panel.WebView.Presentation;
using OfficeCopilot.Panel.WebView.Services;
using OfficeCopilot.Panel.WebView.Services.References;
using OfficeCopilot.Localization;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using DiagnosticsTranscriptLogMode = OfficeCopilot.Settings.Abstractions.ModelTranscriptLogMode;

namespace OfficeCopilot.Panel.WebView
{
    public partial class CopilotPanelControl : UserControl
    {
        private static string UserCanceledNotice
        {
            get { return LocalizationService.Get("Panel.Status.GenerationStopped"); }
        }
        private static readonly IUserTextLocalizer UserTextLocalizer = new UserTextLocalizer();

        public IAgentBrokerClient BrokerClient { get; set; }

        public IToolRegistry ToolRegistry { get; set; }

        private readonly ILogger _logger;

        public IServiceProvider AgentServices { get; set; }

        private readonly ISettingsStore _settingsStore;
        private OfficeCopilotSettings _settings;
        private readonly PanelModelMenuPresenter _modelMenu;
        private InteractiveMode _interactiveMode;

        private readonly PanelWebViewBridge _webBridge;
        private readonly BrokerSessionRepository _sessionRepository;
        private bool _historySelectionUpdating;
        private bool _isSending;
        private bool _cancelRequestedByUser;
        private bool _loaded;
        private string _activePlaceholderTitleKey;
        private string _activePlaceholderMessageKey;
        private Exception _lastPlaceholderException;

        private List<ChatSessionState> _sessions;
        private ChatSessionState _currentSession;
        private CancellationTokenSource _sendCts;
        private string _activeBrokerRequestId;
        private string _activeBrokerConversationId;
        private string _activeBrokerMessageId;
        /// <summary>最近一条用户原话，仅用于给备份点打上可读的标签。</summary>
        private string _lastUserMessageText;

        /// <summary>
        /// Phase 1.3b: 已向 broker 种子注入过历史的会话 ID 集合，避免重复 IPC。Panel 重启即清空。
        /// </summary>
        private readonly HashSet<Guid> _seededBrokerConversations = new HashSet<Guid>();
        private readonly ReferencePreparationCoordinator _referencePreparationCoordinator;
        private readonly ReferenceSendPlanner _referenceSendPlanner;
        private readonly ReferencePromptComposer _referencePromptComposer;
        private readonly IReferenceArtifactStore _referenceArtifactStore;
        private readonly WebReferenceImporter _webReferenceImporter;
        private readonly List<ReferenceItem> _selectedReferences;

        internal IReferenceArtifactStore ReferenceArtifactStore => _referenceArtifactStore;

        /// <summary>
        /// Phase 7C: 待决的用户交互登记簿（requestId → TCS / toolName）。
        /// </summary>
        private readonly PanelInteractionCoordinator _interactions = new PanelInteractionCoordinator();

        public CopilotPanelControl()
            : this(null, null)
        {
        }

        public CopilotPanelControl(
            ISettingsStore settingsStore,
            ILogger logger)
        {
            InitializeComponent();
            StartupTimingLog.Mark("panel-init-component");

            _settingsStore = settingsStore ?? new JsonSettingsStore();
            _logger = logger;
            _webBridge = new PanelWebViewBridge(
                this,
                wvConversation,
                ShowPlaceholder,
                HidePlaceholder,
                BuildInitializationErrorMessage,
                () => HostAdapter == null ? null : HostAdapter.HostApplicationName);
            _webBridge.WebMessageReceived += CoreWebView2_WebMessageReceived;
            _sessionRepository = new BrokerSessionRepository(() => BrokerClient, logger);
            _modelMenu = new PanelModelMenuPresenter(
                barManager,
                menuModels,
                SyncSelectedModelFromCurrentSession,
                SyncCurrentSessionModelFromSelection,
                EnsureDefaultModelProviderId);
            _interactiveMode = InteractiveMode.Agent;
            BrokerClient = new NullAgentBrokerClient();

            _sessions = new List<ChatSessionState>();
            _referencePreparationCoordinator = new ReferencePreparationCoordinator();
            _referenceSendPlanner = new ReferenceSendPlanner();
            _referencePromptComposer = new ReferencePromptComposer();
            _referenceArtifactStore = new InMemoryReferenceArtifactStore();
            _webReferenceImporter = new WebReferenceImporter();
            _selectedReferences = new List<ReferenceItem>();
            InitializeAttachmentReferenceState();

            InitializePlaceholderOverlay();
            WireEvents();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += CopilotPanelControl_Disposed;
            ApplyPanelLocalization();
            LoadSessions();
            RefreshRuntimeSettings();
            StartupTimingLog.Mark("panel-init-done");
            UpdateSendButtonAppearance(false);

            Load += CopilotPanelControl_Load;
        }

        /// <summary>
        /// Fix: SplitterDistance is a fixed design-time value that WinForms AutoScale
        /// does NOT adjust. Ensure Panel2 (input area) has enough height regardless
        /// of DPI scaling, by shrinking Panel1 (WebView) when necessary.
        /// </summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            AdjustSplitterForDpi();
        }

        private void AdjustSplitterForDpi()
        {
            if (!_loaded) return;

            // --- spcMain (vertical): protect Panel2 (input area) height ---
            int minPanel2 = sbdcAttachmentMenu.Height + panel2BottomDock.Height + 80;
            int currentPanel2 = spcMain.Height - spcMain.SplitterDistance - spcMain.SplitterWidth;

            if (currentPanel2 < minPanel2)
            {
                int newSplitter = spcMain.Height - minPanel2 - spcMain.SplitterWidth;
                if (newSplitter > 0)
                {
                    spcMain.SplitterDistance = newSplitter;
                }
            }

            LayoutAttachmentStrip();
        }

        public ICopilotHostAdapter HostAdapter { get; set; }

        public int? SelectedModelProviderId
        {
            get { return _modelMenu.SelectedModelProviderId; }
        }

        public string SelectedInteractiveMode
        {
            get { return _interactiveMode == InteractiveMode.Agent ? "agent" : "ask"; }
        }

        private void WireEvents()
        {
            btnAgentType.ItemClick += btnAgentType_ItemClick;
            btnAsk.ItemClick += btnAsk_ItemClick;
            btnSend.ItemClick += btnSend_ItemClick;
            btnNewChat.ItemClick += btnNewChat_ItemClick;
            btnDelChat.ItemClick += btnDelChat_ItemClick;
            listHistoryChat.EditValueChanged += listHistoryChat_EditValueChanged;
            txtChat.KeyDown += txtChat_KeyDown;
            WireAttachmentReferenceEvents();
        }

        private async void CopilotPanelControl_Load(object sender, EventArgs e)
        {
            StartupTimingLog.Mark("panel-load-begin");
            _loaded = true;
            ApplyPanelLocalization();
            RefreshRuntimeSettings();
            AdjustSplitterForDpi();
            var webViewReady = EnsureWebViewReadyAsync();
            StartupTimingLog.Mark("panel-load-end");
            await webViewReady;
        }

        public void RefreshRuntimeSettings()
        {
            LoadSettings();
            _modelMenu.Refresh();
            ApplyInteractiveMode(_interactiveMode);
        }

        private void LoadSettings()
        {
            try
            {
                _settings = OfficeCopilotSettingsFactory.Normalize(
                    _settingsStore.Load() ?? OfficeCopilotSettingsFactory.CreateDefault());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "加载运行时设置失败。");
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }

            if (_settings.ModelProviders == null)
            {
                _settings.ModelProviders = new List<ModelProviderSettings>();
            }

            if (_settings.ToolConfirmationPolicies == null)
            {
                _settings.ToolConfirmationPolicies = new List<ToolConfirmationPolicySettings>();
            }

            ToolCallDiagnosticsLogger.Configure(
                _settings.Logging != null
                && _settings.Logging.ToolCalls != null
                && _settings.Logging.ToolCalls.IsEnabled);

            if (_logger != null)
            {
                OfficeCopilotDiagnostics.CreateAndWireToolCallLogSink(_logger, _settings.Logging);
            }

            _modelMenu.SetAvailableModels(_settings.ModelProviders
                .Where(m => m != null && m.IsEnabled)
                .OrderBy(m => m.Id));
        }

        private void EnsureDefaultModelProviderId(int? modelId)
        {
            if (_settings == null)
            {
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }

            if (_settings.DefaultModelProviderId == modelId)
            {
                return;
            }

            _settings.DefaultModelProviderId = modelId;
            SaveSettingsPreservingToolTrustModes();
        }

        /// <summary>
        /// 写回设置前先合并存储中最新的 TrustMode，避免缓存过期覆盖窗体刚保存的值。
        /// </summary>
        private void SaveSettingsPreservingToolTrustModes()
        {
            RefreshToolTrustModesFromStore();
            _settingsStore.Save(_settings);
        }

        private void RefreshToolTrustModesFromStore()
        {
            try
            {
                OfficeCopilotSettingsFactory.RefreshToolTrustModes(_settings, _settingsStore.Load());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "读取最新工具执行权限模式失败。");
            }
        }

        private string ResolveToolTrustMode()
        {
            RefreshToolTrustModesFromStore();
            return OfficeCopilotSettingsFactory.GetToolTrustMode(_settings, HostAdapter == null ? null : HostAdapter.HostApplicationName);
        }

        
        private void btnAgentType_ItemClick(object sender, ItemClickEventArgs e)
        {
            ApplyInteractiveMode(InteractiveMode.Agent);
            PushCurrentSessionToWeb();
        }

        private void btnAsk_ItemClick(object sender, ItemClickEventArgs e)
        {
            ApplyInteractiveMode(InteractiveMode.Ask);
            PushCurrentSessionToWeb();
        }

        private async void btnSend_ItemClick(object sender, ItemClickEventArgs e)
        {
            try
            {
                if (_isSending)
                {
                    _logger?.LogInformation("用户取消当前生成。");
                    await CancelCurrentTurnAsync();
                    return;
                }

                await SendCurrentPromptAsync(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "发送消息时发生未处理异常。");
                MessageBox.Show(LocalizationService.Format("Web.panel.sendException", ex.Message));
            }
            finally
            {
                // === 防崩溃: 确保 UI 一定恢复 ===
                // 不论取消或发送过程中发生任何异常，强制恢复输入框状态。
                if (_isSending)
                {
                    ForceRecoverBusyState();
                }
            }
        }

        private void btnNewChat_ItemClick(object sender, ItemClickEventArgs e)
        {
            CreateNewSession(true);
        }

        private void btnDelChat_ItemClick(object sender, ItemClickEventArgs e)
        {
            DeleteCurrentSession();
        }

        private void listHistoryChat_EditValueChanged(object sender, EventArgs e)
        {
            if (_historySelectionUpdating)
            {
                return;
            }

            var rawValue = listHistoryChat.EditValue as string;
            Guid sessionId;
            if (!Guid.TryParse(rawValue, out sessionId))
            {
                return;
            }

            var session = _sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null || session == _currentSession)
            {
                return;
            }

            _currentSession = session;
            _modelMenu.Refresh();
            RefreshHistoryMenuSelection();
            // 1.4c：消息列表如未加载则从 broker 拉取后再渲染。
            var _ = LoadAndShowCurrentSessionAsync(CancellationToken.None);
        }

        private async Task LoadAndShowCurrentSessionAsync(CancellationToken cancellationToken)
        {
            var session = _currentSession;
            if (session == null)
            {
                return;
            }

            if (session.Messages == null || session.Messages.Count == 0)
            {
                await EnsureSessionMessagesLoadedAsync(session, cancellationToken).ConfigureAwait(false);
            }

            if (InvokeRequired)
            {
                Invoke(new Action(PushCurrentSessionToWeb));
            }
            else
            {
                PushCurrentSessionToWeb();
            }
        }

        private async void txtChat_KeyDown(object sender, KeyEventArgs e)
        {
            if (TryHandleChatPasteShortcut(e))
            {
                return;
            }

            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await SendCurrentPromptAsync(false);
            }
        }

        private void ApplyInteractiveMode(InteractiveMode mode)
        {
            _interactiveMode = mode;

            btnAgentType.ButtonStyle = BarButtonStyle.Check;
            btnAsk.ButtonStyle = BarButtonStyle.Check;

            btnAgentType.Down = mode == InteractiveMode.Agent;
            btnAsk.Down = mode == InteractiveMode.Ask;

            menuInteractiveMode.Caption = LocalizationService.Get(mode == InteractiveMode.Agent ? "Web.panel.agent" : "Web.panel.ask");
        }

        private async Task EnsureWebViewReadyAsync()
        {
            if (IsInDesignMode())
            {
                return;
            }

            await _webBridge.EnsureReadyAsync();
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var payload = JObject.Parse(e.WebMessageAsJson ?? "{}");
                var messageType = payload.Value<string>("type") ?? string.Empty;

                switch (messageType)
                {
                    case "ready":
                        _webBridge.IsClientReady = true;
                        PostLocaleToWeb();
                        PushHostCapabilitiesToWeb();
                        PushAssistantAvatarToWeb();
                        PushCurrentSessionToWeb();
                        break;

                    case "clearExecutionModePreference":
                        await ClearExecutionModePreferenceAsync();
                        break;

                    case "requestRegenerateLastReply":
                        await SendCurrentPromptAsync(true);
                        break;

                    case "requestRetryMessage":
                        await RetryMessageAsync(payload.Value<string>("messageId"));
                        break;

                    case "executeCodeBlock":
                        await HandleExecuteCodeBlockAsync(payload);
                        break;

                    case "openCodeInVbaEditor":
                        await HandleOpenCodeInVbaEditorAsync(payload);
                        break;

                    case "openBackupArtifact":
                        OpenBackupArtifacts(payload);
                        break;

                    case "restoreBackupArtifact":
                        RestoreBackupArtifact(payload);
                        break;

                    case "submitToolConfirmation":
                        HandleToolConfirmationResponse(payload);
                        break;

                    case "submitUserInteraction":
                        {
                            var reqId = payload.Value<string>("requestId") ?? string.Empty;
                            PanelTrace.Write(
                                $"[CopilotPanelControl] submitUserInteraction received from JS: requestId='{reqId}', cancelled={payload.Value<bool>("cancelled")}");

                            // Phase 7C: apply allow-session / allow-always BEFORE completing TCS
                            // (HandleSubmitUserInteraction cleans up _pendingInteractionToolNames)
                            ApplyInteractionApproval(reqId, payload.Value<string>("answers") ?? "{}");

                            HandleSubmitUserInteraction(
                                reqId,
                                payload.Value<string>("answers") ?? "{}",
                                payload.Value<string>("annotations") ?? "{}",
                                payload.Value<bool>("cancelled"));
                        }
                        break;

                    case "traceLog":
                        PanelTrace.Write(
                            $"[CopilotPanelControl] JS received: type='{payload.Value<string>("msgType")}', requestId='{payload.Value<string>("requestId")}'");
                        break;
                }

            }
            catch
            {
                // 忽略前端非关键消息
            }
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(ApplyPanelLocalization));
                }

                return;
            }

            ApplyPanelLocalization();
        }

        private void CopilotPanelControl_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void ApplyPanelLocalization()
        {
            var get = new Func<string, string>(LocalizationService.Get);
            topBar.Text = get("Web.panel.toolbar");
            listHistoryChat.Caption = get("Web.panel.history");
            btnNewChat.Caption = get("Web.panel.newSession");
            btnDelChat.Caption = get("Web.panel.deleteSession");
            btnSelectFiles.Caption = get("Web.panel.files");
            btnAgentType.Caption = get("Web.panel.agent");
            btnAsk.Caption = get("Web.panel.ask");
            menuMruRefs.Caption = get("Web.panel.recent");
            menuRefLinks.Caption = get("Web.panel.references");
            beiInputWebUrl.Caption = get("Web.panel.url");
            btnSend.Caption = get(_isSending ? "Web.panel.stop" : "Web.panel.send");
            btnSend.Hint = get(_isSending ? "Web.panel.stop" : "Web.panel.send");
            menuInteractiveMode.Caption = get(_interactiveMode == InteractiveMode.Agent ? "Web.panel.agent" : "Web.panel.ask");
            if (!_modelMenu.HasModels)
            {
                menuModels.Caption = get("Web.panel.noModel");
            }

            _modelMenu.RefreshLocalizedLabels();
            if (_webViewPlaceholderPanel != null && _webViewPlaceholderPanel.Visible)
            {
                if (!string.IsNullOrEmpty(_activePlaceholderTitleKey))
                {
                    _webViewPlaceholderTitleLabel.Text = get(_activePlaceholderTitleKey);
                }

                if (!string.IsNullOrEmpty(_activePlaceholderMessageKey))
                {
                    _webViewPlaceholderMessageLabel.Text = get(_activePlaceholderMessageKey);
                }
                else if (_lastPlaceholderException != null
                    && _activePlaceholderTitleKey == "Web.panel.loadFailed")
                {
                    _webViewPlaceholderMessageLabel.Text = BuildInitializationErrorMessage(_lastPlaceholderException);
                }
            }
            if (_lastContextPercent.HasValue)
            {
                UpdateContextUsageTooltip(_lastContextPercent, _lastContextTokens, _lastContextWindow);
            }
            foreach (var group in _recentReferenceGroups)
            {
                group.Caption = BuildRecentReferenceGroupCaption(group.References);
            }

            RefreshRecentReferenceMenu();
            RefreshSelectedReferenceDisplay();
            CompleteAttachmentPreparationProgress(_attachmentPreparationReferences, _attachmentPreparationPlan);
            PostLocaleToWeb();
        }

        private void PostLocaleToWeb()
        {
            if (!_webBridge.CanPost)
            {
                return;
            }

            PostToWeb(new
            {
                type = "locale",
                locale = LocalizationService.Preference,
                messages = LocalizationService.GetAll("Web.")
            });
        }

        private async Task RetryMessageAsync(string messageId)
        {
            if (_isSending || string.IsNullOrWhiteSpace(messageId))
            {
                return;
            }

            EnsureCurrentSession();
            if (_currentSession == null || _currentSession.Messages == null)
            {
                return;
            }

            var userMessage = _currentSession.Messages
                .LastOrDefault(m =>
                    m != null
                    && m.Role == "user"
                    && string.Equals(m.Id, messageId, StringComparison.OrdinalIgnoreCase));

            if (userMessage == null || string.IsNullOrWhiteSpace(userMessage.Content))
            {
                return;
            }

            txtChat.Text = userMessage.Content;
            txtChat.SelectionStart = txtChat.Text.Length;
            await SendCurrentPromptAsync(false);
        }

        private async Task ClearExecutionModePreferenceAsync()
        {
            if (_isSending)
            {
                PostToWeb(new
                {
                    type = "executionModePreferenceCleared",
                    success = false,
                    wasCleared = false,
                    message = LocalizationService.Get("Panel.Status.ClearExecutionModePreferenceBusy")
                });
                return;
            }

            EnsureCurrentSession();
            if (_currentSession == null)
            {
                PostToWeb(new
                {
                    type = "executionModePreferenceCleared",
                    success = false,
                    wasCleared = false,
                    message = LocalizationService.Get("Panel.Status.NoSessionForClearExecutionModePreference")
                });
                return;
            }

            var conversationId = _currentSession.Id.ToString("D");
            try
            {
                var result = await BrokerClient.ClearExecutionModePreferenceAsync(
                    new AgentBrokerClearExecutionModePreferenceRequest
                    {
                        ConversationId = conversationId
                    },
                    CancellationToken.None);

                PostToWeb(new
                {
                    type = "executionModePreferenceCleared",
                    success = result != null && result.Success,
                    wasCleared = result != null && result.WasCleared,
                    message = result == null
                        ? LocalizationService.Get("Panel.Error.BrokerNoResult")
                        : (result.ErrorMessage ?? string.Empty)
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "清除会话执行方式默认值失败。ConversationId={ConversationId}", conversationId);
                PostToWeb(new
                {
                    type = "executionModePreferenceCleared",
                    success = false,
                    wasCleared = false,
                    message = LocalizationService.Format("Panel.Status.ClearFailed", ex.Message)
                });
            }
        }

        private async Task SendCurrentPromptAsync(bool regenerateLastAnswer)
        {
            if (_isSending)
            {
                return;
            }

            _logger?.LogInformation(
                "开始发送消息。Mode={Mode} Regenerate={Regenerate} SessionId={SessionId}",
                SelectedInteractiveMode,
                regenerateLastAnswer,
                _currentSession == null ? string.Empty : _currentSession.Id.ToString("D"));

            var model = GetSelectedModel();
            if (model == null)
            {
                MessageBox.Show(
                    this,
                    LocalizationService.Get("Web.panel.modelRequired"),
                    LocalizationService.Get("Web.panel.sendFailed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            EnsureCurrentSession();

            string userText;
            ChatMessageState userMessage;

            if (regenerateLastAnswer)
            {
                userMessage = GetLastUserMessage();
                if (userMessage == null)
                {
                    return;
                }

                RemoveTrailingAssistantMessage();
                userText = userMessage.Content ?? string.Empty;
                ReplaceConversationInWeb(_currentSession);
            }
            else
            {
                userText = (txtChat.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(userText))
                {
                    return;
                }

                txtChat.Text = string.Empty;
                userMessage = new ChatMessageState
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Role = "user",
                    Content = userText,
                    CreatedAt = DateTime.Now
                };

                _currentSession.Messages.Add(userMessage);
                _currentSession.UpdatedAt = DateTime.Now;
                // 用户新问题发送后立即恢复追底：先恢复自动跟随，再由 appendMessage 滚动到底部。
                PostResumeAutoFollow();
                PostAppendMessage(userMessage);
            }

            var referencesUsedForTurn = SnapshotSelectedReferences();
            CompleteReferenceSelectionForSend(referencesUsedForTurn);
            var conversationId = _currentSession == null ? string.Empty : _currentSession.Id.ToString("D");

            var assistantMessage = new ChatMessageState
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = "assistant",
                Content = string.Empty,
                CreatedAt = DateTime.Now,
                InfoItems = new List<string>(),
                ToolCalls = new List<ChatToolCallState>()
            };

            _currentSession.Messages.Add(assistantMessage);
            _currentSession.UpdatedAt = DateTime.Now;

            var busyStateApplied = false;
            var assistantStream = new AssistantResponseStreamAccumulator(this, assistantMessage.Id);
            ReferenceSendPlan referenceSendPlan = null;
            _cancelRequestedByUser = false;

            try
            {
                SetBusyState(true);
                busyStateApplied = true;

                RefreshHistoryMenuSelection();
                PostBeginAssistantMessage(assistantMessage.Id, null);

                _sendCts?.Cancel();
                _sendCts?.Dispose();
                _sendCts = new CancellationTokenSource();

                if (referencesUsedForTurn.Count > 0)
                {
                    _logger?.LogInformation("Starting attachment preparation on an STA worker. ReferenceCount={ReferenceCount}", referencesUsedForTurn.Count);
                    var preparationToken = _sendCts.Token;
                    referenceSendPlan = await BuildReferenceSendPlanAsync(
                        model,
                        referencesUsedForTurn,
                        conversationId,
                        preparationToken,
                        progress =>
                        {
                            if (!preparationToken.IsCancellationRequested)
                            {
                                UpdateAttachmentPreparationProgress(progress);
                            }
                        });
                    ApplyPreparedAttachmentSummaries(referencesUsedForTurn, referenceSendPlan);
                    CompleteAttachmentPreparationProgress(referencesUsedForTurn, referenceSendPlan);
                }

                // Phase 1.3b 之后 broker 是唯一执行路径，Chat / Agent 两种 InteractiveMode 均走这里。
                await RunBrokerTurnAsync(
                    model,
                    userText,
                    referenceSendPlan,
                    assistantStream,
                    _sendCts.Token);

                assistantStream.Complete();
                assistantMessage.Content = assistantStream.GetVisibleContent();

                PostCompleteAssistantMessage(assistantMessage.Id, assistantMessage.Content);
                _currentSession.UpdatedAt = DateTime.Now;

                if (busyStateApplied)
                {
                    SetBusyState(false);
                    busyStateApplied = false;
                }

                if (_currentSession.Title == ChatSessionFactory.DefaultNewSessionTitle)
                {
                    await GenerateTitleIfNeededAsync(model, _currentSession, _sendCts.Token);
                    // 1.4c：LLM 生成的标题立即推到 broker，写入 JSONL head metadata，
                    // 否则重启后 ListConversations 只能用 firstUserContent 兜底，标题会跳。
                    await PersistSessionTitleToBrokerAsync(_currentSession, _sendCts.Token).ConfigureAwait(true);
                }

                // 上下文压缩由 Broker 侧 ContextCompactionEngine / AutoCompactPolicy 统一负责，
                // Panel 不再重复压缩（原 CompressSessionIfNeededAsync 的结果会被下面的
                // RefreshCurrentSessionMessagesFromBroker 覆盖，等于白白多花一次模型调用）。

                // 从 Broker 重新加载消息以获取正确的 ContentParts（文本与工具调用的交错顺序），
                // 否则切换会话再切回来时，JS 回退路径会把所有工具调用堆在应答卡片底部。
                await RefreshCurrentSessionMessagesFromBrokerAsync(CancellationToken.None);

                RefreshHistoryMenuSelection();
            }
            catch (OperationCanceledException)
            {
                _logger?.LogInformation("消息生成已取消。");
                FinalizeAssistantMessageOnCancellation(assistantMessage, assistantStream);
            }
            catch (Exception ex)
            {
                if (ShouldTreatAsCanceled(ex))
                {
                    _logger?.LogInformation("消息生成已取消。Reason={Reason}", ex.Message ?? string.Empty);
                    FinalizeAssistantMessageOnCancellation(assistantMessage, assistantStream);
                }
                else
                {
                    _logger?.LogError(ex, "发送消息失败。");
                    FinalizeAssistantMessageOnFailure(assistantMessage, assistantStream, ex);
                }
            }
            finally
            {
                ClearAttachmentPreparationProgress();
                ClearActiveBrokerTurn();
                _cancelRequestedByUser = false;

                if (busyStateApplied)
                {
                    SetBusyState(false);
                }
            }
        }

        private async Task GenerateTitleIfNeededAsync(
            ModelProviderSettings model,
            ChatSessionState session,
            CancellationToken cancellationToken)
        {
            if (session == null || session.Title != ChatSessionFactory.DefaultNewSessionTitle)
            {
                return;
            }

            var transcript = BuildTitleTranscript(session);

            if (string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            try
            {
                var messages = new List<AgentBrokerCompletionMessage>
                {
                    new AgentBrokerCompletionMessage
                    {
                        Role = "system",
                        Content = "请根据对话生成一个简短中文标题，不超过12个字，只返回标题本身，不要标点，不要解释。"
                    },
                    new AgentBrokerCompletionMessage
                    {
                        Role = "user",
                        Content = transcript
                    }
                };

                var titleResult = await BrokerClient.CompleteAsync(
                    new AgentBrokerCompletionRequest
                    {
                        ModelOptions = CreateBrokerModelOptions(model),
                        Messages = messages
                    },
                    cancellationToken).ConfigureAwait(true);
                var title = (titleResult?.Success == true ? titleResult.Content : string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = BuildFallbackTitle(session);
                }

                session.Title = NormalizeTitle(title);
            }
            catch
            {
                session.Title = BuildFallbackTitle(session);
            }
        }

        private string BuildTitleTranscript(ChatSessionState session)
        {
            if (session == null || session.Messages == null || session.Messages.Count == 0)
            {
                return string.Empty;
            }

            var lines = new List<string>();

            foreach (var message in session.Messages)
            {
                if (message == null)
                {
                    continue;
                }

                if (!string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var line = BuildTitleTranscriptLine(message);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                lines.Add(line);
                if (lines.Count >= 4)
                {
                    break;
                }
            }

            return string.Join("\n", lines);
        }

        private string BuildTitleTranscriptLine(ChatMessageState message)
        {
            if (message == null)
            {
                return string.Empty;
            }

            var prefix = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? "用户："
                : "助手：";

            var content = NormalizeTitleSourceText(message.Content);
            if (!string.IsNullOrWhiteSpace(content))
            {
                return prefix + content;
            }

            if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                || message.ToolCalls == null
                || message.ToolCalls.Count == 0)
            {
                return string.Empty;
            }

            var toolNames = message.ToolCalls
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.ToolName))
                .Select(t => t.ToolName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (toolNames.Count == 0)
            {
                return "助手：已调用工具";
            }

            return "助手：已调用工具 " + string.Join("、", toolNames);
        }

        private string BuildSystemPrompt(ModelProviderSettings model)
        {
            var mode = _interactiveMode == InteractiveMode.Agent ? HostPromptMode.Agent : HostPromptMode.Ask;
            var hostPrompt = HostSystemPromptStore.GetEffectivePrompt(
                HostAdapter == null ? null : HostAdapter.HostApplicationName,
                mode);

            // 模型级提示词是补充，不覆盖宿主提示词。
            var modelPrompt = model == null ? null : model.SystemPrompt;
            if (string.IsNullOrWhiteSpace(modelPrompt))
            {
                return hostPrompt;
            }

            return string.IsNullOrWhiteSpace(hostPrompt)
                ? modelPrompt.Trim()
                : hostPrompt + Environment.NewLine + Environment.NewLine + modelPrompt.Trim();
        }

        private Task<ReferenceSendPlan> BuildReferenceSendPlanAsync(
            ModelProviderSettings model,
            IList<ReferenceItem> selectedReferences,
            string conversationId,
            CancellationToken cancellationToken,
            Action<ReferencePreparationProgress> progress)
        {
            return RunOnStaThreadAsync(
                () => BuildReferenceSendPlan(model, selectedReferences, conversationId, cancellationToken, progress),
                cancellationToken);
        }

        private ReferenceSendPlan BuildReferenceSendPlan(
            ModelProviderSettings model,
            IList<ReferenceItem> selectedReferences,
            string conversationId,
            CancellationToken cancellationToken,
            Action<ReferencePreparationProgress> progress)
        {
            if (model == null || selectedReferences == null || selectedReferences.Count == 0)
            {
                return null;
            }

            // 检查 PDF 附件页数是否超过限制
            var chatDefaults = _settings == null ? null : _settings.ChatDefaults;
            var capabilities = model.GetInputCapabilitySnapshot();
            var attachmentCountThreshold = chatDefaults == null
                ? 5
                : Math.Max(1, chatDefaults.AttachmentFileCountThreshold);
            var selectedReferenceCount = selectedReferences.Count(reference => reference != null);
            var availableVisualAttachmentSlots = Math.Max(0, attachmentCountThreshold - selectedReferenceCount);
            var maxVisualPages = chatDefaults == null
                ? 5
                : Math.Max(0, chatDefaults.MaxVisualPages);
            maxVisualPages = Math.Min(maxVisualPages, availableVisualAttachmentSlots);
            var configuredInlineBudget = chatDefaults == null
                ? 12000
                : Math.Max(1000, chatDefaults.AttachmentInlineCharBudget);
            capabilities.MaxInlineReferenceChars = Math.Min(
                capabilities.MaxInlineReferenceChars,
                configuredInlineBudget);
            var supportsVisualFallback = capabilities.SupportsImageInput
                && capabilities.SupportsMixedContentParts
                && capabilities.SupportsNativeInputForExtension(".png");

            var preparedReferences = _referencePreparationCoordinator.Prepare(
                selectedReferences,
                supportsVisualFallback,
                supportsVisualFallback ? maxVisualPages : 0,
                capabilities.MaxInlineReferenceChars,
                _logger,
                progress,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (preparedReferences.Count == 0)
            {
                return null;
            }

            var plan = _referenceSendPlanner.CreatePlan(
                capabilities,
                preparedReferences,
                true,
                _settings == null ? null : _settings.ChatDefaults);

            _referencePromptComposer.Compose(plan);

            cancellationToken.ThrowIfCancellationRequested();
            _referenceArtifactStore.Save(
                conversationId,
                plan.ToolFetchReferences);

            return plan;
        }

        private static Task<T> RunOnStaThreadAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration cancellationRegistration = default(CancellationTokenRegistration);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(() => completion.TrySetCanceled());
                completion.Task.ContinueWith(
                    unused => cancellationRegistration.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            var worker = new Thread(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = operation();
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    completion.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            })
            {
                IsBackground = true,
                Name = "OfficeCopilot attachment preparation"
            };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return completion.Task;
        }

        private IList<string> BuildBrokerTransientSystemMessages(ReferenceSendPlan referenceSendPlan)
        {
            var messages = new List<string>();

            if (referenceSendPlan != null)
            {
                if (!string.IsNullOrWhiteSpace(referenceSendPlan.ManifestText))
                {
                    messages.Add(referenceSendPlan.ManifestText);
                }

                if (!string.IsNullOrWhiteSpace(referenceSendPlan.InlineContextText))
                {
                    messages.Add(referenceSendPlan.InlineContextText);
                }
            }

            return messages;
        }

        private HostContextSnapshot TryGetHostContextSnapshot()
        {
            try
            {
                return HostAdapter == null ? null : HostAdapter.GetContextSnapshot();
            }
            catch
            {
                return null;
            }
        }

        private ModelProviderSettings GetSelectedModel()
        {
            if (!_modelMenu.HasModels)
            {
                return null;
            }

            EnsureCurrentSession();

            return _modelMenu.ResolveEffectiveModel(
                _currentSession == null ? null : _currentSession.ModelProviderId);
        }

        private void LoadSessions()
        {
            // 1.4c：先创建一个空 in-memory 会话作为占位。
            // 历史拉取由 BeginLoadSessionsFromBroker() 在 BrokerClient 注入完成后触发；
            // Repository 会合并 Broker 历史与旧版本地 sessions.json
            // （ctor 调用此方法时 BrokerClient 还是 NullAgentBrokerClient 占位）。
            _sessions = new List<ChatSessionState>();
            _currentSession = ChatSessionFactory.CreateNewSession();
            _currentSession.ModelProviderId = _modelMenu.SelectedModelProviderId;
            _sessions.Add(_currentSession);

            RefreshHistoryMenuSelection();
        }

        /// <summary>
        /// 1.4c 入口：在 host 完成 <see cref="BrokerClient"/> 注入后由 task pane manager 调用，
        /// 异步从 broker 拉取持久化的历史会话列表。可多次调用（每次刷新）。
        /// </summary>
        public void BeginLoadSessionsFromBroker()
        {
            var hostName = HostAdapter == null ? string.Empty : HostAdapter.HostApplicationName ?? string.Empty;

            // 整条链路放到线程池：LoadRecentAsync 在首个 await 之前会同步读旧版会话文件，
            // 并经 BrokerRuntimeHost 同步拉起 Broker 进程、轮询 Ping（最长数秒），
            // 若留在 UI 线程上会让 Office 假死。
            _ = Task.Run(() => LoadSessionsFromBrokerAsync(hostName, CancellationToken.None));
        }

        private async Task LoadSessionsFromBrokerAsync(string hostName, CancellationToken cancellationToken)
        {
            try
            {
                var loaded = await _sessionRepository.LoadRecentAsync(hostName, 200, cancellationToken).ConfigureAwait(false);
                StartupTimingLog.Mark("history-loaded");
                if (loaded == null)
                {
                    return; // 没有历史会话或调用失败，保留空会话占位。
                }

                RunOnUiWhenReady(() => ApplyLoadedSessions(loaded.MostRecent, loaded.Others));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "后台加载历史会话失败。");
            }
        }

        private void ApplyLoadedSessions(ChatSessionState mostRecent, List<ChatSessionState> others)
        {
            StartupTimingLog.Mark("history-apply-begin");

            // 行为变更：面板首次打开时不再自动恢复上一次会话，而是展示空白新会话（提高加载速度）。
            // 历史会话仍保留在历史下拉列表中，供用户主动切换；其消息在切换时按需从 Broker 加载。
            var restored = new List<ChatSessionState>();

            if (mostRecent != null)
            {
                restored.Add(mostRecent);
            }

            if (others != null)
            {
                restored.AddRange(others.Where(s => s != null && !restored.Any(r => r.Id == s.Id)));
            }

            var blankSession = ChatSessionFactory.CreateNewSession();
            blankSession.ModelProviderId = _modelMenu.SelectedModelProviderId;

            _sessions = new List<ChatSessionState> { blankSession };
            _sessions.AddRange(restored);

            _currentSession = blankSession;

            RefreshHistoryMenuSelection();
            _modelMenu.Refresh();
            PushCurrentSessionToWeb();
            StartupTimingLog.Mark("history-apply-end");
        }

        private Task EnsureSessionMessagesLoadedAsync(ChatSessionState session, CancellationToken cancellationToken)
        {
            return _sessionRepository.EnsureMessagesLoadedAsync(session, cancellationToken);
        }

        /// <summary>
        /// 从 Broker 强制重新加载当前会话消息，用于在 broker turn 完成后刷新
        /// <see cref="ChatMessageState.ContentParts"/>（文本与工具调用的交错顺序）。
        /// 解决切换会话再切回时，因 ContentParts 为 null 导致 JS 回退路径把所有工具调用堆在底部的渲染问题。
        /// </summary>
        private Task RefreshCurrentSessionMessagesFromBrokerAsync(CancellationToken cancellationToken)
        {
            return _sessionRepository.RefreshMessagesAsync(_currentSession, cancellationToken);
        }

        private void CreateNewSession(bool pushToWeb)
        {
            var session = ChatSessionFactory.CreateNewSession();
            session.ModelProviderId = _modelMenu.SelectedModelProviderId;

            _sessions.Add(session);
            _currentSession = session;

            // 1.4c：不再写本地 sessions.json；broker 会在首条 SendTurnAsync 时自动落盘。
            RefreshHistoryMenuSelection();
            _modelMenu.Refresh();

            if (pushToWeb)
            {
                PushCurrentSessionToWeb();
            }
        }

        private void DeleteCurrentSession()
        {
            EnsureCurrentSession();

            var deletedId = _currentSession == null ? Guid.Empty : _currentSession.Id;

            if (_sessions.Count == 1)
            {
                _sessions.Clear();
                _currentSession = ChatSessionFactory.CreateNewSession();
                _currentSession.ModelProviderId = _modelMenu.SelectedModelProviderId;
                _sessions.Add(_currentSession);
            }
            else
            {
                _sessions.Remove(_currentSession);
                _currentSession = _sessions
                    .OrderByDescending(s => s.UpdatedAt)
                    .FirstOrDefault();
            }

            // 1.4c：broker 侧异步删除 JSONL（fire-and-forget；失败仅记录日志）。
            if (deletedId != Guid.Empty)
            {
                _ = _sessionRepository.DeleteAsync(deletedId, CancellationToken.None);
            }

            RefreshHistoryMenuSelection();
            _modelMenu.Refresh();
            // 使用 LoadAndShowCurrentSessionAsync 而非 PushCurrentSessionToWeb，
            // 确保自动选中的前一个会话的消息已从 Broker 加载后再渲染到面板。
            _ = LoadAndShowCurrentSessionAsync(CancellationToken.None);
        }

        private void EnsureCurrentSession()
        {
            if (_currentSession != null)
            {
                if (_currentSession.Messages == null)
                {
                    _currentSession.Messages = new List<ChatMessageState>();
                }
                return;
            }

            _currentSession = _sessions.FirstOrDefault() ?? ChatSessionFactory.CreateNewSession();
            if (!_sessions.Contains(_currentSession))
            {
                _sessions.Add(_currentSession);
            }
        }

        private void RefreshHistoryMenuSelection()
        {
            _historySelectionUpdating = true;
            try
            {
                riicbHistoryChat.Items.Clear();

                foreach (var session in _sessions.OrderByDescending(s => s.UpdatedAt))
                {
                    var caption = BuildHistoryCaption(session);
                    riicbHistoryChat.Items.Add(new ImageComboBoxItem(caption, session.Id.ToString("D"), 0));
                }

                listHistoryChat.EditValue = _currentSession == null ? null : _currentSession.Id.ToString("D");
            }
            finally
            {
                _historySelectionUpdating = false;
            }
        }

        private string BuildHistoryCaption(ChatSessionState session)
        {
            var title = string.IsNullOrWhiteSpace(session.Title) ? ChatSessionFactory.DefaultNewSessionTitle : session.Title.Trim();
            const int maxTitleChars = 20;
            if (title.Length > maxTitleChars)
            {
                title = title.Substring(0, maxTitleChars) + "...";
            }
            var time = session.UpdatedAt.ToString("MM-dd HH:mm");
            return title + "  [" + time + "]";
        }

        private void PushCurrentSessionToWeb()
        {
            if (!_webBridge.IsClientReady)
            {
                return;
            }

            EnsureCurrentSession();
            ReplaceConversationInWeb(_currentSession);
        }

        private void ReplaceConversationInWeb(ChatSessionState session)
        {
            if (session == null)
            {
                return;
            }

            PostToWeb(new
            {
                type = "hydrateConversation",
                title = session.Title,
                messages = session.Messages.Select(m => new
                {
                    id = m.Id,
                    role = m.Role,
                    content = m.Content,
                    infoItems = (m.InfoItems ?? new List<string>()).ToList(),
                    codeExecutions = (m.CodeExecutions ?? new List<ChatCodeExecutionState>()).Select(c => new
                    {
                        codeBlockIndex = c.CodeBlockIndex,
                        language = c.Language,
                        backupPath = c.BackupPath
                    }).ToList(),
                    toolCalls = (m.ToolCalls ?? new List<ChatToolCallState>()).Select(t => new
                    {
                        toolCallId = t.ToolCallId,
                        toolName = string.IsNullOrWhiteSpace(t.DisplayName)
                            ? UserTextLocalizer.GetToolDisplayName(t.ToolName, t.ToolName, LocalizationService.Culture)
                            : t.DisplayName,
                        success = t.Success,
                        status = t.Status,
                        content = t.Content,
                        errorCode = t.ErrorCode,
                        errorMessage = t.ErrorMessage,
                        requiresBackup = t.RequiresBackup,
                        backupPath = t.BackupPath
                    }).ToList(),
                    contentParts = (m.ContentParts ?? new List<ChatContentPart>()).Select(p => new
                    {
                        type = p.Type,
                        text = p.Text,
                        toolCallId = p.ToolCallId
                    }).ToList()
                }).ToList()
            });
        }

        private void PostAppendMessage(ChatMessageState message)
        {
            if (message == null)
            {
                return;
            }

            PostToWeb(new
            {
                type = "appendMessage",
                message = new
                {
                    id = message.Id,
                    role = message.Role,
                    content = message.Content
                }
            });
        }

        private void PostResumeAutoFollow()
        {
            PostToWeb(new
            {
                type = "resumeAutoFollow"
            });
        }

        private void PostBeginAssistantMessage(string messageId, IEnumerable<string> infoItems = null)
        {
            var normalizedItems = NormalizeAssistantInfoItems(infoItems);

            PostToWeb(new
            {
                type = "beginAssistantMessage",
                messageId = messageId,
                infoItems = normalizedItems
            });
        }

        private void PostAssistantChunk(string messageId, string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            PostToWeb(new
            {
                type = "appendAssistantChunk",
                messageId = messageId,
                chunk = chunk
            });
        }

        private void PostCompleteAssistantMessage(string messageId, string content)
        {
            PostToWeb(new
            {
                type = "completeAssistantMessage",
                messageId = messageId,
                content = content
            });
        }

        private void PostAssistantInfo(string messageId, IEnumerable<string> items)
        {
            var normalizedItems = NormalizeAssistantInfoItems(items);
            if (string.IsNullOrWhiteSpace(messageId) || normalizedItems.Count == 0)
            {
                return;
            }

            PostToWeb(new
            {
                type = "appendAssistantInfo",
                messageId = messageId,
                items = normalizedItems
            });
        }

        private void PostStatusMessage(string text, string messageId = null)
        {
            PostToWeb(new
            {
                type = "status",
                text = text,
                messageId = messageId ?? string.Empty
            });
        }

        /// <summary>
        /// Phase 2/7C: 发送 Todo 更新到前端 WebView（内联渲染）。
        /// </summary>
        private void PostTodoUpdate(string todosJson)
        {
            PostToWeb(new
            {
                type = "todoUpdate",
                todos = todosJson
            });
        }

        /// <summary>
        /// Phase 7C: 处理来自 JS 的用户交互提交。
        /// 由 CoreWebView2_WebMessageReceived 调用，完成对应的 TCS。
        /// </summary>
        private void HandleSubmitUserInteraction(string requestId, string answersJson, string annotationsJson, bool cancelled)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            var response = new AgentBrokerToolUserInteractionResponse
            {
                ToolCallId = requestId,
                AnswersJson = answersJson ?? "{}",
                AnnotationsJson = annotationsJson ?? "{}",
                Cancelled = cancelled
            };

            if (cancelled)
            {
                response.ErrorCode = "user.cancelled";
                response.ErrorMessage = "User cancelled the interaction.";
            }

            if (!_interactions.TryComplete(requestId, response))
            {
                return;
            }

            PanelTrace.Write(
                $"[CopilotPanelControl] User interaction completed: requestId={requestId}, cancelled={cancelled}");
        }

        /// <summary>
        /// Phase 7C: 从 submitUserInteraction 的 answers JSON 中提取 decision，
        /// 如果是 allow-session / allow-always，调用对应的授权方法。
        /// </summary>
        private void ApplyInteractionApproval(string requestId, string answersJson)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            string decision;
            try
            {
                var answers = Newtonsoft.Json.Linq.JObject.Parse(answersJson ?? "{}");
                decision = answers["decision"]?.ToString() ?? string.Empty;
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(decision))
                return;

            string toolName;
            if (!_interactions.TryGetToolName(requestId, out toolName))
                return;

            if (string.IsNullOrWhiteSpace(toolName))
                return;

            if (string.Equals(decision, "allow-always", StringComparison.OrdinalIgnoreCase))
            {
                ApproveToolPermanently(toolName);
                ApproveToolForCurrentSession(toolName);
                PanelTrace.Write($"[CopilotPanelControl] Tool '{toolName}' permanently approved via interaction");
            }
            else if (string.Equals(decision, "allow-session", StringComparison.OrdinalIgnoreCase))
            {
                ApproveToolForCurrentSession(toolName);
                PanelTrace.Write($"[CopilotPanelControl] Tool '{toolName}' session-approved via interaction");
            }
        }

        private void PushAssistantInfoItems(string messageId, IEnumerable<string> items)
        {
            var normalizedItems = NormalizeAssistantInfoItems(items);
            if (normalizedItems.Count == 0)
            {
                return;
            }

            AttachAssistantInfoItems(messageId, normalizedItems);
            PostAssistantInfo(messageId, normalizedItems);
        }

        private void AttachAssistantInfoItems(string messageId, IEnumerable<string> items)
        {
            if (string.IsNullOrWhiteSpace(messageId) || items == null)
            {
                return;
            }

            EnsureCurrentSession();

            var assistantMessage = _currentSession.Messages
                .LastOrDefault(m => string.Equals(m.Id, messageId, StringComparison.OrdinalIgnoreCase));

            if (assistantMessage == null)
            {
                return;
            }

            if (assistantMessage.InfoItems == null)
            {
                assistantMessage.InfoItems = new List<string>();
            }

            var changed = false;

            foreach (var item in NormalizeAssistantInfoItems(items))
            {
                if (assistantMessage.InfoItems.Any(existing => string.Equals(existing, item, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                assistantMessage.InfoItems.Add(item);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            _currentSession.UpdatedAt = DateTime.Now;
        }

        private List<string> NormalizeAssistantInfoItems(IEnumerable<string> items)
        {
            var results = new List<string>();

            if (items == null)
            {
                return results;
            }

            foreach (var item in items)
            {
                var normalized = NormalizeAssistantInfoItem(item);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (results.Any(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                results.Add(normalized);
            }

            return results;
        }

        private string NormalizeAssistantInfoItem(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
        }

        private List<string> BuildAssistantInfoItems()
        {
            var items = new List<string>();

            items.AddRange(BuildAssistantContextInfoItems(TryGetHostContextSnapshot()));
            items.AddRange(BuildAssistantToolInfoItems());

            return NormalizeAssistantInfoItems(items);
        }

        private List<string> BuildAssistantContextInfoItems(HostContextSnapshot snapshot)
        {
            var items = new List<string>();

            if (snapshot == null)
            {
                items.Add(LocalizationService.Get("Web.info.noHostContext"));
                return items;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.DocumentName))
            {
                items.Add(LocalizationService.Format("Web.info.activeDocument", snapshot.DocumentName.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(snapshot.WorksheetName))
            {
                items.Add(LocalizationService.Format("Web.info.worksheet", snapshot.WorksheetName.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(snapshot.SelectionAddress))
            {
                items.Add(LocalizationService.Format("Web.info.selection", snapshot.SelectionAddress.Trim()));
            }

            if (items.Count > 0)
            {
                return items;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Summary))
            {
                items.AddRange(SplitAssistantInfoText(snapshot.Summary));
                return items;
            }

            items.Add(LocalizationService.Get("Web.info.noHostContext"));
            return items;
        }

        private List<string> BuildAssistantToolInfoItems()
        {
            var items = new List<string>();

            if (ToolRegistry == null)
            {
                return items;
            }

            var definitions = ToolRegistry.GetDefinitions();
            if (definitions == null || definitions.Count == 0)
            {
                return items;
            }

            items.Add(LocalizationService.Format("Web.info.toolsCount", definitions.Count));
            items.Add(LocalizationService.Get("Web.info.toolsHelp"));
            items.Add(LocalizationService.Get("Web.info.callHelp"));
            return items;
        }

        private bool TryHandleAssistantInfoStatus(string assistantMessageId, string text)
        {
            if (string.IsNullOrWhiteSpace(assistantMessageId))
            {
                return false;
            }

            var infoItems = MapAssistantInfoItemsFromStatus(text);
            if (infoItems.Count == 0)
            {
                return false;
            }

            PushAssistantInfoItems(assistantMessageId, infoItems);
            return true;
        }

        private List<string> MapAssistantInfoItemsFromStatus(string text)
        {
            var value = (text ?? string.Empty).Trim();
            var items = new List<string>();

            if (string.IsNullOrWhiteSpace(value))
            {
                return items;
            }

            if (value.StartsWith("已加载宿主上下文", StringComparison.OrdinalIgnoreCase))
            {
                var splitIndex = value.IndexOf('：');
                if (splitIndex < 0)
                {
                    splitIndex = value.IndexOf(':');
                }

                var detail = splitIndex >= 0 && splitIndex < value.Length - 1
                    ? value.Substring(splitIndex + 1).Trim()
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(detail))
                {
                    items.Add(LocalizationService.Get("Web.info.hostContextLoaded"));
                }
                else
                {
                    items.AddRange(SplitAssistantInfoText(detail));
                }

                return items;
            }

            if (value.StartsWith("当前未获取到有效宿主上下文", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(LocalizationService.Get("Web.info.noHostContext"));
                return items;
            }

            if (value.StartsWith("已注册 ", StringComparison.OrdinalIgnoreCase) && value.IndexOf("个工具", StringComparison.OrdinalIgnoreCase) > 0)
            {
                var startIndex = "已注册 ".Length;
                var endIndex = value.IndexOf("个工具", StringComparison.OrdinalIgnoreCase);
                var countText = endIndex > startIndex
                    ? value.Substring(startIndex, endIndex - startIndex).Trim()
                    : string.Empty;

                items.Add(string.IsNullOrWhiteSpace(countText)
                    ? LocalizationService.Get("Web.info.toolsRegistered")
                    : LocalizationService.Format("Web.info.toolsCount", countText));
                items.Add(LocalizationService.Get("Web.info.toolsHelp"));
                items.Add(LocalizationService.Get("Web.info.callHelp"));
                return items;
            }

            return items;
        }

        private List<string> SplitAssistantInfoText(string text)
        {
            var items = new List<string>();
            var normalized = (text ?? string.Empty)
                .Replace("；", "\n")
                .Replace(";", "\n")
                .Replace("。", "\n");

            foreach (var part in normalized.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var item = NormalizeAssistantInfoItem(part);
                if (!string.IsNullOrWhiteSpace(item))
                {
                    items.Add(item);
                }
            }

            return items;
        }

        private void SetBusyState(bool busy)
        {
            _isSending = busy;

            SafeUiInvoke(() =>
            {
                btnSend.Enabled = true;
                txtChat.Enabled = true;
                txtChat.Properties.ReadOnly = busy;

                UpdateSendButtonAppearance(busy);

                if (!busy && !txtChat.IsDisposed && txtChat.CanFocus)
                {
                    txtChat.Focus();
                }
            });

            PostToWeb(new
            {
                type = "setBusy",
                busy = busy
            });
        }

        private void UpdateSendButtonAppearance(bool busy)
        {
            btnSend.Caption = string.Empty;
            btnSend.Hint = LocalizationService.Get(busy ? "Web.panel.stop" : "Web.panel.send");
            btnSend.ImageOptions.SvgImage = busy
                ? Properties.Resources.stop
                : Properties.Resources.actions_send;
            btnSend.ImageOptions.SvgImageSize = new System.Drawing.Size(18, 16);

            // Show context usage in Description (tooltip on hover) — re-apply stored values
            if (!busy)
            {
                ApplyStoredContextTooltip();
            }
        }

        // Stored context usage values for tooltip persistence
        private int? _lastContextPercent;
        private int? _lastContextTokens;
        private int? _lastContextWindow;

        /// <summary>
        /// Update btnSend.Description with context usage percentage for tooltip display.
        /// Stores values so they survive UpdateSendButtonAppearance calls.
        /// </summary>
        private void UpdateContextUsageTooltip(int? percent = null, int? tokens = null, int? windowSize = null)
        {
            _lastContextPercent = percent;
            _lastContextTokens = tokens;
            _lastContextWindow = windowSize;
            ApplyStoredContextTooltip();
        }

        private void ApplyStoredContextTooltip()
        {
            if (_lastContextPercent.HasValue && _lastContextTokens.HasValue && _lastContextWindow.HasValue && _lastContextWindow.Value > 0)
            {
                var pct = Math.Min(100, Math.Max(0, _lastContextPercent.Value));
                var tip = new DevExpress.Utils.SuperToolTip();
                tip.Items.Add(new DevExpress.Utils.ToolTipTitleItem { Text = LocalizationService.Get("Web.panel.send") });
                tip.Items.Add(new DevExpress.Utils.ToolTipItem
                {
                    Text = LocalizationService.Format("Web.context.tokens", pct, _lastContextTokens.Value, _lastContextWindow.Value)
                });
                btnSend.SuperTip = tip;
            }
            else
            {
                btnSend.SuperTip = null;
            }
        }

        private void PostToWeb(object payload)
        {
            _webBridge.Post(payload, SafeUiBeginInvoke);
        }

        private void SafeUiInvoke(Action action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                Invoke(action);
                return;
            }

            action();
        }

        /// <summary>
        /// 后台结果回 UI：句柄尚未创建时挂到 HandleCreated 上延后执行，避免结果被静默丢弃。
        /// </summary>
        private void RunOnUiWhenReady(Action action)
        {
            if (action == null || IsDisposed)
            {
                return;
            }

            if (IsHandleCreated)
            {
                SafeUiBeginInvoke(action);
                return;
            }

            var fired = 0;
            EventHandler handler = null;
            handler = (s, e) =>
            {
                HandleCreated -= handler;
                if (Interlocked.Exchange(ref fired, 1) == 0)
                {
                    SafeUiBeginInvoke(action);
                }
            };
            HandleCreated += handler;

            // 订阅与句柄创建之间可能存在竞态，订阅后再检查一次。
            if (IsHandleCreated)
            {
                HandleCreated -= handler;
                if (Interlocked.Exchange(ref fired, 1) == 0)
                {
                    SafeUiBeginInvoke(action);
                }
            }
        }

        private void SafeUiBeginInvoke(Action action)
        {
            if (action == null || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(action);
                return;
            }

            action();
        }

        private ChatMessageState GetLastUserMessage()
        {
            EnsureCurrentSession();
            return _currentSession.Messages
                .LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        }

        private void RemoveTrailingAssistantMessage()
        {
            EnsureCurrentSession();

            if (_currentSession.Messages.Count == 0)
            {
                return;
            }

            var last = _currentSession.Messages[_currentSession.Messages.Count - 1];
            if (string.Equals(last.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                _currentSession.Messages.RemoveAt(_currentSession.Messages.Count - 1);
            }
        }

        private void RemoveMessageById(string messageId)
        {
            EnsureCurrentSession();
            _currentSession.Messages.RemoveAll(m => string.Equals(m.Id, messageId, StringComparison.OrdinalIgnoreCase));
            _currentSession.UpdatedAt = DateTime.Now;
        }

        private string NormalizeTitle(string title)
        {
            var normalized = (title ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("：", string.Empty)
                .Replace(":", string.Empty)
                .Replace("《", string.Empty)
                .Replace("》", string.Empty)
                .Trim();

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return ChatSessionFactory.DefaultNewSessionTitle;
            }

            if (normalized.Length > 18)
            {
                normalized = normalized.Substring(0, 18).Trim();
            }

            return normalized;
        }

        private string BuildFallbackTitle(ChatSessionState session)
        {
            var firstUser = session.Messages.FirstOrDefault(m => m.Role == "user");
            if (firstUser == null || string.IsNullOrWhiteSpace(firstUser.Content))
            {
                return ChatSessionFactory.DefaultNewSessionTitle;
            }

            var content = firstUser.Content
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            return NormalizeTitle(content.Length <= 18 ? content : content.Substring(0, 18));
        }

        private static string NormalizeTitleSourceText(string text)
        {
            var normalized = StripThinkBlocks(text)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();

            if (string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return normalized;
        }

        private static string SummarizeUserIntent(string userText)
        {
            var value = (userText ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            while (value.Contains("  "))
            {
                value = value.Replace("  ", " ");
            }

            return value.Length <= 120 ? value : value.Substring(0, 120) + "…";
        }

        private async Task RunBrokerTurnAsync(
            ModelProviderSettings model,
            string userText,
            ReferenceSendPlan referenceSendPlan,
            AssistantResponseStreamAccumulator assistantStream,
            CancellationToken cancellationToken)
        {
            if (BrokerClient == null)
            {
                throw new InvalidOperationException(LocalizationService.Get("Panel.Error.BrokerClientNotConfigured"));
            }

            var request = new AgentBrokerTurnRequest
            {
                ConversationId = _currentSession == null ? string.Empty : _currentSession.Id.ToString("D"),
                MessageId = assistantStream == null ? string.Empty : assistantStream.MessageId,
                UserInput = userText ?? string.Empty,
                UserInputContentParts = BuildBrokerUserInputContentParts(userText, referenceSendPlan),
                TransientSystemMessages = BuildBrokerTransientSystemMessages(referenceSendPlan),
                InteractiveMode = SelectedInteractiveMode,
                ModelProviderId = model == null ? (int?)null : model.Id,
                ModelOptions = CreateBrokerModelOptions(model),
                SessionId = _currentSession == null ? string.Empty : _currentSession.Id.ToString("D"),
                UiCulture = SupportedLanguages.NormalizePreference(_settings == null ? null : _settings.UiLanguage)
            };

            LocalizationService.SetLanguage(request.UiCulture);

            _activeBrokerRequestId = Guid.NewGuid().ToString("N");
            _activeBrokerConversationId = request.ConversationId ?? string.Empty;
            _activeBrokerMessageId = request.MessageId ?? string.Empty;
            request.Metadata["brokerRequestId"] = _activeBrokerRequestId;
            request.Metadata[ToolMetadataKeys.SessionId] = request.SessionId ?? string.Empty;
            request.Metadata[ToolMetadataKeys.ConversationId] = request.ConversationId ?? string.Empty;
            request.Metadata[ToolMetadataKeys.SourceMessageId] = request.MessageId ?? string.Empty;
            _lastUserMessageText = userText ?? string.Empty;
            request.Metadata[ToolMetadataKeys.UserIntent] = SummarizeUserIntent(userText);
            // 每轮最大工具调用次数以 settings.json 的 Runtime.MaxToolRoundCount 为准；
            // 仅在 Panel 侧确实读到有效值时才下发，否则交给 Broker 自己的默认值，避免面板硬编码覆盖设置。
            var maxToolRoundCount = _settings == null || _settings.Runtime == null ? 0 : _settings.Runtime.MaxToolRoundCount;
            if (maxToolRoundCount > 0)
            {
                request.Metadata["runtime.maxToolRoundCount"] = maxToolRoundCount.ToString(CultureInfo.InvariantCulture);
            }
            // 1.4c：把宿主应用名 / 标题写进 metadata，broker 会把它们写入 JSONL head metadata，
            // 之后 ListConversationsAsync 才能按宿主过滤、按标题显示。
            request.Metadata["hostApplicationName"] = HostAdapter == null ? string.Empty : (HostAdapter.HostApplicationName ?? string.Empty);
            // 工具执行权限模式按宿主分别保存在 settings.json，本轮取该宿主的最新值下发给 Broker。
            request.Metadata[ToolMetadataKeys.ToolTrustMode] = ResolveToolTrustMode();
            if (_currentSession != null && !string.IsNullOrWhiteSpace(_currentSession.Title)
                && !string.Equals(_currentSession.Title, ChatSessionFactory.DefaultNewSessionTitle, StringComparison.Ordinal))
            {
                request.Metadata["title"] = _currentSession.Title.Trim();
            }

            // Phase 1.3b: 旧会话首次走 broker 时，先把 panel 已知历史种子注入 broker。Broker 端是幂等的：
            // 仅在自己 JSONL 该 conversationId 为空时才接受种子，避免覆盖已有上下文。
            await SeedBrokerConversationIfNeededAsync(userText, cancellationToken).ConfigureAwait(true);

            var eventSink = new BrokerEventSink(this, assistantStream);
            var result = await BrokerClient.SendTurnAsync(request, eventSink, cancellationToken);

            if (result == null)
            {
                throw new InvalidOperationException(LocalizationService.Get("Panel.Error.BrokerNoResult"));
            }

            if (!string.IsNullOrWhiteSpace(result.Content))
            {
                var streamedContent = assistantStream == null
                    ? string.Empty
                    : assistantStream.GetVisibleContent();
                if (string.IsNullOrWhiteSpace(streamedContent))
                {
                    await StreamAssistantContentAsync(assistantStream, result.Content, cancellationToken);
                }
                else if (!string.Equals(
                    streamedContent,
                    result.Content,
                    StringComparison.Ordinal))
                {
                    assistantStream.ReplaceVisibleContent(result.Content);
                }
            }

            if (!result.Success)
            {
                var hasVisibleAssistantContent = !string.IsNullOrWhiteSpace(result.Content)
                    || !string.IsNullOrWhiteSpace(
                        assistantStream == null ? string.Empty : assistantStream.GetVisibleContent());
                var isOperationalFailure = AgentBrokerTurnResult.IsOperationalFailure(
                    result,
                    hasVisibleAssistantContent);
                PanelTrace.Write($"[CopilotPanelControl] Broker returned non-success turn: OperationalFailure={isOperationalFailure}, HasVisibleAssistantContent={hasVisibleAssistantContent}, ErrorCode='{result.ErrorCode}', ErrorMessage='{result.ErrorMessage}', FinishReason='{result.FinishReason}'");
                if (isOperationalFailure)
                {
                    var errorMessage = UserTextLocalizer.GetErrorMessage(
                        result.ErrorCode,
                        string.IsNullOrWhiteSpace(result.ErrorMessage)
                            ? LocalizationService.Get("Panel.Error.BrokerExecutionFailed")
                            : result.ErrorMessage,
                        LocalizationService.Culture,
                        GetErrorArguments(result.ErrorParameters));
                    if (assistantStream != null)
                    {
                        assistantStream.ReplaceVisibleContent(errorMessage);
                    }
                    throw new InvalidOperationException(errorMessage);
                }
            }

            // Update context usage tooltip
            SafeUiInvoke(() => UpdateContextUsageTooltip(result.ContextUsagePercent, result.ContextUsageTokens, result.ContextWindowSize));
        }

        private static object[] GetErrorArguments(IDictionary<string, string> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return new object[0];
            }

            var argument = parameters.ContainsKey("details")
                ? parameters["details"]
                : parameters.ContainsKey("toolDisplayName")
                ? parameters["toolDisplayName"]
                : parameters.ContainsKey("toolName") ? parameters["toolName"] : string.Empty;
            return new object[] { argument };
        }

        /// <summary>
        /// Phase 1.3b: 把 panel 已知的会话历史一次性同步给 broker。
        /// 调用语义：每个 conversationId 每次 panel 进程生命周期内只调一次。
        /// Broker 端是幂等的（仅当 broker JSONL 该 convId 为空时才接受），所以即便我们多调几次也无害。
        /// 当前 turn 的 user 消息和 placeholder assistant 消息会被排除——它们由 SendTurnAsync 路径负责入库。
        /// </summary>
        private async Task SeedBrokerConversationIfNeededAsync(string currentTurnUserText, CancellationToken cancellationToken)
        {
            if (BrokerClient == null || _currentSession == null)
            {
                return;
            }

            var sessionId = _currentSession.Id;
            if (_seededBrokerConversations.Contains(sessionId))
            {
                return;
            }

            // 标记在前，无论后续 IPC 成功失败都不再尝试，避免反复 IPC 浪费时间
            _seededBrokerConversations.Add(sessionId);

            var messages = _currentSession.Messages;
            if (messages == null || messages.Count == 0)
            {
                return;
            }

            // 排除当前 turn 的两条:
            //   - 最后一条 placeholder assistant（content 还为空）
            //   - 倒数第二条 user 消息（broker 自己会从 request.UserInput 添加）
            // 用 content 比对，避免误删历史里恰好同文的消息
            var seedMessages = new List<AgentBrokerSeedMessage>();
            for (var i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (m == null)
                {
                    continue;
                }

                // 跳过末尾的 placeholder assistant（最后一条且 content 为空，role 是 assistant）
                if (i == messages.Count - 1
                    && string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrEmpty(m.Content))
                {
                    continue;
                }

                // 跳过当前 turn 的 user 消息（倒数第二条 + content 等于 userText）
                if (i == messages.Count - 2
                    && string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(m.Content, currentTurnUserText, StringComparison.Ordinal))
                {
                    continue;
                }

                seedMessages.Add(new AgentBrokerSeedMessage
                {
                    MessageId = m.Id ?? Guid.NewGuid().ToString("N"),
                    MessageType = string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) ? "user"
                        : string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant"
                        : "message",
                    Role = m.Role ?? string.Empty,
                    Content = m.Content ?? string.Empty,
                    CreatedAtUtc = m.CreatedAt.ToUniversalTime()
                });
            }

            if (seedMessages.Count == 0)
            {
                return;
            }

            var seedRequest = new AgentBrokerSeedConversationRequest
            {
                ConversationId = sessionId.ToString("D"),
                Title = _currentSession.Title ?? string.Empty,
                HostApplicationName = HostAdapter == null ? string.Empty : HostAdapter.HostApplicationName ?? string.Empty,
                Messages = seedMessages,
                OverwriteExisting = false
            };

            try
            {
                var seedResult = await BrokerClient.SeedConversationAsync(seedRequest, cancellationToken).ConfigureAwait(true);
                if (seedResult != null && !seedResult.Success && !string.IsNullOrWhiteSpace(seedResult.ErrorCode))
                {
                    _logger?.LogWarning(
                        "Broker 会话历史种子注入失败。ConversationId={ConversationId} ErrorCode={ErrorCode} ErrorMessage={ErrorMessage}",
                        seedRequest.ConversationId,
                        seedResult.ErrorCode,
                        seedResult.ErrorMessage ?? string.Empty);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Broker 会话历史种子注入异常。ConversationId={ConversationId}", seedRequest.ConversationId);
            }
        }

        /// <summary>
        /// 1.4c：把 panel 侧新生成的会话标题异步推到 broker，写入 JSONL head metadata。
        /// 复用 SeedConversation IPC（无消息体时仅做 metadata 更新）。失败仅记录日志，不抛。
        /// </summary>
        private Task PersistSessionTitleToBrokerAsync(ChatSessionState session, CancellationToken cancellationToken)
        {
            return _sessionRepository.PersistTitleAsync(
                session,
                HostAdapter == null ? string.Empty : (HostAdapter.HostApplicationName ?? string.Empty),
                cancellationToken);
        }

        private IList<AgentBrokerInputContentPart> BuildBrokerUserInputContentParts(string userText, ReferenceSendPlan referenceSendPlan)
        {
            if (referenceSendPlan == null || referenceSendPlan.NativeReferences == null || referenceSendPlan.NativeReferences.Count == 0)
            {
                return new List<AgentBrokerInputContentPart>();
            }

            var parts = new List<AgentBrokerInputContentPart>
            {
                new AgentBrokerInputContentPart
                {
                    Type = "input_text",
                    Text = userText ?? string.Empty
                }
            };

            foreach (var reference in referenceSendPlan.NativeReferences)
            {
                var part = BuildBrokerNativeReferenceContentPart(reference);
                if (part != null)
                {
                    parts.Add(part);
                }
            }

            return parts;
        }

        private AgentBrokerInputContentPart BuildBrokerNativeReferenceContentPart(PreparedReference reference)
        {
            var content = NativeAttachmentContentBuilder.Build(
                reference,
                _settings == null ? null : _settings.ChatDefaults,
                _logger);

            if (content == null)
            {
                return null;
            }

            return new AgentBrokerInputContentPart
            {
                Type = content.Type,
                MediaType = content.MediaType,
                FileName = content.FileName,
                FileContentBase64 = content.FileContentBase64
            };
        }

        private async Task CancelCurrentTurnAsync()
        {
            AgentBrokerCancelTurnResult cancelResult = null;
            _cancelRequestedByUser = true;

            if (BrokerClient != null
                && !string.IsNullOrWhiteSpace(_activeBrokerMessageId))
            {
                try
                {
                    // === 防崩溃: 取消 IPC 调用加超时保护 (5s) ===
                    // 如果 broker 进程挂住（如 HTTP 流式读取卡死），
                    // CancelTurn IPC 可能永远不返回。超时后放弃等待，不阻塞 UI。
                    using (var cancelTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    {
                        cancelResult = await BrokerClient.CancelTurnAsync(
                            new AgentBrokerCancelTurnRequest
                            {
                                TargetRequestId = _activeBrokerRequestId ?? string.Empty,
                                ConversationId = _activeBrokerConversationId ?? string.Empty,
                                MessageId = _activeBrokerMessageId ?? string.Empty,
                                SessionId = _currentSession == null ? string.Empty : _currentSession.Id.ToString("D"),
                                Reason = "user_cancelled"
                            },
                            cancelTimeoutCts.Token).ConfigureAwait(true);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger?.LogWarning("向 Broker 发送取消请求超时（5秒）。MessageId={MessageId}", _activeBrokerMessageId ?? string.Empty);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "向 Broker 发送取消请求失败。MessageId={MessageId}", _activeBrokerMessageId ?? string.Empty);
                }
            }

            // 无论 Broker IPC 是否成功，取消本地 CTS 让 SendCurrentPromptAsync 收到 OCE
            _sendCts?.Cancel();

            if (cancelResult != null && !cancelResult.Success)
            {
                PostStatusMessage(string.IsNullOrWhiteSpace(cancelResult.ErrorMessage)
                    ? LocalizationService.Get("Panel.Status.CurrentTurnCancelFailed")
                    : LocalizationService.Format("Panel.Status.CancelFailed", cancelResult.ErrorMessage));
            }
        }

        /// <summary>
        /// === 防崩溃: 强制恢复 UI 状态 ===
        /// 在取消操作超时或异常时，确保 txtChat 输入框一定恢复 enabled 状态，
        /// 避免用户界面永久卡死。
        /// </summary>
        private void ForceRecoverBusyState()
        {
            _logger?.LogWarning("强制执行 UI 状态恢复（防崩溃保护）。");
            SafeUiInvoke(() =>
            {
                _isSending = false;
                btnSend.Enabled = true;
                txtChat.Enabled = true;
                txtChat.Properties.ReadOnly = false;
                UpdateSendButtonAppearance(false);
                if (!txtChat.IsDisposed && txtChat.CanFocus)
                {
                    txtChat.Focus();
                }
            });

            PostToWeb(new
            {
                type = "setBusy",
                busy = false
            });
        }

        private void ClearActiveBrokerTurn()
        {
            _activeBrokerRequestId = string.Empty;
            _activeBrokerConversationId = string.Empty;
            _activeBrokerMessageId = string.Empty;
        }

        private AgentModelOptions CreateBrokerModelOptions(ModelProviderSettings model)
        {
            if (model == null)
            {
                return null;
            }

            var additionalParameters = model.AdditionalParameters == null
                ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, object>(model.AdditionalParameters, StringComparer.OrdinalIgnoreCase);

            var requestTimeoutSeconds = _settings != null && _settings.ChatDefaults != null
                ? Math.Max(1, _settings.ChatDefaults.RequestTimeoutSeconds)
                : 60;

            if (!additionalParameters.ContainsKey(AgentModelAdditionalParameterNames.RequestTimeoutSeconds)
                && !additionalParameters.ContainsKey("request_timeout_seconds")
                && !additionalParameters.ContainsKey("requestTimeoutSeconds"))
            {
                additionalParameters[AgentModelAdditionalParameterNames.RequestTimeoutSeconds] = requestTimeoutSeconds;
            }

            ApplyBrokerTranscriptOptions(additionalParameters);
            additionalParameters[AgentModelAdditionalParameterNames.SupportsImageInput] = model.SupportsImageInput;

            return new AgentModelOptions
            {
                ProviderKind = model.ProviderKind.ToString(),
                Endpoint = model.Endpoint ?? string.Empty,
                ApiKey = model.ApiKey ?? string.Empty,
                ModelId = model.ModelId ?? string.Empty,
                MaxContextTokens = model.MaxContextTokens,
                HttpHeaders = model.HttpHeaders == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(model.HttpHeaders, StringComparer.OrdinalIgnoreCase),
                AdditionalParameters = additionalParameters,
                SystemPrompt = BuildSystemPrompt(model)
            };
        }

        private void ApplyBrokerTranscriptOptions(IDictionary<string, object> additionalParameters)
        {
            if (additionalParameters == null)
            {
                return;
            }

            var transcriptSettings = _settings == null || _settings.Logging == null
                ? null
                : _settings.Logging.ModelTranscripts;

            if (transcriptSettings == null
                || !transcriptSettings.IsEnabled
                || transcriptSettings.Mode == DiagnosticsTranscriptLogMode.Disabled)
            {
                additionalParameters[AgentModelAdditionalParameterNames.ModelTranscriptEnabled] = false;
                return;
            }

            var rootFolder = string.IsNullOrWhiteSpace(transcriptSettings.RootFolder)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NixerOfficeCopilot",
                    "logs")
                : transcriptSettings.RootFolder.Trim();

            var fileName = string.IsNullOrWhiteSpace(transcriptSettings.FileName)
                ? "model-transcript.log"
                : Path.GetFileName(transcriptSettings.FileName.Trim());

            additionalParameters[AgentModelAdditionalParameterNames.ModelTranscriptEnabled] = true;
            additionalParameters[AgentModelAdditionalParameterNames.ModelTranscriptMode] = transcriptSettings.Mode.ToString();
            additionalParameters[AgentModelAdditionalParameterNames.ModelTranscriptFilePath] = Path.Combine(rootFolder, fileName);
        }

        private async Task StreamAssistantContentAsync(
            AssistantResponseStreamAccumulator assistantStream,
            string content,
            CancellationToken cancellationToken)
        {
            foreach (var chunk in SplitAssistantContent(content))
            {
                cancellationToken.ThrowIfCancellationRequested();
                assistantStream.AppendContentDelta(chunk);
                await Task.Delay(12, cancellationToken);
            }
        }

        private IEnumerable<string> SplitAssistantContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                yield break;
            }

            var buffer = new StringBuilder();

            foreach (var ch in content)
            {
                buffer.Append(ch);

                if (ch == '\n' || buffer.Length >= 24)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }
            }

            if (buffer.Length > 0)
            {
                yield return buffer.ToString();
            }
        }

        private void RecordToolCallStarted(string assistantMessageId, ApiToolCall toolCall, string backupPath = null, string toolDisplayName = null)
        {
            if (string.IsNullOrWhiteSpace(assistantMessageId) || toolCall == null)
            {
                return;
            }

            EnsureCurrentSession();

            var assistantMessage = _currentSession.Messages
                .LastOrDefault(m => string.Equals(m.Id, assistantMessageId, StringComparison.OrdinalIgnoreCase));

            if (assistantMessage == null)
            {
                return;
            }

            if (assistantMessage.ToolCalls == null)
            {
                assistantMessage.ToolCalls = new List<ChatToolCallState>();
            }

            var toolName = toolCall.Function == null ? string.Empty : toolCall.Function.Name ?? string.Empty;
            var displayName = UserTextLocalizer.GetToolDisplayName(
                toolName,
                string.IsNullOrWhiteSpace(toolDisplayName) ? toolName : toolDisplayName,
                LocalizationService.Culture);
            var requiresBackup = ToolRequiresBackup(toolName);
            var sharedBackupPath = string.IsNullOrWhiteSpace(backupPath)
                ? GetAssistantMessageBackupPath(assistantMessage)
                : backupPath.Trim();

            var existing = assistantMessage.ToolCalls
                .FirstOrDefault(t => string.Equals(t.ToolCallId, toolCall.Id, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.ToolName = toolName;
                existing.DisplayName = displayName;
                existing.Status = "running";
                existing.StartedAt = DateTime.Now;
                existing.RequiresBackup = requiresBackup;
                existing.BackupPath = sharedBackupPath;
            }
            else
            {
                assistantMessage.ToolCalls.Add(new ChatToolCallState
                {
                    ToolCallId = toolCall.Id ?? string.Empty,
                    ToolName = toolName,
                    DisplayName = displayName,
                    Status = "running",
                    StartedAt = DateTime.Now,
                    RequiresBackup = requiresBackup,
                    BackupPath = sharedBackupPath
                });
            }

            _currentSession.UpdatedAt = DateTime.Now;
        }

        private void RecordToolCallCompleted(string assistantMessageId, ToolExecutionResult result)
        {
            if (string.IsNullOrWhiteSpace(assistantMessageId) || result == null)
            {
                return;
            }

            EnsureCurrentSession();

            var assistantMessage = _currentSession.Messages
                .LastOrDefault(m => string.Equals(m.Id, assistantMessageId, StringComparison.OrdinalIgnoreCase));

            if (assistantMessage == null)
            {
                return;
            }

            if (assistantMessage.ToolCalls == null)
            {
                assistantMessage.ToolCalls = new List<ChatToolCallState>();
            }

            var item = assistantMessage.ToolCalls
                .FirstOrDefault(t => string.Equals(t.ToolCallId, result.ToolCallId, StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                item = new ChatToolCallState
                {
                    ToolCallId = result.ToolCallId ?? string.Empty,
                    StartedAt = DateTime.Now
                };

                assistantMessage.ToolCalls.Add(item);
            }

            var backupPath = GetBackupPath(result);

            item.ToolName = result.ToolName ?? string.Empty;
            item.DisplayName = string.IsNullOrWhiteSpace(result.ToolDisplayName)
                ? UserTextLocalizer.GetToolDisplayName(item.ToolName, item.ToolName, LocalizationService.Culture)
                : UserTextLocalizer.GetToolDisplayName(item.ToolName, result.ToolDisplayName, LocalizationService.Culture);
            item.Success = result.Success;
            item.Status = result.Success ? "completed" : "failed";
            item.Content = result.Content ?? string.Empty;
            item.ErrorCode = result.ErrorCode ?? string.Empty;
            item.ErrorMessage = result.ErrorMessage ?? string.Empty;
            item.CompletedAt = DateTime.Now;
            item.RequiresBackup = item.RequiresBackup || ToolRequiresBackup(item.ToolName);

            if (!string.IsNullOrWhiteSpace(backupPath))
            {
                item.BackupPath = backupPath;
            }

            _currentSession.UpdatedAt = DateTime.Now;
        }

        private void PostToolCallStarted(string assistantMessageId, ApiToolCall toolCall, string backupPath = null, string toolDisplayName = null)
        {
            if (toolCall == null)
            {
                return;
            }

            var toolName = toolCall.Function == null ? string.Empty : toolCall.Function.Name ?? string.Empty;

            PostToWeb(new
            {
                type = "agentToolCallStarted",
                assistantMessageId = assistantMessageId ?? string.Empty,
                toolCallId = toolCall.Id ?? string.Empty,
                toolName = toolName,
                toolDisplayName = UserTextLocalizer.GetToolDisplayName(
                    toolName,
                    string.IsNullOrWhiteSpace(toolDisplayName) ? toolName : toolDisplayName,
                    LocalizationService.Culture),
                requiresBackup = ToolRequiresBackup(toolName),
                backupPath = backupPath ?? string.Empty
            });
        }

        private string GetAssistantMessageBackupPath(ChatMessageState assistantMessage)
        {
            if (assistantMessage == null || assistantMessage.ToolCalls == null)
            {
                return string.Empty;
            }

            var item = assistantMessage.ToolCalls.FirstOrDefault(t =>
                t != null
                && !string.IsNullOrWhiteSpace(t.BackupPath));

            return item == null ? string.Empty : item.BackupPath ?? string.Empty;
        }

        private void PostToolCallCompleted(string assistantMessageId, ToolExecutionResult result)
        {
            PostToWeb(new
            {
                type = "agentToolCallCompleted",
                assistantMessageId = assistantMessageId ?? string.Empty,
                toolCallId = result == null ? string.Empty : result.ToolCallId ?? string.Empty,
                toolName = result == null ? string.Empty : result.ToolName ?? string.Empty,
                toolDisplayName = result == null
                    ? string.Empty
                    : UserTextLocalizer.GetToolDisplayName(
                        result.ToolName,
                        result.ToolDisplayName ?? result.ToolName,
                        LocalizationService.Culture),
                success = result != null && result.Success,
                content = result == null ? string.Empty : result.Content ?? string.Empty,
                errorCode = result == null ? string.Empty : result.ErrorCode ?? string.Empty,
                errorMessage = result == null
                    ? string.Empty
                    : UserTextLocalizer.GetErrorMessage(
                        result.ErrorCode,
                        result.ErrorMessage,
                        LocalizationService.Culture,
                        result.ErrorCode == "permission.denied_by_user"
                            ? new object[] { UserTextLocalizer.GetToolDisplayName(result.ToolName, result.ToolName, LocalizationService.Culture) }
                            : new object[0]),
                backupPath = result == null ? string.Empty : GetBackupPath(result)
            });
        }

        private int? ResolveSessionModelProviderId(ChatSessionState session)
        {
            return _modelMenu.ResolveModelProviderId(
                session == null ? null : session.ModelProviderId,
                _settings == null ? null : _settings.DefaultModelProviderId);
        }

        private void SyncSelectedModelFromCurrentSession()
        {
            var resolvedModelId = ResolveSessionModelProviderId(_currentSession);
            _modelMenu.SelectedModelProviderId = resolvedModelId;

            if (_currentSession != null && _currentSession.ModelProviderId != resolvedModelId)
            {
                _currentSession.ModelProviderId = resolvedModelId;
            }
        }

        private void SyncCurrentSessionModelFromSelection()
        {
            EnsureCurrentSession();
            if (_currentSession == null)
            {
                return;
            }

            if (_currentSession.ModelProviderId == _modelMenu.SelectedModelProviderId)
            {
                return;
            }

            _currentSession.ModelProviderId = _modelMenu.SelectedModelProviderId;
        }

        private static readonly System.Text.RegularExpressions.Regex ThinkBlockRegex =
    new System.Text.RegularExpressions.Regex(
        @"<think>[\s\S]*?</think>",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex ThinkTagRegex =
            new System.Text.RegularExpressions.Regex(
                @"</?think>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private void PostAssistantThinkingChunk(string messageId, string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            PostToWeb(new
            {
                type = "appendAssistantThinkingChunk",
                messageId = messageId,
                chunk = chunk
            });
        }

        private void PostAssistantThinkingCompleted(string messageId)
        {
            PostToWeb(new
            {
                type = "completeAssistantThinking",
                messageId = messageId
            });
        }

        private static string StripThinkBlocks(string content)
        {
            var value = content ?? string.Empty;
            if (value.Length == 0)
            {
                return string.Empty;
            }

            value = ThinkBlockRegex.Replace(value, string.Empty);
            value = ThinkTagRegex.Replace(value, string.Empty);
            return value.Trim();
        }

        private void FinalizeAssistantMessageOnFailure(
    ChatMessageState assistantMessage,
    AssistantResponseStreamAccumulator assistantStream,
    Exception ex)
        {
            if (assistantMessage == null)
            {
                return;
            }

            if (assistantStream != null)
            {
                assistantStream.Complete();
            }

            assistantMessage.Content = BuildAssistantFailureContent(
                assistantMessage,
                assistantStream == null ? string.Empty : assistantStream.GetVisibleContent(),
                ex);

            _currentSession.UpdatedAt = DateTime.Now;
            RefreshHistoryMenuSelection();
            PostAssistantThinkingCompleted(assistantMessage.Id);
            PostCompleteAssistantMessage(assistantMessage.Id, assistantMessage.Content);
        }

        private void FinalizeAssistantMessageOnCancellation(
            ChatMessageState assistantMessage,
            AssistantResponseStreamAccumulator assistantStream)
        {
            if (assistantMessage == null)
            {
                return;
            }

            if (assistantStream != null)
            {
                assistantStream.Complete();
            }

            assistantMessage.Content = BuildAssistantCanceledContent(
                assistantStream == null ? string.Empty : assistantStream.GetVisibleContent());

            _currentSession.UpdatedAt = DateTime.Now;
            RefreshHistoryMenuSelection();
            PostCompleteAssistantMessage(assistantMessage.Id, assistantMessage.Content);
        }

        private string BuildAssistantCanceledContent(string streamedContent)
        {
            var builder = new StringBuilder();
            var visibleContent = (streamedContent ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(visibleContent))
            {
                builder.Append(visibleContent);
            }

            AppendUniqueParagraph(builder, UserCanceledNotice);
            return builder.ToString().Trim();
        }

        private bool ShouldTreatAsCanceled(Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                return true;
            }

            if (_cancelRequestedByUser)
            {
                return true;
            }

            return _sendCts != null && _sendCts.IsCancellationRequested;
        }

        private string BuildAssistantFailureContent(
            ChatMessageState assistantMessage,
            string streamedContent,
            Exception ex)
        {
            var builder = new StringBuilder();
            var visibleContent = (streamedContent ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(visibleContent))
            {
                builder.Append(visibleContent);
            }

            var failedToolLines = GetFailedToolLines(assistantMessage);
            foreach (var line in failedToolLines)
            {
                AppendUniqueParagraph(builder, line);
            }

            var exceptionMessage = GetDetailedExceptionMessage(ex);
            if (!string.IsNullOrWhiteSpace(exceptionMessage))
            {
                var isGenericToolFailure = string.Equals(
                    exceptionMessage,
                    "One or more tool calls failed during execution.",
                    StringComparison.OrdinalIgnoreCase);

                if (!isGenericToolFailure || failedToolLines.Count == 0)
                {
                    AppendUniqueParagraph(
                        builder,
                        LocalizationService.Format("Panel.Error.ErrorDetails", exceptionMessage));
                }
            }

            var finalText = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(finalText)
                ? LocalizationService.Get("Panel.Error.GenericRequestFailed")
                : finalText;
        }

        private List<string> GetFailedToolLines(ChatMessageState assistantMessage)
        {
            var lines = new List<string>();
            if (assistantMessage == null || assistantMessage.ToolCalls == null)
            {
                return lines;
            }

            foreach (var toolCall in assistantMessage.ToolCalls)
            {
                if (toolCall == null)
                {
                    continue;
                }

                if (toolCall.Success && !string.Equals(toolCall.Status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var detail = !string.IsNullOrWhiteSpace(toolCall.ErrorMessage)
                    ? toolCall.ErrorMessage
                    : !string.IsNullOrWhiteSpace(toolCall.Content)
                        ? toolCall.Content
                        : !string.IsNullOrWhiteSpace(toolCall.ErrorCode)
                            ? toolCall.ErrorCode
                            : LocalizationService.Get("Panel.Error.ToolExecutionFailed");

                lines.Add(LocalizationService.Format(
                    "Panel.Error.ToolFailedLine",
                    toolCall.ToolName ?? "tool",
                    detail.Trim()));
            }

            return lines;
        }

        private static string GetDetailedExceptionMessage(Exception ex)
        {
            var parts = new List<string>();

            while (ex != null)
            {
                var message = (ex.Message ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(message)
                    && !parts.Any(p => string.Equals(p, message, StringComparison.OrdinalIgnoreCase)))
                {
                    parts.Add(message);
                }

                ex = ex.InnerException;
            }

            return string.Join(" --> ", parts);
        }

        private static void AppendUniqueParagraph(StringBuilder builder, string text)
        {
            var normalized = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (builder.Length > 0
                && builder.ToString().IndexOf(normalized, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(normalized);
        }

        private sealed class AssistantResponseStreamAccumulator
        {
            private const string ThinkStartTag = "<think>";
            private const string ThinkEndTag = "</think>";

            private readonly CopilotPanelControl _owner;
            private readonly StringBuilder _visibleContent;
            private readonly StringBuilder _thinkingContent;
            private readonly StringBuilder _buffer;

            private bool _insideThinkBlock;
            private bool _thinkingCompleted;
            private bool _hasThinkingContent;

            public AssistantResponseStreamAccumulator(CopilotPanelControl owner, string messageId)
            {
                _owner = owner;
                MessageId = messageId ?? string.Empty;
                _visibleContent = new StringBuilder();
                _thinkingContent = new StringBuilder();
                _buffer = new StringBuilder();
            }

            public string MessageId { get; private set; }

            public void AppendContentDelta(string delta)
            {
                if (string.IsNullOrEmpty(delta))
                {
                    return;
                }

                _buffer.Append(delta);
                DrainBuffer(false);
            }

            public void AppendThinkingDelta(string delta)
            {
                if (string.IsNullOrEmpty(delta))
                {
                    return;
                }

                AppendThinkingText(delta);
            }

            public void Complete()
            {
                DrainBuffer(true);
                CompleteThinking();
            }

            public string GetVisibleContent()
            {
                return _visibleContent.ToString().Trim();
            }

            public void ReplaceVisibleContent(string content)
            {
                _buffer.Clear();
                _visibleContent.Clear();
                _visibleContent.Append(FilterThinkingContent(content));
                _insideThinkBlock = false;
            }

            private static string FilterThinkingContent(string content)
            {
                if (string.IsNullOrEmpty(content))
                {
                    return string.Empty;
                }

                var visible = new StringBuilder();
                var index = 0;
                var insideThinkBlock = false;
                while (index < content.Length)
                {
                    var marker = insideThinkBlock ? ThinkEndTag : ThinkStartTag;
                    var markerIndex = content.IndexOf(
                        marker,
                        index,
                        StringComparison.OrdinalIgnoreCase);
                    if (markerIndex < 0)
                    {
                        if (!insideThinkBlock)
                        {
                            visible.Append(content, index, content.Length - index);
                        }

                        break;
                    }

                    if (!insideThinkBlock && markerIndex > index)
                    {
                        visible.Append(content, index, markerIndex - index);
                    }

                    index = markerIndex + marker.Length;
                    insideThinkBlock = !insideThinkBlock;
                }

                return visible.ToString();
            }

            private void DrainBuffer(bool flushAll)
            {
                while (_buffer.Length > 0)
                {
                    if (_insideThinkBlock)
                    {
                        var bufferText = _buffer.ToString();
                        var closeIndex = bufferText.IndexOf(ThinkEndTag, StringComparison.OrdinalIgnoreCase);
                        if (closeIndex >= 0)
                        {
                            AppendThinkingText(bufferText.Substring(0, closeIndex));
                            _buffer.Remove(0, closeIndex + ThinkEndTag.Length);
                            CompleteThinking();
                            continue;
                        }

                        var thinkingLength = flushAll
                            ? _buffer.Length
                            : GetSafeEmitLength(bufferText, ThinkEndTag);

                        if (thinkingLength <= 0)
                        {
                            break;
                        }

                        AppendThinkingText(bufferText.Substring(0, thinkingLength));
                        _buffer.Remove(0, thinkingLength);

                        if (!flushAll)
                        {
                            break;
                        }

                        continue;
                    }

                    var plainText = _buffer.ToString();
                    var openIndex = plainText.IndexOf(ThinkStartTag, StringComparison.OrdinalIgnoreCase);
                    if (openIndex >= 0)
                    {
                        AppendVisibleText(plainText.Substring(0, openIndex));
                        _buffer.Remove(0, openIndex + ThinkStartTag.Length);
                        _insideThinkBlock = true;
                        continue;
                    }

                    var visibleLength = flushAll
                        ? _buffer.Length
                        : GetSafeEmitLength(plainText, ThinkStartTag);

                    if (visibleLength <= 0)
                    {
                        break;
                    }

                    AppendVisibleText(plainText.Substring(0, visibleLength));
                    _buffer.Remove(0, visibleLength);

                    if (!flushAll)
                    {
                        break;
                    }
                }
            }

            private void AppendVisibleText(string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                _visibleContent.Append(text);
                _owner.PostAssistantChunk(MessageId, text);
            }

            private void AppendThinkingText(string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                _thinkingContent.Append(text);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    _hasThinkingContent = true;
                }

                _owner.PostAssistantThinkingChunk(MessageId, text);
            }

            private void CompleteThinking()
            {
                _insideThinkBlock = false;

                if (_thinkingCompleted)
                {
                    return;
                }

                if (_hasThinkingContent)
                {
                    _thinkingCompleted = true;
                    _owner.PostAssistantThinkingCompleted(MessageId);
                }
            }

            private static int GetSafeEmitLength(string text, string tag)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return 0;
                }

                var keepLength = Math.Min(text.Length, tag.Length - 1);
                for (var i = keepLength; i > 0; i--)
                {
                    if (tag.StartsWith(text.Substring(text.Length - i), StringComparison.OrdinalIgnoreCase))
                    {
                        return text.Length - i;
                    }
                }

                return text.Length;
            }
        }

        private string GetAssistantMessageBackupPath(string assistantMessageId)
        {
            if (string.IsNullOrWhiteSpace(assistantMessageId))
            {
                return string.Empty;
            }

            EnsureCurrentSession();
            if (_currentSession == null || _currentSession.Messages == null)
            {
                return string.Empty;
            }

            var assistantMessage = _currentSession.Messages
                .LastOrDefault(m => string.Equals(m.Id, assistantMessageId, StringComparison.OrdinalIgnoreCase));

            return GetAssistantMessageBackupPath(assistantMessage);
        }

        private sealed class BrokerEventSink : IAgentBrokerEventSink
        {
            private readonly CopilotPanelControl _owner;
            private readonly AssistantResponseStreamAccumulator _assistantStream;
            private readonly string _assistantMessageId;

            public BrokerEventSink(CopilotPanelControl owner, AssistantResponseStreamAccumulator assistantStream)
            {
                _owner = owner;
                _assistantStream = assistantStream;
                _assistantMessageId = assistantStream == null ? string.Empty : assistantStream.MessageId;
            }

            public Task OnEventAsync(AgentBrokerEvent brokerEvent, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_owner == null || brokerEvent == null)
                {
                    return Task.CompletedTask;
                }

                switch (brokerEvent.EventType)
                {
                    case AgentBrokerEventTypes.ThinkingDelta:
                        if (_assistantStream != null)
                        {
                            _assistantStream.AppendThinkingDelta(brokerEvent.Text ?? string.Empty);
                        }
                        else
                        {
                            _owner.PostAssistantThinkingChunk(_assistantMessageId, brokerEvent.Text ?? string.Empty);
                        }
                        break;

                    case AgentBrokerEventTypes.ContentDelta:
                        if (_assistantStream != null)
                        {
                            _assistantStream.AppendContentDelta(brokerEvent.Text ?? string.Empty);
                        }
                        else
                        {
                            _owner.PostAssistantChunk(_assistantMessageId, brokerEvent.Text ?? string.Empty);
                        }
                        break;

                    case AgentBrokerEventTypes.ToolCallStarted:
                        var toolCall = new ApiToolCall
                        {
                            Id = brokerEvent.ToolCallId ?? string.Empty,
                            Function = new ApiToolFunctionCall
                            {
                                Name = brokerEvent.ToolName ?? string.Empty
                            }
                        };

                        _owner.RecordToolCallStarted(_assistantMessageId, toolCall, brokerEvent.BackupPath ?? string.Empty, brokerEvent.ToolDisplayName);
                        _owner.PostToolCallStarted(_assistantMessageId, toolCall, brokerEvent.BackupPath ?? string.Empty, brokerEvent.ToolDisplayName);
                        break;

                    case AgentBrokerEventTypes.ToolCallCompleted:
                        var toolResult = new ToolExecutionResult
                        {
                            ToolCallId = brokerEvent.ToolCallId ?? string.Empty,
                            ToolName = brokerEvent.ToolName ?? string.Empty,
                            ToolDisplayName = brokerEvent.ToolDisplayName ?? string.Empty,
                            Success = brokerEvent.Success,
                            Content = brokerEvent.Text ?? string.Empty,
                            ErrorCode = brokerEvent.ErrorCode ?? string.Empty,
                            ErrorMessage = UserTextLocalizer.GetErrorMessage(
                                brokerEvent.ErrorCode,
                                brokerEvent.ErrorMessage,
                                LocalizationService.Culture,
                                GetErrorArguments(brokerEvent.ErrorParameters))
                        };

                        if (!string.IsNullOrWhiteSpace(brokerEvent.BackupPath))
                        {
                            toolResult.Metadata[ToolMetadataKeys.BackupPath] = brokerEvent.BackupPath;
                        }

                        _owner.RecordToolCallCompleted(_assistantMessageId, toolResult);
                        _owner.PostToolCallCompleted(_assistantMessageId, toolResult);
                        break;

                    case AgentBrokerEventTypes.ExecutionMode:
                        _owner.PostToWeb(new
                        {
                            type = "executionMode",
                            messageId = _assistantMessageId,
                            text = brokerEvent.Text ?? string.Empty,
                            warning = !brokerEvent.Success
                        });
                        break;

                    case AgentBrokerEventTypes.Status:
                        // Phase 1: 检查是否为用户交互请求
                        if (TryHandleUserInteractionRequest(brokerEvent.Text ?? string.Empty, cancellationToken))
                        {
                            break;
                        }

                        // Phase 5: 检查是否为推送通知或进度更新
                        if (TryHandleNotificationEvent(brokerEvent.Text ?? string.Empty))
                        {
                            break;
                        }

                        // Phase 4: 检查是否为 Brief 消息
                        if (TryHandleBriefMessage(brokerEvent.Text ?? string.Empty))
                        {
                            break;
                        }

                        // Phase 2: 检查是否为 Todo 更新
                        if (TryHandleTodoUpdate(brokerEvent.Text ?? string.Empty))
                        {
                            break;
                        }

                        if (!_owner.TryHandleAssistantInfoStatus(_assistantMessageId, brokerEvent.Text ?? string.Empty))
                        {
                            _owner.PostStatusMessage(brokerEvent.Text ?? string.Empty);
                        }
                        break;

                    case AgentBrokerEventTypes.TurnCompleted:
                        _owner.PostAssistantThinkingCompleted(_assistantMessageId);
                        break;

                    case AgentBrokerEventTypes.Error:
                        _owner._logger?.LogWarning("Broker 返回错误事件。Tool={ToolName} Code={ErrorCode} Message={ErrorMessage}", brokerEvent.ToolName ?? string.Empty, brokerEvent.ErrorCode ?? string.Empty, string.IsNullOrWhiteSpace(brokerEvent.ErrorMessage) ? (brokerEvent.Text ?? string.Empty) : brokerEvent.ErrorMessage);
                        var errorMessage = UserTextLocalizer.GetErrorMessage(
                            brokerEvent.ErrorCode,
                            string.IsNullOrWhiteSpace(brokerEvent.ErrorMessage)
                                ? brokerEvent.Text
                                : brokerEvent.ErrorMessage,
                            LocalizationService.Culture,
                            GetErrorArguments(brokerEvent.ErrorParameters));
                        if (!string.IsNullOrWhiteSpace(errorMessage))
                        {
                            _owner.PostStatusMessage(errorMessage);
                        }
                        break;
                }

                return Task.CompletedTask;
            }

            /// <summary>
            /// Phase 1/7C: 检测并处理用户交互请求。
            /// Broker 通过 Status 事件发送 JSON 序列化的 <see cref="AgentBrokerToolUserInteractionRequest"/>。
            /// Phase 7C: AskUserQuestion 改为嵌入 WebView 会话流 HTML，不再弹出 WinForms 对话框。
            /// Plan 审批和工具确认仍使用 WinForms 对话框。
            /// </summary>
            private bool TryHandleUserInteractionRequest(string text, CancellationToken cancellationToken)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return false;
                }

                PanelTrace.Write(
                    $"[CopilotPanelControl] TryHandleUserInteractionRequest received text (len={text.Length}): {(text.Length > 120 ? text.Substring(0, 120) + "..." : text)}");

                AgentBrokerToolUserInteractionRequest request;
                try
                {
                    // Phase 7C: Broker 用 System.Text.Json 序列化（默认 camelCase），
                    // 需用 CamelCasePropertyNamesContractResolver 匹配 toolCallId ↔ ToolCallId。
                    var settings = new Newtonsoft.Json.JsonSerializerSettings
                    {
                        ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
                    };
                    request = Newtonsoft.Json.JsonConvert.DeserializeObject<AgentBrokerToolUserInteractionRequest>(text, settings);
                }
                catch (Exception ex)
                {
                    PanelTrace.Write(
                        $"[CopilotPanelControl] TryHandleUserInteractionRequest deserialize FAILED: {ex.Message}");
                    return false;
                }

                if (request == null)
                {
                    PanelTrace.Write("[CopilotPanelControl] TryHandleUserInteractionRequest: request is null");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(request.ToolCallId))
                {
                    PanelTrace.Write(
                        $"[CopilotPanelControl] TryHandleUserInteractionRequest: ToolCallId is empty. ToolName='{request.ToolName}', InteractionDataJson length={(request.InteractionDataJson?.Length ?? 0)}");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(request.InteractionDataJson))
                {
                    PanelTrace.Write(
                        $"[CopilotPanelControl] TryHandleUserInteractionRequest: InteractionDataJson is empty. ToolCallId='{request.ToolCallId}', ToolName='{request.ToolName}'");
                    return false;
                }

                PanelTrace.Write(
                    $"[CopilotPanelControl] TryHandleUserInteractionRequest SUCCESS: ToolCallId='{request.ToolCallId}', ToolName='{request.ToolName}', TimeoutSeconds={request.TimeoutSeconds}");

                // Phase 3/7C: Plan 审批 → 嵌入 WebView 会话流 HTML
                if (IsPlanApprovalRequest(request.InteractionDataJson))
                {
                    string planText = string.Empty;
                    try
                    {
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(request.InteractionDataJson ?? "{}");
                        planText = obj["plan"]?.ToString() ?? string.Empty;
                    }
                    catch { }

                    _owner.PostToWeb(new { type = "planApproval", requestId = request.ToolCallId ?? string.Empty, plan = planText });
                    StartWebViewInteractionAsync(request, cancellationToken, "planApproval");
                    return true;
                }

                // Phase P4/7C: 工具确认 → 嵌入 WebView 会话流 HTML
                if (IsToolConfirmationRequest(request.InteractionDataJson))
                {
                    string toolName = string.Empty;
                    string toolDescription = string.Empty;
                    string operationSummary = string.Empty;
                    string riskLevel = "high";
                    string argumentsSummary = string.Empty;
                    string argumentsJson = string.Empty;
                    try
                    {
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(request.InteractionDataJson ?? "{}");
                        toolName = obj["toolName"]?.ToString() ?? string.Empty;
                        toolDescription = obj["toolDescription"]?.ToString() ?? string.Empty;
                        operationSummary = obj["operationSummary"]?.ToString() ?? string.Empty;
                        riskLevel = obj["riskLevel"]?.ToString() ?? "high";
                        argumentsSummary = obj["argumentsSummary"]?.ToString() ?? string.Empty;
                        argumentsJson = obj["argumentsJson"]?.ToString() ?? argumentsSummary;
                    }
                    catch { }

                    // Check if this tool is already approved for current session or permanently
                    bool autoConfirm = false;
                    string autoDecision = string.Empty;
                    if (!string.IsNullOrWhiteSpace(toolName))
                    {
                        if (_owner.IsToolApprovedPermanently(toolName))
                        {
                            autoConfirm = true;
                            autoDecision = "allow-always";
                            PanelTrace.Write(
                                $"[CopilotPanelControl] Tool '{toolName}' permanently approved — auto-confirming (callId={request.ToolCallId})");
                        }
                        else if (_owner.IsToolApprovedForCurrentSession(toolName))
                        {
                            autoConfirm = true;
                            autoDecision = "allow-session";
                            PanelTrace.Write(
                                $"[CopilotPanelControl] Tool '{toolName}' session-approved — auto-confirming (callId={request.ToolCallId})");
                        }
                    }

                    if (autoConfirm)
                    {
                        // Skip WebView entirely — set up TCS, complete it, respond to broker
                        StartAutoConfirmInteractionAsync(request, cancellationToken, autoDecision);
                    }
                    else
                    {
                        _owner.PostToWeb(new
                        {
                            type = "toolConfirmation",
                            requestId = request.ToolCallId ?? string.Empty,
                            toolName,
                            toolDescription,
                            operationSummary,
                            riskLevel,
                            argumentsSummary,
                            argumentsJson
                        });
                        StartWebViewInteractionAsync(request, cancellationToken, "toolConfirmation");
                    }

                    return true;
                }

                // Phase 7C: AskUserQuestion → 嵌入 WebView 会话流 HTML
                string questionsJson = "[]";
                try
                {
                    var interactionObj = Newtonsoft.Json.Linq.JObject.Parse(request.InteractionDataJson ?? "{}");
                    var questionsToken = interactionObj["questions"];
                    if (questionsToken != null)
                        questionsJson = questionsToken.ToString(Newtonsoft.Json.Formatting.None);
                    PanelTrace.Write(
                        $"[CopilotPanelControl] AskUserQuestion: questionsToken is null={questionsToken == null}, questionsJson length={questionsJson.Length}, preview={(questionsJson.Length > 200 ? questionsJson.Substring(0, 200) + "..." : questionsJson)}");
                }
                catch (Exception ex)
                {
                    PanelTrace.Write($"[CopilotPanelControl] AskUserQuestion parse questions FAILED: {ex.Message}");
                }

                _owner.PostToWeb(new { type = "askUserQuestion", requestId = request.ToolCallId ?? string.Empty, questions = questionsJson });
                StartWebViewInteractionAsync(request, cancellationToken, "askUserQuestion");
                return true;
            }

            /// <summary>
            /// Phase 7C: 统一的 WebView 内联交互异步处理器。
            /// 所有用户交互类型共享：注册 TCS → await 响应（120s 超时）→ 发回 Broker → 清理。
            /// </summary>
            private void StartWebViewInteractionAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken,
                string interactionType)
            {
                var requestId = request.ToolCallId ?? string.Empty;
                var tcs = _owner._interactions.Register(requestId, request.ToolName ?? string.Empty);

                PanelTrace.Write(
                    $"[CopilotPanelControl] {interactionType} sent to WebView: requestId={requestId}");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var timeoutSeconds = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : 120;
                        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                        {
                            linkedCts.Token.Register(() =>
                        {
                            if (!tcs.Task.IsCompleted)
                                tcs.TrySetResult(new AgentBrokerToolUserInteractionResponse
                                {
                                    ToolCallId = requestId,
                                    Cancelled = true,
                                    ErrorCode = "user.timeout",
                                    ErrorMessage = "User interaction timed out or was cancelled."
                                });
                        });

                        var response = await tcs.Task.ConfigureAwait(false);

                        // Broker 侧权限门的确认结果也要记入授权缓存，否则用户选“本会话/
                        // 始终允许”后下一次调用依旧会被询问（旧实现只在宿主侧那道门里记录）。
                        if (string.Equals(interactionType, "toolConfirmation", StringComparison.OrdinalIgnoreCase))
                        {
                            _owner.RememberToolConfirmationDecision(request.ToolName, response);
                        }

                        if (_owner.BrokerClient != null)
                            await _owner.BrokerClient.SendUserInteractionResponseAsync(response, cancellationToken).ConfigureAwait(false);

                        _owner.PostToWeb(new { type = "userInteractionCompleted", requestId = requestId });
                        } // using blocks
                    } // try
                    catch (Exception ex)
                    {
                        PanelTrace.Write(
                            $"[CopilotPanelControl] {interactionType} WebView failed: {ex.Message}");
                    }
                    finally
                    {
                        _owner._interactions.Unregister(requestId, false);
                    }
                }, cancellationToken);
            }

            /// <summary>
            /// Phase 7C: Auto-confirm a tool interaction without showing any WebView dialog.
            /// Creates TCS, completes it immediately, and sends response to broker.
            /// </summary>
            private void StartAutoConfirmInteractionAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken,
                string decision)
            {
                var requestId = request.ToolCallId ?? string.Empty;
                var tcs = _owner._interactions.Register(requestId, request.ToolName ?? string.Empty);

                // Build response and complete TCS
                var response = new AgentBrokerToolUserInteractionResponse
                {
                    ToolCallId = requestId,
                    AnswersJson = $"{{\"decision\":\"{decision}\"}}",
                    AnnotationsJson = "{}",
                    Cancelled = false
                };
                tcs.TrySetResult(response);

                // Send response to broker in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (_owner.BrokerClient != null)
                            await _owner.BrokerClient.SendUserInteractionResponseAsync(response, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        PanelTrace.Write($"[CopilotPanelControl] Auto-confirm broker response failed: {ex.Message}");
                    }
                    finally
                    {
                        _owner._interactions.Unregister(requestId, true);
                    }
                }, cancellationToken);

                PanelTrace.Write(
                    $"[CopilotPanelControl] Auto-confirmed '{request.ToolName}' with decision={decision} (callId={requestId})");
            }

            /// <summary>
            /// Phase 1 (legacy): 在主 UI 线程显示 AskUserQuestion 对话框，等待用户回答。
            /// Phase 7C: 此方法保留用于兼容，新路径走 WebView 嵌入。
            /// </summary>
            private async Task<AgentBrokerToolUserInteractionResponse> ShowAskUserQuestionDialogAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken)
            {
                var tcs = new System.Threading.Tasks.TaskCompletionSource<AgentBrokerToolUserInteractionResponse>();

                // 在主 UI 线程创建并显示对话框
                _owner.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        using (var dialog = new AskUserQuestionDialog(request))
                        {
                            var result = dialog.ShowDialog(_owner);

                            if (result == System.Windows.Forms.DialogResult.OK)
                            {
                                tcs.TrySetResult(dialog.GetResponse());
                            }
                            else
                            {
                                tcs.TrySetResult(new AgentBrokerToolUserInteractionResponse
                                {
                                    ToolCallId = request.ToolCallId ?? string.Empty,
                                    Cancelled = true,
                                    ErrorCode = "user.cancelled",
                                    ErrorMessage = "User cancelled the interaction."
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetResult(new AgentBrokerToolUserInteractionResponse
                        {
                            ToolCallId = request?.ToolCallId ?? string.Empty,
                            Cancelled = true,
                            ErrorCode = "panel.ui_error",
                            ErrorMessage = $"Failed to show AskUserQuestion dialog: {ex.Message}"
                        });
                    }
                }));

                return await tcs.Task.ConfigureAwait(false);
            }

            /// <summary>
            /// Phase 5: 检测并处理推送通知和监控进度事件。
            /// Broker 通过 Status 事件发送 JSON: {"type":"notification.push",...} 或 {"type":"monitor.progress",...}
            /// </summary>
            private bool TryHandleNotificationEvent(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                try
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(text);
                    var eventType = obj["type"]?.ToString() ?? string.Empty;

                    if (eventType == "notification.push")
                    {
                        var message = obj["message"]?.ToString() ?? string.Empty;
                        var notifType = obj["notificationType"]?.ToString() ?? "info";
                        _owner.PostToWeb(new { type = "notification.push", message, notificationType = notifType });
                        return true;
                    }

                    if (eventType == "monitor.progress")
                    {
                        var task = obj["task"]?.ToString() ?? string.Empty;
                        var progress = obj["progress"]?.ToString() ?? "0";
                        var status = obj["status"]?.ToString() ?? "running";
                        _owner.PostToWeb(new { type = "monitor.progress", task, progress, status });
                        return true;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            }

            /// Phase 4: 检测并处理 Brief 消息。
            /// Broker 通过 Status 事件发送 JSON: {"type":"brief.message","message":"...","status":"...","attachments":[...]}
            /// </summary>
            private bool TryHandleBriefMessage(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                try
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(text);
                    if (obj["type"]?.ToString() != "brief.message")
                        return false;

                    var message = obj["message"]?.ToString() ?? string.Empty;
                    var status = obj["status"]?.ToString() ?? "normal";
                    var attachments = obj["attachments"]?.ToString() ?? "[]";

                    // 记录到会话 InfoItems 中，确保持久化
                    if (_owner._currentSession != null && _owner._currentSession.Messages != null)
                    {
                        var assistantMsg = _owner._currentSession.Messages
                            .LastOrDefault(m => string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase));
                        if (assistantMsg != null)
                        {
                            if (assistantMsg.InfoItems == null)
                                assistantMsg.InfoItems = new List<string>();
                            assistantMsg.InfoItems.Add(message);
                            _owner._currentSession.UpdatedAt = DateTime.Now;
                        }
                    }

                    // Send to WebView for rendering as a special message
                    _owner.PostToWeb(new
                    {
                        type = "brief.message",
                        messageId = _assistantMessageId ?? string.Empty,
                        message,
                        status,
                        attachments
                    });

                    PanelTrace.Write(
                        $"[CopilotPanelControl] Brief message received: {message}");
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Phase 2: 检测并处理 Todo 更新事件。
            /// Broker 通过 Status 事件发送 JSON: {"type":"todo.update","todos":[...],"oldTodos":[...]}
            /// </summary>
            private bool TryHandleTodoUpdate(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                try
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(text);
                    if (obj["type"]?.ToString() != "todo.update")
                        return false;

                    var todos = obj["todos"]?.ToString() ?? "[]";
                    var oldTodos = obj["oldTodos"]?.ToString() ?? "[]";

                    // 通过 WebView 通知前端更新 Todo UI
                    _owner.PostTodoUpdate(todos);

                    PanelTrace.Write(
                        $"[CopilotPanelControl] Todo update received: {todos}");
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            /// <summary>
            /// Phase 3: 判断是否为 Plan 审批请求。
            /// </summary>
            private static bool IsPlanApprovalRequest(string interactionDataJson)
            {
                if (string.IsNullOrWhiteSpace(interactionDataJson)) return false;
                try
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(interactionDataJson);
                    return obj["type"]?.ToString() == "plan.approval";
                }
                catch { return false; }
            }

            /// <summary>
            /// Phase P4: 检测工具确认请求。
            /// </summary>
            private static bool IsToolConfirmationRequest(string interactionDataJson)
            {
                if (string.IsNullOrWhiteSpace(interactionDataJson)) return false;
                try
                {
                    var obj = Newtonsoft.Json.Linq.JObject.Parse(interactionDataJson);
                    return obj["type"]?.ToString() == "tool.confirmation";
                }
                catch { return false; }
            }

            /// <summary>
            /// Phase P4: 显示工具确认对话框（复用 Panel 已有的 ChatToolConfirmationHandler）。
            /// </summary>
            private async System.Threading.Tasks.Task<AgentBrokerToolUserInteractionResponse> ShowToolConfirmationDialogAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken)
            {
                var tcs = new System.Threading.Tasks.TaskCompletionSource<AgentBrokerToolUserInteractionResponse>();

                _owner.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        string toolName = string.Empty;
                        string toolDescription = string.Empty;
                        string operationSummary = string.Empty;
                        string riskLevel = "high";
                        string argumentsSummary = string.Empty;

                        try
                        {
                            var obj = Newtonsoft.Json.Linq.JObject.Parse(request.InteractionDataJson ?? "{}");
                            toolName = obj["toolName"]?.ToString() ?? string.Empty;
                            toolDescription = obj["toolDescription"]?.ToString() ?? string.Empty;
                            operationSummary = obj["operationSummary"]?.ToString() ?? string.Empty;
                            riskLevel = obj["riskLevel"]?.ToString() ?? "high";
                            argumentsSummary = obj["argumentsSummary"]?.ToString() ?? string.Empty;
                        }
                        catch { }

                        var confirmRequest = new OfficeCopilot.Host.Abstractions.ToolConfirmationRequest
                        {
                            MessageId = string.Empty,
                            ToolCallId = request.ToolCallId ?? string.Empty,
                            ToolName = toolName,
                            ToolDisplayName = toolName,
                            ToolDescription = toolDescription,
                            OperationSummary = string.IsNullOrWhiteSpace(operationSummary)
                                ? toolDescription
                                : operationSummary,
                            ArgumentsSummary = argumentsSummary,
                            RiskLevel = riskLevel,
                            DocumentName = string.Empty,
                            BackupPath = string.Empty
                        };

                        var handler = _owner.ResolveConfirmationHandler();
                        var approved = handler.Confirm(confirmRequest);

                        tcs.TrySetResult(new AgentBrokerToolUserInteractionResponse
                        {
                            ToolCallId = request.ToolCallId ?? string.Empty,
                            Cancelled = !approved,
                            AnswersJson = approved ? "{\"approved\":true}" : "{}",
                            ErrorCode = approved ? string.Empty : "permission.denied_by_user",
                            ErrorMessage = approved ? string.Empty : $"Tool '{toolName}' denied by user."
                        });
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetResult(new AgentBrokerToolUserInteractionResponse
                        {
                            ToolCallId = request?.ToolCallId ?? string.Empty,
                            Cancelled = true,
                            ErrorCode = "panel.ui_error",
                            ErrorMessage = $"Failed to show tool confirmation: {ex.Message}"
                        });
                    }
                }));

                return await tcs.Task.ConfigureAwait(false);
            }

            /// <summary>
            /// Phase 3: 显示 Plan 审批对话框。
            /// </summary>
            private async System.Threading.Tasks.Task<AgentBrokerToolUserInteractionResponse> ShowPlanApprovalDialogAsync(
                AgentBrokerToolUserInteractionRequest request,
                CancellationToken cancellationToken)
            {
                var tcs = new System.Threading.Tasks.TaskCompletionSource<AgentBrokerToolUserInteractionResponse>();

                _owner.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        string planText = string.Empty;
                        try
                        {
                            var obj = Newtonsoft.Json.Linq.JObject.Parse(request.InteractionDataJson ?? "{}");
                            planText = obj["plan"]?.ToString() ?? string.Empty;
                        }
                        catch { }

                        using (var dialog = new PlanApprovalDialog(planText))
                        {
                            var result = dialog.ShowDialog(_owner);
                            tcs.TrySetResult(result == System.Windows.Forms.DialogResult.OK
                                ? new AgentBrokerToolUserInteractionResponse
                                {
                                    ToolCallId = request.ToolCallId ?? string.Empty,
                                    AnswersJson = "{\"approved\": true}",
                                    Cancelled = false
                                }
                                : new AgentBrokerToolUserInteractionResponse
                                {
                                    ToolCallId = request.ToolCallId ?? string.Empty,
                                    Cancelled = true,
                                    ErrorCode = "plan.rejected",
                                    ErrorMessage = "User rejected the plan."
                                });
                        }
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetResult(new AgentBrokerToolUserInteractionResponse
                        {
                            ToolCallId = request?.ToolCallId ?? string.Empty,
                            Cancelled = true,
                            ErrorCode = "panel.ui_error",
                            ErrorMessage = $"Plan approval dialog failed: {ex.Message}"
                        });
                    }
                }));

                return await tcs.Task.ConfigureAwait(false);
            }
        }
    }
}