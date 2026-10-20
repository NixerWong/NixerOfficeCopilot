using Microsoft.Win32;
using OfficeCopilot.Host.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Host.PowerPoint
{
    /// <summary>
    /// PowerPoint VBA 执行（蓝图 P4，基础能力）：注入临时标准模块 → Application.Run → 移除。
    /// 需要用户在信任中心勾选「信任对 VBA 工程对象模型的访问」。PowerPoint 没有 Excel 的中断机制，不支持超时看门狗。
    /// </summary>
    public sealed partial class PowerPointHostAdapter : IVbaExecutionHostAdapter
    {
        private const string VbaSuccessToken = "__NIXER_OK__";
        private const string VbaErrorToken = "__NIXER_ERR__";
        private const string VbaLogToken = "__NIXER_LOG__";
        private const int MaxVbaLogEntries = 200;
        private const string VbaTrustDisabledMessage =
            "PowerPoint 未信任对 VBA 工程对象模型的访问。请在 文件 → 选项 → 信任中心 → 信任中心设置 → 宏设置 中勾选「信任对 VBA 工程对象模型的访问」后重试。";

        private static readonly Regex ProcedurePattern = new Regex(
            @"^\s*(?:Public\s+|Private\s+|Friend\s+)?(?:Static\s+)?(Sub|Function)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        public bool CanExecuteVba
        {
            get { return _application != null; }
        }

        public VbaExecutionAvailability GetVbaExecutionAvailability(bool verifyWriteAccess)
        {
            if (_application == null)
            {
                return new VbaExecutionAvailability { CanExecute = false, Reason = "PowerPoint 未连接。" };
            }

            var trust = _dispatcher.Invoke(() => TryGetVbaTrustState());
            if (trust.HasValue && !trust.Value)
            {
                return new VbaExecutionAvailability { CanExecute = false, Reason = VbaTrustDisabledMessage };
            }

            return new VbaExecutionAvailability { CanExecute = true, Reason = string.Empty };
        }

        public VbaSyntaxValidationResult ValidateVbaSyntax(string code)
        {
            var normalized = NormalizeVba(code);
            if (normalized.Length == 0)
            {
                return new VbaSyntaxValidationResult { IsValid = false, Message = "VBA 代码为空。" };
            }

            if (!ProcedurePattern.IsMatch(normalized))
            {
                return new VbaSyntaxValidationResult { IsValid = false, Message = "未找到无参数的 Sub/Function 入口过程。" };
            }

            var opens = Regex.Matches(normalized, @"^\s*(?:Public\s+|Private\s+|Friend\s+)?(?:Static\s+)?(?:Sub|Function)\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Multiline).Count;
            var closes = Regex.Matches(normalized, @"^\s*End\s+(?:Sub|Function)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline).Count;
            if (opens != closes)
            {
                return new VbaSyntaxValidationResult { IsValid = false, Message = "Sub/Function 与 End Sub/End Function 数量不匹配（" + opens + " vs " + closes + "）。" };
            }

            return new VbaSyntaxValidationResult { IsValid = true, Message = string.Empty };
        }

        public VbaExecutionResult ExecuteVba(VbaExecutionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var code = NormalizeVba(request.Code);
            var validation = ValidateVbaSyntax(code);
            if (!validation.IsValid)
            {
                return new VbaExecutionResult { Success = false, Message = validation.Message, ModuleName = string.Empty, EntryPoint = string.Empty };
            }

            var entryPoint = ResolveVbaEntryPoint(code, request.EntryPoint);
            return _dispatcher.Invoke(() => ExecuteVbaOnUiThread(request, code, entryPoint));
        }

        private VbaExecutionResult ExecuteVbaOnUiThread(VbaExecutionRequest request, string code, string entryPoint)
        {
            var presentation = GetWritablePresentation();
            var trust = TryGetVbaTrustState();
            if (trust.HasValue && !trust.Value) throw new InvalidOperationException(VbaTrustDisabledMessage);

            var moduleName = string.IsNullOrWhiteSpace(request.ModuleName)
                ? "NixerTmp_" + Guid.NewGuid().ToString("N").Substring(0, 8)
                : request.ModuleName.Trim();
            var wrapperName = "NixerProtectedRun_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            object components = null;
            object component = null;
            object originalSecurity = null;
            try
            {
                var project = ComLate.Get(presentation, "VBProject");
                components = ComLate.Get(project, "VBComponents");
                component = ComLate.Call(components, "Add", 1);
                ComLate.Set(component, "Name", moduleName);
                ComLate.Call(ComLate.Get(component, "CodeModule"), "AddFromString", BuildWrappedVba(code, entryPoint, wrapperName));

                try
                {
                    originalSecurity = ComLate.Get(_application, "AutomationSecurity");
                    ComLate.Set(_application, "AutomationSecurity", 1);
                }
                catch (Exception)
                {
                    originalSecurity = null;
                }

                BeginUndo();
                var macro = "'" + presentation.Name.Replace("'", "''") + "'!" + moduleName + "." + wrapperName;
                var stopwatch = Stopwatch.StartNew();
                var raw = Convert.ToString(ComLate.Call(_application, "Run", macro)) ?? string.Empty;
                stopwatch.Stop();

                var logs = new List<string>();
                var logIndex = raw.IndexOf(VbaLogToken, StringComparison.Ordinal);
                var status = logIndex < 0 ? raw : raw.Substring(0, logIndex);
                if (logIndex >= 0)
                {
                    foreach (var line in raw.Substring(logIndex + VbaLogToken.Length).Split('\n'))
                    {
                        if (line.Trim().Length > 0) logs.Add(line.Trim());
                    }
                }

                if (status.StartsWith(VbaErrorToken, StringComparison.Ordinal))
                {
                    return new VbaExecutionResult
                    {
                        Success = false,
                        Message = "VBA 运行错误：" + status.Substring(VbaErrorToken.Length),
                        ModuleName = moduleName,
                        EntryPoint = entryPoint,
                        LogEntries = logs,
                        ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                    };
                }

                return new VbaExecutionResult
                {
                    Success = true,
                    Message = "VBA 已执行完成：" + entryPoint,
                    ModuleName = moduleName,
                    EntryPoint = entryPoint,
                    LogEntries = logs,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                if (inner is COMException && (inner.Message.IndexOf("trust", StringComparison.OrdinalIgnoreCase) >= 0
                    || inner.Message.IndexOf("信任", StringComparison.Ordinal) >= 0))
                {
                    throw new InvalidOperationException(VbaTrustDisabledMessage, inner);
                }

                if (inner is COMException)
                {
                    throw new InvalidOperationException("VBA 执行失败（可能是编译错误）：" + inner.Message, inner);
                }

                throw;
            }
            finally
            {
                if (originalSecurity != null)
                {
                    try { ComLate.Set(_application, "AutomationSecurity", originalSecurity); } catch (Exception) { }
                }

                if (request.RemoveModuleAfterRun && components != null && component != null)
                {
                    try { ComLate.Call(components, "Remove", component); } catch (Exception) { }
                }
            }
        }

        private bool? TryGetVbaTrustState()
        {
            try
            {
                var version = Convert.ToString(ComLate.Get(_application, "Version")) ?? "16.0";
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Office\" + version + @"\PowerPoint\Security"))
                {
                    var value = key == null ? null : key.GetValue("AccessVBOM");
                    if (value == null) return null;
                    return Convert.ToInt32(value) != 0;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string NormalizeVba(string code)
        {
            return (code ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }

        private static string ResolveVbaEntryPoint(string code, string requested)
        {
            var matches = ProcedurePattern.Matches(code);
            if (!string.IsNullOrWhiteSpace(requested))
            {
                foreach (Match match in matches)
                {
                    if (string.Equals(match.Groups[2].Value, requested.Trim(), StringComparison.OrdinalIgnoreCase)) return match.Groups[2].Value;
                }

                throw new ArgumentException("代码中找不到入口过程「" + requested + "」（需无参数 Sub/Function）。");
            }

            foreach (Match match in matches)
            {
                if (string.Equals(match.Groups[2].Value, "Main", StringComparison.OrdinalIgnoreCase)) return match.Groups[2].Value;
            }

            return matches[0].Groups[2].Value;
        }

        private static string BuildWrappedVba(string code, string entryPoint, string wrapperName)
        {
            var options = new StringBuilder();
            var body = new StringBuilder();
            foreach (var line in code.Split('\n'))
            {
                if (Regex.IsMatch(line, @"^\s*Option\s+\w+", RegexOptions.IgnoreCase)) options.AppendLine(line.Trim());
                else body.AppendLine(line);
            }

            var isFunction = Regex.IsMatch(code,
                @"^\s*(?:Public\s+|Private\s+|Friend\s+)?(?:Static\s+)?Function\s+" + Regex.Escape(entryPoint) + @"\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.Multiline);

            var builder = new StringBuilder();
            if (options.Length > 0) builder.Append(options);
            builder.AppendLine("Private NixerLogBuffer As String");
            builder.AppendLine("Private NixerLogCount As Long");
            builder.AppendLine();
            builder.AppendLine("Public Sub NixerLog(ByVal msg As String)");
            builder.AppendLine("    NixerLogCount = NixerLogCount + 1");
            builder.AppendLine("    If NixerLogCount <= " + MaxVbaLogEntries + " Then NixerLogBuffer = NixerLogBuffer & Replace(Replace(CStr(msg), vbCr, \" \"), vbLf, \" \") & vbLf");
            builder.AppendLine("End Sub");
            builder.AppendLine();
            builder.AppendLine(body.ToString().Trim());
            builder.AppendLine();
            builder.AppendLine("Public Function " + wrapperName + "() As String");
            builder.AppendLine("    On Error GoTo Failed");
            builder.AppendLine("    NixerLogBuffer = \"\"");
            builder.AppendLine("    NixerLogCount = 0");
            if (isFunction)
            {
                builder.AppendLine("    Dim ignored As Variant");
                builder.AppendLine("    ignored = " + entryPoint + "()");
            }
            else
            {
                builder.AppendLine("    Call " + entryPoint);
            }

            builder.AppendLine("    " + wrapperName + " = \"" + VbaSuccessToken + "\" & \"" + VbaLogToken + "\" & NixerLogBuffer");
            builder.AppendLine("    Exit Function");
            builder.AppendLine("Failed:");
            builder.AppendLine("    " + wrapperName + " = \"" + VbaErrorToken + "\" & CStr(Err.Number) & \"|\" & Replace(Replace(Err.Description, vbCr, \" \"), vbLf, \" \") & \"" + VbaLogToken + "\" & NixerLogBuffer");
            builder.AppendLine("End Function");
            return builder.ToString();
        }
    }
}
