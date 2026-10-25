using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace OfficeCopilot.Settings.Abstractions
{
    public enum ModelProviderKind
    {
        OpenAI = 0,
        AzureOpenAI = 1,
        Ollama = 2,
        LMStudio = 3,
        Custom = 4,
        Anthropic = 5,
        DeepSeek = 6
    }

    public enum HttpTransportMode
    {
        AutoDetect = 0,
        Sse = 1,
        StreamableHttp = 2
    }

    public enum McpServerType
    {
        Stdio = 0,
        Http = 1
    }

    public enum ConversationMessageRole
    {
        System = 0,
        User = 1,
        Assistant = 2,
        Tool = 3
    }

    public enum AppLogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }

    public enum ModelTranscriptLogMode
    {
        Disabled = 0,
        MetadataOnly = 1,
        SanitizedContent = 2,
        RawContent = 3
    }

    public enum ConversationStorageMode
    {
        InMemory = 0,
        Jsonl = 1
    }

    public sealed class OfficeCopilotSettings
    {
        public OfficeCopilotSettings()
        {
            ConfigurationVersion = 8;
            CreatedAtUtc = DateTime.UtcNow;
            UpdatedAtUtc = DateTime.UtcNow;
            ChatDefaults = new ChatDefaultsSettings();
            ModelProviders = new List<ModelProviderSettings>();
            McpServers = new List<McpServerSettings>();
            Logging = new LoggingSettings();
            ToolConfirmationPolicies = new List<ToolConfirmationPolicySettings>();
            ToolGroupRegistrations = new List<ToolGroupRegistrationSettings>();
            ToolRegistrations = new List<ToolRegistrationSettings>();
            ToolDefinitionOverrides = new List<ToolDefinitionOverrideSettings>();
            ExcelToolTrustMode = ToolTrustModeValues.NeedConfirm;
            WordToolTrustMode = ToolTrustModeValues.NeedConfirm;
            PowerPointToolTrustMode = ToolTrustModeValues.NeedConfirm;
            Runtime = new RuntimeSettings();
            PowerPointSlides = new PowerPointSlidesSettings();
            UiLanguage = "en";
        }

        public int ConfigurationVersion { get; set; }

        public string UiLanguage { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public int? DefaultModelProviderId { get; set; }

        public ChatDefaultsSettings ChatDefaults { get; set; }

        public RuntimeSettings Runtime { get; set; }

        public PowerPointSlidesSettings PowerPointSlides { get; set; }

        public List<ModelProviderSettings> ModelProviders { get; set; }

        public List<McpServerSettings> McpServers { get; set; }

        public LoggingSettings Logging { get; set; }

        public List<ToolConfirmationPolicySettings> ToolConfirmationPolicies { get; set; }

        public List<ToolGroupRegistrationSettings> ToolGroupRegistrations { get; set; }

        public List<ToolRegistrationSettings> ToolRegistrations { get; set; }

        public List<ToolDefinitionOverrideSettings> ToolDefinitionOverrides { get; set; }

        /// <summary>
        /// 各宿主的工具执行权限模式，取值见 <see cref="ToolTrustModeValues"/>。三个宿主各用独立参数名保存。
        /// </summary>
        public string ExcelToolTrustMode { get; set; }

        public string WordToolTrustMode { get; set; }

        public string PowerPointToolTrustMode { get; set; }

        /// <summary>
        /// 通用元数据存储，用于持久化 UI 状态（如 Skill 启用状态等）。
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }
    }

    public sealed class PowerPointSlidesSettings
    {
        public PowerPointSlidesSettings()
        {
            DefaultStyle = string.Empty;
            DefaultDensity = "medium";
            DefaultBuildMode = "fidelity";
            RepairIterationCap = 3;
            WorkspaceRetentionDays = 30;
            EmbedFontsByDefault = true;
        }

        public string DefaultStyle { get; set; }

        public string DefaultDensity { get; set; }

        public string DefaultBuildMode { get; set; }

        public int RepairIterationCap { get; set; }

        public int WorkspaceRetentionDays { get; set; }

        public bool EmbedFontsByDefault { get; set; }
    }

    public sealed class LoggingSettings
    {
        public LoggingSettings()
        {
            Runtime = new RuntimeLoggingSettings();
            ModelTranscripts = new ModelTranscriptLoggingSettings();
            ToolCalls = new ToolCallLoggingSettings();
        }

        public RuntimeLoggingSettings Runtime { get; set; }

        public ModelTranscriptLoggingSettings ModelTranscripts { get; set; }

        public ToolCallLoggingSettings ToolCalls { get; set; }
    }

    public sealed class RuntimeLoggingSettings
    {
        public RuntimeLoggingSettings()
        {
            IsEnabled = true;
            MinimumLevel = AppLogLevel.Warning;
            EnableDebugTrace = false;
            EnableFile = true;
            IncludeScopes = true;
            RootFolder = string.Empty;
            ApplicationLogFileName = "application.log";
            MaxFileSizeMb = 5;
            RetainedFileCountLimit = 10;
        }

        public bool IsEnabled { get; set; }

        public AppLogLevel MinimumLevel { get; set; }

        public bool EnableDebugTrace { get; set; }

        public bool EnableFile { get; set; }

        public bool IncludeScopes { get; set; }

        public string RootFolder { get; set; }

        public string ApplicationLogFileName { get; set; }

        public int MaxFileSizeMb { get; set; }

        public int RetainedFileCountLimit { get; set; }
    }

    public sealed class ModelTranscriptLoggingSettings
    {
        public ModelTranscriptLoggingSettings()
        {
            IsEnabled = false;
            Mode = ModelTranscriptLogMode.Disabled;
            RootFolder = string.Empty;
            FileName = "model-transcript.log";
            IncludeToolCalls = true;
            IncludeHostContext = true;
            MaskSensitiveData = true;
            MaxContentLength = 0;
        }

        public bool IsEnabled { get; set; }

        public ModelTranscriptLogMode Mode { get; set; }

        public string RootFolder { get; set; }

        public string FileName { get; set; }

        public bool IncludeToolCalls { get; set; }

        public bool IncludeHostContext { get; set; }

        public bool MaskSensitiveData { get; set; }

        public int MaxContentLength { get; set; }
    }

    public sealed class ToolCallLoggingSettings
    {
        public ToolCallLoggingSettings()
        {
            IsEnabled = true;
        }

        public bool IsEnabled { get; set; }
    }

    public sealed class ChatDefaultsSettings
    {
        public ChatDefaultsSettings()
        {
            RequestTimeoutSeconds = 60;
            AutoRestoreConversation = true;
            AttachmentFileCountThreshold = 5;
            MaxVisualPages = 5;
            AttachmentInlineCharBudget = 12000;
            NativeThresholdKb = 1024;
            InlineTextThresholdMb = 10;
            ToolFetchFileThresholdMb = 10;
            AutoResizeBigImage = true;
        }

        public int RequestTimeoutSeconds { get; set; }

        public bool AutoRestoreConversation { get; set; }

        public int AttachmentFileCountThreshold { get; set; }

        public int MaxVisualPages { get; set; }

        public int AttachmentInlineCharBudget { get; set; }

        public int NativeThresholdKb { get; set; }

        public int InlineTextThresholdMb { get; set; }

        public int ToolFetchFileThresholdMb { get; set; }

        public bool AutoResizeBigImage { get; set; }
    }

    public sealed class ModelProviderSettings
    {
        public const string DefaultSupportedInputFileTypes = ".png,.jpg,.bmp";

        public const int DefaultMaxContextTokens = 64000;

        public ModelProviderSettings()
        {
            Id = 0;
            IsEnabled = true;
            ProviderKind = ModelProviderKind.Custom;
            SupportedInputFileTypes = DefaultSupportedInputFileTypes;
            MaxContextTokens = DefaultMaxContextTokens;
            SystemPrompt = string.Empty;
            AdditionalParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            HttpHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public int Id { get; set; }

        public string Name { get; set; }

        public string SupportedInputFileTypes { get; set; }

        public bool IsEnabled { get; set; }

        public ModelProviderKind ProviderKind { get; set; }

        public string Endpoint { get; set; }

        public string ApiKey { get; set; }

        public string ModelId { get; set; }

        public int MaxContextTokens { get; set; }

        public bool SupportsImageInput { get; set; }

        public string SystemPrompt { get; set; }

        public Dictionary<string, object> AdditionalParameters { get; set; }

        public Dictionary<string, string> HttpHeaders { get; set; }
    }

    public static class ModelProviderAdditionalParameterNames
    {
        public const string Stream = "Stream";
        public const string Temperature = "Temperature";
        public const string TopP = "TopP";
        public const string MaxTokens = "MaxTokens";
        public const string SupportsMixedContentParts = "SupportsMixedContentParts";
        public const string SupportsReferenceToolFetch = "SupportsReferenceToolFetch";
        public const string MaxInlineReferenceChars = "MaxInlineReferenceChars";
        public const string MaxNativeInputFileCount = "MaxNativeInputFileCount";
    }

    public sealed class ModelInputCapabilitySnapshot
    {
        public ModelInputCapabilitySnapshot()
        {
            SupportedInputFileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MaxInlineReferenceChars = 12000;
            MaxNativeInputFileCount = 4;
        }

        public bool NativeInputEnabled { get; set; }

        public bool SupportsImageInput { get; set; }

        public bool SupportsMixedContentParts { get; set; }

        public bool SupportsReferenceToolFetch { get; set; }

        public int MaxInlineReferenceChars { get; set; }

        public int MaxNativeInputFileCount { get; set; }

        public ISet<string> SupportedInputFileTypes { get; private set; }

        public bool SupportsNativeInputForExtension(string extension)
        {
            if (!NativeInputEnabled)
            {
                return false;
            }

            var normalizedExtension = ModelProviderSettingsExtensions.NormalizeFileExtension(extension);
            return !string.IsNullOrWhiteSpace(normalizedExtension)
                && SupportedInputFileTypes.Contains(normalizedExtension);
        }
    }

    public static class ModelProviderSettingsExtensions
    {
        private static readonly string[] StreamKeys =
        {
            ModelProviderAdditionalParameterNames.Stream,
            "stream"
        };

        private static readonly string[] TemperatureKeys =
        {
            ModelProviderAdditionalParameterNames.Temperature,
            "temperature"
        };

        private static readonly string[] TopPKeys =
        {
            ModelProviderAdditionalParameterNames.TopP,
            "top_p",
            "topP"
        };

        private static readonly string[] MaxTokensKeys =
        {
            ModelProviderAdditionalParameterNames.MaxTokens,
            "max_tokens",
            "maxTokens"
        };

        private static readonly string[] SupportsMixedContentPartsKeys =
        {
            ModelProviderAdditionalParameterNames.SupportsMixedContentParts,
            "supports_mixed_content_parts",
            "supportsMixedContentParts"
        };

        private static readonly string[] SupportsReferenceToolFetchKeys =
        {
            ModelProviderAdditionalParameterNames.SupportsReferenceToolFetch,
            "supports_reference_tool_fetch",
            "supportsReferenceToolFetch"
        };

        private static readonly string[] MaxInlineReferenceCharsKeys =
        {
            ModelProviderAdditionalParameterNames.MaxInlineReferenceChars,
            "max_inline_reference_chars",
            "maxInlineReferenceChars"
        };

        private static readonly string[] MaxNativeInputFileCountKeys =
        {
            ModelProviderAdditionalParameterNames.MaxNativeInputFileCount,
            "max_native_input_file_count",
            "maxNativeInputFileCount"
        };

        public static IDictionary<string, object> EnsureAdditionalParameters(this ModelProviderSettings model)
        {
            if (model == null)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            if (model.AdditionalParameters == null)
            {
                model.AdditionalParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            return model.AdditionalParameters;
        }

        public static bool GetStream(this ModelProviderSettings model)
        {
            bool result;
            return TryReadBoolean(model == null ? null : model.AdditionalParameters, StreamKeys, out result)
                ? result
                : true;
        }

        public static void SetStream(this ModelProviderSettings model, bool value)
        {
            SetValue(model, ModelProviderAdditionalParameterNames.Stream, value);
        }

        public static double GetTemperature(this ModelProviderSettings model)
        {
            double result;
            return TryReadDouble(model == null ? null : model.AdditionalParameters, TemperatureKeys, out result)
                ? result
                : 0.7d;
        }

        public static void SetTemperature(this ModelProviderSettings model, double value)
        {
            SetValue(model, ModelProviderAdditionalParameterNames.Temperature, value);
        }

        public static double GetTopP(this ModelProviderSettings model)
        {
            double result;
            return TryReadDouble(model == null ? null : model.AdditionalParameters, TopPKeys, out result)
                ? result
                : 1.0d;
        }

        public static void SetTopP(this ModelProviderSettings model, double value)
        {
            SetValue(model, ModelProviderAdditionalParameterNames.TopP, value);
        }

        public static int GetMaxTokens(this ModelProviderSettings model)
        {
            int result;
            return TryReadInt(model == null ? null : model.AdditionalParameters, MaxTokensKeys, out result) && result > 0
                ? result
                : 8192;
        }

        public static void SetMaxTokens(this ModelProviderSettings model, int value)
        {
            SetValue(model, ModelProviderAdditionalParameterNames.MaxTokens, value);
        }

        public static bool GetSupportsMixedContentParts(this ModelProviderSettings model)
        {
            bool result;
            return TryReadBoolean(model == null ? null : model.AdditionalParameters, SupportsMixedContentPartsKeys, out result)
                ? result
                : model != null && model.SupportsImageInput;
        }

        public static bool GetSupportsReferenceToolFetch(this ModelProviderSettings model)
        {
            bool result;
            return TryReadBoolean(model == null ? null : model.AdditionalParameters, SupportsReferenceToolFetchKeys, out result)
                ? result
                : true;
        }

        public static int GetMaxInlineReferenceChars(this ModelProviderSettings model)
        {
            int result;
            return TryReadInt(model == null ? null : model.AdditionalParameters, MaxInlineReferenceCharsKeys, out result) && result > 0
                ? result
                : 12000;
        }

        public static int GetMaxNativeInputFileCount(this ModelProviderSettings model)
        {
            int result;
            return TryReadInt(model == null ? null : model.AdditionalParameters, MaxNativeInputFileCountKeys, out result) && result > 0
                ? result
                : 4;
        }

        public static ISet<string> GetSupportedInputFileTypeSet(this ModelProviderSettings model)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rawValue = model == null ? string.Empty : model.SupportedInputFileTypes ?? string.Empty;

            foreach (var token in rawValue.Split(new[] { ',', ';', '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = NormalizeFileExtension(token);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    result.Add(normalized);
                }
            }

            return result;
        }

        public static bool SupportsNativeInputForExtension(this ModelProviderSettings model, string extension)
        {
            if (model == null || !model.SupportsImageInput)
            {
                return false;
            }

            var normalized = NormalizeFileExtension(extension);
            return !string.IsNullOrWhiteSpace(normalized)
                && model.GetSupportedInputFileTypeSet().Contains(normalized);
        }

        public static bool SupportsNativeInputForFile(this ModelProviderSettings model, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            return SupportsNativeInputForExtension(model, Path.GetExtension(fileName));
        }

        public static ModelInputCapabilitySnapshot GetInputCapabilitySnapshot(this ModelProviderSettings model)
        {
            var snapshot = new ModelInputCapabilitySnapshot
            {
                NativeInputEnabled = model != null && model.SupportsImageInput,
                SupportsImageInput = model != null && model.SupportsImageInput,
                SupportsMixedContentParts = GetSupportsMixedContentParts(model),
                SupportsReferenceToolFetch = GetSupportsReferenceToolFetch(model),
                MaxInlineReferenceChars = GetMaxInlineReferenceChars(model),
                MaxNativeInputFileCount = GetMaxNativeInputFileCount(model)
            };

            foreach (var extension in GetSupportedInputFileTypeSet(model))
            {
                snapshot.SupportedInputFileTypes.Add(extension);
            }

            return snapshot;
        }

        public static string NormalizeFileExtension(string extension)
        {
            var value = (extension ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (!value.StartsWith(".", StringComparison.Ordinal))
            {
                value = "." + value;
            }

            return value.ToLowerInvariant();
        }

        private static void SetValue(ModelProviderSettings model, string key, object value)
        {
            if (model == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            model.EnsureAdditionalParameters()[key] = value;
        }

        private static bool TryReadDouble(IDictionary<string, object> values, IEnumerable<string> keys, out double result)
        {
            object value;
            if (!TryReadValue(values, keys, out value))
            {
                result = 0d;
                return false;
            }

            if (value is double)
            {
                result = (double)value;
                return true;
            }

            if (value is float || value is decimal || value is int || value is long || value is short || value is byte)
            {
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }

            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryReadInt(IDictionary<string, object> values, IEnumerable<string> keys, out int result)
        {
            object value;
            if (!TryReadValue(values, keys, out value))
            {
                result = 0;
                return false;
            }

            if (value is int)
            {
                result = (int)value;
                return true;
            }

            if (value is long || value is short || value is byte)
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }

            return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryReadBoolean(IDictionary<string, object> values, IEnumerable<string> keys, out bool result)
        {
            object value;
            if (!TryReadValue(values, keys, out value))
            {
                result = false;
                return false;
            }

            if (value is bool)
            {
                result = (bool)value;
                return true;
            }

            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result);
        }

        private static bool TryReadValue(IDictionary<string, object> values, IEnumerable<string> keys, out object value)
        {
            value = null;
            if (values == null || keys == null)
            {
                return false;
            }

            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (values.TryGetValue(key, out value))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class McpServerSettings
    {
        public McpServerSettings()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = string.Empty;
            Description = string.Empty;
            IsEnabled = true;
            Tools = new List<McpToolSettings>();
        }

        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public bool IsEnabled { get; set; }

        public StdioMcpServerSettings Stdio { get; set; }

        public HttpMcpServerSettings Http { get; set; }

        public List<McpToolSettings> Tools { get; set; }

        public McpServerType McpType
        {
            get { return Http == null ? McpServerType.Stdio : McpServerType.Http; }
        }

        public bool IsStdio
        {
            get { return Stdio != null; }
        }

        public bool IsHttp
        {
            get { return Http != null; }
        }

        public void UseStdio()
        {
            if (Stdio == null)
            {
                Stdio = new StdioMcpServerSettings();
            }

            Http = null;
        }

        public void UseHttp()
        {
            if (Http == null)
            {
                Http = new HttpMcpServerSettings();
            }

            Stdio = null;
        }
    }

    public sealed class StdioMcpServerSettings
    {
        public StdioMcpServerSettings()
        {
            Command = string.Empty;
            Arguments = new List<string>();
            EnvironmentVariables = new List<NameValueItem>();
            WorkingDirectory = string.Empty;
        }

        public string Command { get; set; }

        public List<string> Arguments { get; set; }

        public string WorkingDirectory { get; set; }

        public List<NameValueItem> EnvironmentVariables { get; set; }
    }

    public sealed class HttpMcpServerSettings
    {
        public HttpMcpServerSettings()
        {
            TransportMode = HttpTransportMode.AutoDetect;
            Endpoint = string.Empty;
            Headers = new List<NameValueItem>();
        }

        public HttpTransportMode TransportMode { get; set; }

        public string Endpoint { get; set; }

        public List<NameValueItem> Headers { get; set; }
    }

    public sealed class NameValueItem
    {
        public string Name { get; set; }

        public string Value { get; set; }
    }

    public sealed class McpToolSettings
    {
        public McpToolSettings()
        {
            Name = string.Empty;
            DisplayName = string.Empty;
            Description = string.Empty;
            InputSchemaJson = "{}";
            OutputSchemaJson = string.Empty;
            SourceName = string.Empty;
            Metadata = new List<NameValueItem>();
            IsEnabled = true;
        }

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public string SourceName { get; set; }

        public string InputSchemaJson { get; set; }

        public string OutputSchemaJson { get; set; }

        public bool IsReadOnly { get; set; }

        public bool IsEnabled { get; set; }

        public List<NameValueItem> Metadata { get; set; }
    }

    public sealed class ConversationSession
    {
        public ConversationSession()
        {
            SessionId = Guid.NewGuid().ToString("N");
            CreatedAtUtc = DateTime.UtcNow;
            UpdatedAtUtc = DateTime.UtcNow;
            Messages = new List<ConversationMessage>();
        }

        public string SessionId { get; set; }

        public string HostApplication { get; set; }

        public string DocumentKey { get; set; }

        public string DocumentName { get; set; }

        public string Title { get; set; }

        public string ModelProviderId { get; set; }

        public string Summary { get; set; }

        public bool IsPinned { get; set; }

        public bool IsArchived { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public List<ConversationMessage> Messages { get; set; }
    }

    public sealed class ConversationMessage
    {
        public ConversationMessage()
        {
            MessageId = Guid.NewGuid().ToString("N");
            CreatedAtUtc = DateTime.UtcNow;
        }

        public string MessageId { get; set; }

        public ConversationMessageRole Role { get; set; }

        public string Content { get; set; }

        public string Name { get; set; }

        public string ToolCallId { get; set; }

        public string MetadataJson { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class ConversationIndexItem
    {
        public string SessionId { get; set; }

        public string HostApplication { get; set; }

        public string DocumentKey { get; set; }

        public string DocumentName { get; set; }

        public string Title { get; set; }

        public string LastMessagePreview { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public bool IsPinned { get; set; }

        public bool IsArchived { get; set; }
    }

    public sealed class SettingsValidationResult
    {
        public SettingsValidationResult()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public List<string> Errors { get; private set; }

        public List<string> Warnings { get; private set; }

        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }

    public interface ISettingsStore
    {
        OfficeCopilotSettings Load();

        void Save(OfficeCopilotSettings settings);
    }

    public interface IConversationStore
    {
        ConversationSession LoadBySessionId(string sessionId);

        ConversationSession LoadLatest(string hostApplication, string documentKey);

        IReadOnlyList<ConversationIndexItem> ListByDocument(string hostApplication, string documentKey);

        void Save(ConversationSession session);

        void Delete(string sessionId);
    }

    /// <summary>
    /// 运行时参数设置。对应 AgentRuntimeOptions，持久化到 settings.json 的 Runtime 节点。
    /// </summary>
    public sealed class RuntimeSettings
    {
        public RuntimeSettings()
        {
            MaxToolRoundCount = 300;
            EnableSkillPlanning = true;
            DefaultSkillName = "read-analysis";
            SkillPlannerMode = "keyword";
            StorageMode = ConversationStorageMode.Jsonl;
            JsonlRootDirectory = string.Empty;
            ContextWindowSize = 200_000;
            AutoCompactEnabled = true;
            MicroCompactEnabled = true;
            MicroCompactCharThreshold = 50_000;
            TokenBudgetStopThreshold = 0.85;
            TokenBudgetSmallDeltaTokenThreshold = 500;
            TokenBudgetMaxConsecutiveSmallDeltas = 100;
            MaxConsecutiveTurnFailures = 3;
            BrokerReadyTimeoutSeconds = 5;
        }

        /// <summary>每轮最大工具调用次数。默认 10。</summary>
        public int MaxToolRoundCount { get; set; }

        /// <summary>是否启用 Skill 规划。默认 true。</summary>
        public bool EnableSkillPlanning { get; set; }

        /// <summary>默认 Skill 名称。默认 "read-analysis"。</summary>
        public string DefaultSkillName { get; set; }

        /// <summary>Skill 规划策略。默认 "keyword"。</summary>
        public string SkillPlannerMode { get; set; }

        /// <summary>会话存储模式。默认 InMemory。</summary>
        public ConversationStorageMode StorageMode { get; set; }

        /// <summary>JSONL 存储根目录。仅 StorageMode=Jsonl 时使用。</summary>
        public string JsonlRootDirectory { get; set; }

        /// <summary>模型上下文窗口大小（token）。默认 200,000。</summary>
        public int ContextWindowSize { get; set; }

        /// <summary>是否启用自动上下文压缩。默认 true。</summary>
        public bool AutoCompactEnabled { get; set; }

        /// <summary>是否启用 Micro 压缩。默认 true。</summary>
        public bool MicroCompactEnabled { get; set; }

        /// <summary>Micro 压缩触发阈值（字符数）。默认 50,000。</summary>
        public int MicroCompactCharThreshold { get; set; }

        /// <summary>上下文占用达到窗口的此比例时停止生成。默认 0.85。</summary>
        public double TokenBudgetStopThreshold { get; set; }

        /// <summary>小于此值的 text delta 视为「边际收益递减」。默认 500。</summary>
        public int TokenBudgetSmallDeltaTokenThreshold { get; set; }

        /// <summary>连续小 delta 次数上限。默认 100（SSE 逐字返回时阈值太低会误截断）。</summary>
        public int TokenBudgetMaxConsecutiveSmallDeltas { get; set; }

        /// <summary>单轮内连续模型调用失败多少次后放弃。默认 3。</summary>
        public int MaxConsecutiveTurnFailures { get; set; }

        /// <summary>Panel 启动 Broker 后等待就绪的超时（秒）。默认 5。</summary>
        public int BrokerReadyTimeoutSeconds { get; set; }
    }
}