using OfficeCopilot.Documents.Markdown;
using System;
using System.Threading;

namespace OfficeCopilot.Agent.Broker
{
    internal static class LegacyOfficeNormalizationWorker
    {
        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args == null || args.Length == 0 || !string.Equals(args[0], "--normalize-office", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int parentProcessId;
            if (args.Length != 5
                || !string.Equals(args[3], "--parentPid", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(args[4], out parentProcessId)
                || parentProcessId <= 0)
            {
                Console.Error.WriteLine("Usage: OfficeCopilot.Agent.Broker.exe --normalize-office <sourcePath> <outputPath> --parentPid <pid>");
                exitCode = 2;
                return true;
            }

            Exception failure = null;
            using (var conversionCompleted = new ManualResetEvent(false))
            {
                var parentMonitor = new Thread(() => MonitorParentProcess(parentProcessId, args[2], conversionCompleted))
                {
                    IsBackground = true,
                    Name = "OfficeCopilot normalization parent monitor"
                };
                parentMonitor.Start();

                var worker = new Thread(() =>
                {
                    try
                    {
                        new LegacyOfficeFormatNormalizer().NormalizeInProcess(args[1], args[2]);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        conversionCompleted.Set();
                    }
                })
                {
                    IsBackground = false,
                    Name = "OfficeCopilot legacy Office normalization"
                };

                try
                {
                    worker.SetApartmentState(ApartmentState.STA);
                    worker.Start();
                    worker.Join();
                }
                catch (Exception ex)
                {
                    failure = ex;
                    conversionCompleted.Set();
                }
                finally
                {
                    conversionCompleted.Set();
                    parentMonitor.Join(1000);
                }
            }

            if (failure != null)
            {
                Console.Error.WriteLine(failure.GetBaseException().ToString());
                exitCode = 1;
            }

            return true;
        }

        private static void MonitorParentProcess(int parentProcessId, string outputPath, WaitHandle conversionCompleted)
        {
            try
            {
                using (var parent = System.Diagnostics.Process.GetProcessById(parentProcessId))
                {
                    while (!conversionCompleted.WaitOne(250))
                    {
                        if (parent.HasExited)
                        {
                            LegacyOfficeFormatNormalizer.TryTerminateOwnedOfficeProcess(
                                outputPath,
                                System.Diagnostics.Process.GetCurrentProcess().Id);
                            TerminateCurrentProcess();
                            return;
                        }
                    }
                }
            }
            catch (ArgumentException)
            {
                if (!conversionCompleted.WaitOne(0))
                {
                    LegacyOfficeFormatNormalizer.TryTerminateOwnedOfficeProcess(
                        outputPath,
                        System.Diagnostics.Process.GetCurrentProcess().Id);
                    TerminateCurrentProcess();
                }
            }
            catch (InvalidOperationException)
            {
                if (!conversionCompleted.WaitOne(0))
                {
                    LegacyOfficeFormatNormalizer.TryTerminateOwnedOfficeProcess(
                        outputPath,
                        System.Diagnostics.Process.GetCurrentProcess().Id);
                    TerminateCurrentProcess();
                }
            }
        }

        private static void TerminateCurrentProcess()
        {
            try
            {
                using (var current = System.Diagnostics.Process.GetCurrentProcess())
                {
                    if (!current.HasExited)
                    {
                        current.Kill();
                    }
                }
            }
            catch
            {
            }
        }
    }
}