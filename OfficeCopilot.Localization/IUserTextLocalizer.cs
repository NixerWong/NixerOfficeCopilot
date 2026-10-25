using System.Globalization;

namespace OfficeCopilot.Localization
{
    public interface IUserTextLocalizer
    {
        string Get(string key, CultureInfo culture);

        string Format(string key, CultureInfo culture, params object[] arguments);

        string GetToolDisplayName(string toolName, string fallback, CultureInfo culture);

        string GetErrorMessage(string errorCode, string fallback, CultureInfo culture, params object[] arguments);
    }
}
