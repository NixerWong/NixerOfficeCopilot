using OfficeCopilot.Diagnostics;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OfficeCopilot.Agent.Broker
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // 日志系统初始化之前就写入启动标记：没有这条记录说明进程根本没有进入 Main（依赖缺失、位数不匹配等）。
            WriteStartupMarker("main_entered", "Args: " + string.Join(" ", args ?? Array.Empty<string>()));
            try
            {
                var exitCode = RunBroker(args);
                WriteStartupMarker("main_exiting", "ExitCode=" + exitCode);
                return exitCode;
            }
            catch (Exception ex)
            {
                WriteCrashLog("MainException", ex.ToString());
                WriteStartupMarker("main_exception", ex.GetType().FullName + ": " + ex.Message);
                return 1;
            }
        }

        private static int RunBroker(string[] args)
        {
            int workerExitCode;
            if (LegacyOfficeNormalizationWorker.TryRun(args, out workerExitCode))
            {
                return workerExitCode;
            }

            // === 初始化日志系统（必须在一切操作之前）===
            BrokerRuntimeLogger.Initialize();
            BrokerRuntimeLogger.Info("Program", "Broker process starting. Args: " + string.Join(" ", args ?? Array.Empty<string>()));

            // === 防崩溃体系: 全局异常守护 ===
            // 1. 捕获所有未处理的异常，记录日志后确保进程干净退出
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                var message = ex?.ToString() ?? (e.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
                BrokerRuntimeLogger.Error("AppDomain", "UnhandledException: " + message);
                WriteCrashLog("UnhandledException", message);
            };

            // 2. 观察 fire-and-forget Task 中的未处理异常，防止 .NET 4.0 风格进程崩溃
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                BrokerRuntimeLogger.Error("TaskScheduler", "UnobservedTaskException: " + (e.Exception?.ToString() ?? "Unknown"));
                WriteCrashLog("UnobservedTaskException", e.Exception?.ToString() ?? "Unknown");
                // 标记为已观察，防止进程崩溃
                e.SetObserved();
            };

            // === 防崩溃体系: 进程心跳 ===
            // 后台定时记录心跳日志，用于诊断进程是否存活、是否假死。
            var heartbeatCts = new CancellationTokenSource();
            var heartbeatTask = StartHeartbeatAsync(heartbeatCts.Token);

            int exitCode;
            try
            {
                var application = new BrokerApplication(args);
                exitCode = application.RunAsync(args, CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                heartbeatCts.Cancel();
                try { heartbeatTask.GetAwaiter().GetResult(); } catch { }
                heartbeatCts.Dispose();
            }

            BrokerRuntimeLogger.Info("Program", $"Broker process exiting. ExitCode={exitCode}");
            return exitCode;
        }

        /// <summary>
        /// 进程心跳：每 30 秒写一条 INFO 日志，用于诊断进程是否假死。
        /// 如果超过 90 秒没有心跳日志，则说明进程已挂。
        /// </summary>
        private static async Task StartHeartbeatAsync(CancellationToken cancellationToken)
        {
            var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            var startTime = DateTime.UtcNow;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var uptime = DateTime.UtcNow - startTime;
                var memoryMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                BrokerRuntimeLogger.Info("Heartbeat",
                    $"Process alive. Pid={processId}, Uptime={uptime.TotalMinutes:F1}min, WorkingSet={memoryMb}MB");
            }
        }

        /// <summary>
        /// 将启动阶段标记追加到 logs/broker-startup.log。不依赖日志库，任何失败都静默忽略。
        /// </summary>
        private static void WriteStartupMarker(string stage, string detail)
        {
            try
            {
                var logDir = OfficeCopilotDiagnostics.GetDefaultLogsFolder();
                Directory.CreateDirectory(logDir);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} pid={System.Diagnostics.Process.GetCurrentProcess().Id} [{stage}] {detail}{Environment.NewLine}";
                File.AppendAllText(Path.Combine(logDir, "broker-startup.log"), line, Encoding.UTF8);
            }
            catch
            {
                // 启动标记本身不能影响 Broker 启动
            }
        }

        /// <summary>
        /// 将崩溃日志写入 %LOCALAPPDATA%/NixerOfficeCopilot/logs/broker-crash.log，
        /// 与应用日志同目录；同时输出到 stderr 以便进程启动方也能捕获。
        /// </summary>
        private static void WriteCrashLog(string category, string message)
        {
            try
            {
                var logLine = $"{DateTime.UtcNow:O} [{category}] {message}{Environment.NewLine}";
                Console.Error.Write(logLine);

                var logDir = OfficeCopilotDiagnostics.GetDefaultLogsFolder();
                Directory.CreateDirectory(logDir);
                File.AppendAllText(
                    Path.Combine(logDir, "broker-crash.log"),
                    logLine,
                    System.Text.Encoding.UTF8);
            }
            catch
            {
                // 崩溃日志本身不能引发二次崩溃
            }
        }
    }
}
