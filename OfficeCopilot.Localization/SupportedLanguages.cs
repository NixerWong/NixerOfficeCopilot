using System;
using System.Globalization;

namespace OfficeCopilot.Localization
{
    public static class SupportedLanguages
    {
        public const string English = "en";
        public const string SimplifiedChinese = "zh-Hans";
        public const string Japanese = "ja";
        public const string German = "de";
        public const string Spanish = "es";

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

            if (string.Equals(preference, German, StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "de-DE", StringComparison.OrdinalIgnoreCase))
            {
                return German;
            }

            if (string.Equals(preference, Spanish, StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "es-ES", StringComparison.OrdinalIgnoreCase))
            {
                return Spanish;
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
                case 3: return German;
                case 4: return Spanish;
                default: return English;
            }
        }

        public static int GetIndex(string preference)
        {
            switch (NormalizePreference(preference))
            {
                case SimplifiedChinese: return 1;
                case Japanese: return 2;
                case German: return 3;
                case Spanish: return 4;
                default: return 0;
            }
        }
    }
}
