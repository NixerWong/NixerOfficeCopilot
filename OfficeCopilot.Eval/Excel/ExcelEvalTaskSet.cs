using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace OfficeCopilot.Eval.Excel
{
    internal sealed class ExcelEvalTaskSetDocument
    {
        public int SchemaVersion { get; set; }

        public string FixtureDirectory { get; set; }

        public int RunsPerTask { get; set; }

        public List<ExcelEvalTaskDefinition> Tasks { get; set; }
    }

    internal sealed class ExcelEvalTaskDefinition
    {
        public string Id { get; set; }

        public string Fixture { get; set; }

        public string Skill { get; set; }

        public string RoutingMode { get; set; }

        public string ExecutionMode { get; set; }

        public string Prompt { get; set; }

        public List<string> Tags { get; set; }

        public Dictionary<string, object> Expect { get; set; }

        public Dictionary<string, object> Scenario { get; set; }
    }

    internal sealed class LoadedExcelEvalTaskSet
    {
        public string TaskSetPath { get; set; }

        public string FixtureDirectory { get; set; }

        public ExcelEvalTaskSetDocument Document { get; set; }

        public Dictionary<string, Dictionary<string, object>> AnswersByFixture { get; set; }

        public ExcelEvalTaskDefinition FindTask(string taskId)
        {
            return Document.Tasks.FirstOrDefault(
                task => string.Equals(task.Id, taskId, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class ExcelEvalTaskSetLoader
    {
        private static readonly Regex _answerPathToken = new Regex(
            @"\[\*\]|[^.\[\]]+",
            RegexOptions.Compiled);

        private static readonly HashSet<string> _knownSkills = new HashSet<string>(
            new[] { "read-analysis", "live-result", "structural-transform", "bulk-format" },
            StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> _knownWorkbookAssertionTypes = new HashSet<string>(
            new[]
            {
                "formulaRange",
                "rangeSumEquals",
                "columnSumEquals",
                "rangeValuesEqual",
                "sheetRowCount",
                "valueCount",
                "keySetEquals",
                "numberFormat",
                "numericCellsNumberFormat",
                "errorCellsRetained",
                "boldRange"
            },
            StringComparer.Ordinal);

        public static LoadedExcelEvalTaskSet Load(string taskSetPath, string fixtureDirectoryOverride)
        {
            if (string.IsNullOrWhiteSpace(taskSetPath))
            {
                throw new ArgumentException("A task-set path is required.", nameof(taskSetPath));
            }

            taskSetPath = Path.GetFullPath(taskSetPath);
            if (!File.Exists(taskSetPath))
            {
                throw new FileNotFoundException("Excel Eval task-set file not found.", taskSetPath);
            }

            var serializer = CreateSerializer();
            var document = serializer.Deserialize<ExcelEvalTaskSetDocument>(
                File.ReadAllText(taskSetPath));
            ValidateDocument(document, taskSetPath);

            var taskSetDirectory = Path.GetDirectoryName(taskSetPath);
            var fixtureDirectory = string.IsNullOrWhiteSpace(fixtureDirectoryOverride)
                ? Path.GetFullPath(Path.Combine(taskSetDirectory, document.FixtureDirectory))
                : Path.GetFullPath(fixtureDirectoryOverride);
            if (!Directory.Exists(fixtureDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Excel Eval fixture directory not found: " + fixtureDirectory);
            }

            var answersByFixture = new Dictionary<string, Dictionary<string, object>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var task in document.Tasks)
            {
                ValidateTask(task);

                var workbookPath = Path.Combine(fixtureDirectory, task.Fixture + ".xlsx");
                var answerPath = Path.Combine(fixtureDirectory, task.Fixture + ".answers.json");
                if (!File.Exists(workbookPath))
                {
                    throw new FileNotFoundException(
                        "Workbook fixture for task '" + task.Id + "' was not found.",
                        workbookPath);
                }

                if (!File.Exists(answerPath))
                {
                    throw new FileNotFoundException(
                        "Answer fixture for task '" + task.Id + "' was not found.",
                        answerPath);
                }

                if (!answersByFixture.ContainsKey(task.Fixture))
                {
                    var answerDocument = serializer.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(answerPath));
                    object answerRoot;
                    if (answerDocument == null
                        || !answerDocument.TryGetValue("answers", out answerRoot)
                        || !(answerRoot is Dictionary<string, object>))
                    {
                        throw new InvalidDataException(
                            "Answer fixture must contain an 'answers' object: " + answerPath);
                    }

                    answersByFixture.Add(
                        task.Fixture,
                        (Dictionary<string, object>)answerRoot);
                }

                ValidateAnswerReferences(task, answersByFixture[task.Fixture]);
            }

            return new LoadedExcelEvalTaskSet
            {
                TaskSetPath = taskSetPath,
                FixtureDirectory = fixtureDirectory,
                Document = document,
                AnswersByFixture = answersByFixture
            };
        }

        private static void ValidateDocument(ExcelEvalTaskSetDocument document, string taskSetPath)
        {
            if (document == null)
            {
                throw new InvalidDataException("Excel Eval task-set JSON is empty or invalid: " + taskSetPath);
            }

            if (document.SchemaVersion != 1)
            {
                throw new InvalidDataException(
                    "Unsupported Excel Eval task-set schemaVersion: " + document.SchemaVersion);
            }

            if (string.IsNullOrWhiteSpace(document.FixtureDirectory))
            {
                throw new InvalidDataException("Excel Eval task-set fixtureDirectory is required.");
            }

            if (document.RunsPerTask < 1)
            {
                throw new InvalidDataException("Excel Eval task-set runsPerTask must be at least 1.");
            }

            if (document.Tasks == null || document.Tasks.Count == 0)
            {
                throw new InvalidDataException("Excel Eval task-set must contain at least one task.");
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in document.Tasks)
            {
                if (task == null || string.IsNullOrWhiteSpace(task.Id))
                {
                    throw new InvalidDataException("Every Excel Eval task must have an id.");
                }

                if (!ids.Add(task.Id))
                {
                    throw new InvalidDataException("Duplicate Excel Eval task id: " + task.Id);
                }
            }

            var realRoutingTaskCount = document.Tasks.Count(task =>
                task != null
                && string.Equals(
                    (task.RoutingMode ?? string.Empty).Trim(),
                    "real",
                    StringComparison.OrdinalIgnoreCase));
            if (realRoutingTaskCount > 0
                && ((double)realRoutingTaskCount / document.Tasks.Count) < 0.30d)
            {
                throw new InvalidDataException(
                    "Task sets using real skill routing must apply it to at least 30% of tasks.");
            }
        }

        private static void ValidateTask(ExcelEvalTaskDefinition task)
        {
            if (string.IsNullOrWhiteSpace(task.Fixture)
                || !string.Equals(Path.GetFileName(task.Fixture), task.Fixture, StringComparison.Ordinal)
                || task.Fixture.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidDataException(
                    "Task '" + task.Id + "' has an invalid fixture name.");
            }

            var routingMode = string.IsNullOrWhiteSpace(task.RoutingMode)
                ? "pinned"
                : task.RoutingMode.Trim();
            if (!string.Equals(routingMode, "pinned", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(routingMode, "real", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Task '" + task.Id + "' has an unsupported routingMode: " + routingMode);
            }

            if (!_knownSkills.Contains(task.Skill ?? string.Empty))
            {
                throw new InvalidDataException(
                    "Task '" + task.Id + "' uses an unknown skill: " + (task.Skill ?? string.Empty));
            }

            if (string.IsNullOrWhiteSpace(task.Prompt))
            {
                throw new InvalidDataException("Task '" + task.Id + "' has no prompt.");
            }

            if (task.Expect == null)
            {
                throw new InvalidDataException("Task '" + task.Id + "' has no expect rubric.");
            }

            var requiresClarification = task.Tags != null && task.Tags.Any(tag =>
                string.Equals(tag, "clarification-required", StringComparison.OrdinalIgnoreCase));
            var isCasualRouting = task.Tags != null && task.Tags.Any(tag =>
                string.Equals(tag, "casual-routing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "fuzzy-routing", StringComparison.OrdinalIgnoreCase));
            object clarificationOnlyValue;
            var clarificationOnly = task.Expect.TryGetValue("clarificationOnly", out clarificationOnlyValue)
                && Equals(clarificationOnlyValue, true);
            if (requiresClarification != clarificationOnly || (requiresClarification && isCasualRouting))
            {
                throw new InvalidDataException(
                    "Task '" + task.Id
                    + "' must use clarification-required and clarificationOnly=true together, "
                    + "separately from casual-routing.");
            }

            if (string.Equals(task.ExecutionMode, "automatic-read-only", StringComparison.OrdinalIgnoreCase))
            {
                object unchanged;
                if (!string.Equals(task.Skill, "read-analysis", StringComparison.OrdinalIgnoreCase)
                    || !task.Expect.TryGetValue("workbookUnchanged", out unchanged)
                    || !Equals(unchanged, true))
                {
                    throw new InvalidDataException(
                        "Automatic task '" + task.Id + "' must use read-analysis and require workbookUnchanged=true.");
                }

            }
            else if (!string.Equals(task.ExecutionMode, "manual-confirmation", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Task '" + task.Id + "' has an unsupported executionMode: " + (task.ExecutionMode ?? string.Empty));
            }
            else if (clarificationOnly)
            {
                throw new InvalidDataException(
                    "Clarification-only task '" + task.Id + "' must be automatic-read-only.");
            }
            else
            {
                object unchanged;
                object assertionValue;
                object traceValue;
                if (!task.Expect.TryGetValue("workbookUnchanged", out unchanged)
                    || !Equals(unchanged, false)
                    || !task.Expect.TryGetValue("workbookAssertions", out assertionValue)
                    || !(assertionValue is IList)
                    || ((IList)assertionValue).Count == 0
                    || !task.Expect.TryGetValue("trace", out traceValue)
                    || !(traceValue is Dictionary<string, object>))
                {
                    throw new InvalidDataException(
                        "Manual task '" + task.Id
                        + "' must declare workbookUnchanged=false, workbookAssertions, and trace requirements.");
                }

                var assertions = (IList)assertionValue;
                foreach (var item in assertions)
                {
                    var assertion = item as Dictionary<string, object>;
                    object typeValue;
                    var type = assertion != null && assertion.TryGetValue("type", out typeValue)
                        ? Convert.ToString(typeValue)
                        : string.Empty;
                    if (!_knownWorkbookAssertionTypes.Contains(type))
                    {
                        throw new InvalidDataException(
                            "Manual task '" + task.Id
                            + "' uses an unsupported workbook assertion type: " + type);
                    }

                    if (string.Equals(type, "formulaRange", StringComparison.Ordinal))
                    {
                        object formulaTemplate;
                        object formulaTemplatesAny;
                        var hasFormulaTemplate = assertion.TryGetValue("formulaTemplate", out formulaTemplate)
                            && !string.IsNullOrWhiteSpace(Convert.ToString(formulaTemplate));
                        var hasFormulaAlternatives =
                            assertion.TryGetValue("formulaTemplatesAny", out formulaTemplatesAny)
                            && formulaTemplatesAny is IList
                            && ((IList)formulaTemplatesAny).Count > 0;
                        if (!hasFormulaTemplate && !hasFormulaAlternatives)
                        {
                            throw new InvalidDataException(
                                "Manual task '" + task.Id
                                + "' formulaRange must declare formulaTemplate or formulaTemplatesAny.");
                        }
                    }
                }

                object expectedChangeScope;
                if (!task.Expect.TryGetValue("expectedChangeScope", out expectedChangeScope)
                    || !(expectedChangeScope is IList))
                {
                    throw new InvalidDataException(
                        "Manual task '" + task.Id + "' must declare expectedChangeScope.");
                }

                try
                {
                    WorkbookChangeScopeAuditor.ValidateExpectedScope((IList)expectedChangeScope);
                }
                catch (InvalidDataException exception)
                {
                    throw new InvalidDataException(
                        "Manual task '" + task.Id + "' has an invalid expectedChangeScope: "
                        + exception.Message,
                        exception);
                }

                var trace = (Dictionary<string, object>)traceValue;
                object requiredTools;
                if (!trace.TryGetValue("requiredToolsAny", out requiredTools)
                    || !(requiredTools is IList)
                    || ((IList)requiredTools).Count == 0)
                {
                    throw new InvalidDataException(
                        "Manual task '" + task.Id + "' must require at least one write tool.");
                }

                object confirmationTools;
                if (!task.Expect.TryGetValue("confirmationTools", out confirmationTools)
                    || !(confirmationTools is IList)
                    || ((IList)confirmationTools).Count == 0)
                {
                    throw new InvalidDataException(
                        "Manual task '" + task.Id
                        + "' must declare the write tools whose confirmations may be auto-approved.");
                }

                var allowedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var confirmationTool in (IList)confirmationTools)
                {
                    var toolName = Convert.ToString(confirmationTool);
                    if (string.IsNullOrWhiteSpace(toolName))
                    {
                        throw new InvalidDataException(
                            "Manual task '" + task.Id + "' has an empty confirmation tool name.");
                    }

                    allowedTools.Add(toolName);
                }

                foreach (var requiredTool in (IList)requiredTools)
                {
                    var toolName = Convert.ToString(requiredTool);
                    if (string.IsNullOrWhiteSpace(toolName) || !allowedTools.Contains(toolName))
                    {
                        throw new InvalidDataException(
                            "Manual task '" + task.Id + "' requires tool '" + toolName
                            + "' but does not allow its confirmation.");
                    }
                }
            }
        }

        private static void ValidateAnswerReferences(
            ExcelEvalTaskDefinition task,
            IDictionary<string, object> answerRoot)
        {
            foreach (var reference in FindAnswerReferences(task.Expect))
            {
                ResolveAnswerPath(answerRoot, reference);
            }
        }

        private static IEnumerable<string> FindAnswerReferences(object value)
        {
            if (value is IDictionary<string, object>)
            {
                foreach (var pair in (IDictionary<string, object>)value)
                {
                    if ((string.Equals(pair.Key, "from", StringComparison.Ordinal)
                            || string.Equals(pair.Key, "answer", StringComparison.Ordinal)
                            || string.Equals(pair.Key, "countAnswer", StringComparison.Ordinal))
                        && pair.Value is string
                        && ((string)pair.Value).StartsWith("answers.", StringComparison.Ordinal))
                    {
                        yield return (string)pair.Value;
                    }

                    foreach (var nested in FindAnswerReferences(pair.Value))
                    {
                        yield return nested;
                    }
                }
            }
            else if (value is IEnumerable && !(value is string))
            {
                foreach (var item in (IEnumerable)value)
                {
                    foreach (var nested in FindAnswerReferences(item))
                    {
                        yield return nested;
                    }
                }
            }
        }

        public static IList<object> ResolveAnswerPath(
            IDictionary<string, object> answerRoot,
            string answerPath)
        {
            if (answerRoot == null)
            {
                throw new ArgumentNullException(nameof(answerRoot));
            }

            if (string.IsNullOrWhiteSpace(answerPath)
                || !answerPath.StartsWith("answers.", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Answer path must start with 'answers.': " + answerPath);
            }

            var tokens = _answerPathToken.Matches(answerPath.Substring("answers.".Length));
            var current = new List<object> { answerRoot };
            foreach (Match tokenMatch in tokens)
            {
                var token = tokenMatch.Value;
                var next = new List<object>();
                if (string.Equals(token, "[*]", StringComparison.Ordinal))
                {
                    foreach (var value in current)
                    {
                        var values = value as IList;
                        if (values == null)
                        {
                            throw new InvalidDataException(
                                "Answer path wildcard does not address an array: " + answerPath);
                        }

                        foreach (var item in values)
                        {
                            next.Add(item);
                        }
                    }
                }
                else if (string.Equals(token, "length", StringComparison.Ordinal))
                {
                    foreach (var value in current)
                    {
                        var values = value as IList;
                        if (values != null)
                        {
                            next.Add(values.Count);
                        }
                        else if (value is string)
                        {
                            next.Add(((string)value).Length);
                        }
                        else
                        {
                            throw new InvalidDataException(
                                "Answer path 'length' does not address an array or string: " + answerPath);
                        }
                    }
                }
                else
                {
                    foreach (var value in current)
                    {
                        var dictionary = value as IDictionary<string, object>;
                        object nextValue;
                        if (dictionary == null || !dictionary.TryGetValue(token, out nextValue))
                        {
                            throw new InvalidDataException(
                                "Answer path '" + answerPath + "' cannot resolve member '" + token + "'.");
                        }

                        next.Add(nextValue);
                    }
                }

                current = next;
            }

            if (current.Count == 0)
            {
                throw new InvalidDataException("Answer path resolved to no values: " + answerPath);
            }

            return current;
        }

        public static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 128
            };
        }
    }
}
