using System;
using System.Globalization;

namespace OfficeCopilot.Localization
{
    public sealed class UserTextLocalizer : IUserTextLocalizer
    {
        public string Get(string key, CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return key ?? string.Empty;
            }

            return LocalizationService.Get(key, culture);
        }

        public string Format(string key, CultureInfo culture, params object[] arguments)
        {
            return string.Format(
                culture ?? CultureInfo.GetCultureInfo(SupportedLanguages.English),
                Get(key, culture),
                arguments ?? new object[0]);
        }

        public string GetToolDisplayName(string toolName, string fallback, CultureInfo culture)
        {
            var key = "tool." + (toolName ?? string.Empty).Trim().ToLowerInvariant() + ".display_name";
            var value = Get(key, culture);
            if (!string.Equals(value, key, StringComparison.Ordinal))
            {
                return value;
            }

            return string.IsNullOrWhiteSpace(fallback) ? (toolName ?? string.Empty) : fallback;
        }

        public string GetErrorMessage(string errorCode, string fallback, CultureInfo culture, params object[] arguments)
        {
            var key = "panel.error." + (errorCode ?? string.Empty).Trim().ToLowerInvariant();
            var value = Get(key, culture);
            if (string.Equals(value, key, StringComparison.Ordinal))
            {
                return string.IsNullOrWhiteSpace(fallback) ? (errorCode ?? string.Empty) : fallback;
            }

            var formattingArguments = arguments == null || arguments.Length == 0
                ? new object[] { string.Empty }
                : arguments;
            return string.Format(
                culture ?? CultureInfo.GetCultureInfo(SupportedLanguages.English),
                value,
                formattingArguments);
        }
    }
}
