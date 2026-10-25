using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Threading;

namespace OfficeCopilot.Localization
{
    public static class LocalizationService
    {
        private static readonly ResourceManager Resources =
            new ResourceManager("OfficeCopilot.Localization.Strings", typeof(LocalizationService).Assembly);

        private static readonly object Sync = new object();
        private static string _preference = SupportedLanguages.English;
        private static CultureInfo _culture = SupportedLanguages.ResolveCulture(SupportedLanguages.English);

        public static event EventHandler LanguageChanged;

        public static string Preference
        {
            get
            {
                lock (Sync)
                {
                    return _preference;
                }
            }
        }

        public static CultureInfo Culture
        {
            get
            {
                lock (Sync)
                {
                    return _culture;
                }
            }
        }

        public static void SetLanguage(string preference)
        {
            var normalizedPreference = SupportedLanguages.NormalizePreference(preference);
            var resolvedCulture = SupportedLanguages.ResolveCulture(normalizedPreference);
            Thread.CurrentThread.CurrentUICulture = resolvedCulture;
            var changed = false;

            lock (Sync)
            {
                if (!string.Equals(_preference, normalizedPreference, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(_culture.Name, resolvedCulture.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _preference = normalizedPreference;
                    _culture = resolvedCulture;
                    changed = true;
                }
            }

            if (changed)
            {
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static CultureInfo ApplyUiCultureToCurrentThread(string preference)
        {
            SetLanguage(preference);
            var culture = Culture;
            return culture;
        }

        public static string Get(string key)
        {
            return Get(key, Culture);
        }

        public static string Format(string key, params object[] arguments)
        {
            var culture = Culture;
            return string.Format(
                culture,
                Get(key, culture),
                arguments ?? new object[0]);
        }

        public static string Get(string key, CultureInfo culture)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A resource key is required.", nameof(key));
            }

            var value = Resources.GetString(key, culture ?? CultureInfo.GetCultureInfo(SupportedLanguages.English));
            return value ?? key;
        }

        /// <summary>
        /// Returns all resources whose full keys start with <paramref name="prefix"/>.
        /// English (neutral) values are loaded first, then values from the current culture
        /// override them so callers always receive a complete dictionary.
        /// </summary>
        public static Dictionary<string, string> GetAll(string prefix)
        {
            if (prefix == null)
            {
                throw new ArgumentNullException(nameof(prefix));
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            AddResources(values, CultureInfo.InvariantCulture, prefix);

            var culture = Culture;
            if (!culture.Equals(CultureInfo.InvariantCulture))
            {
                AddResources(values, culture, prefix);
            }

            return values;
        }

        private static void AddResources(IDictionary<string, string> values, CultureInfo culture, string prefix)
        {
            var resourceSet = Resources.GetResourceSet(culture, true, true);
            if (resourceSet == null)
            {
                return;
            }

            foreach (System.Collections.DictionaryEntry entry in resourceSet)
            {
                var key = entry.Key as string;
                if (key != null && key.StartsWith(prefix, StringComparison.Ordinal) && entry.Value is string)
                {
                    values[key] = (string)entry.Value;
                }
            }
        }
    }
}
