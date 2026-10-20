using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using GCore.Antlr.Grammers.Vba;

namespace OfficeCopilot.Host.Excel.Tools
{
    public static class VbaSyntaxValidator
    {
        public sealed class VbaPreCheckResult
        {
            public bool IsValid { get; set; }
            public List<VbaSyntaxIssue> Issues { get; set; } = new List<VbaSyntaxIssue>();

            public string ToDiagnosticMessage()
            {
                if (IsValid) return string.Empty;
                var sb = new StringBuilder();
                sb.AppendLine("VBA syntax check failed. Please fix and retry:");
                sb.AppendLine();
                for (int i = 0; i < Issues.Count; i++)
                {
                    var issue = Issues[i];
                    sb.AppendLine(string.Format("{0}. [Line {1}, Col {2}] {3}", i + 1, issue.LineNumber, issue.Column, issue.Message));
                    if (!string.IsNullOrWhiteSpace(issue.Snippet))
                        sb.AppendLine("   Code: " + issue.Snippet);
                    if (!string.IsNullOrWhiteSpace(issue.Suggestion))
                        sb.AppendLine("   Hint: " + issue.Suggestion);
                    sb.AppendLine();
                }
                return sb.ToString().TrimEnd();
            }
        }

        public sealed class VbaSyntaxIssue
        {
            public int LineNumber { get; set; }
            public int Column { get; set; }
            public string Message { get; set; }
            public string Snippet { get; set; }
            public string Suggestion { get; set; }
        }

