using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using OfficeCopilot.Host.Excel.Analysis;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel
{
    public static class ExcelStagedWorksheetProtection
    {
        private const string RecoveryDirectoryName = "StagedWorksheetProtection";

        public static IList<string> RecoverOpenWorkbooks(ExcelInterop.Application application)
        {
            var failures = new List<string>();
            if (application == null)
            {
                return failures;
            }

            object workbooks = null;
            try
            {
                workbooks = ((dynamic)application).Workbooks;
                var count = Convert.ToInt32(((dynamic)workbooks).Count);
                for (var index = 1; index <= count; index++)
                {
                    object workbookObject = null;
                    var workbookName = "unknown";
                    try
                    {
                        workbookObject = ((dynamic)workbooks).Item(index);
                        var workbook = (ExcelInterop.Workbook)workbookObject;
                        workbookName = Convert.ToString(((dynamic)workbook).Name) ?? workbookName;
                        foreach (var failure in RecoverWorkbook(workbook))
                        {
                            failures.Add(failure);
                        }
                    }
                    catch (Exception ex)
                    {
                        failures.Add("工作簿 \"" + workbookName + "\" 的中间表保护恢复失败：" + ex.Message);
                    }
                    finally
                    {
                        ReleaseComObject(workbookObject);
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add("无法检查当前 Excel 会话的中间表保护恢复记录：" + ex.Message);
            }
            finally
            {
                ReleaseComObject(workbooks);
            }

            return failures;
        }

        public static IList<string> RecoverWorkbook(ExcelInterop.Workbook workbook)
        {
            var failures = new List<string>();
            if (workbook == null)
            {
                return failures;
            }

            string workbookFullName;
            try
            {
                workbookFullName = TryGetWorkbookFullName(workbook);
                if (string.IsNullOrWhiteSpace(workbookFullName))
                {
                    return failures;
                }
            }
            catch (Exception ex)
            {
                failures.Add("无法读取工作簿路径，不能检查保护恢复记录：" + ex.Message);
                return failures;
            }

            string manifestPath;
            try
            {
                manifestPath = GetManifestPath(workbookFullName);
            }
            catch (Exception ex)
            {
                failures.Add("无法确定工作表保护恢复记录路径：" + ex.Message);
                return failures;
            }

            if (!File.Exists(manifestPath))
            {
                return failures;
            }

            RecoveryManifest manifest;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(RecoveryManifest));
                using (var stream = File.OpenRead(manifestPath))
                {
                    manifest = serializer.ReadObject(stream) as RecoveryManifest;
                }
            }
            catch (Exception ex)
            {
                failures.Add("保护恢复记录无法读取，文件已保留供后续重试："
                    + manifestPath + "；" + ex.Message);
                return failures;
            }

            var manifestMatchesWorkbook = false;
            try
            {
                manifestMatchesWorkbook = manifest != null
                    && !string.IsNullOrWhiteSpace(manifest.WorkbookFullName)
                    && string.Equals(
                        NormalizeWorkbookFullName(manifest.WorkbookFullName),
                        NormalizeWorkbookFullName(workbookFullName),
                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                manifestMatchesWorkbook = false;
            }

            if (!manifestMatchesWorkbook || manifest.WorksheetNames == null)
            {
                failures.Add("保护恢复记录与当前工作簿不匹配或内容无效，文件已保留："
                    + manifestPath);
                return failures;
            }

            failures.AddRange(RestoreWorksheetNames(workbook, manifest.WorksheetNames));
            if (failures.Count > 0)
            {
                return failures;
            }

            try
            {
                File.Delete(manifestPath);
            }
            catch (Exception ex)
            {
                failures.Add("工作表已尝试恢复，但无法删除恢复记录；下次启动会再次检查："
                    + manifestPath + "；" + ex.Message);
            }

            return failures;
        }

        internal static string GetStagedProtectionAvailabilityFailure(ExcelInterop.Workbook workbook)
        {
            var recoveryFailures = RecoverWorkbook(workbook);
            if (recoveryFailures.Count > 0)
            {
                return "上一次运行时保护尚未恢复：" + string.Join("；", recoveryFailures);
            }

            List<WorksheetState> states = null;
            try
            {
                states = ReadWorksheetStates(workbook, true);
                var needsJournal = false;
                foreach (var state in states)
                {
                    if (ExcelStagingConventions.IsWipSheet(state.Name))
                    {
                        continue;
                    }

                    if (!state.AllCellsLocked)
                    {
                        return "正式工作表 \"" + state.Name
                            + "\" 含未锁定或混合锁定的单元格，工作表保护无法阻止所有写入。"
                            + "请先锁定该表的全部单元格，或确认后执行。";
                    }

                    if (!state.ProtectContents)
                    {
                        needsJournal = true;
                    }
                }

                if (!needsJournal)
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                return "无法确认正式工作表的锁定状态，不能安全免确认：" + ex.Message;
            }
            finally
            {
                ReleaseWorksheetStates(states);
            }

            string workbookFullName;
            try
            {
                workbookFullName = GetSavedWorkbookFullName(workbook);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            try
            {
                VerifyRecoveryJournalWritable();
                GetManifestPath(workbookFullName);
            }
            catch (Exception ex)
            {
                return "无法创建崩溃恢复记录，不能安全免确认：" + ex.Message;
            }

            return null;
        }

        internal static string GetDeclaredProtectionAvailabilityFailure(
            ExcelInterop.Workbook workbook,
            IList<string> allowedWorksheetNames)
        {
            var recoveryFailures = RecoverWorkbook(workbook);
            if (recoveryFailures.Count > 0)
            {
                return "上一次运行时保护尚未恢复：" + string.Join("；", recoveryFailures);
            }

            var allowed = new HashSet<string>(
                allowedWorksheetNames ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            List<WorksheetState> states = null;
            try
            {
                states = ReadWorksheetStates(workbook, true);
                var needsJournal = false;
                foreach (var state in states)
                {
                    if (allowed.Contains(state.Name))
                    {
                        continue;
                    }

                    if (!state.AllCellsLocked)
                    {
                        return "未声明目标工作表 \"" + state.Name
                            + "\" 含未锁定或混合锁定的单元格，无法用工作表保护隔离 VBA 写入。"
                            + "请先锁定该表的全部单元格，或确认后执行。";
                    }

                    if (!state.ProtectContents)
                    {
                        needsJournal = true;
                    }
                }

                if (!needsJournal)
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                return "无法确认未声明目标工作表的锁定状态，不能安全启用 VBA 隔离：" + ex.Message;
            }
            finally
            {
                ReleaseWorksheetStates(states);
            }

            string workbookFullName;
            try
            {
                workbookFullName = GetSavedWorkbookFullName(workbook);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            try
            {
                VerifyRecoveryJournalWritable();
                GetManifestPath(workbookFullName);
            }
            catch (Exception ex)
            {
                return "无法创建崩溃恢复记录，不能安全启用 VBA 隔离：" + ex.Message;
            }

            return null;
        }

        internal static WorksheetProtectionScope ProtectFormalWorksheets(ExcelInterop.Workbook workbook)
        {
            return ProtectWorksheetsExceptCore(workbook, new List<string>(), true);
        }

        internal static WorksheetProtectionScope ProtectWorksheetsExcept(
            ExcelInterop.Workbook workbook,
            IList<string> allowedWorksheetNames)
        {
            return ProtectWorksheetsExceptCore(workbook, allowedWorksheetNames, false);
        }

        private static WorksheetProtectionScope ProtectWorksheetsExceptCore(
            ExcelInterop.Workbook workbook,
            IList<string> allowedWorksheetNames,
            bool skipWipWorksheets)
        {
            var availabilityFailure = skipWipWorksheets
                ? GetStagedProtectionAvailabilityFailure(workbook)
                : GetDeclaredProtectionAvailabilityFailure(workbook, allowedWorksheetNames);
            if (!string.IsNullOrWhiteSpace(availabilityFailure))
            {
                throw new ExcelVbaStagedProtectionUnavailableException(availabilityFailure);
            }

            var allowed = new HashSet<string>(
                allowedWorksheetNames ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            List<WorksheetState> states = null;
            var worksheetNames = new List<string>();
            try
            {
                states = ReadWorksheetStates(workbook, false);
                foreach (var state in states)
                {
                    if ((!skipWipWorksheets || !ExcelStagingConventions.IsWipSheet(state.Name))
                        && !allowed.Contains(state.Name)
                        && !state.ProtectContents)
                    {
                        worksheetNames.Add(state.Name);
                    }
                }
            }
            finally
            {
                ReleaseWorksheetStates(states);
            }

            if (worksheetNames.Count == 0)
            {
                return new WorksheetProtectionScope(workbook, null, worksheetNames);
            }

            var workbookFullName = GetSavedWorkbookFullName(workbook);
            var manifestPath = GetManifestPath(workbookFullName);
            WriteManifest(manifestPath, new RecoveryManifest
            {
                WorkbookFullName = workbookFullName,
                WorksheetNames = worksheetNames
            });

            var scope = new WorksheetProtectionScope(workbook, manifestPath, worksheetNames);
            try
            {
                states = ReadWorksheetStates(workbook, false);
                foreach (var name in worksheetNames)
                {
                    var state = FindWorksheetState(states, name);
                    if (state == null)
                    {
                        throw new InvalidOperationException(
                            "工作表 \"" + name + "\" 在运行时保护建立前已不存在。");
                    }

                    ((dynamic)state.Worksheet).Protect();
                }

                return scope;
            }
            catch (Exception protectFailure)
            {
                try
                {
                    scope.Restore();
                }
                catch (Exception restoreFailure)
                {
                    throw new AggregateException(
                        "运行时保护建立失败，且未能完整恢复已处理的工作表。",
                        protectFailure,
                        restoreFailure);
                }

                throw;
            }
            finally
            {
                ReleaseWorksheetStates(states);
            }
        }

        private static IList<string> RestoreWorksheetNames(
            ExcelInterop.Workbook workbook,
            IList<string> worksheetNames)
        {
            var failures = new List<string>();
            List<WorksheetState> states = null;
            try
            {
                states = ReadWorksheetStates(workbook, false);
                foreach (var name in worksheetNames)
                {
                    var state = FindWorksheetState(states, name);
                    if (state == null || !state.ProtectContents)
                    {
                        continue;
                    }

                    try
                    {
                        ((dynamic)state.Worksheet).Unprotect();
                    }
                    catch (Exception ex)
                    {
                        failures.Add("无法解除工作表 \"" + name + "\" 的临时保护：" + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add("无法读取工作簿工作表以恢复临时保护：" + ex.Message);
            }
            finally
            {
                ReleaseWorksheetStates(states);
            }

            return failures;
        }

        private static List<WorksheetState> ReadWorksheetStates(
            ExcelInterop.Workbook workbook,
            bool includeCellLockState)
        {
            var states = new List<WorksheetState>();
            object worksheets = null;
            try
            {
                worksheets = ((dynamic)workbook).Worksheets;
                var count = Convert.ToInt32(((dynamic)worksheets).Count);
                for (var index = 1; index <= count; index++)
                {
                    object worksheet = null;
                    try
                    {
                        worksheet = ((dynamic)worksheets).Item(index);
                        var state = new WorksheetState
                        {
                            Name = Convert.ToString(((dynamic)worksheet).Name) ?? string.Empty,
                            ProtectContents = Convert.ToBoolean(((dynamic)worksheet).ProtectContents),
                            AllCellsLocked = false,
                            Worksheet = worksheet
                        };

                        if (includeCellLockState)
                        {
                            object cells = null;
                            try
                            {
                                cells = ((dynamic)worksheet).Cells;
                                var lockedValue = ((dynamic)cells).Locked;
                                state.AllCellsLocked = lockedValue is bool && (bool)lockedValue;
                            }
                            finally
                            {
                                ReleaseComObject(cells);
                            }
                        }

                        states.Add(state);
                        worksheet = null;
                    }
                    finally
                    {
                        ReleaseComObject(worksheet);
                    }
                }
            }
            catch
            {
                ReleaseWorksheetStates(states);
                throw;
            }
            finally
            {
                ReleaseComObject(worksheets);
            }

            return states;
        }

        private static WorksheetState FindWorksheetState(
            IList<WorksheetState> states,
            string worksheetName)
        {
            foreach (var state in states)
            {
                if (string.Equals(state.Name, worksheetName, StringComparison.OrdinalIgnoreCase))
                {
                    return state;
                }
            }

            return null;
        }

        private static void ReleaseWorksheetStates(IList<WorksheetState> states)
        {
            if (states == null)
            {
                return;
            }

            foreach (var state in states)
            {
                ReleaseComObject(state.Worksheet);
                state.Worksheet = null;
            }
        }

        private static string TryGetWorkbookFullName(ExcelInterop.Workbook workbook)
        {
            var fullName = Convert.ToString(((dynamic)workbook).FullName);
            return string.IsNullOrWhiteSpace(fullName) || !Path.IsPathRooted(fullName)
                ? null
                : NormalizeWorkbookFullName(fullName);
        }

        private static string GetSavedWorkbookFullName(ExcelInterop.Workbook workbook)
        {
            var fullName = TryGetWorkbookFullName(workbook);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                throw new InvalidOperationException(
                    "当前工作簿尚未保存为文件，无法建立崩溃恢复记录。请先保存工作簿再执行免确认的中间表步骤。");
            }

            return fullName;
        }

        private static string NormalizeWorkbookFullName(string fullName)
            => Path.GetFullPath(fullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static string GetManifestPath(string workbookFullName)
        {
            var normalizedPath = NormalizeWorkbookFullName(workbookFullName).ToUpperInvariant();
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
            }

            var directory = GetRecoveryDirectory();
            return Path.Combine(directory, BitConverter.ToString(hash).Replace("-", string.Empty) + ".json");
        }

        private static string GetRecoveryDirectory()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException("无法确定本地应用数据目录来保存保护恢复记录。");
            }

            return Path.Combine(localAppData, "NixerOfficeCopilot", RecoveryDirectoryName);
        }

        private static void VerifyRecoveryJournalWritable()
        {
            var directory = GetRecoveryDirectory();
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            File.Delete(probePath);
        }

        private static void WriteManifest(string manifestPath, RecoveryManifest manifest)
        {
            var directory = Path.GetDirectoryName(manifestPath);
            Directory.CreateDirectory(directory);
            var tempPath = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(RecoveryManifest));
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    serializer.WriteObject(stream, manifest);
                    stream.Flush(true);
                }

                if (File.Exists(manifestPath))
                {
                    File.Replace(tempPath, manifestPath, null);
                }
                else
                {
                    File.Move(tempPath, manifestPath);
                }
            }
            catch (Exception writeFailure)
            {
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "无法写入工作表保护恢复记录，且临时文件清理也失败。",
                        writeFailure,
                        cleanupFailure);
                }

                throw;
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.ReleaseComObject(value);
            }
        }

        internal sealed class WorksheetProtectionScope : IDisposable
        {
            private readonly ExcelInterop.Workbook _workbook;
            private readonly IList<string> _worksheetNames;
            private string _manifestPath;

            public WorksheetProtectionScope(
                ExcelInterop.Workbook workbook,
                string manifestPath,
                IList<string> worksheetNames)
            {
                _workbook = workbook;
                _manifestPath = manifestPath;
                _worksheetNames = worksheetNames ?? new List<string>();
            }

            public void Dispose()
            {
                Restore();
            }

            public void Restore()
            {
                if (string.IsNullOrWhiteSpace(_manifestPath))
                {
                    return;
                }

                var failures = RestoreWorksheetNames(_workbook, _worksheetNames);
                if (failures.Count > 0)
                {
                    throw new InvalidOperationException(
                        "VBA 阶段结束后无法完整解除临时工作表保护；恢复记录已保留，加载项下次启动会重试。"
                        + string.Join("；", failures));
                }

                try
                {
                    File.Delete(_manifestPath);
                    _manifestPath = null;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "工作表保护已解除，但无法删除恢复记录；加载项下次启动会再次检查。",
                        ex);
                }
            }
        }

        [DataContract]
        private sealed class RecoveryManifest
        {
            [DataMember(Name = "workbookFullName", Order = 1)]
            public string WorkbookFullName { get; set; }

            [DataMember(Name = "worksheetNames", Order = 2)]
            public List<string> WorksheetNames { get; set; }
        }

        private sealed class WorksheetState
        {
            public string Name { get; set; }

            public bool ProtectContents { get; set; }

            public bool AllCellsLocked { get; set; }

            public object Worksheet { get; set; }
        }
    }

    internal sealed class ExcelVbaStagedProtectionUnavailableException : InvalidOperationException
    {
        public const string ErrorCode = "excel.execute_vba_staged_only_unavailable";

        public ExcelVbaStagedProtectionUnavailableException(string message)
            : base(message)
        {
        }
    }
}
