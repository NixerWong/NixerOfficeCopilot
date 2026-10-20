using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using OfficeCopilot.Agent.Runtime.Verification;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    internal sealed class CalculateTool
    {
        public const string ToolName = "runtime.calculate";

        private const int MaximumExpressionLength = 4096;
        private const int MaximumNodeCount = 512;
        private const int MaximumNestingDepth = 64;

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public ToolExecutionOutcome Execute(
            string toolCallId,
            string toolName,
            string argumentsJson,
            EvidenceLedger evidenceLedger)
        {
            if (!TryReadExpression(argumentsJson, out var expression, out var argumentError))
            {
                return Failure(toolCallId, toolName, "runtime.calculate.invalid_arguments", argumentError);
            }

            if (expression.Length > MaximumExpressionLength)
            {
                return Failure(
                    toolCallId,
                    toolName,
                    "runtime.calculate.expression_too_long",
                    "表达式过长，最多允许 4096 个字符。");
            }

            try
            {
                var parser = new DecimalExpressionParser(
                    expression,
                    MaximumNodeCount,
                    MaximumNestingDepth,
                    evidenceLedger);
                var value = parser.Evaluate();
                var isPercentage = parser.IsPercentageResult;
                var unsourcedOperands = parser.Operands
                    .Where(operand => !IsAllowedConstant(operand)
                        && (evidenceLedger == null || !evidenceLedger.ContainsOperand(operand)))
                    .Distinct()
                    .ToList();
                var operandsSourced = unsourcedOperands.Count == 0;
                var result = new CalculateToolResult
                {
                    Expression = expression,
                    Value = value,
                    IsPercentage = isPercentage,
                    OperandsSourced = operandsSourced,
                    UnsourcedOperands = unsourcedOperands
                };
                var formattedValue = value.ToString("G", CultureInfo.InvariantCulture);
                var content = "计算结果：" + formattedValue + (isPercentage ? "%" : string.Empty);
                if (!operandsSourced)
                {
                    content += "；操作数未溯源："
                        + string.Join("、", unsourcedOperands.Select(
                            operand => operand.ToString("G", CultureInfo.InvariantCulture)))
                        + "。该结果不计入数字证据。";
                }

                return new ToolExecutionOutcome
                {
                    ToolCallId = toolCallId ?? string.Empty,
                    ToolName = toolName ?? ToolName,
                    Success = true,
                    Content = content,
                    StructuredContentJson = JsonSerializer.Serialize(result, SerializerOptions)
                };
            }
            catch (ExpressionParseException ex)
            {
                return Failure(
                    toolCallId,
                    toolName,
                    "runtime.calculate.invalid_expression",
                    "表达式无效：" + ex.Message);
            }
            catch (DivideByZeroException)
            {
                return Failure(
                    toolCallId,
                    toolName,
                    "runtime.calculate.division_by_zero",
                    "表达式包含除以零，无法计算。");
            }
            catch (OverflowException)
            {
                return Failure(
                    toolCallId,
                    toolName,
                    "runtime.calculate.overflow",
                    "计算结果超出 decimal 可表示范围。");
            }
        }

        private static bool TryReadExpression(
            string argumentsJson,
            out string expression,
            out string error)
        {
            expression = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                error = "必须提供 expression 字符串。";
                return false;
            }
            if (argumentsJson.Length > MaximumExpressionLength * 6 + 128)
            {
                error = "arguments 超出安全长度限制。";
                return false;
            }

            try
            {
                using (var document = JsonDocument.Parse(argumentsJson))
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        error = "arguments 必须是包含 expression 字符串的 JSON 对象。";
                        return false;
                    }
                    var propertyCount = 0;
                    foreach (var property in root.EnumerateObject())
                    {
                        propertyCount++;
                        if (propertyCount > 1
                            || !string.Equals(property.Name, "expression", StringComparison.Ordinal))
                        {
                            error = "arguments 只允许一个 expression 字段。";
                            return false;
                        }
                    }
                    if (propertyCount != 1
                        || !root.TryGetProperty("expression", out var value)
                        || value.ValueKind != JsonValueKind.String)
                    {
                        error = "arguments 必须是包含 expression 字符串的 JSON 对象。";
                        return false;
                    }

                    expression = value.GetString()?.Trim() ?? string.Empty;
                    if (expression.Length == 0)
                    {
                        error = "expression 不能为空。";
                        return false;
                    }

                    return true;
                }
            }
            catch (JsonException)
            {
                error = "arguments 不是有效的 JSON。";
                return false;
            }
        }

        private static bool IsAllowedConstant(decimal operand)
        {
            return operand >= 0m
                && operand <= 100m
                && decimal.Truncate(operand) == operand;
        }

        private static ToolExecutionOutcome Failure(
            string toolCallId,
            string toolName,
            string errorCode,
            string errorMessage)
        {
            return new ToolExecutionOutcome
            {
                ToolCallId = toolCallId ?? string.Empty,
                ToolName = toolName ?? ToolName,
                Success = false,
                Content = errorMessage ?? string.Empty,
                ErrorCode = errorCode ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }

        private sealed class CalculateToolResult
        {
            public string Expression { get; set; }

            public decimal Value { get; set; }

            public bool IsPercentage { get; set; }

            public bool OperandsSourced { get; set; }

            public List<decimal> UnsourcedOperands { get; set; }
        }

        private sealed class DecimalExpressionParser
        {
            private readonly string _expression;
            private readonly int _maximumNodeCount;
            private readonly int _maximumNestingDepth;
            private readonly EvidenceLedger _evidenceLedger;
            private readonly List<decimal> _operands = new List<decimal>();
            private int _position;
            private int _nodeCount;
            private int _nestingDepth;

            private struct ExpressionValue
            {
                public ExpressionValue(decimal value, bool isFraction = false, bool isPercentage = false)
                {
                    Value = value;
                    IsFraction = isFraction;
                    IsPercentage = isPercentage;
                }

                public decimal Value { get; private set; }

                public bool IsFraction { get; private set; }

                public bool IsPercentage { get; private set; }
            }

            public DecimalExpressionParser(
                string expression,
                int maximumNodeCount,
                int maximumNestingDepth,
                EvidenceLedger evidenceLedger)
            {
                _expression = expression ?? string.Empty;
                _maximumNodeCount = maximumNodeCount;
                _maximumNestingDepth = maximumNestingDepth;
                _evidenceLedger = evidenceLedger;
            }

            public IReadOnlyList<decimal> Operands
            {
                get { return _operands; }
            }

            public bool IsPercentageResult { get; private set; }

            public decimal Evaluate()
            {
                var value = ParseAdditive();
                SkipWhitespace();
                if (_position != _expression.Length)
                {
                    throw Error("包含不支持的字符。");
                }

                IsPercentageResult = value.IsPercentage;
                return value.Value;
            }

            private ExpressionValue ParseAdditive()
            {
                var value = ParseMultiplicative();
                while (true)
                {
                    SkipWhitespace();
                    if (Match('+'))
                    {
                        CountNode();
                        var right = ParseMultiplicative();
                        value = new ExpressionValue(
                            value.Value + right.Value,
                            value.IsFraction && right.IsFraction,
                            value.IsPercentage && right.IsPercentage);
                    }
                    else if (Match('-'))
                    {
                        CountNode();
                        var right = ParseMultiplicative();
                        value = new ExpressionValue(
                            value.Value - right.Value,
                            value.IsFraction && right.IsFraction,
                            value.IsPercentage && right.IsPercentage);
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private ExpressionValue ParseMultiplicative()
            {
                var value = ParseUnary();
                while (true)
                {
                    SkipWhitespace();
                    if (Match('*'))
                    {
                        CountNode();
                        var right = ParseUnary();
                        var isPercentage = (value.IsFraction && IsScalar(right) && right.Value == 100m)
                            || (right.IsFraction && IsScalar(value) && value.Value == 100m)
                            || (value.IsPercentage && IsScalar(right))
                            || (right.IsPercentage && IsScalar(value));
                        var isFraction = !isPercentage
                            && ((value.IsFraction && IsScalar(right))
                                || (right.IsFraction && IsScalar(value)));
                        value = new ExpressionValue(
                            value.Value * right.Value,
                            isFraction,
                            isPercentage);
                    }
                    else if (Match('/'))
                    {
                        CountNode();
                        var right = ParseUnary();
                        var isPercentage = value.IsPercentage && IsScalar(right);
                        var isFraction = !isPercentage
                            && ((value.IsFraction && IsScalar(right))
                                || (!value.IsPercentage && !right.IsPercentage));
                        value = new ExpressionValue(
                            value.Value / right.Value,
                            isFraction,
                            isPercentage);
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private ExpressionValue ParseUnary()
            {
                SkipWhitespace();
                var negative = false;
                var hasSign = false;
                while (Match('+') || Match('-'))
                {
                    hasSign = true;
                    if (_expression[_position - 1] == '-')
                    {
                        negative = !negative;
                    }
                    CountNode();
                    SkipWhitespace();
                }

                var operandStart = _operands.Count;
                var value = ParsePrimary(out var directNumber);
                if (negative)
                {
                    value = new ExpressionValue(
                        -value.Value,
                        value.IsFraction,
                        value.IsPercentage);
                    if (directNumber && _operands.Count == operandStart + 1)
                    {
                        _operands[operandStart] = value.Value;
                    }
                }
                else if (hasSign && directNumber && _operands.Count == operandStart + 1)
                {
                    _operands[operandStart] = value.Value;
                }

                return value;
            }

            private ExpressionValue ParsePrimary(out bool directNumber)
            {
                SkipWhitespace();
                directNumber = false;
                if (Match('('))
                {
                    CountNode();
                    EnterNesting();
                    try
                    {
                        var value = ParseAdditive();
                        SkipWhitespace();
                        Require(')', "缺少右括号。");
                        return value;
                    }
                    finally
                    {
                        ExitNesting();
                    }
                }

                if (IsDigit(Current) || Current == '.')
                {
                    directNumber = true;
                    var value = ParseNumber();
                    var isPercentageFraction = _evidenceLedger != null
                        && _evidenceLedger.IsPercentageFraction(value);
                    return new ExpressionValue(value, isPercentageFraction);
                }

                if (char.IsLetter(Current))
                {
                    CountNode();
                    return ParseFunction();
                }

                throw Error("需要数字、函数或括号表达式。");
            }

            private ExpressionValue ParseFunction()
            {
                var name = ParseIdentifier();
                SkipWhitespace();
                Require('(', "函数名后需要左括号。");
                EnterNesting();
                try
                {
                    var arguments = new List<ExpressionValue>();
                    SkipWhitespace();
                    if (Current != ')')
                    {
                        arguments.Add(ParseAdditive());
                        SkipWhitespace();
                        while (Match(','))
                        {
                            CountNode();
                            arguments.Add(ParseAdditive());
                            SkipWhitespace();
                        }
                    }
                    Require(')', "函数参数缺少右括号。");

                    switch (name.ToLowerInvariant())
                    {
                        case "sum":
                            RequireArgumentCount(name, arguments, 1, int.MaxValue);
                            return new ExpressionValue(
                                arguments.Aggregate(0m, (total, argument) => total + argument.Value),
                                arguments.All(argument => argument.IsFraction),
                                arguments.All(argument => argument.IsPercentage));
                        case "avg":
                            RequireArgumentCount(name, arguments, 1, int.MaxValue);
                            return new ExpressionValue(
                                arguments.Aggregate(0m, (total, argument) => total + argument.Value)
                                    / arguments.Count,
                                arguments.All(argument => argument.IsFraction),
                                arguments.All(argument => argument.IsPercentage));
                        case "round":
                            RequireArgumentCount(name, arguments, 2, 2);
                            var digits = arguments[1].Value;
                            if (decimal.Truncate(digits) != digits || digits < 0m || digits > 28m)
                            {
                                throw Error("round 的小数位数必须是 0 到 28 的整数。");
                            }
                            return new ExpressionValue(
                                decimal.Round(
                                    arguments[0].Value,
                                    decimal.ToInt32(digits),
                                    MidpointRounding.AwayFromZero),
                                arguments[0].IsFraction,
                                arguments[0].IsPercentage);
                        default:
                            throw Error("不支持函数 " + name + "；仅支持 sum、avg、round。");
                    }
                }
                finally
                {
                    ExitNesting();
                }
            }

            private decimal ParseNumber()
            {
                var start = _position;
                var digitsBeforeDecimal = 0;
                while (IsDigit(Current))
                {
                    _position++;
                    digitsBeforeDecimal++;
                }

                var digitsAfterDecimal = 0;
                if (Match('.'))
                {
                    while (IsDigit(Current))
                    {
                        _position++;
                        digitsAfterDecimal++;
                    }
                }

                if (digitsBeforeDecimal == 0 && digitsAfterDecimal == 0)
                {
                    throw Error("数字格式无效。");
                }

                var numericText = _expression.Substring(start, _position - start);
                if (!decimal.TryParse(
                    numericText,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var value))
                {
                    throw Error("数字超出 decimal 可表示范围。");
                }

                CountNode();
                _operands.Add(value);
                return value;
            }

            private string ParseIdentifier()
            {
                var start = _position;
                while (char.IsLetter(Current))
                {
                    _position++;
                }
                return _expression.Substring(start, _position - start);
            }

            private void RequireArgumentCount(
                string functionName,
                IList<ExpressionValue> arguments,
                int minimum,
                int maximum)
            {
                if (arguments.Count < minimum || arguments.Count > maximum)
                {
                    throw Error(functionName + " 的参数数量不正确。");
                }
            }

            private static bool IsScalar(ExpressionValue value)
            {
                return !value.IsFraction && !value.IsPercentage;
            }

            private void EnterNesting()
            {
                _nestingDepth++;
                if (_nestingDepth > _maximumNestingDepth)
                {
                    throw Error("表达式嵌套过深。");
                }
            }

            private void ExitNesting()
            {
                _nestingDepth--;
            }

            private void CountNode()
            {
                _nodeCount++;
                if (_nodeCount > _maximumNodeCount)
                {
                    throw Error("表达式过于复杂。");
                }
            }

            private void Require(char expected, string message)
            {
                SkipWhitespace();
                if (!Match(expected))
                {
                    throw Error(message);
                }
            }

            private bool Match(char expected)
            {
                if (Current != expected)
                {
                    return false;
                }

                _position++;
                return true;
            }

            private void SkipWhitespace()
            {
                while (char.IsWhiteSpace(Current))
                {
                    _position++;
                }
            }

            private char Current
            {
                get { return _position < _expression.Length ? _expression[_position] : '\0'; }
            }

            private static bool IsDigit(char value)
            {
                return value >= '0' && value <= '9';
            }

            private ExpressionParseException Error(string message)
            {
                return new ExpressionParseException(
                    message + "（位置 " + _position.ToString(CultureInfo.InvariantCulture) + "）");
            }
        }

        private sealed class ExpressionParseException : Exception
        {
            public ExpressionParseException(string message)
                : base(message)
            {
            }
        }
    }
}
