using DevExpress.LookAndFeel;
using DevExpress.Skins;
using DevExpress.Utils.Drawing;
using DevExpress.XtraBars;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using Microsoft.Extensions.Logging;
using OfficeCopilot.Panel.Abstractions;
using OfficeCopilot.Localization;
using OfficeCopilot.Panel.WebView.Services.References;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OfficeCopilot.Panel.WebView
{
    public partial class CopilotPanelControl
    {
        // Width reserved for the paperclip menu at 96 DPI; the chip area starts right after it.
        private const int AttachmentMenuDesignWidth = 60;
        // Single-row height = row padding + chip margin + chip height + chip margin + row padding.
        // It equals the menu bar height (44) so the strip is never shorter than the menu button.
        private const int AttachmentContainerMinimumHeight = 44;
        private const int AttachmentRowPaddingHorizontal = 6;
        private const int AttachmentRowPaddingVertical = 5;
        private const int AttachmentChipHeight = 28;
        private const int AttachmentChipVerticalMargin = 3;
        private const int AttachmentChipGap = 6;
        private const int AttachmentChipLabelLeft = 10;
        private const int AttachmentChipTextSlack = 4;
        private const int AttachmentChipButtonWidth = 18;
        private const int AttachmentChipButtonRightMargin = 6;
        private const int AttachmentChipMaxWidth = 560;
        private const string WebReferencePreviewPathMetadataKey = "previewTempFilePath";
        private const string ClipboardImageTempDirectoryName = "ClipboardAttachments";
        private const int MaxRecentReferenceGroupCount = 3;
        private static readonly string[] AttachmentSummaryMetadataKeys =
        {
            "pageCount", "sheetCount", "slideCount", "contentTruncated", "conversionError"
        };
        private readonly List<RecentReferenceGroup> _recentReferenceGroups = new List<RecentReferenceGroup>();
        private readonly List<BarButtonItem> _recentReferenceMenuButtons = new List<BarButtonItem>();
        private readonly ToolTip _chipTooltip = new ToolTip();
        private Label _attachmentPreparationLabel;
        private IList<ReferenceItem> _attachmentPreparationReferences;
        private ReferenceSendPlan _attachmentPreparationPlan;

        private sealed class RecentReferenceGroup
        {
            public RecentReferenceGroup()
            {
                Key = string.Empty;
                Caption = string.Empty;
                References = new List<ReferenceItem>();
            }

            public string Key { get; set; }

            public string Caption { get; set; }

            public List<ReferenceItem> References { get; set; }
        }

        private float DpiScaleFactor
        {
            get
            {
                if (Font == null)
                {
                    return 1.0f;
                }

                // AutoScaleDimensions uses 15F as the design-time font height.
                return (float)Font.Height / 15f;
            }
        }

        private void InitializeAttachmentReferenceState()
        {
            var scale = DpiScaleFactor;

            if (sbdcAttachmentMenu != null)
            {
                sbdcAttachmentMenu.Height = (int)(AttachmentContainerMinimumHeight * scale);
                SyncAttachmentBarHeight();
                EnableAttachmentFileDrop(sbdcAttachmentMenu);
            }

            ApplyAttachmentSurfaceStyle();

            if (flpAttachments != null)
            {
                flpAttachments.WrapContents = true;
                flpAttachments.FlowDirection = FlowDirection.LeftToRight;
                flpAttachments.AutoScroll = false;
                flpAttachments.Padding = new Padding(
                    (int)(AttachmentRowPaddingHorizontal * scale),
                    (int)(AttachmentRowPaddingVertical * scale),
                    (int)(AttachmentRowPaddingHorizontal * scale),
                    (int)(AttachmentRowPaddingVertical * scale));
            }

            LayoutAttachmentStrip();
            RefreshSelectedReferenceDisplay();
            RefreshRecentReferenceMenu();
        }

        private void ApplyAttachmentSurfaceStyle()
        {
            var backgroundColor = sbdcAttachmentMenu == null
                ? SystemColors.Control
                : sbdcAttachmentMenu.BackColor;

            if (flpAttachments != null)
            {
                flpAttachments.BackColor = backgroundColor;
            }
        }

        private void WireAttachmentReferenceEvents()
        {
            btnSelectFiles.ItemClick += btnSelectFiles_ItemClick;
            ribeWebUrl.ButtonClick += ribeWebUrl_ButtonClick;
            ribeWebUrl.KeyDown += ribeWebUrl_KeyDown;
            if (sbdcAttachmentMenu != null)
            {
                sbdcAttachmentMenu.SizeChanged += sbdcAttachmentMenu_SizeChanged;
            }
            if (flpAttachments != null)
            {
                flpAttachments.SizeChanged += flpAttachments_SizeChanged;
                flpAttachments.Paint += flpAttachments_Paint;
            }
        }

        // The chip area lives inside the full-width menu bar, to the right of the paperclip menu.
        private void LayoutAttachmentStrip()
        {
            if (sbdcAttachmentMenu == null || flpAttachments == null)
            {
                return;
            }

            var menuWidth = (int)(AttachmentMenuDesignWidth * DeviceDpi / 96f);
            var chipAreaWidth = Math.Max(0, sbdcAttachmentMenu.ClientSize.Width - menuWidth);
            flpAttachments.SetBounds(menuWidth, 0, chipAreaWidth, sbdcAttachmentMenu.ClientSize.Height);
        }

        private void sbdcAttachmentMenu_SizeChanged(object sender, EventArgs e)
        {
            LayoutAttachmentStrip();
        }

        // Paints the menu bar's skin element with the same origin as the bar, so the hatch stays continuous.
        private void flpAttachments_Paint(object sender, PaintEventArgs e)
        {
            var element = SkinManager.GetSkinElement(SkinProductId.Bars, UserLookAndFeel.Default, BarSkins.SkinBar);
            if (element == null)
            {
                return;
            }

            var barRect = new Rectangle(
                -flpAttachments.Left,
                -flpAttachments.Top,
                sbdcAttachmentMenu.ClientSize.Width,
                sbdcAttachmentMenu.ClientSize.Height);

            using (var cache = new GraphicsCache(e.Graphics))
            {
                SkinElementPainter.Default.DrawObject(new SkinElementInfo(element, barRect) { Cache = cache });
            }
        }

        private bool TryHandleChatPasteShortcut(KeyEventArgs e)
        {
            if (e == null || !e.Control || e.KeyCode != Keys.V)
            {
                return false;
            }

            if (!TryPasteClipboardReferences())
            {
                return false;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            return true;
        }

        private bool TryPasteClipboardReferences()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    return TryPasteClipboardFiles();
                }

                if (Clipboard.ContainsImage())
                {
                    return TryPasteClipboardImage();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "读取剪贴板参考内容失败。");
                MessageBox.Show(this, LocalizationService.Format("Web.panel.pasteFailed", ex.Message), LocalizationService.Get("Web.panel.pasteTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }

            return false;
        }

        private bool TryPasteClipboardFiles()
        {
            var files = Clipboard.GetFileDropList();
            if (files == null || files.Count == 0)
            {
                return false;
            }

            var references = new List<ReferenceItem>();
            foreach (var filePath in files.Cast<string>().Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var reference = CreateFileReferenceItem(filePath);
                if (reference != null)
                {
                    references.Add(reference);
                }
            }

            return AddPastedReferences(references, LocalizationService.Get("Web.panel.pasteTitle"), LocalizationService.Get("Web.panel.noNewReferences"));
        }

        private void EnableAttachmentFileDrop(Control control)
        {
            if (control == null)
            {
                return;
            }

            control.AllowDrop = true;
            control.DragEnter += AttachmentFileDrop_DragEnter;
            control.DragDrop += AttachmentFileDrop_DragDrop;
            foreach (Control child in control.Controls)
            {
                EnableAttachmentFileDrop(child);
            }
        }

        private void AttachmentFileDrop_DragEnter(object sender, DragEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            var paths = GetSupportedDroppedFilePaths(e.Data);
            e.Effect = paths.Length > 0 && (e.AllowedEffect & DragDropEffects.Copy) != 0
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void AttachmentFileDrop_DragDrop(object sender, DragEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            var references = GetSupportedDroppedFilePaths(e.Data)
                .Select(CreateFileReferenceItem)
                .Where(reference => reference != null)
                .ToList();
            AddPastedReferences(references, LocalizationService.Get("Web.panel.dragReferences"), LocalizationService.Get("Web.panel.noSupportedFiles"));
        }

        private static string[] GetSupportedDroppedFilePaths(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
            {
                return new string[0];
            }

            var paths = data.GetData(DataFormats.FileDrop) as string[];
            return paths == null
                ? new string[0]
                : paths.Where(path => !string.IsNullOrWhiteSpace(path)
                    && AttachmentFileTypes.IsSupported(Path.GetExtension(path))).ToArray();
        }

        private bool TryPasteClipboardImage()
        {
            using (var image = Clipboard.GetImage())
            {
                if (image == null)
                {
                    return false;
                }

                var tempFilePath = SaveClipboardImageToTempFile(image);
                var reference = CreateFileReferenceItem(tempFilePath);
                if (reference == null)
                {
                    MessageBox.Show(this, LocalizationService.Get("Web.panel.clipboardImageFailed"), LocalizationService.Get("Web.panel.pasteTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return true;
                }

                return AddPastedReferences(new[] { reference }, LocalizationService.Get("Web.panel.pasteTitle"), LocalizationService.Get("Web.panel.noNewClipboardImages"));
            }
        }

        private bool AddPastedReferences(IEnumerable<ReferenceItem> references, string caption, string emptyMessage)
        {
            var items = references == null
                ? new List<ReferenceItem>()
                : references.Where(reference => reference != null).ToList();

            if (items.Count == 0)
            {
                MessageBox.Show(this, emptyMessage, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }

            var limitReached = false;
            var addedCount = TryAddReferences(items, true, true, out limitReached);
            RefreshSelectedReferenceDisplay();

            if (addedCount == 0)
            {
                MessageBox.Show(this, limitReached ? LocalizationService.Get("Web.panel.referenceLimit") : emptyMessage, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return true;
        }

        private string SaveClipboardImageToTempFile(Image image)
        {
            var folderPath = Path.Combine(Path.GetTempPath(), "OfficeCopilot", ClipboardImageTempDirectoryName);
            Directory.CreateDirectory(folderPath);

            for (var index = 1; index < int.MaxValue; index++)
            {
                var fileName = LocalizationService.Format("Web.attachment.clipboardImageFile", index);
                var filePath = Path.Combine(folderPath, fileName);

                try
                {
                    using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        image.Save(stream, ImageFormat.Png);
                    }

                    return filePath;
                }
                catch (IOException)
                {
                }
            }

            throw new IOException(LocalizationService.Get("Web.attachment.noTempFile"));
        }

        public void SetSelectedReferences(IEnumerable<ReferenceItem> references)
        {
            _selectedReferences.Clear();
            TryAddReferences(references, false, false, out _);
            RefreshSelectedReferenceDisplay();
        }

        public IReadOnlyList<ReferenceItem> GetSelectedReferences()
        {
            return SnapshotSelectedReferences();
        }

        private List<ReferenceItem> SnapshotSelectedReferences()
        {
            return _selectedReferences
                .Where(r => r != null)
                .Select(CloneReferenceItem)
                .ToList();
        }

        private void CompleteReferenceSelectionForSend(IReadOnlyList<ReferenceItem> referencesUsedForTurn)
        {
            RegisterRecentReferenceGroup(referencesUsedForTurn);
            ClearSelectedReferences();
        }

        private void ClearSelectedReferences()
        {
            _selectedReferences.Clear();
            if (beiInputWebUrl != null)
            {
                beiInputWebUrl.EditValue = string.Empty;
            }

            RefreshSelectedReferenceDisplay();
        }

        private void RefreshSelectedReferenceDisplay()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshSelectedReferenceDisplay));
                return;
            }

            var names = _selectedReferences
                .Where(r => r != null)
                .Select(GetReferenceDisplayText)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            RebuildAttachmentChips(_selectedReferences);

            //if (menuAddRef != null)
            //{
            //    menuAddRef.Caption = _selectedReferences.Count == 0
            //        ? ""
            //        : "(" + _selectedReferences.Count.ToString() + ")";
            //}
        }

        private void flpAttachments_SizeChanged(object sender, EventArgs e)
        {
            if (_attachmentPreparationLabel != null && !_attachmentPreparationLabel.IsDisposed)
            {
                _attachmentPreparationLabel.Width = Math.Max(40, GetAttachmentRowInnerWidth());
            }
            flpAttachments.Invalidate();
            UpdateAttachmentContainerHeight();
        }

        private void UpdateAttachmentPreparationProgress(ReferencePreparationProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            SafeUiInvoke(() =>
            {
                if (flpAttachments == null)
                {
                    return;
                }

                var scale = DpiScaleFactor;
                if (_attachmentPreparationLabel == null || _attachmentPreparationLabel.IsDisposed)
                {
                    _attachmentPreparationLabel = new Label
                    {
                        AutoEllipsis = true,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Font = Font,
                        Height = (int)(AttachmentChipHeight * scale),
                        Margin = new Padding(0, (int)(AttachmentChipVerticalMargin * scale), 0, (int)(AttachmentChipVerticalMargin * scale))
                    };
                    EnableAttachmentFileDrop(_attachmentPreparationLabel);
                }

                var total = Math.Max(1, progress.TotalCount);
                var current = Math.Min(total, progress.CompletedCount >= total ? total : progress.CompletedCount + 1);
                var name = string.IsNullOrWhiteSpace(progress.DisplayName) ? string.Empty : "：" + progress.DisplayName;
                _attachmentPreparationLabel.Text = progress.Status + " " + current.ToString() + "/" + total.ToString() + name;
                _attachmentPreparationLabel.Width = Math.Max(40, GetAttachmentRowInnerWidth());
                _chipTooltip.SetToolTip(_attachmentPreparationLabel, _attachmentPreparationLabel.Text);

                if (_attachmentPreparationLabel.Parent != flpAttachments)
                {
                    flpAttachments.Controls.Clear();
                    flpAttachments.Controls.Add(_attachmentPreparationLabel);
                }

                UpdateAttachmentContainerHeight();
            });
        }

        private void ClearAttachmentPreparationProgress()
        {
            SafeUiInvoke(() =>
            {
                if (_attachmentPreparationLabel == null)
                {
                    return;
                }

                if (flpAttachments != null)
                {
                    flpAttachments.Controls.Remove(_attachmentPreparationLabel);
                }

                _attachmentPreparationLabel.Dispose();
                _attachmentPreparationLabel = null;
                UpdateAttachmentContainerHeight();
            });
        }

        private void CompleteAttachmentPreparationProgress(IList<ReferenceItem> references, ReferenceSendPlan plan)
        {
            if (references == null)
            {
                return;
            }

            _attachmentPreparationReferences = references.ToList();
            _attachmentPreparationPlan = plan;

            var details = new List<string>();
            var failedCount = 0;
            var truncatedCount = 0;
            foreach (var reference in references.Where(item => item != null))
            {
                var prepared = plan == null || plan.PreparedReferences == null
                    ? null
                    : plan.PreparedReferences.FirstOrDefault(item => item != null
                        && string.Equals(item.ReferenceId, reference.ReferenceId, StringComparison.OrdinalIgnoreCase));
                var status = LocalizationService.Get("Web.attachment.parseComplete");
                if (prepared == null)
                {
                    status = LocalizationService.Get("Web.attachment.noParseResult");
                }
                else
                {
                    string conversionError;
                    string truncated;
                    if (prepared.Metadata != null
                        && prepared.Metadata.TryGetValue("conversionError", out conversionError)
                        && !string.IsNullOrWhiteSpace(conversionError))
                    {
                        status = LocalizationService.Format("Web.attachment.parseFailed", conversionError);
                        failedCount++;
                    }
                    else if (prepared.Metadata != null
                        && prepared.Metadata.TryGetValue("contentTruncated", out truncated)
                        && string.Equals(truncated, bool.TrueString, StringComparison.OrdinalIgnoreCase))
                    {
                        status = LocalizationService.Get("Web.attachment.parseTruncated");
                        truncatedCount++;
                    }
                }

                details.Add(GetReferenceDisplayText(reference) + "：" + status);
            }

            var summary = LocalizationService.Format("Web.attachment.parsed", LocalizationService.Format("Web.attachment.count", references.Count));
            if (truncatedCount > 0)
            {
                summary += LocalizationService.Get("Web.attachment.listSeparator")
                    + LocalizationService.Format("Web.attachment.truncatedCount", truncatedCount);
            }
            if (failedCount > 0)
            {
                summary += LocalizationService.Get("Web.attachment.listSeparator")
                    + LocalizationService.Format("Web.attachment.failedCount", failedCount);
            }

            SafeUiInvoke(() =>
            {
                if (_attachmentPreparationLabel == null || _attachmentPreparationLabel.IsDisposed)
                {
                    return;
                }

                _attachmentPreparationLabel.Text = summary;
                _chipTooltip.SetToolTip(_attachmentPreparationLabel, string.Join(Environment.NewLine, details));
            });
        }

        private void RebuildAttachmentChips(IEnumerable<ReferenceItem> references)
        {
            if (flpAttachments == null)
            {
                return;
            }

            flpAttachments.SuspendLayout();
            try
            {
                flpAttachments.Controls.Clear();

                if (references != null)
                {
                    foreach (var reference in references.Where(r => r != null))
                    {
                        flpAttachments.Controls.Add(CreateAttachmentChip(reference));
                    }
                }
            }
            finally
            {
                flpAttachments.ResumeLayout(true);
            }

            UpdateAttachmentContainerHeight();
        }

        private Control CreateAttachmentChip(ReferenceItem reference)
        {
            var identity = BuildReferenceIdentity(reference);
            var text = GetReferenceDisplayText(reference);
            var scale = DpiScaleFactor;
            var chipHeight = (int)(AttachmentChipHeight * scale);
            var innerHeight = chipHeight - 2;
            var labelLeft = (int)(AttachmentChipLabelLeft * scale);
            var buttonSize = (int)(AttachmentChipButtonWidth * scale);
            var buttonRightMargin = (int)(AttachmentChipButtonRightMargin * scale);
            var gap = (int)(AttachmentChipGap * scale);
            var rowGap = (int)(AttachmentChipVerticalMargin * scale);
            var labelMinWidth = (int)(40 * scale);

            var chipWidth = CalculateAttachmentChipWidth(text);
            var buttonLeft = chipWidth - 2 - buttonRightMargin - buttonSize;
            var labelWidth = Math.Max(labelMinWidth, buttonLeft - gap - labelLeft);

            var chip = new System.Windows.Forms.Panel();
            chip.Tag = identity;
            chip.Size = new Size(chipWidth, chipHeight);
            chip.Margin = new Padding(0, rowGap, gap, rowGap);
            chip.Padding = new Padding(0);
            chip.BackColor = Color.White;
            chip.BorderStyle = BorderStyle.FixedSingle;
            chip.Cursor = Cursors.Hand;
            chip.DoubleClick += AttachmentChip_DoubleClick;

            var label = new Label();
            label.AutoEllipsis = true;
            label.Text = text;
            label.Font = Font;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Location = new Point(labelLeft, 0);
            label.Size = new Size(labelWidth, innerHeight);
            label.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            label.Tag = identity;
            label.Cursor = Cursors.Hand;
            label.DoubleClick += AttachmentChip_DoubleClick;
            var tooltipText = BuildAttachmentChipTooltip(reference, text);
            _chipTooltip.SetToolTip(label, tooltipText);
            _chipTooltip.SetToolTip(chip, tooltipText);

            var button = new Button();
            button.Text = "×";
            button.Tag = identity;
            button.TabStop = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.Transparent;
            button.Cursor = Cursors.Hand;
            button.Font = Font;
            button.Location = new Point(buttonLeft, (innerHeight - buttonSize) / 2);
            button.Size = new Size(buttonSize, buttonSize);
            button.Anchor = AnchorStyles.Right;
            button.Click += AttachmentChipRemoveButton_Click;

            chip.Controls.Add(label);
            chip.Controls.Add(button);
            EnableAttachmentFileDrop(chip);
            return chip;
        }

        private void AttachmentChip_DoubleClick(object sender, EventArgs e)
        {
            var identity = string.Empty;
            var control = sender as Control;
            if (control != null)
            {
                identity = Convert.ToString(control.Tag);
                if (string.IsNullOrWhiteSpace(identity) && control.Parent != null)
                {
                    identity = Convert.ToString(control.Parent.Tag);
                }
            }

            if (string.IsNullOrWhiteSpace(identity))
            {
                return;
            }

            var reference = _selectedReferences.FirstOrDefault(item => string.Equals(BuildReferenceIdentity(item), identity, StringComparison.OrdinalIgnoreCase));
            if (reference == null)
            {
                return;
            }

            OpenReferencePreview(reference);
        }

        private int GetAttachmentRowInnerWidth()
        {
            if (flpAttachments == null)
            {
                return (int)(AttachmentChipMaxWidth * DpiScaleFactor);
            }

            return Math.Max(0, flpAttachments.ClientSize.Width - flpAttachments.Padding.Horizontal);
        }

        private int CalculateAttachmentChipWidth(string text)
        {
            var scale = DpiScaleFactor;
            var scaledMaxWidth = (int)(AttachmentChipMaxWidth * scale);
            var availableWidth = Math.Max((int)(120 * scale), GetAttachmentRowInnerWidth());
            var maxWidth = Math.Min(scaledMaxWidth, availableWidth);
            var measured = TextRenderer.MeasureText(
                text ?? string.Empty,
                Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            // The trailing +2 accounts for the chip's FixedSingle border.
            var width = (int)(AttachmentChipLabelLeft * scale)
                + measured.Width
                + (int)(AttachmentChipTextSlack * scale)
                + (int)(AttachmentChipGap * scale)
                + (int)(AttachmentChipButtonWidth * scale)
                + (int)(AttachmentChipButtonRightMargin * scale)
                + 2;
            return Math.Max((int)(100 * scale), Math.Min(maxWidth, width));
        }

        private void AttachmentChipRemoveButton_Click(object sender, EventArgs e)
        {
            var button = sender as Button;
            if (button == null)
            {
                return;
            }

            var identity = Convert.ToString(button.Tag);
            if (RemoveSelectedReferenceByIdentity(identity))
            {
                RefreshSelectedReferenceDisplay();
            }
        }

        private bool RemoveSelectedReferenceByIdentity(string identity)
        {
            var value = (identity ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (var index = 0; index < _selectedReferences.Count; index++)
            {
                var reference = _selectedReferences[index];
                if (string.Equals(BuildReferenceIdentity(reference), value, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedReferences.RemoveAt(index);
                    return true;
                }
            }

            return false;
        }

        private void UpdateAttachmentContainerHeight()
        {
            if (IsDisposed || sbdcAttachmentMenu == null || flpAttachments == null)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(UpdateAttachmentContainerHeight));
                return;
            }

            var scale = DpiScaleFactor;
            var minHeight = (int)(AttachmentContainerMinimumHeight * scale);
            var desiredHeight = minHeight;
            if (flpAttachments.Controls.Count > 0)
            {
                flpAttachments.PerformLayout();
                var contentBottom = 0;
                foreach (Control control in flpAttachments.Controls)
                {
                    contentBottom = Math.Max(contentBottom, control.Bottom);
                }

                desiredHeight = Math.Max(
                    minHeight,
                    contentBottom + flpAttachments.Padding.Bottom + (int)(AttachmentChipVerticalMargin * scale));
            }

            if (sbdcAttachmentMenu.Height != desiredHeight)
            {
                sbdcAttachmentMenu.Height = desiredHeight;
            }

            SyncAttachmentBarHeight();
        }

        // The bar only paints its skin border up to MinHeight, so it must fill the dock height.
        private void SyncAttachmentBarHeight()
        {
            if (panel2TopBar?.OptionsBar != null && sbdcAttachmentMenu != null && panel2TopBar.OptionsBar.MinHeight != sbdcAttachmentMenu.Height)
            {
                panel2TopBar.OptionsBar.MinHeight = sbdcAttachmentMenu.Height;
            }
        }

        private static string GetReferenceDisplayText(ReferenceItem reference)
        {
            if (reference == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(reference.DisplayName))
            {
                return reference.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(reference.Source))
            {
                return reference.Source.Trim();
            }

            if (!string.IsNullOrWhiteSpace(reference.LocalPath))
            {
                return Path.GetFileName(reference.LocalPath);
            }

            return reference.Kind.ToString();
        }

        private void ApplyPreparedAttachmentSummaries(IList<ReferenceItem> references, ReferenceSendPlan plan)
        {
            if (references == null || plan == null || plan.PreparedReferences == null)
            {
                return;
            }

            foreach (var prepared in plan.PreparedReferences.Where(item => item != null))
            {
                var reference = references.FirstOrDefault(item => item != null
                    && string.Equals(item.ReferenceId, prepared.ReferenceId, StringComparison.OrdinalIgnoreCase));
                if (reference == null)
                {
                    continue;
                }

                if (reference.Metadata == null)
                {
                    reference.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                CopyAttachmentSummaryMetadata(prepared.Metadata, reference.Metadata);
            }

            foreach (var group in _recentReferenceGroups)
            {
                foreach (var savedReference in group.References.Where(item => item != null))
                {
                    var reference = references.FirstOrDefault(item => item != null
                        && string.Equals(BuildReferenceIdentity(item), BuildReferenceIdentity(savedReference), StringComparison.OrdinalIgnoreCase));
                    if (reference != null)
                    {
                        CopyAttachmentSummaryMetadata(reference.Metadata, savedReference.Metadata);
                    }
                }

                group.Caption = BuildRecentReferenceGroupCaption(group.References);
            }

            RefreshRecentReferenceMenu();
        }

        private static void CopyAttachmentSummaryMetadata(IDictionary<string, string> source, IDictionary<string, string> target)
        {
            if (source == null || target == null)
            {
                return;
            }

            foreach (var key in AttachmentSummaryMetadataKeys)
            {
                string value;
                if (source.TryGetValue(key, out value))
                {
                    target[key] = value ?? string.Empty;
                }
            }
        }

        private static string BuildAttachmentChipTooltip(ReferenceItem reference, string displayText)
        {
            var summary = BuildAttachmentSummaryText(reference);
            return string.IsNullOrWhiteSpace(summary)
                ? displayText
                : displayText + Environment.NewLine + summary;
        }

        private static string BuildAttachmentSummaryText(ReferenceItem reference)
        {
            if (reference == null || reference.Metadata == null)
            {
                return string.Empty;
            }

            var details = new List<string>();
            AddCountSummary(details, reference.Metadata, "pageCount", "Web.attachment.pages");
            AddCountSummary(details, reference.Metadata, "sheetCount", "Web.attachment.visibleWorksheets");
            AddCountSummary(details, reference.Metadata, "slideCount", "Web.attachment.slides");

            string truncated;
            if (reference.Metadata.TryGetValue("contentTruncated", out truncated)
                && string.Equals(truncated, bool.TrueString, StringComparison.OrdinalIgnoreCase))
            {
                details.Add(LocalizationService.Get("Web.attachment.contentTruncated"));
            }

            string conversionError;
            if (reference.Metadata.TryGetValue("conversionError", out conversionError)
                && !string.IsNullOrWhiteSpace(conversionError))
            {
                details.Add(LocalizationService.Get("Web.attachment.parseFailure"));
            }

            return string.Join("，", details);
        }

        private static void AddCountSummary(ICollection<string> details, IDictionary<string, string> metadata, string key, string resourceKey)
        {
            string value;
            int count;
            if (metadata.TryGetValue(key, out value)
                && int.TryParse(value, out count)
                && count > 0)
            {
                details.Add(LocalizationService.Format(resourceKey, count));
            }
        }

        private void btnSelectFiles_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = LocalizationService.Get("Web.panel.addReferenceFiles");
                dialog.Filter = AttachmentFileTypes.BuildOpenFileDialogFilter();
                dialog.Multiselect = true;

                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames == null || dialog.FileNames.Length == 0)
                {
                    return;
                }

                var references = dialog.FileNames
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(CreateFileReferenceItem)
                    .Where(reference => reference != null)
                    .ToList();

                var limitReached = false;
                var addedCount = TryAddReferences(references, true, true, out limitReached);
                RefreshSelectedReferenceDisplay();

                if (addedCount == 0)
                {
                    MessageBox.Show(this, limitReached ? LocalizationService.Get("Web.panel.referenceLimit") : LocalizationService.Get("Web.panel.noNewReferences"), LocalizationService.Get("Web.panel.fileReferences"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private async void ribeWebUrl_ButtonClick(object sender, ButtonPressedEventArgs e)
        {
            await AddWebReferenceFromInputAsync();
        }

        private async void ribeWebUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e == null || e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            await AddWebReferenceFromInputAsync();
        }

        private async Task AddWebReferenceFromInputAsync()
        {
            var url = GetCurrentWebReferenceInputText();

            url = url.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                ClearCurrentWebReferenceInputText();
                CloseAttachmentPopupMenu();
                return;
            }

            string feedbackMessage = null;
            string feedbackCaption = null;
            MessageBoxIcon feedbackIcon = MessageBoxIcon.None;

            UseWaitCursor = true;
            try
            {
                var reference = await _webReferenceImporter.ImportAsync(
                    url,
                    ResolveReferenceImportTimeoutSeconds(),
                    CancellationToken.None);

                var limitReached = false;
                var addedCount = TryAddReferences(new[] { reference }, true, true, out limitReached);
                if (addedCount == 0)
                {
                    feedbackMessage = limitReached ? LocalizationService.Get("Web.panel.referenceLimit") : LocalizationService.Get("Web.panel.webReferenceDuplicate");
                    feedbackCaption = LocalizationService.Get("Web.panel.webReference");
                    feedbackIcon = MessageBoxIcon.Information;
                }
                else
                {
                    RefreshSelectedReferenceDisplay();
                }
            }
            catch (Exception ex)
            {
                feedbackMessage = LocalizationService.Format("Web.panel.webReferenceFailed", ex.Message);
                feedbackCaption = LocalizationService.Get("Web.panel.webReference");
                feedbackIcon = MessageBoxIcon.Error;
            }
            finally
            {
                ClearCurrentWebReferenceInputText();
                CloseAttachmentPopupMenu();
                UseWaitCursor = false;
            }

            if (!string.IsNullOrWhiteSpace(feedbackMessage))
            {
                MessageBox.Show(this, feedbackMessage, feedbackCaption, MessageBoxButtons.OK, feedbackIcon);
            }
        }

        private string GetCurrentWebReferenceInputText()
        {
            var activeEditor = barManager == null ? null : barManager.ActiveEditor as BaseEdit;
            var value = Convert.ToString(activeEditor == null ? null : activeEditor.EditValue);
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (beiInputWebUrl != null)
                {
                    beiInputWebUrl.EditValue = value;
                }

                return value;
            }

            return Convert.ToString(beiInputWebUrl == null ? null : beiInputWebUrl.EditValue) ?? string.Empty;
        }

        private void ClearCurrentWebReferenceInputText()
        {
            if (beiInputWebUrl != null)
            {
                beiInputWebUrl.EditValue = string.Empty;
            }

            var activeEditor = barManager == null ? null : barManager.ActiveEditor as BaseEdit;
            if (activeEditor != null)
            {
                activeEditor.EditValue = string.Empty;
            }
        }

        private void CloseAttachmentPopupMenu()
        {
            if (barManager != null)
            {
                barManager.HideToolBarsPopup();
            }

            if (txtChat != null && txtChat.CanFocus)
            {
                txtChat.Focus();
            }
        }

        private void OpenReferencePreview(ReferenceItem reference)
        {
            if (reference == null)
            {
                return;
            }

            try
            {
                if (reference.Kind == ReferenceItemKind.WebLink)
                {
                    var previewFilePath = EnsureWebReferencePreviewFile(reference);
                    var startInfo = new ProcessStartInfo("notepad.exe", "\"" + previewFilePath + "\"")
                    {
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                    return;
                }

                var filePath = reference.LocalPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    MessageBox.Show(this, LocalizationService.Get("Web.panel.referenceFileMissing"), LocalizationService.Get("Web.panel.fileReferences"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo(filePath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Web.panel.referenceContentFailed", ex.Message), LocalizationService.Get("Web.panel.openReferenceTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string EnsureWebReferencePreviewFile(ReferenceItem reference)
        {
            var path = reference.Metadata != null && reference.Metadata.ContainsKey(WebReferencePreviewPathMetadataKey)
                ? reference.Metadata[WebReferencePreviewPathMetadataKey]
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }

            var folderPath = Path.Combine(Path.GetTempPath(), "OfficeCopilot", "WebReferencePreview");
            Directory.CreateDirectory(folderPath);

            var fileName = SanitizeFileName(GetReferenceDisplayText(reference));
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "web-reference";
            }

            path = Path.Combine(folderPath, fileName + "-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, BuildWebReferencePreviewContent(reference), new UTF8Encoding(true));

            if (reference.Metadata != null)
            {
                reference.Metadata[WebReferencePreviewPathMetadataKey] = path;
            }

            return path;
        }

        private string BuildWebReferencePreviewContent(ReferenceItem reference)
        {
            var builder = new StringBuilder();
            builder.AppendLine(LocalizationService.Format("Web.attachment.previewTitle", GetReferenceDisplayText(reference)));

            if (!string.IsNullOrWhiteSpace(reference.Source))
            {
                builder.AppendLine(LocalizationService.Format("Web.attachment.previewUrl", reference.Source));
            }

            if (reference.Metadata != null && reference.Metadata.ContainsKey("detectedEncoding"))
            {
                builder.AppendLine(LocalizationService.Format("Web.attachment.previewEncoding", reference.Metadata["detectedEncoding"]));
            }

            if (!string.IsNullOrWhiteSpace(reference.Summary))
            {
                builder.AppendLine();
                builder.AppendLine(LocalizationService.Get("Web.attachment.previewSummary"));
                builder.AppendLine(reference.Summary);
            }

            builder.AppendLine();
            builder.AppendLine(LocalizationService.Get("Web.attachment.previewContent"));
            builder.AppendLine(string.IsNullOrWhiteSpace(reference.InlineText) ? LocalizationService.Get("Web.attachment.emptyContent") : reference.InlineText);
            return builder.ToString();
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var value = (fileName ?? string.Empty).Trim();
            foreach (var invalidChar in invalidChars)
            {
                value = value.Replace(invalidChar, '_');
            }

            return value;
        }

        private int TryAddReferences(
            IEnumerable<ReferenceItem> references,
            bool stopWhenLimitReached,
            bool showLimitMessage,
            out bool limitReached)
        {
            limitReached = false;
            if (references == null)
            {
                return 0;
            }

            var addedCount = 0;
            var selectionLimit = ResolveReferenceSelectionLimit();

            foreach (var reference in references.Where(r => r != null))
            {
                if (_selectedReferences.Count >= selectionLimit)
                {
                    limitReached = true;
                    if (stopWhenLimitReached)
                    {
                        break;
                    }

                    continue;
                }

                if (!ContainsSelectedReference(reference))
                {
                    _selectedReferences.Add(CloneReferenceItem(reference));
                    addedCount++;
                }
            }

            if (limitReached && showLimitMessage)
            {
                MessageBox.Show(
                    this,
                    LocalizationService.Format("Web.panel.referenceLimitCount", selectionLimit),
                    LocalizationService.Get("Web.panel.referenceLimitTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return addedCount;
        }

        private bool ContainsSelectedReference(ReferenceItem reference)
        {
            var identity = BuildReferenceIdentity(reference);
            return _selectedReferences.Any(existing => string.Equals(BuildReferenceIdentity(existing), identity, StringComparison.OrdinalIgnoreCase));
        }

        private int ResolveReferenceSelectionLimit()
        {
            return _settings != null
                && _settings.ChatDefaults != null
                && _settings.ChatDefaults.AttachmentFileCountThreshold > 0
                ? _settings.ChatDefaults.AttachmentFileCountThreshold
                : 5;
        }

        private void RegisterRecentReferenceGroup(IReadOnlyList<ReferenceItem> references)
        {
            if (references == null || references.Count == 0)
            {
                RefreshRecentReferenceMenu();
                return;
            }

            var normalizedReferences = references
                .Where(r => r != null)
                .Select(CloneReferenceItem)
                .ToList();
            if (normalizedReferences.Count == 0)
            {
                RefreshRecentReferenceMenu();
                return;
            }

            var key = BuildReferenceGroupKey(normalizedReferences);
            var existing = _recentReferenceGroups.FirstOrDefault(group => string.Equals(group.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _recentReferenceGroups.Remove(existing);
            }

            _recentReferenceGroups.Insert(0, new RecentReferenceGroup
            {
                Key = key,
                Caption = BuildRecentReferenceGroupCaption(normalizedReferences),
                References = normalizedReferences
            });

            while (_recentReferenceGroups.Count > MaxRecentReferenceGroupCount)
            {
                _recentReferenceGroups.RemoveAt(_recentReferenceGroups.Count - 1);
            }

            RefreshRecentReferenceMenu();
        }

        private void RefreshRecentReferenceMenu()
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(RefreshRecentReferenceMenu));
                return;
            }

            if (menuMruRefs == null)
            {
                return;
            }

            menuMruRefs.ClearLinks();
            foreach (var item in _recentReferenceMenuButtons)
            {
                if (item != null)
                {
                    barManager.Items.Remove(item);
                    item.Dispose();
                }
            }

            _recentReferenceMenuButtons.Clear();

            if (_recentReferenceGroups.Count == 0)
            {
                var emptyItem = new BarButtonItem();
                emptyItem.Caption = LocalizationService.Get("Web.panel.emptyRecent");
                emptyItem.Enabled = false;
                barManager.Items.Add(emptyItem);
                menuMruRefs.AddItem(emptyItem);
                _recentReferenceMenuButtons.Add(emptyItem);
                menuMruRefs.Enabled = false;
                return;
            }

            menuMruRefs.Enabled = true;

            for (var index = 0; index < _recentReferenceGroups.Count; index++)
            {
                var group = _recentReferenceGroups[index];
                var item = new BarButtonItem();
                item.Caption = (index + 1).ToString() + ". " + group.Caption;
                item.Tag = group.Key;
                item.ItemClick += RecentReferenceGroupItem_ItemClick;
                barManager.Items.Add(item);
                _recentReferenceMenuButtons.Add(item);
                menuMruRefs.AddItem(item);
            }
        }

        private void RecentReferenceGroupItem_ItemClick(object sender, ItemClickEventArgs e)
        {
            var key = e == null || e.Item == null || e.Item.Tag == null
                ? string.Empty
                : Convert.ToString(e.Item.Tag);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            var group = _recentReferenceGroups.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
            if (group == null)
            {
                return;
            }

            bool limitReached;
            var addedCount = TryAddReferences(group.References, true, true, out limitReached);
            RefreshSelectedReferenceDisplay();

            if (addedCount == 0 && !limitReached)
            {
                MessageBox.Show(this, LocalizationService.Get("Web.panel.duplicateRecent"), LocalizationService.Get("Web.panel.recentTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string BuildRecentReferenceGroupCaption(IList<ReferenceItem> references)
        {
            var names = references
                .Where(r => r != null)
                .Select(reference =>
                {
                    var name = GetReferenceDisplayText(reference);
                    var summary = BuildAttachmentSummaryText(reference);
                    return string.IsNullOrWhiteSpace(summary) ? name : LocalizationService.Format("Web.attachment.summarySuffix", name, summary);
                })
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Take(3)
                .ToList();

            var caption = names.Count == 0
                ? LocalizationService.Get("Web.panel.unnamedReference")
                : string.Join(LocalizationService.Get("Web.attachment.nameSeparator"), names);

            if (references.Count > names.Count)
            {
                caption += LocalizationService.Format("Web.panel.otherReferencesCount", references.Count - names.Count);
            }

            return caption;
        }

        private static string BuildReferenceGroupKey(IEnumerable<ReferenceItem> references)
        {
            return string.Join("||", references
                .Where(r => r != null)
                .Select(BuildReferenceIdentity)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        }

        private static string BuildReferenceIdentity(ReferenceItem reference)
        {
            if (reference == null)
            {
                return string.Empty;
            }

            var source = (reference.Source ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(source))
            {
                return "src:" + source.ToLowerInvariant();
            }

            var localPath = (reference.LocalPath ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(localPath))
            {
                return "path:" + localPath.ToLowerInvariant();
            }

            return "kind:" + reference.Kind.ToString() + "|name:" + (reference.DisplayName ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static ReferenceItem CloneReferenceItem(ReferenceItem reference)
        {
            var clone = new ReferenceItem
            {
                ReferenceId = string.IsNullOrWhiteSpace(reference == null ? null : reference.ReferenceId)
                    ? Guid.NewGuid().ToString("N")
                    : reference.ReferenceId,
                Kind = reference == null ? ReferenceItemKind.Unknown : reference.Kind,
                DisplayName = reference == null ? string.Empty : reference.DisplayName ?? string.Empty,
                MimeType = reference == null ? string.Empty : reference.MimeType ?? string.Empty,
                LocalPath = reference == null ? string.Empty : reference.LocalPath ?? string.Empty,
                FileExtension = reference == null ? string.Empty : reference.FileExtension ?? string.Empty,
                SizeBytes = reference == null ? (long?)null : reference.SizeBytes,
                InlineText = reference == null ? string.Empty : reference.InlineText ?? string.Empty,
                Summary = reference == null ? string.Empty : reference.Summary ?? string.Empty,
                Source = reference == null ? string.Empty : reference.Source ?? string.Empty
            };

            if (reference != null && reference.Metadata != null)
            {
                foreach (var pair in reference.Metadata)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        clone.Metadata[pair.Key] = pair.Value ?? string.Empty;
                    }
                }
            }

            return clone;
        }

        private ReferenceItem CreateFileReferenceItem(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var extension = ModelProviderSettingsExtensions.NormalizeFileExtension(Path.GetExtension(filePath));
                if (!AttachmentFileTypes.IsSupported(extension))
                {
                    return null;
                }

                var fileInfo = new FileInfo(filePath);
                var mimeType = GuessMimeType(extension);
                var isImage = !string.IsNullOrWhiteSpace(mimeType)
                    && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

                return new ReferenceItem
                {
                    Kind = isImage ? ReferenceItemKind.ImageFile : ReferenceItemKind.LocalFile,
                    DisplayName = Path.GetFileName(filePath),
                    MimeType = mimeType,
                    LocalPath = filePath,
                    FileExtension = extension,
                    SizeBytes = fileInfo.Length,
                    Summary = Path.GetFileName(filePath),
                    Source = filePath
                };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "读取参考文件信息失败：{FilePath}", filePath ?? string.Empty);
                return null;
            }
        }

        private int ResolveReferenceImportTimeoutSeconds()
        {
            return _settings != null && _settings.ChatDefaults != null
                ? Math.Max(5, _settings.ChatDefaults.RequestTimeoutSeconds)
                : 30;
        }

        private static string GuessMimeType(string extension)
        {
            switch ((extension ?? string.Empty).Trim().ToLowerInvariant())
            {
                case ".png":
                    return "image/png";
                case ".jpg":
                case ".jpeg":
                    return "image/jpeg";
                case ".bmp":
                    return "image/bmp";
                case ".gif":
                    return "image/gif";
                case ".webp":
                    return "image/webp";
                case ".pdf":
                    return "application/pdf";
                case ".doc":
                    return "application/msword";
                case ".docx":
                    return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                case ".ppt":
                    return "application/vnd.ms-powerpoint";
                case ".pptx":
                    return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                case ".xls":
                    return "application/vnd.ms-excel";
                case ".xlsx":
                case ".xlsm":
                    return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                case ".csv":
                    return "text/csv";
                case ".txt":
                case ".md":
                case ".markdown":
                case ".json":
                case ".xml":
                case ".yaml":
                case ".yml":
                    return "text/plain";
                default:
                    return "application/octet-stream";
            }
        }
    }
}