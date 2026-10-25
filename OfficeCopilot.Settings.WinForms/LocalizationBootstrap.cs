using OfficeCopilot.Localization;
using OfficeCopilot.Settings;
using System;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace OfficeCopilot.Settings.WinForms
{
    public static class LocalizationBootstrap
    {
        public static void Initialize()
        {
            var preference = LoadUiLanguagePreference();
            LocalizationService.ApplyUiCultureToCurrentThread(preference);
        }

        private static string LoadUiLanguagePreference()
        {
            try
            {
                var settings = new JsonSettingsStore().Load();
                return settings == null ? SupportedLanguages.English : settings.UiLanguage;
            }
            catch (IOException ex)
            {
                Trace.TraceWarning("Unable to read the UI language setting; using English. " + ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Trace.TraceWarning("Unable to read the UI language setting; using English. " + ex.Message);
            }
            catch (SecurityException ex)
            {
                Trace.TraceWarning("Unable to read the UI language setting; using English. " + ex.Message);
            }

            return SupportedLanguages.English;
        }
    }
}
