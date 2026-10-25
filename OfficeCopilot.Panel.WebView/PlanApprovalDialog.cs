using System;
using System.Drawing;
using System.Windows.Forms;
using OfficeCopilot.Localization;

namespace OfficeCopilot.Panel.WebView
{
    /// <summary>
    /// Phase 3: Plan 审批对话框。
    /// 显示 Agent 生成的计划文本，用户可批准或拒绝。
    /// </summary>
    internal sealed class PlanApprovalDialog : Form
    {
        private readonly string _planText;
        private TextBox _planTextBox;
        private Button _approveButton;
        private Button _rejectButton;
        private Label _headerLabel;
        private Label _descriptionLabel;

        public PlanApprovalDialog(string planText)
        {
            _planText = planText ?? string.Empty;
            InitializeForm();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += (sender, args) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            ApplyLocalization();
        }

        private void InitializeForm()
        {
            Text = LocalizationService.Get("Web.panel.planTitle");
            Size = new Size(700, 550);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Padding = new Padding(16);

            _headerLabel = new Label
            {
                Tag = "Web.panel.planHeader",
                Text = LocalizationService.Get("Web.panel.planHeader"),
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.DodgerBlue,
                AutoSize = true,
                Location = new Point(16, 16)
            };
            Controls.Add(_headerLabel);

            _descriptionLabel = new Label
            {
                Tag = "Web.panel.planDescription",
                Text = LocalizationService.Get("Web.panel.planDescription"),
                AutoSize = false,
                Size = new Size(650, 36),
                Location = new Point(16, 44),
                ForeColor = Color.DimGray
            };
            Controls.Add(_descriptionLabel);

            _planTextBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = false,
                Location = new Point(16, 88),
                Size = new Size(650, 340),
                Font = new Font("Consolas", 10f),
                Text = _planText
            };
            Controls.Add(_planTextBox);

            _approveButton = new Button
            {
                Text = LocalizationService.Get("Web.panel.approve"),
                Tag = "Web.panel.approve",
                Location = new Point(ClientSize.Width - 310, 440),
                Size = new Size(140, 36),
                BackColor = Color.FromArgb(0, 122, 204),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK
            };
            _approveButton.FlatAppearance.BorderSize = 0;

            _rejectButton = new Button
            {
                Text = LocalizationService.Get("Web.panel.reject"),
                Tag = "Web.panel.reject",
                Location = new Point(ClientSize.Width - 160, 440),
                Size = new Size(140, 36),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(_approveButton);
            Controls.Add(_rejectButton);

            AcceptButton = _approveButton;
            CancelButton = _rejectButton;
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(ApplyLocalization));
                return;
            }

            ApplyLocalization();
        }

        private void ApplyLocalization()
        {
            Text = LocalizationService.Get("Web.panel.planTitle");
            _headerLabel.Text = LocalizationService.Get("Web.panel.planHeader");
            _descriptionLabel.Text = LocalizationService.Get("Web.panel.planDescription");
            _approveButton.Text = LocalizationService.Get("Web.panel.approve");
            _rejectButton.Text = LocalizationService.Get("Web.panel.reject");
        }
    }
}
