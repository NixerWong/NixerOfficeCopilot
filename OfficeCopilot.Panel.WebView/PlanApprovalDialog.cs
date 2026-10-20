using System;
using System.Drawing;
using System.Windows.Forms;

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

        public PlanApprovalDialog(string planText)
        {
            _planText = planText ?? string.Empty;
            InitializeForm();
        }

        private void InitializeForm()
        {
            Text = "Nixer Copilot — Plan Approval";
            Size = new Size(700, 550);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Padding = new Padding(16);

            var headerLabel = new Label
            {
                Text = "▎Agent has prepared an implementation plan for your review.",
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.DodgerBlue,
                AutoSize = true,
                Location = new Point(16, 16)
            };
            Controls.Add(headerLabel);

            var subLabel = new Label
            {
                Text = "Please review the plan below. You can edit the text if needed. Click Approve to proceed or Reject to cancel.",
                AutoSize = false,
                Size = new Size(650, 36),
                Location = new Point(16, 44),
                ForeColor = Color.DimGray
            };
            Controls.Add(subLabel);

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
                Text = "✓ Approve",
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
                Text = "✗ Reject",
                Location = new Point(ClientSize.Width - 160, 440),
                Size = new Size(140, 36),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(_approveButton);
            Controls.Add(_rejectButton);

            AcceptButton = _approveButton;
            CancelButton = _rejectButton;
        }
    }
}
