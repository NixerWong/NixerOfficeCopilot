using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace OfficeCopilot.Documents.Markdown
{
    public sealed class LegacyOfficeFormatNormalizer
    {
        private const int WorkerTimeoutMilliseconds = 120000;

        public bool CanNormalize(string extension)
        {
            return string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ppt", StringComparison.OrdinalIgnoreCase);
        }

        public string GetTargetExtension(string extension)
        {
            if (string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase))
            {
                return ".docx";
            }
            if (string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
            {
                return ".xlsx";
            }
            if (string.Equals(extension, ".ppt", StringComparison.OrdinalIgnoreCase))
            {
                return ".pptx";
            }

            throw new NotSupportedException("The legacy Office format is not supported: " + extension);
        }

        public void Normalize(string sourcePath, string outputPath)
        {
            Normalize(sourcePath, outputPath, CancellationToken.None);
        }

        /// <summary>
        /// Returns a normalized OpenXML copy keyed by the source content hash, so the Markdown conversion,
        /// visual rendering and page-image tool calls share one Office COM conversion per file version.
        /// The returned file belongs to the cache and must not be deleted by callers.
        /// </summary>
        public string GetOrCreateNormalizedCopy(string sourcePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The legacy Office file does not exist.", sourcePath);
            }

            var targetExtension = GetTargetExtension(Path.GetExtension(sourcePath));
            var cacheDirectory = Path.Combine(Path.GetTempPath(), "OfficeCopilot", "LegacyNormalizedCache");
            Directory.CreateDirectory(cacheDirectory);
            var cachedPath = Path.Combine(cacheDirectory, ComputeSha256(sourcePath) + targetExtension);

            lock (NormalizedCacheLock)
            {
                if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 0)
                {
                    TryTouch(cachedPath);
                    return cachedPath;
                }
            }

            var workDirectory = Path.Combine(cacheDirectory, "work-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);
            try
            {
                var workPath = Path.Combine(workDirectory, Path.GetFileNameWithoutExtension(sourcePath) + targetExtension);
                Normalize(sourcePath, workPath, cancellationToken);
                lock (NormalizedCacheLock)
                {
                    if (!File.Exists(cachedPath))
                    {
                        File.Move(workPath, cachedPath);
                    }
                    TryTouch(cachedPath);
                    PruneNormalizedCache(cacheDirectory, cachedPath);
                }
                return cachedPath;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(workDirectory))
                    {
                        Directory.Delete(workDirectory, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static readonly object NormalizedCacheLock = new object();
        private const int MaxNormalizedCacheFiles = 16;
        private static readonly TimeSpan NormalizedCacheLifetime = TimeSpan.FromHours(24);

        private static string ComputeSha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static void TryTouch(string path)
        {
            try
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch
            {
            }
        }

        private static void PruneNormalizedCache(string cacheDirectory, string keepPath)
        {
            try
            {
                var files = new DirectoryInfo(cacheDirectory).GetFiles()
                    .Where(file => !file.Name.EndsWith(".office-owner", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .ToList();
                var expiry = DateTime.UtcNow - NormalizedCacheLifetime;
                for (var index = 0; index < files.Count; index++)
                {
                    var file = files[index];
                    if (string.Equals(file.FullName, keepPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (index >= MaxNormalizedCacheFiles || file.LastWriteTimeUtc < expiry)
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public void Normalize(string sourcePath, string outputPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The legacy Office file does not exist.", sourcePath);
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }

            PrepareOwnerState(outputPath);

            var workerPath = FindWorkerExecutablePath();
            var startInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                Arguments = "--normalize-office \"" + sourcePath + "\" \"" + outputPath
                    + "\" --parentPid " + Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                WorkingDirectory = Path.GetDirectoryName(workerPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var worker = Process.Start(startInfo))
            {
                if (worker == null)
                {
                    throw new InvalidOperationException("The Office normalization worker could not be started.");
                }

                var standardOutput = worker.StandardOutput.ReadToEndAsync();
                var standardError = worker.StandardError.ReadToEndAsync();
                var stopwatch = Stopwatch.StartNew();
                while (!worker.WaitForExit(200))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TerminateWorker(worker);
                        TryTerminateOwnedOfficeProcess(outputPath, worker.Id);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (stopwatch.ElapsedMilliseconds >= WorkerTimeoutMilliseconds)
                    {
                        TerminateWorker(worker);
                        TryTerminateOwnedOfficeProcess(outputPath, worker.Id);
                        throw new TimeoutException("Office normalization exceeded "
                            + (WorkerTimeoutMilliseconds / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + " seconds. The isolated worker was terminated; a separate Office process may require recovery.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                Task.WaitAll(standardOutput, standardError);
                if (worker.ExitCode != 0)
                {
                    TryTerminateOwnedOfficeProcess(outputPath, worker.Id);
                    var error = (standardError.Result ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(error))
                    {
                        error = (standardOutput.Result ?? string.Empty).Trim();
                    }
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? "The Office normalization worker failed with exit code " + worker.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                        : error);
                }

                TryTerminateOwnedOfficeProcess(outputPath, worker.Id);
            }

            if (!File.Exists(outputPath))
            {
                throw new IOException("Office did not create the normalized file.");
            }
        }

        private static void TerminateWorker(Process worker)
        {
            if (worker == null)
            {
                return;
            }

            try
            {
                if (!worker.HasExited)
                {
                    worker.Kill();
                    worker.WaitForExit(5000);
                }
            }
            catch
            {
            }
        }

        public void NormalizeInProcess(string sourcePath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The legacy Office file does not exist.", sourcePath);
            }
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("An output path is required.", nameof(outputPath));
            }
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                throw new InvalidOperationException("Legacy Office conversion must run on an STA thread.");
            }

            var targetExtension = GetTargetExtension(Path.GetExtension(sourcePath));
            if (!string.Equals(Path.GetExtension(outputPath), targetExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The output extension must be " + targetExtension + ".", nameof(outputPath));
            }

            var ownerStatePath = GetOwnerStatePath(outputPath);
            try
            {
                var extension = Path.GetExtension(sourcePath);
                if (string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase))
                {
                    ConvertWordDocument(sourcePath, outputPath, ownerStatePath);
                }
                else if (string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
                {
                    ConvertExcelWorkbook(sourcePath, outputPath, ownerStatePath);
                }
                else
                {
                    ConvertPowerPointPresentation(sourcePath, outputPath, ownerStatePath);
                }

                if (!File.Exists(outputPath))
                {
                    throw new IOException("Office did not create the normalized file.");
                }
            }
            finally
            {
                TryTerminateOwnedOfficeProcess(outputPath, Process.GetCurrentProcess().Id);
            }
        }

        public static bool TryTerminateOwnedOfficeProcess(string outputPath, int expectedOwnerProcessId)
        {
            if (string.IsNullOrWhiteSpace(outputPath) || expectedOwnerProcessId <= 0)
            {
                return false;
            }

            var statePath = GetOwnerStatePath(outputPath);
            if (!File.Exists(statePath))
            {
                return false;
            }

            try
            {
                var state = XElement.Load(statePath);
                var ownerPid = (int?)state.Attribute("ownerPid") ?? 0;
                var officePid = (int?)state.Attribute("processId") ?? 0;
                var startTimeTicks = (long?)state.Attribute("startTimeUtcTicks") ?? 0;
                var processName = (string)state.Attribute("processName") ?? string.Empty;
                if (ownerPid != expectedOwnerProcessId
                    || officePid <= 0
                    || startTimeTicks <= 0
                    || !IsOfficeProcessName(processName))
                {
                    return false;
                }

                using (var officeProcess = Process.GetProcessById(officePid))
                {
                    if (officeProcess.HasExited)
                    {
                        DeleteOwnerState(outputPath);
                        return false;
                    }

                    if (officeProcess.StartTime.ToUniversalTime().Ticks != startTimeTicks
                        || !string.Equals(officeProcess.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    officeProcess.Kill();
                    officeProcess.WaitForExit(5000);
                    if (officeProcess.HasExited)
                    {
                        DeleteOwnerState(outputPath);
                        return true;
                    }
                }
            }
            catch (ArgumentException)
            {
                DeleteOwnerState(outputPath);
            }
            catch
            {
            }

            return false;
        }

        private static string FindWorkerExecutablePath()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDirectory, "OfficeCopilot.Agent.Broker.exe"),
                Path.Combine(baseDirectory, "Broker", "OfficeCopilot.Agent.Broker.exe")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            var normalizedBaseDirectory = baseDirectory.Replace('/', '\\');
            var primaryConfiguration = normalizedBaseDirectory.IndexOf("\\Release\\", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Release"
                : "Debug";
            var configurations = new[] { primaryConfiguration, primaryConfiguration == "Debug" ? "Release" : "Debug" };
            var current = new DirectoryInfo(baseDirectory);
            for (var depth = 0; current != null && depth < 8; depth++, current = current.Parent)
            {
                foreach (var configuration in configurations)
                {
                    var candidate = Path.Combine(
                        current.FullName,
                        "OfficeCopilot.Agent.Broker",
                        "bin",
                        configuration,
                        "net472",
                        "OfficeCopilot.Agent.Broker.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            throw new FileNotFoundException("OfficeCopilot.Agent.Broker.exe was not found. Build or deploy the Broker beside the add-in.");
        }

        private static void ConvertWordDocument(string sourcePath, string outputPath, string ownerStatePath)
        {
            object applicationObject = null;
            object documentsObject = null;
            object documentObject = null;
            try
            {
                applicationObject = CreateApplication("Word.Application", ownerStatePath);
                dynamic application = applicationObject;
                application.Visible = false;
                application.DisplayAlerts = 0;
                application.AutomationSecurity = 3;
                documentsObject = application.Documents;
                dynamic documents = documentsObject;
                documentObject = documents.Open(sourcePath, false, true, false, Type.Missing, Type.Missing, false,
                    Type.Missing, Type.Missing, Type.Missing, Type.Missing, false, true, Type.Missing, true, Type.Missing);
                dynamic document = documentObject;
                document.SaveAs2(outputPath, 16);
            }
            finally
            {
                TryInvokeClose(documentObject, "Close", 0);
                ReleaseComObject(documentObject);
                ReleaseComObject(documentsObject);
                TryInvokeClose(applicationObject, "Quit", 0);
                ReleaseComObject(applicationObject);
            }
        }

        private static void ConvertExcelWorkbook(string sourcePath, string outputPath, string ownerStatePath)
        {
            object applicationObject = null;
            object workbooksObject = null;
            object workbookObject = null;
            try
            {
                applicationObject = CreateApplication("Excel.Application", ownerStatePath);
                dynamic application = applicationObject;
                application.Visible = false;
                application.DisplayAlerts = false;
                application.AskToUpdateLinks = false;
                application.AutomationSecurity = 3;
                workbooksObject = application.Workbooks;
                dynamic workbooks = workbooksObject;
                workbookObject = workbooks.Open(sourcePath, 0, true, Type.Missing, Type.Missing, Type.Missing,
                    true, Type.Missing, Type.Missing, false, false, Type.Missing, false, true, Type.Missing);
                dynamic workbook = workbookObject;
                workbook.SaveAs(outputPath, 51);
            }
            finally
            {
                TryInvokeClose(workbookObject, "Close", false);
                ReleaseComObject(workbookObject);
                ReleaseComObject(workbooksObject);
                TryInvokeClose(applicationObject, "Quit");
                ReleaseComObject(applicationObject);
            }
        }

        private static void ConvertPowerPointPresentation(string sourcePath, string outputPath, string ownerStatePath)
        {
            object applicationObject = null;
            object presentationsObject = null;
            object presentationObject = null;
            try
            {
                applicationObject = CreateApplication("PowerPoint.Application", ownerStatePath);
                dynamic application = applicationObject;
                application.Visible = 0;
                application.DisplayAlerts = 1;
                application.AutomationSecurity = 3;
                presentationsObject = application.Presentations;
                dynamic presentations = presentationsObject;
                presentationObject = presentations.Open(sourcePath, true, false, false);
                dynamic presentation = presentationObject;
                presentation.SaveAs(outputPath, 24);
            }
            finally
            {
                TryInvokeClose(presentationObject, "Close");
                ReleaseComObject(presentationObject);
                ReleaseComObject(presentationsObject);
                TryInvokeClose(applicationObject, "Quit");
                ReleaseComObject(applicationObject);
            }
        }

        private static object CreateApplication(string progId, string ownerStatePath)
        {
            var processName = GetOfficeProcessName(progId);
            var preexistingProcessIds = GetProcessIds(processName);
            var type = Type.GetTypeFromProgID(progId, false);
            if (type == null)
            {
                throw new NotSupportedException("The required Office application is not installed: " + progId);
            }

            var application = Activator.CreateInstance(type);
            try
            {
                var hwndValue = application.GetType().InvokeMember(
                    "Hwnd",
                    System.Reflection.BindingFlags.GetProperty,
                    null,
                    application,
                    null);
                var hwnd = new IntPtr(Convert.ToInt64(hwndValue, System.Globalization.CultureInfo.InvariantCulture));
                uint processId;
                GetWindowThreadProcessId(hwnd, out processId);
                if (processId > 0 && !preexistingProcessIds.Contains((int)processId))
                {
                    using (var officeProcess = Process.GetProcessById((int)processId))
                    {
                        WriteOwnerState(ownerStatePath, Process.GetCurrentProcess().Id,
                            officeProcess.Id, officeProcess.StartTime.ToUniversalTime().Ticks, officeProcess.ProcessName);
                    }
                }
            }
            catch
            {
                // If ownership cannot be proved, never register the process for termination.
            }

            return application;
        }

        private static HashSet<int> GetProcessIds(string processName)
        {
            var processIds = new HashSet<int>();
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    processIds.Add(process.Id);
                }
            }
            return processIds;
        }

        private static string GetOfficeProcessName(string progId)
        {
            if (string.Equals(progId, "Word.Application", StringComparison.OrdinalIgnoreCase)) return "WINWORD";
            if (string.Equals(progId, "Excel.Application", StringComparison.OrdinalIgnoreCase)) return "EXCEL";
            if (string.Equals(progId, "PowerPoint.Application", StringComparison.OrdinalIgnoreCase)) return "POWERPNT";
            throw new NotSupportedException("The Office application is not supported: " + progId);
        }

        private static bool IsOfficeProcessName(string processName)
        {
            return string.Equals(processName, "WINWORD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "EXCEL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(processName, "POWERPNT", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteOwnerState(string statePath, int ownerPid, int officePid, long startTimeUtcTicks, string processName)
        {
            var temporaryPath = statePath + ".tmp";
            using (var ownerProcess = Process.GetCurrentProcess())
            {
                var state = new XElement("OfficeProcessOwner",
                    new XAttribute("ownerPid", ownerPid),
                    new XAttribute("ownerStartTimeUtcTicks", ownerProcess.StartTime.ToUniversalTime().Ticks),
                    new XAttribute("processId", officePid),
                    new XAttribute("startTimeUtcTicks", startTimeUtcTicks),
                    new XAttribute("processName", processName ?? string.Empty));
                File.WriteAllText(temporaryPath, state.ToString(SaveOptions.DisableFormatting), new UTF8Encoding(false));
                File.Move(temporaryPath, statePath);
            }
        }

        private static string GetOwnerStatePath(string outputPath)
        {
            return outputPath + ".office-owner";
        }

        private static void DeleteOwnerState(string outputPath)
        {
            try
            {
                var statePath = GetOwnerStatePath(outputPath);
                if (File.Exists(statePath)) File.Delete(statePath);
                if (File.Exists(statePath + ".tmp")) File.Delete(statePath + ".tmp");
            }
            catch
            {
            }
        }

        private static void PrepareOwnerState(string outputPath)
        {
            var statePath = GetOwnerStatePath(outputPath);
            if (!File.Exists(statePath))
            {
                DeleteOwnerState(outputPath);
                return;
            }

            int ownerPid;
            long ownerStartTimeUtcTicks;
            try
            {
                var state = XElement.Load(statePath);
                ownerPid = (int?)state.Attribute("ownerPid") ?? 0;
                ownerStartTimeUtcTicks = (long?)state.Attribute("ownerStartTimeUtcTicks") ?? 0;
            }
            catch
            {
                throw new IOException("An unreadable Office process ownership record exists; refusing to overwrite it.");
            }

            if (ownerPid <= 0 || ownerStartTimeUtcTicks <= 0)
            {
                throw new IOException("An incomplete Office process ownership record exists; refusing to overwrite it.");
            }

            try
            {
                using (var ownerProcess = Process.GetProcessById(ownerPid))
                {
                    if (!ownerProcess.HasExited
                        && ownerProcess.StartTime.ToUniversalTime().Ticks == ownerStartTimeUtcTicks)
                    {
                        throw new IOException("An Office normalization worker is already using this output path.");
                    }
                }
            }
            catch (ArgumentException)
            {
            }

            TryTerminateOwnedOfficeProcess(outputPath, ownerPid);
            if (File.Exists(statePath))
            {
                throw new IOException("A stale Office process could not be safely cleaned up; refusing to overwrite its ownership record.");
            }

            DeleteOwnerState(outputPath);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        private static void TryInvokeClose(object comObject, string methodName, params object[] arguments)
        {
            if (comObject == null)
            {
                return;
            }

            try
            {
                comObject.GetType().InvokeMember(methodName, System.Reflection.BindingFlags.InvokeMethod, null, comObject, arguments);
            }
            catch
            {
            }
        }

        private static void ReleaseComObject(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                Marshal.FinalReleaseComObject(comObject);
            }
        }
    }
}