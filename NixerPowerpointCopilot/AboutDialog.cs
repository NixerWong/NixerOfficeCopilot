using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Localization;

namespace NixerPowerpointCopilot
{
    /// <summary>
    /// About dialog for NixerPowerPointCopilot.
    /// </summary>
    internal sealed class AboutDialog : Form
    {
        public AboutDialog()
        {
            InitializeForm();
            InitializeControls();
        }

        private void InitializeForm()
        {
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Segoe UI", 9F);

            Text = LocalizationService.Format(
                "Shell.About.Title",
                LocalizationService.Get("Shell.Product.PowerPoint"));
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);
        }

        private void InitializeControls()
        {
            var mainPanel = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = new Padding(8),
                Dock = DockStyle.Fill
            };
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            // Row 0: Icon + Title
            var iconBox = new PictureBox
            {
                Image = Properties.Resources._64x64,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(64, 64),
                Margin = new Padding(0, 0, 12, 0)
            };

            var titleLabel = new Label
            {
                Text = LocalizationService.Get("Shell.Product.PowerPoint"),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(183, 71, 42), // PowerPoint orange
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            var version = "2.3.61019";
            var versionLabel = new Label
            {
                Text = LocalizationService.Format("Shell.About.Version", version),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                AutoSize = true
            };

            var titlePanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                Margin = new Padding(0)
            };
            titlePanel.Controls.Add(titleLabel);
            titlePanel.Controls.Add(versionLabel);

            // Row 0-2: Icon spans 3 rows
            mainPanel.Controls.Add(iconBox, 0, 0);
            mainPanel.SetRowSpan(iconBox, 3);
            mainPanel.Controls.Add(titlePanel, 1, 0);

            // Separator
            var separator = new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                AutoSize = false,
                Height = 2,
                Margin = new Padding(0, 8, 0, 8),
                Dock = DockStyle.Top
            };
            mainPanel.Controls.Add(separator, 0, 3);
            mainPanel.SetColumnSpan(separator, 2);

            // Info rows
            int row = 4;
            AddInfoRow(mainPanel, LocalizationService.Get("Shell.About.Project"), "https://github.com/NixerWong/NixerOfficeCopilot", ref row, isLink: true, target: "https://github.com/NixerWong/NixerOfficeCopilot");
            AddInfoRow(mainPanel, LocalizationService.Get("Shell.About.Contact"), LocalizationService.Get("Shell.About.ContactValue"), ref row, isLink: true, target: "https://github.com/NixerWong/NixerOfficeCopilot/issues");
            AddInfoRow(mainPanel, LocalizationService.Get("Shell.About.License"), "GNU GPL v3.0", ref row);
            AddInfoRow(mainPanel, LocalizationService.Get("Shell.About.Author"), "Nixer", ref row);
            //AddInfoRow(mainPanel, "Notices:", "THIRD_PARTY_NOTICES.md", ref row, isLink: true,
            //target: Path.Combine(SlideAssetPaths.GetDefaultBuiltInRoot(), "THIRD_PARTY_NOTICES.md"));
            // OK button

            var buttonPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 12, 0, 0),
                Dock = DockStyle.Top
            };

            var okButton = new Button
            {
                Text = LocalizationService.Get("Shell.About.Ok"),
                Size = new Size(88, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(183, 71, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10)
            };
            okButton.FlatAppearance.BorderSize = 0;
            okButton.Click += (s, e) => Close();
            buttonPanel.Controls.Add(okButton);

            mainPanel.Controls.Add(buttonPanel, 0, row);
            mainPanel.SetColumnSpan(buttonPanel, 2);

            Controls.Add(mainPanel);
        }

        private void AddInfoRow(TableLayoutPanel panel, string label, string value, ref int row, bool isLink = false, string target = null)
        {
            var lbl = new Label
            {
                Text = label,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 2, 8, 2),
                TextAlign = ContentAlignment.TopRight
            };

            Control val;
            if (isLink)
            {
                var link = new LinkLabel
                {
                    Text = value,
                    Font = new Font("Segoe UI", 9),
                    AutoSize = true,
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    LinkColor = Color.FromArgb(0, 102, 204),
                    ActiveLinkColor = Color.FromArgb(0, 102, 204),
                    VisitedLinkColor = Color.FromArgb(0, 102, 204),
                    Margin = new Padding(0, 2, 0, 2)
                };
                var url = target ?? value;
                link.Click += (s, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch { /* ignore */ }
                };
                val = link;
            }
            else
            {
                val = new Label
                {
                    Text = value,
                    Font = new Font("Segoe UI", 9),
                    AutoSize = true,
                    Margin = new Padding(0, 2, 0, 2)
                };
            }

            panel.Controls.Add(lbl, 0, row);
            panel.Controls.Add(val, 1, row);
            row++;
        }
    }
}
