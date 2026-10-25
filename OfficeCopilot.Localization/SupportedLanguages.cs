using System;
using System.Globalization;

namespace OfficeCopilot.Localization
{
    public static class SupportedLanguages
    {
        public const string English = "en";
        public const string SimplifiedChinese = "zh-Hans";

        public static string NormalizePreference(string preference)
        {
            if (string.Equals(preference, English, StringComparison.OrdinalIgnoreCase))
            {
                return English;
            }

            if (string.Equals(preference, SimplifiedChinese, StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "zh-CN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "zh-SG", StringComparison.OrdinalIgnoreCase))
            {
                return SimplifiedChinese;
            }

            return English;
        }

        public static CultureInfo ResolveCulture(string preference)
        {
            return CultureInfo.GetCultureInfo(NormalizePreference(preference));
        }

        public static string GetCodeAt(int index)
        {
            switch (index)
            {
                case 1: return SimplifiedChinese;
                default: return English;
            }
        }

        public static int GetIndex(string preference)
        {
            switch (NormalizePreference(preference))
            {
                case SimplifiedChinese: return 1;
                default: return 0;
            }
        }
    }
}
