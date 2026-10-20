using OfficeCopilot.Eval.Excel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Eval
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args == null || args.Length == 0 || IsHelp(args[0]))
                {
                    PrintUsage();
                    return 0;
                }

                var command = args[0];
                var options = ParseOptions(args.Skip(1).ToArray());
                if (string.Equals(command, "validate", StringComparison.OrdinalIgnoreCase))
                {
                    return ValidateTaskSet(options);
                }

                if (string.Equals(command, "run", StringComparison.OrdinalIgnoreCase))
                {
                    return Run(options);
                }

                Console.Error.WriteLine("Unknown command: " + command);
                PrintUsage();
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Excel Eval failed:");
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static int ValidateTaskSet(IDictionary<string, string> options)
        {
            var taskSetPath = GetRequiredOption(options, "--task-set");
            string fixtureOverride;
            options.TryGetValue("--fixture-directory", out fixtureOverride);
            var taskSet = ExcelEvalTaskSetLoader.Load(taskSetPath, fixtureOverride);

            var automaticCount = taskSet.Document.Tasks.Count(
                task => string.Equals(
                    task.ExecutionMode,
                    "automatic-read-only",
                    StringComparison.OrdinalIgnoreCase));
            var manualCount = taskSet.Document.Tasks.Count - automaticCount;
            Console.WriteLine(
                "Task set valid: " + taskSet.Document.Tasks.Count.ToString(CultureInfo.InvariantCulture)
                + " tasks, " + automaticCount.ToString(CultureInfo.InvariantCulture)
                + " automatic read-only, " + manualCount.ToString(CultureInfo.InvariantCulture)
                + " manual confirmation.");
            Console.WriteLine("Fixtures: " + taskSet.FixtureDirectory);
            return 0;
        }

        private static int Run(IDictionary<string, string> options)
        {
            var taskSetPath = GetRequiredOption(options, "--task-set");
            var outputDirectory = GetRequiredOption(options, "--output-directory");
            var allReadOnly = options.ContainsKey("--all-read-only");
            var allManualConfirmation = options.ContainsKey("--all-manual-confirmation");
            var allowWorkbookWriteTests = options.ContainsKey("--allow-workbook-write-tests");
            string taskId;
            options.TryGetValue("--task", out taskId);
            var hasTaskId = !string.IsNullOrWhiteSpace(taskId);
            if ((allReadOnly ? 1 : 0) + (allManualConfirmation ? 1 : 0) + (hasTaskId ? 1 : 0) != 1)
            {
                throw new ArgumentException(
                    "Specify exactly one of --task <id>, --all-read-only, or --all-manual-confirmation.");
            }

            if (allManualConfirmation && !allowWorkbookWriteTests)
            {
                throw new ArgumentException(
                    "--all-manual-confirmation requires --allow-workbook-write-tests.");
            }

            string fixtureOverride;
            options.TryGetValue("--fixture-directory", out fixtureOverride);
            var taskSet = ExcelEvalTaskSetLoader.Load(taskSetPath, fixtureOverride);

            string pipeName;
            options.TryGetValue("--broker-pipe", out pipeName);
            string baselineSummaryPath;
            options.TryGetValue("--baseline-summary", out baselineSummaryPath);
            if (allowWorkbookWriteTests && !string.IsNullOrWhiteSpace(baselineSummaryPath))
            {
                throw new ArgumentException(
                    "Write-test runs cannot be compared to an automatic read-only baseline.");
            }

            string timeoutText;
            var timeoutSeconds = options.TryGetValue("--timeout-seconds", out timeoutText)
                ? int.Parse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture)
                : 600;

            var runner = new ExcelEvalRunner();
            var summary = runner.Run(new ExcelEvalRunnerOptions
            {
                TaskSet = taskSet,
                TaskId = taskId,
                RunAllReadOnlyTasks = allReadOnly,
                RunAllManualConfirmationTasks = allManualConfirmation,
                AllowWorkbookWriteTests = allowWorkbookWriteTests,
                OutputDirectory = outputDirectory,
                BrokerPipeName = pipeName,
                BaselineSummaryPath = baselineSummaryPath,
                TimeoutSeconds = timeoutSeconds
            });

            return summary.FailedRuns == 0 ? 0 : 1;
        }

        private static Dictionary<string, string> ParseOptions(IList<string> arguments)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < arguments.Count; index++)
            {
                var name = arguments[index];
                if (string.Equals(name, "--all-read-only", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "--all-manual-confirmation", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "--allow-workbook-write-tests", StringComparison.OrdinalIgnoreCase))
                {
                    if (options.ContainsKey(name))
                    {
                        throw new ArgumentException("Duplicate option: " + name);
                    }

                    options.Add(name, string.Empty);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name)
                    || !name.StartsWith("--", StringComparison.Ordinal)
                    || index + 1 >= arguments.Count
                    || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Invalid command-line option: " + name);
                }

                if (options.ContainsKey(name))
                {
                    throw new ArgumentException("Duplicate option: " + name);
                }

                options.Add(name, arguments[++index]);
            }

            var allowed = new HashSet<string>(
                new[]
                {
                    "--task-set",
                    "--fixture-directory",
                    "--output-directory",
                    "--task",
                    "--all-read-only",
                    "--all-manual-confirmation",
                    "--allow-workbook-write-tests",
                    "--broker-pipe",
                    "--baseline-summary",
                    "--timeout-seconds"
                },
                StringComparer.OrdinalIgnoreCase);
            var unsupported = options.Keys.FirstOrDefault(option => !allowed.Contains(option));
            if (unsupported != null)
            {
                throw new ArgumentException("Unsupported option: " + unsupported);
            }

            return options;
        }

        private static string GetRequiredOption(IDictionary<string, string> options, string name)
        {
            string value;
            if (!options.TryGetValue(name, out value) || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Missing required option: " + name);
            }

            return value;
        }

        private static bool IsHelp(string value)
        {
            return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "/?", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("OfficeCopilot.Eval");
            Console.WriteLine();
            Console.WriteLine("Validate task definitions and local fixtures:");
            Console.WriteLine("  OfficeCopilot.Eval.exe validate --task-set <tasks.json> [--fixture-directory <path>]");
            Console.WriteLine();
            Console.WriteLine("Run one read-only task (repeated runs use tasks.json runsPerTask):");
            Console.WriteLine("  OfficeCopilot.Eval.exe run --task-set <tasks.json> --task <id> --output-directory <path>");
            Console.WriteLine();
            Console.WriteLine("Run all automatic read-only tasks:");
            Console.WriteLine("  OfficeCopilot.Eval.exe run --task-set <tasks.json> --all-read-only --output-directory <path>");
            Console.WriteLine();
            Console.WriteLine("Run manual write tasks against per-run workbook copies (explicit opt-in):");
            Console.WriteLine("  OfficeCopilot.Eval.exe run --task-set <tasks.json> --all-manual-confirmation");
            Console.WriteLine("      --allow-workbook-write-tests --output-directory <path>");
            Console.WriteLine();
            Console.WriteLine("Optional: --fixture-directory <path>, --broker-pipe <name>,");
            Console.WriteLine("          --baseline-summary <summary.json>, --timeout-seconds <1-3600>.");
            Console.WriteLine("The Broker endpoint is discovered from the Excel process registry unless --broker-pipe is supplied.");
            Console.WriteLine("Write tests only modify isolated workbook copies stored under the output directory.");
        }
    }
}
