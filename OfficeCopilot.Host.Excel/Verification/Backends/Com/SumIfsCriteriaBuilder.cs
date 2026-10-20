using System;
using System.Globalization;
using System.Linq;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Verification.Backends.Com
{
    internal static class SumIfsCriteriaBuilder
    {
        internal const int MaximumCriteriaLength = 255;

        internal static bool TryBuild(
            CalcFilter filter,
            string decimalSeparator,
            out string criteria)
        {
            criteria = string.Empty;
            if (filter == null)
            {
                return false;
            }

            var operation = (filter.Operator ?? string.Empty).Trim().ToLowerInvariant();
            switch (operation)
            {
                case "startswith":
                    var prefix = filter.Value ?? string.Empty;
                    if (prefix.Length == 0)
                    {
                        return false;
                    }

                    criteria = "=" + EscapeWildcards(prefix) + "*";
                    return criteria.Length <= MaximumCriteriaLength;
                case "textequals":
                    var textValue = filter.Value ?? string.Empty;
                    string ignoredNumber;
                    if (textValue.Length == 0
                        || TryFormatNumber(textValue, decimalSeparator, out ignoredNumber))
                    {
                        return false;
                    }

                    criteria = "=" + EscapeWildcards(textValue);
                    return criteria.Length <= MaximumCriteriaLength;
                case "isnativenumber":
                case "istextnumber":
                    criteria = string.Empty;
                    return string.IsNullOrEmpty(filter.Value);
                case "blank":
                case "isempty":
                    criteria = string.Empty;
                    return true;
                case "notblank":
                case "notempty":
                    criteria = "<>";
                    return true;
            }

            string comparisonOperator;
            if (!TryGetComparisonOperator(operation, out comparisonOperator))
            {
                return false;
            }

            var value = filter.Value ?? string.Empty;
            if (value.Length == 0 && operation != "eq" && operation != "ne")
            {
                return false;
            }

            string formattedValue;
            if (TryFormatNumber(value, decimalSeparator, out formattedValue))
            {
                criteria = comparisonOperator + formattedValue;
            }
            else
            {
                criteria = comparisonOperator + EscapeWildcards(value);
            }

            return criteria.Length <= MaximumCriteriaLength;
        }

        internal static bool TryBuildFormulaComparison(
            CalcFilter filter,
            string rangeAddress,
            string decimalSeparator,
            out string expression)
        {
            return TryBuildFormulaComparison(
                filter,
                rangeAddress,
                decimalSeparator,
                ",",
                out expression);
        }

        internal static bool TryBuildFormulaComparison(
            CalcFilter filter,
            string rangeAddress,
            string decimalSeparator,
            string listSeparator,
            out string expression)
        {
            expression = string.Empty;
            if (filter == null || string.IsNullOrWhiteSpace(rangeAddress))
            {
                return false;
            }

            var operation = (filter.Operator ?? string.Empty).Trim().ToLowerInvariant();
            var formulaSeparator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            if (IsDatePartFilter(operation))
            {
                return TryBuildDatePartFormulaComparison(
                    operation,
                    filter.Value,
                    rangeAddress,
                    formulaSeparator,
                    out expression);
            }

            if (operation.StartsWith("datetext", StringComparison.Ordinal))
            {
                return TryBuildDateTextComparison(
                    operation,
                    filter.Value,
                    rangeAddress,
                    formulaSeparator,
                    out expression);
            }

            if (operation == "textequals")
            {
                var value = filter.Value ?? string.Empty;
                if (value.Length > MaximumCriteriaLength)
                {
                    return false;
                }

                if (value.Length == 0)
                {
                    expression = "IFERROR(" + rangeAddress + "=\"\""
                        + formulaSeparator + "FALSE)";
                }
                else
                {
                    expression = "IFERROR(ISTEXT(" + rangeAddress + ")*("
                        + rangeAddress + "=\"" + value.Replace("\"", "\"\"")
                        + "\")" + formulaSeparator + "FALSE)";
                }

                return true;
            }

            if (operation == "textnotequals")
            {
                var value = filter.Value ?? string.Empty;
                if (value.Length > MaximumCriteriaLength)
                {
                    return false;
                }

                if (value.Length == 0)
                {
                    expression = "IF(ISERROR(" + rangeAddress + ")"
                        + formulaSeparator + "TRUE"
                        + formulaSeparator + "IFERROR(" + rangeAddress
                        + "<>\"\"" + formulaSeparator + "TRUE))";
                }
                else
                {
                    expression = "IFERROR(ISTEXT(" + rangeAddress + ")*("
                        + rangeAddress + "<>\"" + value.Replace("\"", "\"\"")
                        + "\")+" + "(" + rangeAddress + "=\"\")"
                        + formulaSeparator + "FALSE)";
                }

                return true;
            }

            if (operation == "textcontains")
            {
                var value = filter.Value ?? string.Empty;
                if (value.Length > MaximumCriteriaLength)
                {
                    return false;
                }

                if (value.Length == 0)
                {
                    expression = "TRUE";
                }
                else
                {
                    expression = "IFERROR(ISTEXT(" + rangeAddress
                        + ")*ISNUMBER(SEARCH(\""
                        + EscapeWildcards(value).Replace("\"", "\"\"")
                        + "\"" + formulaSeparator + rangeAddress + "))"
                        + formulaSeparator + "FALSE)";
                }

                return true;
            }

            if (operation == "startswith")
            {
                var prefix = filter.Value ?? string.Empty;
                if (prefix.Length == 0 || prefix.Length > MaximumCriteriaLength)
                {
                    return false;
                }

                var escapedPrefix = prefix.Replace("\"", "\"\"");
                expression = "IFERROR(LEFT(" + rangeAddress + formulaSeparator
                    + "LEN(\""
                    + escapedPrefix + "\"))=\"" + escapedPrefix
                    + "\"" + formulaSeparator + "FALSE)";
                return true;
            }

            if (operation == "textstartswith")
            {
                var prefix = filter.Value ?? string.Empty;
                if (prefix.Length > MaximumCriteriaLength)
                {
                    return false;
                }

                if (prefix.Length == 0)
                {
                    expression = "TRUE";
                }
                else
                {
                    var escapedPrefix = prefix.Replace("\"", "\"\"");
                    expression = "IFERROR(ISTEXT(" + rangeAddress + ")*("
                        + "LEFT(" + rangeAddress + formulaSeparator
                        + "LEN(\"" + escapedPrefix + "\"))=\""
                        + escapedPrefix + "\")" + formulaSeparator + "FALSE)";
                }

                return true;
            }

            if (operation == "textin" || operation == "numberin")
            {
                var values = (filter.Value ?? string.Empty)
                    .Split(',')
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToArray();
                if (values.Length == 0)
                {
                    expression = "FALSE";
                    return true;
                }

                var comparisons = new string[values.Length];
                for (var index = 0; index < values.Length; index++)
                {
                    string comparisonValue;
                    if (operation == "numberin")
                    {
                        if (!TryFormatNumber(values[index], decimalSeparator, out comparisonValue))
                        {
                            return false;
                        }

                        comparisons[index] = "(" + rangeAddress + "="
                            + comparisonValue + ")";
                    }
                    else
                    {
                        if (values[index].Length > MaximumCriteriaLength)
                        {
                            return false;
                        }

                        comparisons[index] = "(ISTEXT(" + rangeAddress + ")*("
                            + rangeAddress + "=\""
                            + values[index].Replace("\"", "\"\"") + "\"))";
                    }
                }

                expression = "IFERROR((" + string.Join("+", comparisons)
                    + ")>0" + formulaSeparator + "FALSE)";
                return expression.Length <= 8192;
            }

            if (operation == "isnativenumber")
            {
                expression = "ISNUMBER(" + rangeAddress + ")";
                return string.IsNullOrEmpty(filter.Value);
            }

            if (operation == "istextnumber")
            {
                if (!string.IsNullOrEmpty(filter.Value))
                {
                    return false;
                }

                var numberValue = "NUMBERVALUE("
                    + rangeAddress + formulaSeparator + "\".\""
                    + formulaSeparator + "\",\")";
                expression = "IFERROR(ISTEXT(" + rangeAddress + ")*ISNUMBER("
                    + numberValue + ")" + formulaSeparator + "FALSE)";
                return true;
            }

            string comparisonOperator;
            switch (operation)
            {
                case "blank":
                case "isempty":
                    comparisonOperator = "=";
                    expression = "IFERROR(" + rangeAddress + comparisonOperator
                        + "\"\"" + formulaSeparator + "FALSE)";
                    return true;
                case "notblank":
                case "notempty":
                    comparisonOperator = "<>";
                    expression = "IF(ISERROR(" + rangeAddress + ")"
                        + formulaSeparator + "TRUE"
                        + formulaSeparator + "IFERROR(" + rangeAddress
                        + comparisonOperator + "\"\"" + formulaSeparator
                        + "TRUE))";
                    return true;
                default:
                    if (!TryGetComparisonOperator(operation, out comparisonOperator))
                    {
                        return false;
                    }

                    var value = filter.Value ?? string.Empty;
                    if (value.Length == 0 && operation != "eq" && operation != "ne")
                    {
                        return false;
                    }

                    string formattedValue;
                    if (TryFormatNumber(value, decimalSeparator, out formattedValue))
                    {
                        expression = "IFERROR(" + rangeAddress + comparisonOperator
                            + formattedValue + formulaSeparator + "FALSE)";
                    }
                    else if (TryFormatIsoDate(value, listSeparator, out formattedValue))
                    {
                        expression = "IFERROR(" + rangeAddress + comparisonOperator
                            + formattedValue + formulaSeparator + "FALSE)";
                    }
                    else
                    {
                        expression = "IFERROR(" + rangeAddress + comparisonOperator
                            + "\"" + value.Replace("\"", "\"\"") + "\""
                            + formulaSeparator + "FALSE)";
                    }

                    return true;
            }
        }

        internal static bool IsDatePartFilter(string operation)
        {
            switch ((operation ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "yearequals":
                case "quarterequals":
                case "monthequals":
                case "yearmonthequals":
                case "dayequals":
                case "dayofweekequals":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuildDatePartFormulaComparison(
            string operation,
            string value,
            string rangeAddress,
            string formulaSeparator,
            out string expression)
        {
            expression = string.Empty;
            string condition;
            int first;
            int second;
            switch (operation)
            {
                case "yearequals":
                    if (!TryParseInteger(value, 1, 9999, out first))
                    {
                        return false;
                    }

                    condition = "YEAR(" + rangeAddress + ")="
                        + first.ToString(CultureInfo.InvariantCulture);
                    break;
                case "quarterequals":
                    if (!TryParseYearQuarter(value, out first, out second))
                    {
                        return false;
                    }

                    condition = "(YEAR(" + rangeAddress + ")="
                        + first.ToString(CultureInfo.InvariantCulture)
                        + ")*(INT((MONTH(" + rangeAddress + ")-1)/3)+1="
                        + second.ToString(CultureInfo.InvariantCulture) + ")";
                    break;
                case "monthequals":
                    if (!TryParseInteger(value, 1, 12, out first))
                    {
                        return false;
                    }

                    condition = "MONTH(" + rangeAddress + ")="
                        + first.ToString(CultureInfo.InvariantCulture);
                    break;
                case "yearmonthequals":
                    if (!TryParseYearMonth(value, out first, out second))
                    {
                        return false;
                    }

                    condition = "(YEAR(" + rangeAddress + ")="
                        + first.ToString(CultureInfo.InvariantCulture)
                        + ")*(MONTH(" + rangeAddress + ")="
                        + second.ToString(CultureInfo.InvariantCulture) + ")";
                    break;
                case "dayequals":
                    if (!TryParseInteger(value, 1, 31, out first))
                    {
                        return false;
                    }

                    condition = "DAY(" + rangeAddress + ")="
                        + first.ToString(CultureInfo.InvariantCulture);
                    break;
                case "dayofweekequals":
                    if (!TryParseInteger(value, 1, 7, out first))
                    {
                        return false;
                    }

                    condition = "WEEKDAY(" + rangeAddress + formulaSeparator + "2)="
                        + first.ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    return false;
            }

            expression = "IFERROR(ISNUMBER(" + rangeAddress + ")*("
                + condition + ")" + formulaSeparator + "FALSE)";
            return true;
        }

        private static bool TryBuildDateTextComparison(
            string operation,
            string value,
            string rangeAddress,
            string formulaSeparator,
            out string expression)
        {
            expression = string.Empty;
            value = value ?? string.Empty;
            if (value.Length > MaximumCriteriaLength)
            {
                return false;
            }

            var textRange = BuildDateText(rangeAddress, formulaSeparator);
            var escapedValue = value.Replace("\"", "\"\"");
            switch (operation)
            {
                case "datetextequals":
                    expression = "IFERROR(" + textRange + "=\"" + escapedValue
                        + "\"" + formulaSeparator + "FALSE)";
                    return expression.Length <= 8192;
                case "datetextnotequals":
                    expression = "IFERROR(" + textRange + "<>\"" + escapedValue
                        + "\"" + formulaSeparator + "FALSE)";
                    return expression.Length <= 8192;
                case "datetextcontains":
                    expression = value.Length == 0
                        ? "TRUE"
                        : "IFERROR(ISNUMBER(SEARCH(\""
                            + EscapeWildcards(value).Replace("\"", "\"\"")
                            + "\"" + formulaSeparator + textRange + "))"
                            + formulaSeparator + "FALSE)";
                    return expression.Length <= 8192;
                case "datetextstartswith":
                    expression = value.Length == 0
                        ? "TRUE"
                        : "IFERROR(LEFT(" + textRange + formulaSeparator
                            + "LEN(\"" + escapedValue
                            + "\"))=\"" + escapedValue + "\"" + formulaSeparator + "FALSE)";
                    return expression.Length <= 8192;
                case "datetextin":
                    var values = value
                        .Split(',')
                        .Select(candidate => candidate.Trim())
                        .Where(candidate => candidate.Length > 0)
                        .ToArray();
                    if (values.Length == 0)
                    {
                        expression = "FALSE";
                        return true;
                    }

                    expression = "IFERROR(("
                        + string.Join(
                            "+",
                            values.Select(candidate => "(" + textRange + "=\""
                                + candidate.Replace("\"", "\"\"") + "\")").ToArray())
                        + ")>0" + formulaSeparator + "FALSE)";
                    return expression.Length <= 8192;
                default:
                    return false;
            }
        }

        private static string BuildDateText(string rangeAddress, string formulaSeparator)
        {
            var numberText = "TEXT(ROUND(" + rangeAddress + formulaSeparator
                + "10),\"0.##########\")";
            var adjustedSerial = "IF(" + rangeAddress + "<60" + formulaSeparator
                + rangeAddress + "+1" + formulaSeparator + rangeAddress + ")";
            var dateText = "IF(" + rangeAddress + "=60" + formulaSeparator
                + "\"1900-02-28\"" + formulaSeparator
                + "IF(MOD(" + rangeAddress + formulaSeparator + "1)=0" + formulaSeparator
                + "TEXT(" + adjustedSerial
                + formulaSeparator + "\"yyyy-mm-dd\")" + formulaSeparator
                + "TEXT(" + adjustedSerial + formulaSeparator
                + "\"yyyy-mm-dd hh:mm:ss\")))";
            var validDate = "(" + rangeAddress + ">0)*("
                + rangeAddress + "<2958466)";
            return "IF(ISNUMBER(" + rangeAddress + ")" + formulaSeparator
                + "IF(" + validDate + formulaSeparator + dateText
                + formulaSeparator + numberText + ")" + formulaSeparator
                + rangeAddress + ")";
        }

        private static bool TryParseInteger(string value, int minimum, int maximum, out int parsed)
        {
            return int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out parsed)
                && parsed >= minimum
                && parsed <= maximum;
        }

        private static bool TryParseYearQuarter(string value, out int year, out int quarter)
        {
            year = 0;
            quarter = 0;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            var marker = value.IndexOf("-Q", StringComparison.Ordinal);
            if (marker <= 0
                || marker != value.LastIndexOf("-Q", StringComparison.Ordinal)
                || !TryParseInteger(value.Substring(0, marker), 1, 9999, out year)
                || !TryParseInteger(value.Substring(marker + 2), 1, 4, out quarter))
            {
                return false;
            }

            return year.ToString(CultureInfo.InvariantCulture)
                + "-Q" + quarter.ToString(CultureInfo.InvariantCulture) == value;
        }

        private static bool TryParseYearMonth(string value, out int year, out int month)
        {
            year = 0;
            month = 0;
            if (value == null
                || value.Length != 7
                || value[4] != '-'
                || !TryParseInteger(value.Substring(0, 4), 1, 9999, out year)
                || !TryParseInteger(value.Substring(5, 2), 1, 12, out month))
            {
                return false;
            }

            return year.ToString("0000", CultureInfo.InvariantCulture)
                + "-" + month.ToString("00", CultureInfo.InvariantCulture) == value;
        }

        internal static bool TryBuildFormulaCriteria(
            CalcFilter filter,
            string decimalSeparator,
            string listSeparator,
            out string expression)
        {
            expression = string.Empty;
            string criteria;
            if (!TryBuild(filter, decimalSeparator, out criteria))
            {
                return false;
            }

            string comparisonOperator;
            string dateExpression;
            if (TryGetComparisonOperator(
                    (filter.Operator ?? string.Empty).Trim().ToLowerInvariant(),
                    out comparisonOperator)
                && TryFormatIsoDate(filter.Value ?? string.Empty, listSeparator, out dateExpression))
            {
                expression = "\"" + comparisonOperator + "\"&" + dateExpression;
            }
            else
            {
                expression = "\"" + criteria.Replace("\"", "\"\"") + "\"";
            }

            return true;
        }

        private static bool TryFormatIsoDate(
            string value,
            string listSeparator,
            out string expression)
        {
            expression = string.Empty;
            DateTime date;
            if (!DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
            {
                return false;
            }

            var separator = string.IsNullOrEmpty(listSeparator) ? "," : listSeparator;
            expression = "DATE("
                + date.Year.ToString(CultureInfo.InvariantCulture) + separator
                + date.Month.ToString(CultureInfo.InvariantCulture) + separator
                + date.Day.ToString(CultureInfo.InvariantCulture) + ")";
            return true;
        }

        private static bool TryGetComparisonOperator(string operation, out string comparisonOperator)
        {
            switch (operation)
            {
                case "eq":
                    comparisonOperator = "=";
                    return true;
                case "ne":
                    comparisonOperator = "<>";
                    return true;
                case "gt":
                    comparisonOperator = ">";
                    return true;
                case "ge":
                    comparisonOperator = ">=";
                    return true;
                case "lt":
                    comparisonOperator = "<";
                    return true;
                case "le":
                    comparisonOperator = "<=";
                    return true;
                default:
                    comparisonOperator = string.Empty;
                    return false;
            }
        }

        private static bool TryFormatNumber(string value, string decimalSeparator, out string formatted)
        {
            formatted = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            double number;
            var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            if (!string.IsNullOrEmpty(decimalSeparator))
            {
                format.NumberDecimalSeparator = decimalSeparator;
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                && !double.TryParse(value, NumberStyles.Float, format, out number))
            {
                return false;
            }

            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                return false;
            }

            formatted = number.ToString("R", CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(decimalSeparator) && decimalSeparator != ".")
            {
                formatted = formatted.Replace(".", decimalSeparator);
            }

            return true;
        }

        private static string EscapeWildcards(string value)
        {
            return (value ?? string.Empty)
                .Replace("~", "~~")
                .Replace("*", "~*")
                .Replace("?", "~?");
        }
    }
}