        private static readonly Regex PythonDef = new Regex(@"^\s*def\s+[\p{L}_]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PythonImport = new Regex(@"^\s*import\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PythonPrint = new Regex(@"\bPrint\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CSharpVar = new Regex(@"^\s*var\s+[\p{L}_]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CSharpConsole = new Regex(@"\bConsole\.(Write|WriteLine)\(", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VariableDeclarationStart = new Regex(
            @"^\s*(?:Dim|Static)\s+(?<declarations>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex VariableName = new Regex(
            @"^\s*(?<name>[\p{L}_][\p{L}\p{Nd}_]*)",
            RegexOptions.Compiled);
        private static readonly HashSet<string> ReservedDeclarationNames = new HashSet<string>(
            new[]
            {
                "Alias", "And", "As", "Boolean", "ByRef", "Byte", "ByVal", "Call", "Case", "Const",
                "Currency", "Date", "Decimal", "Declare", "Dim", "Do", "Double", "Each", "Else",
                "ElseIf", "End", "Enum", "Eqv", "Erase", "Error", "Event", "Exit", "False", "For",
                "Function", "Get", "Global", "GoSub", "GoTo", "If", "Imp", "Implements", "In",
                "Integer", "Is", "Let", "Lib", "Like", "Long", "Loop", "Me", "Mod", "New", "Next",
                "Not", "Nothing", "Object", "On", "Option", "Optional", "Or", "ParamArray", "Preserve",
                "Private", "Property", "Public", "RaiseEvent", "ReDim", "Rem", "Resume", "Select",
                "Set", "Single", "Static", "Step", "Stop", "String", "Sub", "Then", "To", "True",
                "Type", "TypeOf", "Variant", "Wend", "While", "With", "WithEvents", "Xor"
            },
            StringComparer.OrdinalIgnoreCase);

        // Procedure declaration: Sub|Function|Property with name
        private static readonly Regex ProcStartRegex = new Regex(
            @"^\s*(?:(?:Public|Private|Friend)\s+)?(?:Static\s+)?(Sub|Function|Property\s+(?:Get|Let|Set))\s+([\p{L}_][\p{L}\p{Nd}_]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Procedure end: End Sub|End Function|End Property
        private static readonly Regex ProcEndRegex = new Regex(
            @"^\s*End\s+(Sub|Function|Property)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static VbaPreCheckResult Validate(string vbaCode)
        {
            var result = new VbaPreCheckResult();
            if (string.IsNullOrWhiteSpace(vbaCode))
            {
                result.IsValid = false;
                result.Issues.Add(new VbaSyntaxIssue { LineNumber = 0, Column = 0, Message = "VBA code is empty.", Suggestion = "Provide a valid Sub or Function procedure." });
                return result;
            }
            var code = vbaCode.Trim();
            var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            RunAntlrParse(code, lines, result);
            RunCrossLanguageCheck(lines, result);
            RunReservedDeclarationCheck(lines, result);
            RunProcedureCheck(lines, result);
            result.IsValid = result.Issues.Count == 0;
            return result;
        }

        private static void RunAntlrParse(string code, string[] lines, VbaPreCheckResult result)
        {
            try
            {
                var inputStream = new AntlrInputStream(code);
                var lexer = new vbaLexer(inputStream);
                var tokenStream = new CommonTokenStream(lexer);
                var parser = new vbaParser(tokenStream);
                parser.RemoveErrorListeners();
                var listener = new ErrorCollector(lines);
                parser.AddErrorListener(listener);
                parser.startRule();
                result.Issues.AddRange(listener.Errors);
            }
            catch (Exception ex)
            {
                result.Issues.Add(new VbaSyntaxIssue { LineNumber = 0, Column = 0, Message = "Parser error: " + ex.Message });
            }
        }

        private sealed class ErrorCollector : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
        {
            private readonly string[] _lines;
            public List<VbaSyntaxIssue> Errors { get; } = new List<VbaSyntaxIssue>();
            public ErrorCollector(string[] lines) { _lines = lines ?? new string[0]; }

            public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
            {
                Errors.Add(Build(line, charPositionInLine, msg));
            }

            public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
            {
                Errors.Add(Build(line, charPositionInLine, msg));
            }

            private VbaSyntaxIssue Build(int line, int col, string rawMsg)
            {
                var snippet = string.Empty;
                if (line >= 1 && line <= _lines.Length)
                {
                    snippet = _lines[line - 1];
                    if (snippet.Length > 120) snippet = snippet.Substring(0, 117) + "...";
                }
                var msg = (rawMsg ?? "Unknown error")
                    .Replace("extraneous input", "Unexpected token")
                    .Replace("expecting", "expected")
                    .Replace("mismatched input", "Mismatched token")
                    .Replace("no viable alternative at input", "Cannot parse near");
                var lower = msg.ToLowerInvariant();
                string hint;
                if (lower.Contains("mismatched") || lower.Contains("expecting"))
                    hint = "Check if a keyword, operator, or identifier is missing or misspelled.";
                else if (lower.Contains("unexpected token") || lower.Contains("extraneous"))
                    hint = "An unexpected token was found. This may be a typo or extra character.";
                else if (lower.Contains("cannot parse"))
                    hint = "The parser cannot understand this line. It may use syntax from another language.";
                else if (lower.Contains("missing"))
                    hint = "A required element is missing (keyword, operator, or closing delimiter).";
                else
                    hint = "Review the VBA syntax around this location.";
                return new VbaSyntaxIssue { LineNumber = line, Column = col + 1, Message = msg, Snippet = snippet, Suggestion = hint };
            }
        }

        private static void RunCrossLanguageCheck(string[] lines, VbaPreCheckResult result)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.StartsWith("'") || trimmed.ToUpperInvariant().StartsWith("REM ")) continue;
                if (PythonDef.IsMatch(trimmed))
                    result.Issues.Add(Make(i, trimmed, "Python def is not VBA.", "Use Sub or Function."));
                else if (PythonImport.IsMatch(trimmed))
                    result.Issues.Add(Make(i, trimmed, "Python import is not VBA.", "Use Tools References or CreateObject/GetObject."));
                else if (PythonPrint.IsMatch(trimmed) && !trimmed.ToUpperInvariant().Contains("DEBUG.PRINT"))
                    result.Issues.Add(Make(i, trimmed, "Python Print() is not VBA.", "Use Debug.Print."));
                else if (CSharpVar.IsMatch(trimmed))
                    result.Issues.Add(Make(i, trimmed, "C# var is not VBA.", "Use Dim to declare variables."));
                else if (CSharpConsole.IsMatch(trimmed))
                    result.Issues.Add(Make(i, trimmed, "C# Console.Write/Line is not VBA.", "Use Debug.Print."));
            }
        }

        private static VbaSyntaxIssue Make(int lineIdx, string snippet, string msg, string hint)
        {
            return new VbaSyntaxIssue { LineNumber = lineIdx + 1, Column = 1, Message = msg, Snippet = snippet, Suggestion = hint };
        }

        private static void RunReservedDeclarationCheck(string[] lines, VbaPreCheckResult result)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (ProcStartRegex.IsMatch(lines[i]))
                {
                    continue;
                }

                var declarationMatch = VariableDeclarationStart.Match(lines[i]);
                if (!declarationMatch.Success)
                {
                    continue;
                }

                var declarations = declarationMatch.Groups["declarations"].Value;
                foreach (var declaration in SplitTopLevelDeclarations(declarations))
                {
                    var nameMatch = VariableName.Match(declaration);
                    if (!nameMatch.Success)
                    {
                        continue;
                    }

                    var name = nameMatch.Groups["name"].Value;
                    if (!ReservedDeclarationNames.Contains(name))
                    {
                        continue;
                    }

                    var column = lines[i].IndexOf(
                        name,
                        declarationMatch.Groups["declarations"].Index,
                        StringComparison.OrdinalIgnoreCase) + 1;
                    result.Issues.Add(new VbaSyntaxIssue
                    {
                        LineNumber = i + 1,
                        Column = column,
                        Message = "Reserved VBA keyword '" + name + "' cannot be used as a variable name.",
                        Snippet = lines[i].Trim(),
                        Suggestion = "Rename the variable to a non-keyword identifier."
                    });
                }
            }
        }

