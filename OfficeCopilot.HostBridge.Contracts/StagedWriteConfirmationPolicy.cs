using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace OfficeCopilot.HostBridge.Contracts
{
    /// <summary>
    /// 参数级确认豁免判定。确认门原本只看工具定义，于是「落一张可弃的中间表」和
    /// 「覆盖用户的原始数据」弹同一个窗；用户被训练成无脑点确认，确认就失去意义了。
    /// 判据是这次调用**会不会触碰既有数据**：只写 <c>_wip_</c> 中间表就静默放行，
    /// 其余一律强确认。这同时也是激励设计——分段执行不弹窗，模型会自然倾向分段。
    /// </summary>
    /// <remarks>
    /// 放在 HostBridge.Contracts 是因为 Broker 与宿主分属两个进程，但都做工具确认：
    /// Broker 侧 <c>DefaultToolDispatcher</c> 先弹，宿主侧 <c>DefaultHostBridgeRequestHandler</c>
    /// 兜底。两边必须用同一份判定，否则 Broker 先弹窗、宿主的豁免就永远轮不到。
    /// </remarks>
    public static class StagedWriteConfirmationPolicy
    {
        public const string WipPrefix = "_wip_";

        private static readonly Regex OutputSheetRegex = new Regex(
            "(?<!\\\\)\"outputSheet\"\\s*:\\s*\"(?<name>[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DeclaredTargetsArrayRegex = new Regex(
            "(?<!\\\\)\"declaredWriteTargets\"\\s*:\\s*\\[(?<targets>[^\\]]*)\\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DeclaredTargetSheetRegex = new Regex(
            "\"sheet\"\\s*:\\s*\"(?<name>[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DeclaredTargetObjectRegex = new Regex(
            "\\{(?<target>[^{}]*)\\}",
            RegexOptions.Compiled);

        private static readonly Regex DeclaredTargetIsNewRegex = new Regex(
            "\"isNewSheet\"\\s*:\\s*true",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CodeRegex = new Regex(
            "(?<!\\\\)\"code\"\\s*:\\s*\"(?<code>(?:\\\\.|[^\"\\\\])*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Worksheets("名") / Sheets("名") —— 唯一可被静态判定的显式表引用。
        private static readonly Regex SheetLiteralRegex = new Regex(
            "(?:Worksheets|Sheets)\\s*\\(\\s*\"(?<name>[^\"]*)\"\\s*\\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SheetBindingRegex = new Regex(
            "^\\s*Set\\s+(?<var>[A-Za-z][A-Za-z0-9_]*)\\s*=.*?(?:Worksheets|Sheets)\\s*\\(\\s*\"(?<name>[^\"]*)\"\\s*\\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SheetAddBindingRegex = new Regex(
            "^\\s*Set\\s+(?<var>[A-Za-z][A-Za-z0-9_]*)\\s*=.*?(?:Worksheets|Sheets)\\s*\\.\\s*Add\\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SheetAddRegex = new Regex(
            @"\b(?:Worksheets|Sheets)\s*\.\s*Add\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SheetNameAssignmentRegex = new Regex(
            "^\\s*(?<var>[A-Za-z][A-Za-z0-9_]*)\\s*\\.\\s*Name\\s*=\\s*\"(?<name>[^\"]*)\"\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WithBlockRegex = new Regex(
            "^\\s*With\\s+(?<expression>.+?)\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WithVariableRegex = new Regex(
            "^\\s*(?<variable>[A-Za-z][A-Za-z0-9_]*)\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EndWithRegex = new Regex(
            "^\\s*End\\s+With\\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IndexedSheetReferenceRegex = new Regex(
            @"(?:Worksheets|Sheets)\s*\(\s*(?!"")[^)]*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CodeNameReferenceRegex = new Regex(
            @"\bSheet\d+\s*\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ProtectionMethodRegex = new Regex(
            @"\b(?:Unprotect|Protect)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex UnqualifiedRangeReferenceRegex = new Regex(
            @"(?<![.\w])(?:Range|Cells|Rows|Columns)\s*\(",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ObjectMemberRegex = new Regex(
            @"\b(?<name>[A-Za-z][A-Za-z0-9_]*)\s*\.",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] MutatingTokens =
        {
            ".Delete", ".Clear", ".ClearContents", ".ClearFormats", ".Insert", ".PasteSpecial",
            ".Copy", ".Paste", ".Add", ".Add2", ".Sort", ".RemoveDuplicates", ".Cut", ".Merge", ".UnMerge", ".Value", ".Value2",
            ".Formula", ".FormulaR1C1", ".Interior", ".Font", ".NumberFormat", ".Name ="
        };

        /// <summary>
        /// 本次 <c>excel_execute_vba</c> 调用是否只写 <c>_wip_</c> 中间表，可免用户确认。
        /// 任何无法静态判定的写法（ActiveSheet / Selection / 未绑定的变量）一律不豁免。
        /// </summary>
        public static bool IsExemptStagedVbaWrite(string toolName, string argumentsJson)
        {
            if (!string.Equals(toolName, "excel_execute_vba", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(argumentsJson))
            {
                return false;
            }

            var outputSheetMatch = OutputSheetRegex.Match(argumentsJson);
            if (!outputSheetMatch.Success || !IsWipSheet(outputSheetMatch.Groups["name"].Value))
            {
                return false;
            }

            IList<string> declaredTargetNames;
            IList<string> declaredNewTargetNames;
            if (!TryGetDeclaredVbaWriteTargetSheetNames(
                argumentsJson,
                out declaredTargetNames,
                out declaredNewTargetNames))
            {
                return false;
            }

            var outputSheet = outputSheetMatch.Groups["name"].Value.Trim();
            if (declaredTargetNames.Count != 1
                || !string.Equals(declaredTargetNames[0].Trim(), outputSheet, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var codeMatch = CodeRegex.Match(argumentsJson);
            if (!codeMatch.Success)
            {
                return false;
            }

            var code = UnescapeJsonString(codeMatch.Groups["code"].Value);
            return WritesOnlyToWipSheets(code)
                && IsDeclaredVbaWriteScopeProvable(code, declaredTargetNames, declaredNewTargetNames);
        }

        public static string ExtractVbaCode(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return string.Empty;
            }

            var match = CodeRegex.Match(argumentsJson);
            return match.Success
                ? UnescapeJsonString(match.Groups["code"].Value)
                : string.Empty;
        }

        public static bool TryGetDeclaredVbaWriteTargetSheetNames(
            string argumentsJson,
            out IList<string> targetSheetNames,
            out IList<string> newWorksheetNames)
        {
            targetSheetNames = new List<string>();
            newWorksheetNames = new List<string>();
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return false;
            }

            var targetsMatch = DeclaredTargetsArrayRegex.Match(argumentsJson);
            if (!targetsMatch.Success)
            {
                return false;
            }

            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match targetObject in DeclaredTargetObjectRegex.Matches(targetsMatch.Groups["targets"].Value))
            {
                var sheetMatch = DeclaredTargetSheetRegex.Match(targetObject.Groups["target"].Value);
                if (!sheetMatch.Success)
                {
                    return false;
                }

                var sheetName = sheetMatch.Groups["name"].Value.Trim();
                if (sheetName.Length == 0 || !uniqueNames.Add(sheetName))
                {
                    return false;
                }

                targetSheetNames.Add(sheetName);
                if (DeclaredTargetIsNewRegex.IsMatch(targetObject.Groups["target"].Value))
                {
                    newWorksheetNames.Add(sheetName);
                }
            }

            return targetSheetNames.Count > 0;
        }

        /// <summary>
        /// Returns true only for the small, statically understandable VBA subset whose
        /// worksheet writes are bound to declared targets. Any ambiguous reference must
        /// retain the conservative modify classification.
        /// </summary>
        public static bool IsDeclaredVbaWriteScopeProvable(
            string code,
            IEnumerable<string> declaredTargetSheetNames,
            IEnumerable<string> declaredNewWorksheetNames)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (declaredTargetSheetNames != null)
            {
                foreach (var name in declaredTargetSheetNames)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        targets.Add(name.Trim());
                    }
                }
            }

            if (targets.Count == 0 || string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            var newTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (declaredNewWorksheetNames != null)
            {
                foreach (var name in declaredNewWorksheetNames)
                {
                    if (!string.IsNullOrWhiteSpace(name) && targets.Contains(name.Trim()))
                    {
                        newTargets.Add(name.Trim());
                    }
                }
            }

            if (ContainsAny(
                code,
                "ActiveSheet",
                "ActiveCell",
                "Selection",
                "ActiveWorkbook",
                "Workbooks(",
                "Application.Run",
                "ExecuteExcel4Macro",
                "CallByName",
                "Evaluate",
                "DoEvents",
                "SendKeys"))
            {
                return false;
            }

            var targetVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nonTargetVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unnamedAddedSheetVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var rawLine in lines)
            {
                var line = StripComment(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (ProtectionMethodRegex.IsMatch(line)
                    || IndexedSheetReferenceRegex.IsMatch(line)
                    || CodeNameReferenceRegex.IsMatch(line)
                    || WithBlockRegex.IsMatch(line)
                    || EndWithRegex.IsMatch(line)
                    || line.IndexOf(':') >= 0)
                {
                    return false;
                }

                var addedSheetBinding = SheetAddBindingRegex.Match(line);
                if (addedSheetBinding.Success)
                {
                    var variableName = addedSheetBinding.Groups["var"].Value;
                    unnamedAddedSheetVariables.Add(variableName);
                    targetVariables.Remove(variableName);
                    nonTargetVariables.Remove(variableName);
                    continue;
                }

                var sheetNameAssignment = SheetNameAssignmentRegex.Match(line);
                if (sheetNameAssignment.Success)
                {
                    var variableName = sheetNameAssignment.Groups["var"].Value;
                    if (unnamedAddedSheetVariables.Contains(variableName))
                    {
                        var assignedName = sheetNameAssignment.Groups["name"].Value;
                        if (!newTargets.Contains(assignedName))
                        {
                            return false;
                        }

                        unnamedAddedSheetVariables.Remove(variableName);
                        targetVariables.Add(variableName);
                        nonTargetVariables.Remove(variableName);
                        continue;
                    }
                }

                if (SheetAddRegex.IsMatch(line))
                {
                    return false;
                }

                var binding = SheetBindingRegex.Match(line);
                if (binding.Success)
                {
                    var variableName = binding.Groups["var"].Value;
                    var sheetName = SheetLiteralRegex.Match(line).Groups["name"].Value;
                    unnamedAddedSheetVariables.Remove(variableName);
                    if (targets.Contains(sheetName))
                    {
                        if (newTargets.Contains(sheetName))
                        {
                            return false;
                        }

                        targetVariables.Add(variableName);
                        nonTargetVariables.Remove(variableName);
                    }
                    else
                    {
                        nonTargetVariables.Add(variableName);
                        targetVariables.Remove(variableName);
                    }

                    continue;
                }

                foreach (var variableName in unnamedAddedSheetVariables)
                {
                    if (Regex.IsMatch(line, "\\b" + Regex.Escape(variableName) + "\\b"))
                    {
                        return false;
                    }
                }

                if (!ContainsMutatingToken(line))
                {
                    continue;
                }

                if (UnqualifiedRangeReferenceRegex.IsMatch(line))
                {
                    return false;
                }

                var hasDeclaredTargetReference = false;
                foreach (Match sheetReference in SheetLiteralRegex.Matches(line))
                {
                    var sheetName = sheetReference.Groups["name"].Value;
                    if (!targets.Contains(sheetName))
                    {
                        return false;
                    }

                    hasDeclaredTargetReference = true;
                }

                foreach (Match member in ObjectMemberRegex.Matches(line))
                {
                    var variableName = member.Groups["name"].Value;
                    if (nonTargetVariables.Contains(variableName))
                    {
                        return false;
                    }

                    if (unnamedAddedSheetVariables.Contains(variableName))
                    {
                        return false;
                    }

                    if (targetVariables.Contains(variableName))
                    {
                        hasDeclaredTargetReference = true;
                    }
                    else if (!string.Equals(variableName, "ThisWorkbook", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(variableName, "Worksheets", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(variableName, "Sheets", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                if (!hasDeclaredTargetReference)
                {
                    return false;
                }
            }

            return unnamedAddedSheetVariables.Count == 0;
        }

        private static bool IsWipSheet(string name)
            => !string.IsNullOrWhiteSpace(name)
               && name.Trim().StartsWith(WipPrefix, StringComparison.OrdinalIgnoreCase);

        private static bool WritesOnlyToWipSheets(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return false;
            }

            // 隐式引用意味着执行时的目标表由 Excel 当前状态决定，静态上无法证明它只碰中间表。
            if (ContainsAny(code, "ActiveSheet", "ActiveCell", "Selection", "ActiveWorkbook", "Workbooks("))
            {
                return false;
            }

            var originalSheetVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wipSheetVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unnamedAddedSheetVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            foreach (var rawLine in lines)
            {
                var line = StripComment(rawLine);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (ProtectionMethodRegex.IsMatch(line)
                    || IndexedSheetReferenceRegex.IsMatch(line)
                    || CodeNameReferenceRegex.IsMatch(line))
                {
                    return false;
                }

                if (EndWithRegex.IsMatch(line))
                {
                    continue;
                }

                var addedSheetBinding = SheetAddBindingRegex.Match(line);
                if (addedSheetBinding.Success)
                {
                    var variableName = addedSheetBinding.Groups["var"].Value;
                    unnamedAddedSheetVariables.Add(variableName);
                    originalSheetVariables.Remove(variableName);
                    wipSheetVariables.Remove(variableName);
                    continue;
                }

                if (SheetAddRegex.IsMatch(line))
                {
                    return false;
                }

                var addedSheetNameAssignment = SheetNameAssignmentRegex.Match(line);
                if (addedSheetNameAssignment.Success)
                {
                    var variableName = addedSheetNameAssignment.Groups["var"].Value;
                    if (unnamedAddedSheetVariables.Contains(variableName))
                    {
                        if (!IsWipSheet(addedSheetNameAssignment.Groups["name"].Value))
                        {
                            return false;
                        }

                        unnamedAddedSheetVariables.Remove(variableName);
                        wipSheetVariables.Add(variableName);
                        continue;
                    }
                }

                foreach (var variableName in unnamedAddedSheetVariables)
                {
                    if (Regex.IsMatch(line, "\\b" + Regex.Escape(variableName) + "\\b"))
                    {
                        return false;
                    }
                }

                var withBlock = WithBlockRegex.Match(line);
                if (withBlock.Success)
                {
                    var expression = withBlock.Groups["expression"].Value;
                    var sheetLiteral = SheetLiteralRegex.Match(expression);
                    if (sheetLiteral.Success)
                    {
                        if (!IsWipSheet(sheetLiteral.Groups["name"].Value))
                        {
                            return false;
                        }

                        continue;
                    }

                    var variable = WithVariableRegex.Match(expression);
                    if (!variable.Success)
                    {
                        return false;
                    }

                    var variableName = variable.Groups["variable"].Value;
                    if (originalSheetVariables.Contains(variableName)
                        || !wipSheetVariables.Contains(variableName))
                    {
                        return false;
                    }

                    continue;
                }

                var binding = SheetBindingRegex.Match(line);
                if (binding.Success)
                {
                    var variableName = binding.Groups["var"].Value;
                    unnamedAddedSheetVariables.Remove(variableName);
                    if (IsWipSheet(binding.Groups["name"].Value))
                    {
                        wipSheetVariables.Add(variableName);
                        originalSheetVariables.Remove(variableName);
                    }
                    else
                    {
                        originalSheetVariables.Add(variableName);
                        wipSheetVariables.Remove(variableName);
                    }

                    continue;
                }

                if (!ContainsMutatingToken(line))
                {
                    continue;
                }

                foreach (Match sheetReference in SheetLiteralRegex.Matches(line))
                {
                    if (!IsWipSheet(sheetReference.Groups["name"].Value))
                    {
                        return false;
                    }
                }

                foreach (var variable in originalSheetVariables)
                {
                    if (Regex.IsMatch(line, "\\b" + Regex.Escape(variable) + "\\s*\\."))
                    {
                        return false;
                    }
                }
            }

            return unnamedAddedSheetVariables.Count == 0;
        }

        private static bool ContainsMutatingToken(string line)
        {
            foreach (var token in MutatingTokens)
            {
                if (line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            foreach (var value in values)
            {
                if (text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string StripComment(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }

            var inString = false;
            for (var index = 0; index < line.Length; index++)
            {
                var current = line[index];
                if (current == '"')
                {
                    inString = !inString;
                }
                else if (current == '\'' && !inString)
                {
                    return line.Substring(0, index);
                }
            }

            return line;
        }

        private static string UnescapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            return value
                .Replace("\\r\\n", "\n")
                .Replace("\\n", "\n")
                .Replace("\\r", "\n")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
    }
}
