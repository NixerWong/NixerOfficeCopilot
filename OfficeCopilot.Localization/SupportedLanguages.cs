using System;
using System.Globalization;

namespace OfficeCopilot.Localization
{
    public static class SupportedLanguages
    {
        public const string English = "en";
        public const string SimplifiedChinese = "zh-Hans";
        public const string Japanese = "ja";

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

            if (string.Equals(preference, Japanese, StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "ja-JP", StringComparison.OrdinalIgnoreCase))
            {
                return Japanese;
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
                case 2: return Japanese;
                default: return English;
            }
        }

        public static int GetIndex(string preference)
        {
            switch (NormalizePreference(preference))
            {
                case SimplifiedChinese: return 1;
                case Japanese: return 2;
                default: return 0;
            }
        }
    }
}