        private static IEnumerable<string> SplitTopLevelDeclarations(string declarations)
        {
            var start = 0;
            var nesting = 0;
            var inString = false;
            for (var i = 0; i < declarations.Length; i++)
            {
                var character = declarations[i];
                if (character == '"')
                {
                    if (inString && i + 1 < declarations.Length && declarations[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    inString = !inString;
                }
                else if (!inString && character == '(')
                {
                    nesting++;
                }
                else if (!inString && character == ')')
                {
                    nesting--;
                }
                else if (!inString && nesting == 0 && character == ',')
                {
                    yield return declarations.Substring(start, i - start);
                    start = i + 1;
                }
            }

            yield return declarations.Substring(start);
        }

        private static void RunProcedureCheck(string[] lines, VbaPreCheckResult result)
        {
            var openProcs = new Stack<(string Type, string Name, int Line)>();
            bool hasAnyProc = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.StartsWith("'") || trimmed.ToUpperInvariant().StartsWith("REM ")) continue;
                if (trimmed.StartsWith("#")) continue;

                var startMatch = ProcStartRegex.Match(line);
                if (startMatch.Success)
                {
                    hasAnyProc = true;
                    openProcs.Push((startMatch.Groups[1].Value, startMatch.Groups[2].Value, i + 1));
                    continue;
                }

                var endMatch = ProcEndRegex.Match(line);
                if (endMatch.Success)
                {
                    if (openProcs.Count == 0)
                    {
                        result.Issues.Add(new VbaSyntaxIssue
                        {
                            LineNumber = i + 1, Column = 1,
                            Message = "Extra End " + endMatch.Groups[1].Value + " without matching procedure declaration.",
                            Snippet = trimmed,
                            Suggestion = "Check for an extra End Sub / End Function / End Property."
                        });
                    }
                    else
                    {
                        openProcs.Pop();
                    }
                }
            }

            while (openProcs.Count > 0)
            {
                var unclosed = openProcs.Pop();
                var expectedEnd = unclosed.Type.StartsWith("Property") ? "End Property"
                    : unclosed.Type == "Function" ? "End Function" : "End Sub";
                result.Issues.Add(new VbaSyntaxIssue
                {
                    LineNumber = unclosed.Line, Column = 1,
                    Message = string.Format("Procedure '{0}' ({1}) at line {2} is missing {3}.",
                        unclosed.Name, unclosed.Type, unclosed.Line, expectedEnd),
                    Snippet = lines[unclosed.Line - 1].Trim(),
                    Suggestion = string.Format("Add '{0}' at the end of procedure '{1}'.", expectedEnd, unclosed.Name)
                });
            }

            if (!hasAnyProc)
            {
                result.Issues.Add(new VbaSyntaxIssue
                {
                    LineNumber = 1, Column = 1,
                    Message = "No executable Sub or Function procedure found.",
                    Snippet = lines.Length > 0 ? lines[0].Trim() : string.Empty,
                    Suggestion = "Add at least one Sub or Function procedure. Example: Sub MyProc() ... End Sub"
                });
            }
        }
    }
}
