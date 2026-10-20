using Microsoft.Extensions.Logging;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Diagnostics.Logging;
using OfficeCopilot.Diagnostics.Transcripts;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.IO;
using DiagnosticsTranscriptLogMode = OfficeCopilot.Diagnostics.Transcripts.ModelTranscriptLogMode;
using SettingsTranscriptLogMode = OfficeCopilot.Settings.Abstractions.ModelTranscriptLogMode;

namespace OfficeCopilot.Diagnostics
{
    public static class OfficeCopilotDiagnostics
    {
        // 不替换全局 OfficeCopilotLog 工厂：全局由 InitializeDefault 管理，避免按用户级别覆盖后丢失 Information 标记。
        public static ILoggerFactory CreateLoggerFactory(OfficeCopilotLogOptions options)
        {
            var resolvedOptions = options ?? new OfficeCopilotLogOptions();
            var factory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(resolvedOptions.MinimumLevel);

                if (resolvedOptions.EnableDebugTrace)
                {
                    builder.AddProvider(new DebugTraceLoggerProvider(resolvedOptions));
                }

                if (resolvedOptions.EnableFile)
                {
                    builder.AddProvider(new FileLoggerProvider(resolvedOptions));
                }
            });

            return factory;
        }

        public static ILoggerFactory CreateLoggerFactory(LoggingSettings logging)
        {
            return CreateLoggerFactory(MapRuntimeOptions(logging == null ? null : logging.Runtime));
        }

        public static IModelTranscriptLogger CreateModelTranscriptLogger(
            string rootFolder,
            DiagnosticsTranscriptLogMode mode)
        {
            var folder = string.IsNullOrWhiteSpace(rootFolder)
                ? GetDefaultLogsFolder()
                : rootFolder;

            var filePath = Path.Combine(folder, "model-transcript.log");
            return new FileModelTranscriptLogger(filePath, mode);
        }

        public static IModelTranscriptLogger CreateModelTranscriptLogger(LoggingSettings logging)
        {
            var transcript = logging == null ? null : logging.ModelTranscripts;
            var folder = ResolveLogFolder(transcript == null ? null : transcript.RootFolder);
            var fileName = transcript == null || string.IsNullOrWhiteSpace(transcript.FileName)
                ? "model-transcript.log"
                : Path.GetFileName(transcript.FileName.Trim());

            var mode = MapTranscriptMode(transcript);
            var filePath = Path.Combine(folder, fileName);

            return new FileModelTranscriptLogger(filePath, mode);
        }

        /// <summary>
        /// Create an <see cref="IToolCallLogSink"/> backed by <paramref name="logger"/>
        /// and wire it into <see cref="ToolCallDiagnosticsLogger"/>.
        /// </summary>
        public static IToolCallLogSink CreateAndWireToolCallLogSink(ILogger logger, OfficeCopilotLogOptions options)
        {
            var sink = new ToolCallLogSink(logger, options ?? new OfficeCopilotLogOptions());
            ToolCallDiagnosticsLogger.SetSink(sink);
            return sink;
        }

        /// <summary>
        /// Create an <see cref="IToolCallLogSink"/> backed by <paramref name="logger"/>
        /// and wire it into <see cref="ToolCallDiagnosticsLogger"/>.
        /// </summary>
        public static IToolCallLogSink CreateAndWireToolCallLogSink(ILogger logger, LoggingSettings logging)
        {
            return CreateAndWireToolCallLogSink(logger, MapRuntimeOptions(logging?.Runtime));
        }

        private static OfficeCopilotLogOptions MapRuntimeOptions(RuntimeLoggingSettings runtime)
        {
            var options = new OfficeCopilotLogOptions();

            if (runtime == null)
            {
                return options;
            }

            options.IsEnabled = runtime.IsEnabled;
            options.MinimumLevel = MapLogLevel(runtime.MinimumLevel);
            options.EnableDebugTrace = runtime.EnableDebugTrace;
            options.EnableFile = runtime.EnableFile;
            options.IncludeScopes = runtime.IncludeScopes;
            options.RootFolder = ResolveLogFolder(runtime.RootFolder);
            options.ApplicationLogFileName = string.IsNullOrWhiteSpace(runtime.ApplicationLogFileName)
                ? "application.log"
                : Path.GetFileName(runtime.ApplicationLogFileName.Trim());
            options.MaxFileSizeBytes = Math.Max(1, runtime.MaxFileSizeMb) * 1024L * 1024L;
            options.RetainedFileCountLimit = Math.Max(1, runtime.RetainedFileCountLimit);

            return options;
        }

        private static LogLevel MapLogLevel(AppLogLevel level)
        {
            switch (level)
            {
                case AppLogLevel.Trace:
                    return LogLevel.Trace;
                case AppLogLevel.Debug:
                    return LogLevel.Debug;
                case AppLogLevel.Information:
                    return LogLevel.Information;
                case AppLogLevel.Warning:
                    return LogLevel.Warning;
                case AppLogLevel.Error:
                    return LogLevel.Error;
                case AppLogLevel.Critical:
                    return LogLevel.Critical;
                case AppLogLevel.None:
                    return LogLevel.None;
                default:
                    return LogLevel.Information;
            }
        }

        private static DiagnosticsTranscriptLogMode MapTranscriptMode(ModelTranscriptLoggingSettings transcript)
        {
            if (transcript == null || !transcript.IsEnabled)
            {
                return DiagnosticsTranscriptLogMode.Disabled;
            }

            switch (transcript.Mode)
            {
                case SettingsTranscriptLogMode.MetadataOnly:
                    return DiagnosticsTranscriptLogMode.MetadataOnly;
                case SettingsTranscriptLogMode.SanitizedContent:
                    return DiagnosticsTranscriptLogMode.SanitizedContent;
                case SettingsTranscriptLogMode.RawContent:
                    return DiagnosticsTranscriptLogMode.RawContent;
                case SettingsTranscriptLogMode.Disabled:
                default:
                    return DiagnosticsTranscriptLogMode.Disabled;
            }
        }

        private static string ResolveLogFolder(string rootFolder)
        {
            if (!string.IsNullOrWhiteSpace(rootFolder))
            {
                return rootFolder.Trim();
            }

            return GetDefaultLogsFolder();
        }

        public static string GetDefaultLogsFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "logs");
        }
    }
}