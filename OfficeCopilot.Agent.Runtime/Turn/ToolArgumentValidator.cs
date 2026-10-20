using System;
using System.Globalization;
using System.Text.Json;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    internal sealed class ToolArgumentValidationResult
    {
        public ToolArgumentValidationResult(bool schemaValid, bool isValid, string errorMessage)
        {
            SchemaValid = schemaValid;
            IsValid = isValid;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool SchemaValid { get; }

        public bool IsValid { get; }

        public string ErrorMessage { get; }
    }

    internal static class ToolArgumentValidator
    {
        public static ToolArgumentValidationResult Validate(string schemaJson, string argumentsJson)
        {
            JsonDocument schemaDocument;
            try
            {
                schemaDocument = JsonDocument.Parse(schemaJson ?? string.Empty);
            }
            catch (JsonException ex)
            {
                return new ToolArgumentValidationResult(false, false, "Invalid tool schema JSON: " + ex.Message);
            }

            using (schemaDocument)
            {
                if (schemaDocument.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new ToolArgumentValidationResult(false, false, "Tool schema root must be an object.");
                }

                JsonDocument argumentsDocument;
                try
                {
                    argumentsDocument = JsonDocument.Parse(argumentsJson ?? string.Empty);
                }
                catch (JsonException)
                {
                    return new ToolArgumentValidationResult(true, false, "$: expected valid JSON arguments, got invalid JSON.");
                }

                using (argumentsDocument)
                {
                    string validationError;
                    string schemaError;
                    if (!TryValidateValue(
                        schemaDocument.RootElement,
                        argumentsDocument.RootElement,
                        "$",
                        out validationError,
                        out schemaError))
                    {
                        return string.IsNullOrWhiteSpace(schemaError)
                            ? new ToolArgumentValidationResult(true, false, validationError)
                            : new ToolArgumentValidationResult(false, false, schemaError);
                    }
                }
            }

            return new ToolArgumentValidationResult(true, true, string.Empty);
        }

        private static bool TryValidateValue(
            JsonElement schema,
            JsonElement value,
            string path,
            out string validationError,
            out string schemaError)
        {
            validationError = string.Empty;
            schemaError = string.Empty;
            if (schema.ValueKind != JsonValueKind.Object)
            {
                schemaError = path + ": schema must be an object.";
                return false;
            }

            JsonElement type;
            if (schema.TryGetProperty("type", out type))
            {
                if (type.ValueKind != JsonValueKind.String)
                {
                    schemaError = path + ": schema type must be a string.";
                    return false;
                }

                var expectedType = type.GetString();
                if (!IsSupportedType(expectedType))
                {
                    schemaError = path + ": unsupported schema type '" + expectedType + "'.";
                    return false;
                }

                if (!MatchesType(expectedType, value))
                {
                    validationError = path + ": expected type '" + expectedType
                        + "', got '" + GetKindName(value.ValueKind) + "'.";
                    return false;
                }
            }

            JsonElement enumValues;
            if (schema.TryGetProperty("enum", out enumValues))
            {
                if (enumValues.ValueKind != JsonValueKind.Array)
                {
                    schemaError = path + ": schema enum must be an array.";
                    return false;
                }

                var matched = false;
                foreach (var enumValue in enumValues.EnumerateArray())
                {
                    if (JsonValuesEqual(enumValue, value))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    validationError = path + ": expected a declared enum value, got '"
                        + GetKindName(value.ValueKind) + "'.";
                    return false;
                }
            }

            JsonElement required;
            if (schema.TryGetProperty("required", out required))
            {
                if (required.ValueKind != JsonValueKind.Array)
                {
                    schemaError = path + ": schema required must be an array.";
                    return false;
                }

                if (value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var requiredName in required.EnumerateArray())
                    {
                        if (requiredName.ValueKind != JsonValueKind.String)
                        {
                            schemaError = path + ": schema required entries must be strings.";
                            return false;
                        }

                        var propertyName = requiredName.GetString();
                        JsonElement ignored;
                        if (!value.TryGetProperty(propertyName, out ignored))
                        {
                            validationError = AppendPropertyPath(path, propertyName)
                                + ": expected a required property, got missing.";
                            return false;
                        }
                    }
                }
            }

            JsonElement properties;
            if (schema.TryGetProperty("properties", out properties))
            {
                if (properties.ValueKind != JsonValueKind.Object)
                {
                    schemaError = path + ": schema properties must be an object.";
                    return false;
                }

                if (value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in value.EnumerateObject())
                    {
                        JsonElement propertySchema;
                        if (properties.TryGetProperty(property.Name, out propertySchema)
                            && !TryValidateValue(
                                propertySchema,
                                property.Value,
                                AppendPropertyPath(path, property.Name),
                                out validationError,
                                out schemaError))
                        {
                            return false;
                        }
                    }
                }
            }

            JsonElement items;
            if (schema.TryGetProperty("items", out items) && value.ValueKind == JsonValueKind.Array)
            {
                for (var index = 0; index < value.GetArrayLength(); index++)
                {
                    if (!TryValidateValue(items, value[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                        out validationError, out schemaError))
                    {
                        return false;
                    }
                }
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                if (!ValidateBound(schema, "minimum", true, value, path, out validationError, out schemaError)
                    || !ValidateBound(schema, "maximum", false, value, path, out validationError, out schemaError))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateBound(
            JsonElement schema,
            string boundName,
            bool isMinimum,
            JsonElement value,
            string path,
            out string validationError,
            out string schemaError)
        {
            validationError = string.Empty;
            schemaError = string.Empty;
            JsonElement bound;
            if (!schema.TryGetProperty(boundName, out bound))
            {
                return true;
            }

            decimal boundValue;
            if (bound.ValueKind != JsonValueKind.Number || !bound.TryGetDecimal(out boundValue))
            {
                schemaError = path + ": schema " + boundName + " must be a number.";
                return false;
            }

            decimal actualValue;
            if (!value.TryGetDecimal(out actualValue))
            {
                validationError = path + ": expected a number within the declared bounds, got a value outside the supported numeric range.";
                return false;
            }

            if ((isMinimum && actualValue < boundValue) || (!isMinimum && actualValue > boundValue))
            {
                var relation = isMinimum ? ">= " : "<= ";
                validationError = path + ": expected a number " + relation
                    + boundValue.ToString(CultureInfo.InvariantCulture) + ", got a number outside the bound.";
                return false;
            }

            return true;
        }

        private static bool IsSupportedType(string type)
        {
            return string.Equals(type, "object", StringComparison.Ordinal)
                || string.Equals(type, "array", StringComparison.Ordinal)
                || string.Equals(type, "string", StringComparison.Ordinal)
                || string.Equals(type, "number", StringComparison.Ordinal)
                || string.Equals(type, "integer", StringComparison.Ordinal)
                || string.Equals(type, "boolean", StringComparison.Ordinal)
                || string.Equals(type, "null", StringComparison.Ordinal);
        }

        private static bool MatchesType(string type, JsonElement value)
        {
            switch (type)
            {
                case "object":
                    return value.ValueKind == JsonValueKind.Object;
                case "array":
                    return value.ValueKind == JsonValueKind.Array;
                case "string":
                    return value.ValueKind == JsonValueKind.String;
                case "number":
                    return value.ValueKind == JsonValueKind.Number;
                case "integer":
                    decimal number;
                    return value.ValueKind == JsonValueKind.Number
                        && value.TryGetDecimal(out number)
                        && decimal.Truncate(number) == number;
                case "boolean":
                    return value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False;
                case "null":
                    return value.ValueKind == JsonValueKind.Null;
                default:
                    return false;
            }
        }

        private static bool JsonValuesEqual(JsonElement left, JsonElement right)
        {
            if (left.ValueKind != right.ValueKind)
            {
                if (left.ValueKind != JsonValueKind.Number || right.ValueKind != JsonValueKind.Number)
                {
                    return false;
                }
            }

            switch (left.ValueKind)
            {
                case JsonValueKind.Object:
                    var leftCount = 0;
                    foreach (var property in left.EnumerateObject())
                    {
                        leftCount++;
                        JsonElement rightValue;
                        if (!right.TryGetProperty(property.Name, out rightValue)
                            || !JsonValuesEqual(property.Value, rightValue))
                        {
                            return false;
                        }
                    }

                    var rightCount = 0;
                    foreach (var property in right.EnumerateObject())
                    {
                        rightCount++;
                    }

                    return leftCount == rightCount;
                case JsonValueKind.Array:
                    if (left.GetArrayLength() != right.GetArrayLength())
                    {
                        return false;
                    }

                    for (var index = 0; index < left.GetArrayLength(); index++)
                    {
                        if (!JsonValuesEqual(left[index], right[index]))
                        {
                            return false;
                        }
                    }

                    return true;
                case JsonValueKind.String:
                    return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);
                case JsonValueKind.Number:
                    decimal leftNumber;
                    decimal rightNumber;
                    if (left.TryGetDecimal(out leftNumber) && right.TryGetDecimal(out rightNumber))
                    {
                        return leftNumber == rightNumber;
                    }

                    return string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal);
                case JsonValueKind.True:
                case JsonValueKind.False:
                case JsonValueKind.Null:
                    return true;
                default:
                    return false;
            }
        }

        private static string AppendPropertyPath(string path, string propertyName)
        {
            if (!string.IsNullOrEmpty(propertyName) && IsSimplePropertyName(propertyName))
            {
                return path + "." + propertyName;
            }

            return path + "['" + (propertyName ?? string.Empty).Replace("'", "\\'") + "']";
        }

        private static bool IsSimplePropertyName(string value)
        {
            if (!(char.IsLetter(value[0]) || value[0] == '_'))
            {
                return false;
            }

            for (var index = 1; index < value.Length; index++)
            {
                if (!(char.IsLetterOrDigit(value[index]) || value[index] == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetKindName(JsonValueKind kind)
        {
            switch (kind)
            {
                case JsonValueKind.Object:
                    return "object";
                case JsonValueKind.Array:
                    return "array";
                case JsonValueKind.String:
                    return "string";
                case JsonValueKind.Number:
                    return "number";
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return "boolean";
                case JsonValueKind.Null:
                    return "null";
                default:
                    return "undefined";
            }
        }
    }
}
