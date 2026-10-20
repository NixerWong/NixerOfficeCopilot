using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using ExcelInterop = Microsoft.Office.Interop.Excel;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Tools;

namespace OfficeCopilot.Host.Excel
{
    /// <summary>
    /// Excel VBA 能力实现：信任状态探测、模块列举/查看、代码执行与 VBE 编辑器打开。
    /// </summary>
    internal sealed class ExcelVbaService : ExcelServiceBase
    {
        public ExcelVbaService(ExcelInterop.Application application)
            : base(application)
        {
        }

        private static readonly Regex PublicParameterlessSubRegex = new Regex(
            @"^\s*(?:Public\s+)?Sub\s+([\p{L}_][\p{L}\p{Nd}_]*)\s*(?:\(\s*\))?\s*(?:'.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex ParameterlessSubRegex = new Regex(
            @"^\s*(?:Public\s+|Private\s+|Friend\s+)?Sub\s+([\p{L}_][\p{L}\p{Nd}_]*)\s*(?:\(\s*\))?\s*(?:'.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex ParameterlessFunctionRegex = new Regex(
            @"^\s*(?:Public\s+|Private\s+|Friend\s+)?Function\s+([\p{L}_][\p{L}\p{Nd}_]*)\s*(?:\(\s*\))?\s*(?:As\s+[^'\r\n]+)?\s*(?:'.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex VbaProcedureRegex = new Regex(
            @"^\s*(?:Public\s+|Private\s+|Friend\s+)?(?:Sub|Function|Property\s+Get|Property\s+Let|Property\s+Set)\s+([\p{L}_][\p{L}\p{Nd}_]*)\b",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly string[] VbaTrustRegistryPaths = new[]
        {
            @"Software\Microsoft\Office\16.0\Excel\Security",
            @"Software\Microsoft\Office\15.0\Excel\Security",
            @"Software\Microsoft\Office\14.0\Excel\Security"
        };

        private const string VbaTrustDisabledMessage = "VBA 信任访问未启用。请在 Excel 的“文件 > 选项 > 信任中心 > 信任中心设置 > 宏设置”中启用“信任对 VBA 工程对象模型的访问”。";

        private const string ProtectedVbaExecutionSuccessToken = "__NIXER_VBA_OK__";

        private const string ProtectedVbaExecutionErrorToken = "__NIXER_VBA_ERROR__";

        private const string ProtectedVbaExecutionLogToken = "__NIXER_VBA_LOG__";

        // VBA 标识符必须以字母开头，下划线开头会直接编译失败。
        private const string LogBufferVariableName = "NixerCopilotLogBuffer";

        /// <summary>看门狗默认超时；与 ToolSafetyPolicy 对非只读工具的默认超时保持一致。</summary>
        private const int DefaultExecutionTimeoutSeconds = 120;

        private const byte VkControl = 0x11;
        private const byte VkCancel = 0x03;
        private const uint KeyEventFKeyUp = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static int ResolveTimeoutSeconds(int requested)
            => requested > 0 ? requested : DefaultExecutionTimeoutSeconds;

        private static VbaExecutionResult BuildTimeoutResult(int requestedTimeoutSeconds, string moduleName, string entryPoint, long elapsedMilliseconds)
        {
            // 超时的正确处置是"把任务拆小后重试"，模型需要拿到这句话而不是一个 COM 错误码。
            return new VbaExecutionResult
            {
                Success = false,
                TimedOut = true,
                Message = "VBA 执行超过 " + ResolveTimeoutSeconds(requestedTimeoutSeconds)
                    + " 秒，已发送中断信号终止。请把任务拆成更小的步骤后重试，"
                    + "或检查代码中是否存在没有退出条件的循环。",
                ModuleName = moduleName ?? string.Empty,
                EntryPoint = entryPoint ?? string.Empty,
                ElapsedMilliseconds = elapsedMilliseconds
            };
        }

        /// <summary>
        /// 执行看门狗。<c>Application.Run</c> 是同步阻塞的，无法从托管侧取消，
        /// 唯一可用的中断通道是 Ctrl+Break —— 注入代码已把 <c>EnableCancelKey</c> 设为 xlErrorHandler，
        /// 因此它会变成可捕获的 Err 18 而不是弹出 VBA 调试器。
        /// </summary>
        private IDisposable StartExecutionWatchdog(int requestedTimeoutSeconds, Action onTimeout)
        {
            var timeoutSeconds = ResolveTimeoutSeconds(requestedTimeoutSeconds);
            IntPtr hwnd;
            try
            {
                hwnd = new IntPtr(((dynamic)_application).Hwnd);
            }
            catch
            {
                hwnd = IntPtr.Zero;
            }

            var fired = 0;
            Timer timer = null;
            timer = new Timer(
                _ =>
                {
                    if (Interlocked.Exchange(ref fired, 1) != 0)
                    {
                        return;
                    }

                    try
                    {
                        if (hwnd != IntPtr.Zero)
                        {
                            SetForegroundWindow(hwnd);
                        }

                        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
                        keybd_event(VkCancel, 0, 0, UIntPtr.Zero);
                        keybd_event(VkCancel, 0, KeyEventFKeyUp, UIntPtr.Zero);
                        keybd_event(VkControl, 0, KeyEventFKeyUp, UIntPtr.Zero);
                    }
                    catch
                    {
                        // 发不出中断信号也要记下超时事实，否则调用方会把结果当成正常完成。
                    }

                    if (onTimeout != null)
                    {
                        onTimeout();
                    }
                },
                null,
                TimeSpan.FromSeconds(timeoutSeconds),
                Timeout.InfiniteTimeSpan);

            return timer;
        }

        private const string LogCountVariableName = "NixerCopilotLogCount";

        private const int MaxLogEntryCount = 200;

        public bool CanExecuteVba
        {
            get { return GetVbaExecutionAvailability(false).CanExecute; }
        }

        public VbaExecutionAvailability GetStagedWriteProtectionAvailability()
        {
            try
            {
                var workbook = GetRequiredWorkbook();
                var failure = ExcelStagedWorksheetProtection.GetStagedProtectionAvailabilityFailure(workbook);
                return new VbaExecutionAvailability
                {
                    CanExecute = string.IsNullOrWhiteSpace(failure),
                    Reason = failure ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                return new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = "无法确认中间表运行时保护条件：" + ex.Message
                };
            }
        }

        public VbaExecutionAvailability GetDeclaredWriteProtectionAvailability(
            IList<string> allowedWorksheetNames)
        {
            try
            {
                var workbook = GetRequiredWorkbook();
                var failure = ExcelStagedWorksheetProtection.GetDeclaredProtectionAvailabilityFailure(
                    workbook,
                    allowedWorksheetNames);
                return new VbaExecutionAvailability
                {
                    CanExecute = string.IsNullOrWhiteSpace(failure),
                    Reason = failure ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                return new VbaExecutionAvailability
                {
                    CanExecute = false,
                    Reason = "无法确认声明目标外工作表的运行时保护条件：" + ex.Message
                };
            }
        }

        public bool CanOpenInVbaEditor
        {
            get { return GetVbaEditorAvailability(false).CanExecute; }
        }

        public VbaExecutionAvailability GetVbaExecutionAvailability(bool verifyWriteAccess)
        {
            if (_application == null)
            {
                return CreateUnavailableVbaExecutionAvailability("当前未连接 Excel 实例。");
            }

            var workbook = _application.ActiveWorkbook;
            if (workbook == null)
            {
                return CreateUnavailableVbaExecutionAvailability("当前没有活动工作簿。");
            }

            if (workbook.ReadOnly)
            {
                return CreateUnavailableVbaExecutionAvailability("当前工作簿为只读状态，无法注入临时 VBA 模块。");
            }

            if (workbook.ProtectStructure)
            {
                return CreateUnavailableVbaExecutionAvailability("当前工作簿结构受保护，无法注入临时 VBA 模块。");
            }

            var vbaTrustState = TryGetVbaTrustAccessState();
            if (vbaTrustState.HasValue && !vbaTrustState.Value)
            {
                return CreateUnavailableVbaExecutionAvailability(VbaTrustDisabledMessage);
            }

            object vbProject = null;
            object vbComponents = null;
            object component = null;
            var componentAdded = false;

            try
            {
                dynamic runtimeWorkbook = workbook;
                dynamic runtimeProject = runtimeWorkbook.VBProject;
                vbProject = runtimeProject;

                dynamic runtimeComponents = runtimeProject.VBComponents;
                vbComponents = runtimeComponents;

                Convert.ToInt32(runtimeComponents.Count);

                if (verifyWriteAccess)
                {
                    dynamic runtimeComponent = runtimeComponents.Add(1);
                    component = runtimeComponent;
                    componentAdded = true;
                }

                return new VbaExecutionAvailability
                {
                    CanExecute = true,
                    Reason = string.Empty
                };
            }
            catch (COMException ex)
            {
                return CreateUnavailableVbaExecutionAvailability(CreateFriendlyVbaException(ex).Message);
            }
            catch (Exception ex)
            {
                return CreateUnavailableVbaExecutionAvailability(ex.Message);
            }
            finally
            {
                if (componentAdded && vbComponents != null && component != null)
                {
                    try
                    {
                        ((dynamic)vbComponents).Remove((dynamic)component);
                    }
                    catch
                    {
                    }
                }

                ReleaseComObject(component);
                ReleaseComObject(vbComponents);
                ReleaseComObject(vbProject);
            }
        }

        public VbaExecutionAvailability GetVbaEditorAvailability(bool verifyWriteAccess)
        {
            return GetVbaExecutionAvailability(verifyWriteAccess);
        }

        public VbaEditorOpenResult OpenInVbaEditor(VbaEditorOpenRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var code = NormalizeVbaCode(request.Code);
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("VBA 代码为空，无法打开 VBA 编辑器。请提供要打开的 VBA 代码。");
            }

            var workbook = GetRequiredWorkbook();

            if (workbook.ReadOnly)
            {
                throw new InvalidOperationException("当前工作簿为只读状态，无法打开 VBA 编辑器。请先取消只读或另存为可写副本。");
            }

            if (workbook.ProtectStructure)
            {
                throw new InvalidOperationException("当前工作簿的结构已被保护，无法打开 VBA 编辑器。请先取消工作簿结构保护。");
            }

            var vbaTrustState = TryGetVbaTrustAccessState();
            if (vbaTrustState.HasValue && !vbaTrustState.Value)
            {
                throw new InvalidOperationException(VbaTrustDisabledMessage);
            }

            var moduleName = string.IsNullOrWhiteSpace(request.ModuleName)
                ? BuildEditorModuleName()
                : request.ModuleName.Trim();

            object vbProject = null;
            object vbComponents = null;
            object component = null;
            object codeModule = null;
            object vbe = null;
            object mainWindow = null;
            object codePane = null;

            try
            {
                dynamic runtimeWorkbook = workbook;
                dynamic workbookProject = runtimeWorkbook.VBProject;
                vbProject = workbookProject;

                dynamic workbookComponents = workbookProject.VBComponents;
                vbComponents = workbookComponents;

                dynamic runtimeComponent = workbookComponents.Add(1);
                component = runtimeComponent;
                runtimeComponent.Name = moduleName;

                dynamic runtimeCodeModule = runtimeComponent.CodeModule;
                codeModule = runtimeCodeModule;
                runtimeCodeModule.AddFromString(code);

                dynamic runtimeVbe = _application.VBE;
                vbe = runtimeVbe;

                mainWindow = runtimeVbe.MainWindow;
                if (mainWindow != null)
                {
                    ((dynamic)mainWindow).Visible = true;
                }

                runtimeComponent.Activate();

                try
                {
                    codePane = runtimeCodeModule.CodePane;
                    if (codePane != null)
                    {
                        ((dynamic)codePane).Show();
                        ((dynamic)codePane).SetSelection(1, 1, 1, 1);
                    }
                }
                catch
                {
                }

                return new VbaEditorOpenResult
                {
                    Success = true,
                    Message = "已在 VBA 编辑器中打开模块：" + moduleName,
                    ModuleName = moduleName
                };
            }
            catch (COMException ex)
            {
                throw CreateFriendlyVbaException(ex);
            }
            finally
            {
                ReleaseComObject(codePane);
                ReleaseComObject(mainWindow);
                ReleaseComObject(vbe);
                ReleaseComObject(codeModule);
                ReleaseComObject(component);
                ReleaseComObject(vbComponents);
                ReleaseComObject(vbProject);
            }
        }

        private static VbaExecutionAvailability CreateUnavailableVbaExecutionAvailability(string reason)
        {
            return new VbaExecutionAvailability
            {
                CanExecute = false,
                Reason = reason ?? string.Empty
            };
        }

        public ExcelVbaModuleListResult ListVbaModules()
        {
            var workbook = GetRequiredWorkbook();
            var result = new ExcelVbaModuleListResult
            {
                WorkbookName = workbook.Name ?? string.Empty
            };

            object vbProject = null;
            object vbComponents = null;
            object component = null;
            object codeModule = null;

            try
            {
                dynamic runtimeWorkbook = workbook;
                dynamic runtimeProject = runtimeWorkbook.VBProject;
                vbProject = runtimeProject;

                dynamic runtimeComponents = runtimeProject.VBComponents;
                vbComponents = runtimeComponents;

                var count = Convert.ToInt32(runtimeComponents.Count);
                for (var index = 1; index <= count; index++)
                {
                    try
                    {
                        component = runtimeComponents.Item(index);
                        dynamic runtimeComponent = component;

                        codeModule = runtimeComponent.CodeModule;
                        dynamic runtimeCodeModule = codeModule;

                        var lineCount = Convert.ToInt32(runtimeCodeModule.CountOfLines);
                        var code = lineCount > 0
                            ? Convert.ToString(runtimeCodeModule.Lines(1, lineCount)) ?? string.Empty
                            : string.Empty;

                        result.Modules.Add(new ExcelVbaModuleInfo
                        {
                            Name = Convert.ToString(runtimeComponent.Name) ?? string.Empty,
                            Type = MapVbaComponentType(Convert.ToInt32(runtimeComponent.Type)),
                            LineCount = lineCount,
                            Procedures = ExtractProcedureNames(code)
                        });
                    }
                    finally
                    {
                        ReleaseComObject(codeModule);
                        codeModule = null;
                        ReleaseComObject(component);
                        component = null;
                    }
                }
            }
            catch (COMException ex)
            {
                throw CreateFriendlyVbaException(ex);
            }
            finally
            {
                ReleaseComObject(codeModule);
                ReleaseComObject(component);
                ReleaseComObject(vbComponents);
                ReleaseComObject(vbProject);
            }

            return result;
        }

        public ExcelVbaModuleViewResult ViewVbaModule(ExcelVbaModuleViewRequest request)
        {
            request = request ?? new ExcelVbaModuleViewRequest();
            if (string.IsNullOrWhiteSpace(request.ModuleName))
            {
                throw new InvalidOperationException("VBA 模块名称不能为空。");
            }

            var workbook = GetRequiredWorkbook();

            object vbProject = null;
            object vbComponents = null;
            object component = null;
            object codeModule = null;

            try
            {
                dynamic runtimeWorkbook = workbook;
                dynamic runtimeProject = runtimeWorkbook.VBProject;
                vbProject = runtimeProject;

                dynamic runtimeComponents = runtimeProject.VBComponents;
                vbComponents = runtimeComponents;

                component = FindVbaComponentByName(runtimeComponents, request.ModuleName.Trim());
                if (component == null)
                {
                    throw new InvalidOperationException("未找到 VBA 模块：" + request.ModuleName);
                }

                dynamic runtimeComponent = component;
                codeModule = runtimeComponent.CodeModule;
                dynamic runtimeCodeModule = codeModule;

                var lineCount = Convert.ToInt32(runtimeCodeModule.CountOfLines);
                var code = lineCount > 0
                    ? Convert.ToString(runtimeCodeModule.Lines(1, lineCount)) ?? string.Empty
                    : string.Empty;

                return new ExcelVbaModuleViewResult
                {
                    WorkbookName = workbook.Name ?? string.Empty,
                    ModuleName = Convert.ToString(runtimeComponent.Name) ?? string.Empty,
                    ModuleType = MapVbaComponentType(Convert.ToInt32(runtimeComponent.Type)),
                    Code = code,
                    LineCount = lineCount,
                    Procedures = ExtractProcedureNames(code)
                };
            }
            catch (COMException ex)
            {
                throw CreateFriendlyVbaException(ex);
            }
            finally
            {
                ReleaseComObject(codeModule);
                ReleaseComObject(component);
                ReleaseComObject(vbComponents);
                ReleaseComObject(vbProject);
            }
        }

        public VbaSyntaxValidationResult ValidateVbaSyntax(string code)
        {
            var preCheckResult = VbaSyntaxValidator.Validate(code);
            return new VbaSyntaxValidationResult
            {
                IsValid = preCheckResult.IsValid,
                Message = preCheckResult.ToDiagnosticMessage()
            };
        }

        /// <summary>
        /// 复制源表的表头 + 前 <paramref name="rows"/> 行数据到样本表，供金丝雀执行使用。
        /// 返回实际复制的数据行数；源表为空时返回 0。
        /// </summary>
        public int CreateSampleWorksheet(string sourceWorksheetName, string targetWorksheetName, int rows)
        {
            if (string.IsNullOrWhiteSpace(sourceWorksheetName)) throw new ArgumentNullException(nameof(sourceWorksheetName));
            if (string.IsNullOrWhiteSpace(targetWorksheetName)) throw new ArgumentNullException(nameof(targetWorksheetName));

            var workbook = GetRequiredWorkbook();
            EnsureWorkbookWritable(workbook);
            DeleteWorksheetIfExists(targetWorksheetName);

            ExcelInterop.Worksheet source = null;
            ExcelInterop.Worksheet target = null;
            ExcelInterop.Range used = null;
            ExcelInterop.Range sourceBlock = null;
            ExcelInterop.Range targetBlock = null;
            try
            {
                source = ResolveWorksheet(workbook, sourceWorksheetName);
                used = source.UsedRange;
                var totalRows = used.Rows.Count;
                var totalColumns = used.Columns.Count;
                if (totalRows <= 1)
                {
                    return 0;
                }

                // 表头单独占一行，样本行数从数据区算起，否则 rows=1 会退化成"只有表头"。
                var dataRows = Math.Min(Math.Max(rows, 1), totalRows - 1);
                var firstRow = used.Row;
                var firstColumn = used.Column;

                target = (ExcelInterop.Worksheet)workbook.Worksheets.Add(After: workbook.Worksheets[workbook.Worksheets.Count]);
                target.Name = targetWorksheetName;

                sourceBlock = source.Range[
                    source.Cells[firstRow, firstColumn],
                    source.Cells[firstRow + dataRows, firstColumn + totalColumns - 1]];
                targetBlock = target.Range[
                    target.Cells[1, 1],
                    target.Cells[dataRows + 1, totalColumns]];
                targetBlock.Value2 = sourceBlock.Value2;

                return dataRows;
            }
            finally
            {
                ReleaseComObject(targetBlock);
                ReleaseComObject(sourceBlock);
                ReleaseComObject(used);
                ReleaseComObject(target);
                ReleaseComObject(source);
            }
        }

        /// <summary>按名字删除工作表，不存在时静默返回。删除期间抑制 Excel 的确认弹窗。</summary>
        public void DeleteWorksheetIfExists(string worksheetName)
        {
            if (string.IsNullOrWhiteSpace(worksheetName))
            {
                return;
            }

            var workbook = GetRequiredWorkbook();
            var name = worksheetName.Trim();
            dynamic runtimeApplication = _application;
            object originalDisplayAlerts = null;

            for (var index = workbook.Worksheets.Count; index >= 1; index--)
            {
                ExcelInterop.Worksheet worksheet = null;
                try
                {
                    worksheet = (ExcelInterop.Worksheet)workbook.Worksheets[index];
                    if (!string.Equals(worksheet.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (originalDisplayAlerts == null)
                    {
                        originalDisplayAlerts = runtimeApplication.DisplayAlerts;
                        runtimeApplication.DisplayAlerts = false;
                    }

                    worksheet.Delete();
                }
                finally
                {
                    ReleaseComObject(worksheet);
                }
            }

            if (originalDisplayAlerts != null)
            {
                try { runtimeApplication.DisplayAlerts = originalDisplayAlerts; } catch { }
            }
        }

        public VbaExecutionResult ExecuteVba(VbaExecutionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.StagedOnly
                && (request.ProtectionAllowedWorksheetNames == null
                    || request.ProtectionAllowedWorksheetNames.Count == 0))
            {
                return ExecuteVbaCore(request);
            }

            var workbook = GetRequiredWorkbook();
            var protection = request.StagedOnly
                ? ExcelStagedWorksheetProtection.ProtectFormalWorksheets(workbook)
                : ExcelStagedWorksheetProtection.ProtectWorksheetsExcept(
                    workbook,
                    request.ProtectionAllowedWorksheetNames);
            VbaExecutionResult result = null;
            Exception executionFailure = null;
            try
            {
                result = ExecuteVbaCore(request);
            }
            catch (Exception ex)
            {
                executionFailure = ex;
            }

            Exception restorationFailure = null;
            try
            {
                protection.Restore();
            }
            catch (Exception ex)
            {
                restorationFailure = ex;
            }

            if (restorationFailure != null)
            {
                if (executionFailure != null)
                {
                    throw new AggregateException(
                        "VBA 执行结束后无法恢复临时工作表保护。",
                        executionFailure,
                        restorationFailure);
                }

                throw new InvalidOperationException(
                    "VBA 执行结束后无法恢复临时工作表保护；下次加载项启动会自动重试。",
                    restorationFailure);
            }

            var runtimeFailure = executionFailure as VbaRuntimeException;
            if (runtimeFailure != null && runtimeFailure.ErrorNumber == 1004)
            {
                throw new ExcelVbaTouchedFormalSheetException(
                    "VBA 在写入受保护的正式工作表时触发 Excel 错误 1004。"
                    + "本次中间表免确认执行已停止；请检查代码，并在需要修改正式表时确认后执行。",
                    executionFailure);
            }

            if (executionFailure != null)
            {
                ExceptionDispatchInfo.Capture(executionFailure).Throw();
            }

            return result;
        }

        private VbaExecutionResult ExecuteVbaCore(VbaExecutionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var code = NormalizeVbaCode(request.Code);
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("VBA 代码为空，无法执行。");
            }

            // Pre-flight syntax check (ANTLR, offline, no COM)
            var preCheckResult = VbaSyntaxValidator.Validate(code);
            if (!preCheckResult.IsValid)
            {
                return new VbaExecutionResult
                {
                    Success = false,
                    Message = preCheckResult.ToDiagnosticMessage(),
                    ModuleName = string.Empty,
                    EntryPoint = string.Empty
                };
            }

            var workbook = GetRequiredWorkbook();

            if (workbook.ReadOnly)
            {
                throw new InvalidOperationException("当前工作簿为只读状态，无法注入临时 VBA 模块。");
            }

            if (workbook.ProtectStructure)
            {
                throw new InvalidOperationException("当前工作簿结构受保护，无法注入临时 VBA 模块。");
            }

            var vbaTrustState = TryGetVbaTrustAccessState();
            if (vbaTrustState.HasValue && !vbaTrustState.Value)
            {
                throw new InvalidOperationException(VbaTrustDisabledMessage);
            }

            var entryPoint = ResolveEntryPoint(code, request.EntryPoint);
            var moduleName = string.IsNullOrWhiteSpace(request.ModuleName)
                ? BuildTemporaryModuleName(workbook)
                : request.ModuleName.Trim();
            var protectedEntryPoint = BuildProtectedExecutionEntryPointName();
            var protectedCode = BuildProtectedExecutionCode(code, entryPoint, protectedEntryPoint);
            EnsureModuleNameDoesNotExist(workbook, moduleName);

            object vbProject = null;
            object vbComponents = null;
            object component = null;
            object codeModule = null;
            var componentAdded = false;
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            object originalAutomationSecurity = null;
            var timedOut = 0;

            try
            {
                var excelCulture = CultureInfo.GetCultureInfo("en-US");
                CultureInfo.CurrentCulture = excelCulture;
                CultureInfo.CurrentUICulture = excelCulture;

                dynamic runtimeWorkbook = workbook;
                dynamic workbookProject = runtimeWorkbook.VBProject;
                vbProject = workbookProject;

                dynamic workbookComponents = workbookProject.VBComponents;
                vbComponents = workbookComponents;

                dynamic runtimeComponent = workbookComponents.Add(1);
                component = runtimeComponent;
                componentAdded = true;

                runtimeComponent.Name = moduleName;

                dynamic runtimeCodeModule = runtimeComponent.CodeModule;
                codeModule = runtimeCodeModule;
                runtimeCodeModule.AddFromString(protectedCode);

                try
                {
                    dynamic runtimeApplication = _application;
                    originalAutomationSecurity = runtimeApplication.AutomationSecurity;
                    runtimeApplication.AutomationSecurity = 1;
                }
                catch
                {
                    originalAutomationSecurity = null;
                }

                var stopwatch = Stopwatch.StartNew();
                object protectedExecutionResult;
                using (StartExecutionWatchdog(request.TimeoutSeconds, () => Interlocked.Exchange(ref timedOut, 1)))
                {
                    protectedExecutionResult = ((dynamic)_application).Run(
                        BuildMacroReference(workbook.Name, moduleName, protectedEntryPoint));
                }
                stopwatch.Stop();

                if (Volatile.Read(ref timedOut) == 1)
                {
                    return BuildTimeoutResult(request.TimeoutSeconds, moduleName, entryPoint, stopwatch.ElapsedMilliseconds);
                }

                List<string> logEntries;
                var statusText = SplitProtectedExecutionResult(protectedExecutionResult, out logEntries);
                int errorNumber;
                var protectedFailureMessage = TryGetProtectedExecutionFailureMessage(statusText, out errorNumber);
                if (!string.IsNullOrWhiteSpace(protectedFailureMessage))
                {
                    var failureMessage = AppendLogEntries(protectedFailureMessage, logEntries);
                    if (errorNumber > 0)
                    {
                        throw new VbaRuntimeException(errorNumber, failureMessage);
                    }

                    throw new InvalidOperationException(failureMessage);
                }

                return new VbaExecutionResult
                {
                    Success = true,
                    Message = "VBA 已执行完成：" + entryPoint,
                    ModuleName = moduleName,
                    EntryPoint = entryPoint,
                    LogEntries = logEntries,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
            catch (COMException ex)
            {
                // 中断信号可能让 Run 直接抛 COM 错而不是返回错误令牌，
                // 这条路径不认超时就会把看门狗中断误报成"信任未启用"（D7 的同款毛病）。
                if (Volatile.Read(ref timedOut) == 1)
                {
                    return BuildTimeoutResult(request.TimeoutSeconds, moduleName, entryPoint, 0L);
                }

                throw CreateFriendlyVbaException(ex);
            }
            catch (TargetInvocationException ex)
            {
                if (Volatile.Read(ref timedOut) == 1)
                {
                    return BuildTimeoutResult(request.TimeoutSeconds, moduleName, entryPoint, 0L);
                }

                var innerException = ex.InnerException;
                if (innerException is COMException)
                {
                    throw CreateFriendlyVbaException((COMException)innerException);
                }

                throw new InvalidOperationException(innerException == null ? ex.Message : innerException.Message, innerException ?? ex);
            }
            finally
            {
                if (originalAutomationSecurity != null)
                {
                    try
                    {
                        ((dynamic)_application).AutomationSecurity = originalAutomationSecurity;
                    }
                    catch
                    {
                    }
                }

                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;

                if (request.RemoveModuleAfterRun && componentAdded && vbComponents != null && component != null)
                {
                    try
                    {
                        ((dynamic)vbComponents).Remove((dynamic)component);
                    }
                    catch
                    {
                    }
                }

                ReleaseComObject(codeModule);
                ReleaseComObject(component);
                ReleaseComObject(vbComponents);
                ReleaseComObject(vbProject);
            }
        }

        private static object FindVbaComponentByName(dynamic components, string moduleName)
        {
            if (components == null || string.IsNullOrWhiteSpace(moduleName))
            {
                return null;
            }

            object component = null;
            var count = Convert.ToInt32(components.Count);

            for (var index = 1; index <= count; index++)
            {
                try
                {
                    component = components.Item(index);
                    var currentName = Convert.ToString(((dynamic)component).Name) ?? string.Empty;
                    if (string.Equals(currentName, moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        var result = component;
                        component = null;
                        return result;
                    }
                }
                finally
                {
                    ReleaseComObject(component);
                    component = null;
                }
            }

            return null;
        }

        private static string NormalizeVbaCode(string code)
        {
            return (code ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Trim();
        }

        private static string BuildProtectedExecutionEntryPointName()
        {
            return "NixerCopilotProtectedRun_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string BuildProtectedExecutionCode(string code, string entryPoint, string protectedEntryPoint)
        {
            var normalizedCode = NormalizeVbaCode(code);
            var builder = new StringBuilder();

            string optionStatements;
            var bodyCode = ExtractOptionStatements(normalizedCode, out optionStatements);

            // Option 语句必须位于模块最前，注入的模块级声明只能排在它之后。
            if (optionStatements.Length > 0)
            {
                builder.AppendLine(optionStatements);
            }

            builder.AppendLine("Private " + LogBufferVariableName + " As String");
            builder.AppendLine("Private " + LogCountVariableName + " As Long");
            builder.AppendLine();
            builder.AppendLine("Public Sub NixerLog(ByVal msg As String)");
            builder.AppendLine("    " + LogCountVariableName + " = " + LogCountVariableName + " + 1");
            builder.AppendLine("    If " + LogCountVariableName + " <= " + MaxLogEntryCount + " Then");
            builder.AppendLine("        " + LogBufferVariableName + " = " + LogBufferVariableName
                + " & Replace(Replace(CStr(msg), vbCr, \" \"), vbLf, \" \") & vbLf");
            builder.AppendLine("    End If");
            builder.AppendLine("End Sub");
            builder.AppendLine();

            if (!string.IsNullOrWhiteSpace(bodyCode))
            {
                builder.AppendLine(bodyCode);
                builder.AppendLine();
            }

            builder.AppendLine("Public Function " + protectedEntryPoint + "() As String");
            builder.AppendLine("    On Error GoTo ProtectedExecutionFailed");
            builder.AppendLine("    " + LogBufferVariableName + " = \"\"");
            builder.AppendLine("    " + LogCountVariableName + " = 0");
            // xlErrorHandler：Ctrl+Break 变成可捕获的 Err 18，而不是弹出 VBA 调试器把 Excel 挂住。
            builder.AppendLine("    Application.EnableCancelKey = 2");

            if (IsFunctionEntryPoint(bodyCode, entryPoint))
            {
                builder.AppendLine("    Dim NixerCopilotIgnoredResult As Variant");
                builder.AppendLine("    NixerCopilotIgnoredResult = " + entryPoint + "()");
            }
            else
            {
                builder.AppendLine("    Call " + entryPoint + "()");
            }

            builder.AppendLine("    " + protectedEntryPoint + " = \"" + ProtectedVbaExecutionSuccessToken
                + "\" & \"" + ProtectedVbaExecutionLogToken + "\" & " + LogBufferVariableName);
            builder.AppendLine("    Exit Function");
            builder.AppendLine();
            builder.AppendLine("ProtectedExecutionFailed:");
            builder.AppendLine("    " + protectedEntryPoint + " = \"" + ProtectedVbaExecutionErrorToken
                + "\" & CStr(Err.Number) & \"|\" & Replace(Replace(Err.Description, vbCr, \" \"), vbLf, \" \")"
                + " & \"" + ProtectedVbaExecutionLogToken + "\" & " + LogBufferVariableName);
            builder.AppendLine("    Err.Clear");
            builder.AppendLine("End Function");

            return builder.ToString();
        }

        /// <summary>把用户代码里的 Option 语句抽到最前，返回去掉这些行之后的正文。</summary>
        private static string ExtractOptionStatements(string code, out string optionStatements)
        {
            optionStatements = string.Empty;
            if (string.IsNullOrWhiteSpace(code))
            {
                return string.Empty;
            }

            var lines = code.Split('\n');
            var options = new StringBuilder();
            var body = new StringBuilder();

            foreach (var line in lines)
            {
                if (Regex.IsMatch(line, @"^\s*Option\s+\w+", RegexOptions.IgnoreCase))
                {
                    options.AppendLine(line.Trim());
                }
                else
                {
                    body.AppendLine(line);
                }
            }

            optionStatements = options.ToString().TrimEnd();
            return body.ToString().Trim();
        }

        private static bool IsFunctionEntryPoint(string code, string entryPoint)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(entryPoint))
            {
                return false;
            }

            var escapedEntryPoint = Regex.Escape(entryPoint.Trim());
            return Regex.IsMatch(
                code,
                @"^\s*(?:Public\s+|Private\s+|Friend\s+)?Function\s+(" + escapedEntryPoint + @")\s*(?:\(\s*\))?\s*(?:As\s+[^'\r\n]+)?\s*(?:'.*)?$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        private static string AppendLogEntries(string message, List<string> logEntries)
        {
            if (logEntries == null || logEntries.Count == 0)
            {
                return message;
            }

            var builder = new StringBuilder(message);
            builder.AppendLine();
            builder.AppendLine("VBA 日志（NixerLog）：");
            foreach (var entry in logEntries)
            {
                builder.AppendLine("  " + entry);
            }

            return builder.ToString();
        }

        /// <summary>把受保护包装函数的返回值拆成「状态段」和「日志段」。</summary>
        private static string SplitProtectedExecutionResult(object executionResult, out List<string> logEntries)
        {
            logEntries = new List<string>();

            var resultText = Convert.ToString(executionResult) ?? string.Empty;
            var logIndex = resultText.IndexOf(ProtectedVbaExecutionLogToken, StringComparison.Ordinal);
            if (logIndex < 0)
            {
                return resultText;
            }

            var logPayload = resultText.Substring(logIndex + ProtectedVbaExecutionLogToken.Length);
            foreach (var line in logPayload.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    logEntries.Add(trimmed);
                }
            }

            return resultText.Substring(0, logIndex);
        }

        private static string TryGetProtectedExecutionFailureMessage(string resultText, out int parsedErrorNumber)
        {
            parsedErrorNumber = 0;
            if (string.IsNullOrWhiteSpace(resultText))
            {
                return string.Empty;
            }

            if (string.Equals(resultText, ProtectedVbaExecutionSuccessToken, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            if (!resultText.StartsWith(ProtectedVbaExecutionErrorToken, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var payload = resultText.Substring(ProtectedVbaExecutionErrorToken.Length);
            var separatorIndex = payload.IndexOf('|');
            if (separatorIndex < 0)
            {
                return string.IsNullOrWhiteSpace(payload)
                    ? "VBA 执行过程中出错。"
                    : "VBA 执行过程中出错：" + payload.Trim();
            }

            var errorNumber = payload.Substring(0, separatorIndex).Trim();
            var description = payload.Substring(separatorIndex + 1).Trim();
            int.TryParse(
                errorNumber,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsedErrorNumber);

            if (string.Equals(errorNumber, "18", StringComparison.Ordinal))
            {
                return "VBA 执行被用户中断（Ctrl+Break）。如果是因为运行时间过长，请把任务拆成更小的步骤后重试。";
            }

            if (string.IsNullOrWhiteSpace(errorNumber))
            {
                return string.IsNullOrWhiteSpace(description)
                    ? "VBA 执行过程中出错。"
                    : "VBA 执行过程中出错：" + description;
            }

            return string.IsNullOrWhiteSpace(description)
                ? "VBA 执行出错，错误号 " + errorNumber + "。"
                : "VBA 执行出错，错误号 " + errorNumber + "：" + description;
        }

        private static string ResolveEntryPoint(string code, string requestedEntryPoint)
        {
            if (!string.IsNullOrWhiteSpace(requestedEntryPoint))
            {
                var resolvedEntryPoint = ResolveNamedEntryPoint(code, requestedEntryPoint.Trim());
                if (!string.IsNullOrWhiteSpace(resolvedEntryPoint))
                {
                    return resolvedEntryPoint;
                }

                throw new InvalidOperationException("未在提供的 VBA 代码中找到可执行的入口过程：" + requestedEntryPoint.Trim());
            }

            var match = PublicParameterlessSubRegex.Match(code);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            match = ParameterlessSubRegex.Match(code);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            match = ParameterlessFunctionRegex.Match(code);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            throw new InvalidOperationException("未找到可直接运行的入口过程。请提供标准模块中的无参数Sub或Function。");
        }

        private string BuildTemporaryModuleName(ExcelInterop.Workbook workbook)
        {
            string moduleName;
            do
            {
                moduleName = "NixerCopilotRuntime_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            while (VbaModuleExists(workbook, moduleName));

            return moduleName;
        }

        private static string BuildEditorModuleName()
        {
            return "NixerCopilotDraft_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string BuildMacroReference(string workbookName, string moduleName, string entryPoint)
        {
            return "'" + (workbookName ?? string.Empty).Replace("'", "''") + "'!" + moduleName + "." + entryPoint;
        }

        private static string ResolveNamedEntryPoint(string code, string entryPoint)
        {
            var escapedEntryPoint = Regex.Escape(entryPoint ?? string.Empty);
            if (string.IsNullOrWhiteSpace(escapedEntryPoint))
            {
                return string.Empty;
            }

            var subMatch = Regex.Match(
                code ?? string.Empty,
                @"^\s*(?:Public\s+|Private\s+|Friend\s+)?Sub\s+(" + escapedEntryPoint + @")\s*(?:\(\s*\))?\s*(?:'.*)?$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (subMatch.Success)
            {
                return subMatch.Groups[1].Value;
            }

            var functionMatch = Regex.Match(
                code ?? string.Empty,
                @"^\s*(?:Public\s+|Private\s+|Friend\s+)?Function\s+(" + escapedEntryPoint + @")\s*(?:\(\s*\))?\s*(?:As\s+[^'\r\n]+)?\s*(?:'.*)?$",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (functionMatch.Success)
            {
                return functionMatch.Groups[1].Value;
            }

            return string.Empty;
        }

        private static string MapVbaComponentType(int type)
        {
            switch (type)
            {
                case 1:
                    return "StandardModule";
                case 2:
                    return "ClassModule";
                case 3:
                    return "UserForm";
                case 100:
                    return "Document";
                default:
                    return "Unknown(" + type + ")";
            }
        }

        private static List<string> ExtractProcedureNames(string code)
        {
            var procedures = new List<string>();
            if (string.IsNullOrWhiteSpace(code))
            {
                return procedures;
            }

            var matches = VbaProcedureRegex.Matches(code);
            for (var index = 0; index < matches.Count; index++)
            {
                var name = matches[index].Groups[1].Value;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var exists = false;
                for (var i = 0; i < procedures.Count; i++)
                {
                    if (string.Equals(procedures[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    procedures.Add(name);
                }
            }

            return procedures;
        }

        private static Exception CreateFriendlyVbaException(COMException ex)
        {
            var message = ex.Message ?? string.Empty;

            if (message.IndexOf("programmatic access", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("AccessVBOM", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new InvalidOperationException(VbaTrustDisabledMessage, ex);
            }

            // 0x800A03EC 是 Excel 通用的「宏无法运行」错误码。把它一律当成信任未启用，
            // 会把编译失败、入口过程不存在等问题全部误报成设置问题，把用户引向错误的排查方向。
            if (ex.ErrorCode == unchecked((int)0x800A03EC))
            {
                var trustState = TryGetVbaTrustAccessState();
                if (trustState.HasValue && !trustState.Value)
                {
                    return new InvalidOperationException(VbaTrustDisabledMessage, ex);
                }

                return new InvalidOperationException(
                    "无法运行注入的 VBA 宏。最常见原因是代码编译失败："
                    + "标识符以下划线开头（VBA 不允许）、缺少 End Sub/End Function、"
                    + "使用了未声明的变量或对象、或入口过程名与代码不符。"
                    + "请修正代码后重试。原始错误：" + ex.Message,
                    ex);
            }

            if (message.IndexOf("already in use", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new InvalidOperationException("VBA 模块名冲突，请改用其他 moduleName。", ex);
            }

            if (message.IndexOf("VBProject", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("trusted", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("程序化", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("对象模型", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new InvalidOperationException(
                    "Excel 禁止以编程方式访问 VBA 工程。请在 Excel 的“文件 > 选项 > 信任中心 > 信任中心设置”中启用“信任对 VBA 项目对象模型的访问”。",
                    ex);
            }

            return new InvalidOperationException("执行 VBA 失败：" + ex.Message, ex);
        }

        private static bool? TryGetVbaTrustAccessState()
        {
            var foundDisabledValue = false;

            try
            {
                for (var index = 0; index < VbaTrustRegistryPaths.Length; index++)
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(VbaTrustRegistryPaths[index]))
                    {
                        if (key == null)
                        {
                            continue;
                        }

                        var value = key.GetValue("AccessVBOM");
                        if (value == null)
                        {
                            continue;
                        }

                        int accessValue;
                        if (!TryConvertRegistryValueToInt32(value, out accessValue))
                        {
                            continue;
                        }

                        if (accessValue == 1)
                        {
                            return true;
                        }

                        foundDisabledValue = true;
                    }
                }
            }
            catch (SecurityException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            return foundDisabledValue ? (bool?)false : null;
        }

        private static bool TryConvertRegistryValueToInt32(object value, out int result)
        {
            if (value is int)
            {
                result = (int)value;
                return true;
            }

            if (value is string)
            {
                return int.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            }

            try
            {
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }

        private void EnsureModuleNameDoesNotExist(ExcelInterop.Workbook workbook, string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                throw new InvalidOperationException("VBA 模块名不能为空。");
            }

            if (VbaModuleExists(workbook, moduleName))
            {
                throw new InvalidOperationException("VBA 模块 '" + moduleName + "' 已存在，请改用其他 moduleName。");
            }
        }

        private bool VbaModuleExists(ExcelInterop.Workbook workbook, string moduleName)
        {
            object vbProject = null;
            object vbComponents = null;

            try
            {
                dynamic runtimeWorkbook = workbook;
                dynamic runtimeProject = runtimeWorkbook.VBProject;
                vbProject = runtimeProject;

                dynamic runtimeComponents = runtimeProject.VBComponents;
                vbComponents = runtimeComponents;

                var componentCount = Convert.ToInt32(runtimeComponents.Count);
                for (var index = 1; index <= componentCount; index++)
                {
                    object component = null;
                    try
                    {
                        dynamic runtimeComponent = runtimeComponents.Item(index);
                        component = runtimeComponent;
                        var existingName = Convert.ToString(runtimeComponent.Name) ?? string.Empty;
                        if (string.Equals(existingName, moduleName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    finally
                    {
                        ReleaseComObject(component);
                    }
                }

                return false;
            }
            catch (COMException ex)
            {
                throw CreateFriendlyVbaException(ex);
            }
            finally
            {
                ReleaseComObject(vbComponents);
                ReleaseComObject(vbProject);
            }
        }

        private sealed class VbaRuntimeException : InvalidOperationException
        {
            public VbaRuntimeException(int errorNumber, string message)
                : base(message)
            {
                ErrorNumber = errorNumber;
            }

            public int ErrorNumber { get; private set; }
        }
    }

    internal sealed class ExcelVbaTouchedFormalSheetException : InvalidOperationException
    {
        public const string ErrorCode = "excel.execute_vba_touched_formal_sheet";

        public ExcelVbaTouchedFormalSheetException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
