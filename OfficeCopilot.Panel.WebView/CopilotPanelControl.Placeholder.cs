using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using OfficeCopilot.Localization;

namespace OfficeCopilot.Panel.WebView
{
    public partial class CopilotPanelControl
    {
        private System.Windows.Forms.Panel _webViewPlaceholderPanel;
        private Label _webViewPlaceholderTitleLabel;
        private Label _webViewPlaceholderMessageLabel;

        private string BuildInitializationErrorMessage(Exception ex)
        {
            _lastPlaceholderException = ex;
            return LocalizationService.Format(
                "Web.panel.initializationError",
                "\r\n",
                ex.Message,
                Presentation.PanelWebViewBridge.GetUserDataFolder(HostAdapter == null ? null : HostAdapter.HostApplicationName));
        }

        private void InitializePlaceholderOverlay()
        {
            _webViewPlaceholderPanel = new System.Windows.Forms.Panel();
            _webViewPlaceholderTitleLabel = new Label();
            _webViewPlaceholderMessageLabel = new Label();

            var layout = new TableLayoutPanel();

            _webViewPlaceholderPanel.BackColor = Color.White;
            _webViewPlaceholderPanel.Dock = DockStyle.Fill;

            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.Dock = DockStyle.Fill;
            layout.BackColor = Color.White;
            layout.Padding = new Padding(24);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));

            _webViewPlaceholderTitleLabel.AutoSize = true;
            _webViewPlaceholderTitleLabel.Anchor = AnchorStyles.None;
            _webViewPlaceholderTitleLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 134);
            _webViewPlaceholderTitleLabel.ForeColor = Color.FromArgb(31, 41, 55);
            _webViewPlaceholderTitleLabel.Margin = new Padding(3, 3, 3, 8);
            _webViewPlaceholderTitleLabel.Text = LocalizationService.Get("Web.panel.loading");

            _webViewPlaceholderMessageLabel.AutoSize = true;
            _webViewPlaceholderMessageLabel.Anchor = AnchorStyles.None;
            _webViewPlaceholderMessageLabel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point, 134);
            _webViewPlaceholderMessageLabel.ForeColor = Color.FromArgb(107, 114, 128);
            _webViewPlaceholderMessageLabel.MaximumSize = new Size(360, 0);
            _webViewPlaceholderMessageLabel.TextAlign = ContentAlignment.MiddleCenter;
            _webViewPlaceholderMessageLabel.Text = LocalizationService.Get("Web.panel.initializing");

            layout.Controls.Add(_webViewPlaceholderTitleLabel, 0, 1);
            layout.Controls.Add(_webViewPlaceholderMessageLabel, 0, 2);

            _webViewPlaceholderPanel.Controls.Add(layout);
            spcMain.Panel1.Controls.Add(_webViewPlaceholderPanel);
            _webViewPlaceholderPanel.BringToFront();
        }

        private void ShowPlaceholder(string title, string message)
        {
            PanelTrace.Write("[CopilotPanelControl] ShowPlaceholder: title=" + (title ?? string.Empty) + ", message=" + (message ?? string.Empty));
            _activePlaceholderTitleKey = FindLocalizedKey(title, "Web.panel.loading", "Web.panel.loadFailed");
            _activePlaceholderMessageKey = FindLocalizedKey(message, "Web.panel.initializing", "Web.panel.loadingAssets");
            if (_activePlaceholderTitleKey == "Web.panel.loading")
            {
                _lastPlaceholderException = null;
            }
            _webViewPlaceholderTitleLabel.Text = title;
            _webViewPlaceholderMessageLabel.Text = message;
            _webViewPlaceholderPanel.Visible = true;
            _webViewPlaceholderPanel.BringToFront();
        }

        private void HidePlaceholder()
        {
            _webViewPlaceholderPanel.Visible = false;
            _activePlaceholderTitleKey = null;
            _activePlaceholderMessageKey = null;
            _lastPlaceholderException = null;
        }

        private static string FindLocalizedKey(string value, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (string.Equals(value, LocalizationService.Get(key), StringComparison.Ordinal))
                {
                    return key;
                }
            }

            return null;
        }

        private bool IsInDesignMode()
        {
            return LicenseManager.UsageMode == LicenseUsageMode.Designtime || DesignMode;
        }
    }
}