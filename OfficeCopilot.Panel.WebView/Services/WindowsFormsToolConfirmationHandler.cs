using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Localization;
using System;
using System.Text;
using System.Windows.Forms;

namespace OfficeCopilot.Panel.WebView.Services
{
    public sealed class WindowsFormsToolConfirmationHandler : IToolConfirmationHandler
    {
        private static readonly IUserTextLocalizer UserTextLocalizer = new UserTextLocalizer();
        private readonly Control _owner;

        public WindowsFormsToolConfirmationHandler(Control owner)
        {
            _owner = owner;
        }

        public bool Confirm(ToolConfirmationRequest request)
        {
            request = request ?? new ToolConfirmationRequest();
            var culture = LocalizationService.Culture;
            var displayName = UserTextLocalizer.GetToolDisplayName(
                request.ToolName,
                request.ToolDisplayName,
                culture);
            var title = string.IsNullOrWhiteSpace(request.ToolDisplayName)
                ? LocalizationService.Get("Web.confirmation.title")
                : LocalizationService.Format("Web.confirmation.titleSuffix", displayName);
            var message = BuildMessage(request, displayName);

            DialogResult result = DialogResult.No;
            if (_owner != null && _owner.IsHandleCreated)
            {
                if (_owner.InvokeRequired)
                {
                    _owner.Invoke(new Action(() =>
                    {
                        result = MessageBox.Show(_owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    }));
                }
                else
                {
                    result = MessageBox.Show(_owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                }
            }
            else
            {
                result = MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            }

            return result == DialogResult.Yes;
        }

        private static string BuildMessage(ToolConfirmationRequest request, string displayName)
        {
            var culture = LocalizationService.Culture;
            var builder = new StringBuilder();
            builder.AppendLine(LocalizationService.Get("Web.confirmation.warningText"));
            builder.AppendLine(LocalizationService.Get("Web.confirmation.tool") + ": " + Safe(displayName, request.ToolName));
            builder.AppendLine(LocalizationService.Get("Web.confirmation.risk") + ": " + Safe(request.RiskLevel, "high"));

            if (!string.IsNullOrWhiteSpace(request.DocumentName))
            {
                builder.AppendLine(LocalizationService.Get("Web.confirmation.document") + ": " + request.DocumentName);
            }

            if (!string.IsNullOrWhiteSpace(request.OperationSummary))
            {
                builder.AppendLine(LocalizationService.Get("Web.confirmation.operation") + ": " + request.OperationSummary);
            }

            if (!string.IsNullOrWhiteSpace(request.BackupPath))
            {
                builder.AppendLine(LocalizationService.Get("Web.confirmation.preExecutionBackup") + request.BackupPath);
            }

            if (!string.IsNullOrWhiteSpace(request.ArgumentsSummary))
            {
                builder.AppendLine();
                builder.AppendLine(LocalizationService.Get("Web.confirmation.argumentsSummary"));
                builder.AppendLine(request.ArgumentsSummary);
            }

            builder.AppendLine();
            builder.Append(LocalizationService.Get("Web.confirmation.choices"));
            return builder.ToString().Trim();
        }

        private static string Safe(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
