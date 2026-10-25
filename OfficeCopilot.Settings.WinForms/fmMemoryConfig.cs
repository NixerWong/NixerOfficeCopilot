using DevExpress.XtraBars;
using DevExpress.XtraRichEdit;
using DevExpress.XtraTab;
using DevExpress.XtraTreeList;
using DevExpress.XtraTreeList.Nodes;
using Markdig;
using OfficeCopilot.Localization;
using OfficeCopilot.Memory;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmMemoryConfig : HostAwareForm
    {
        private MemoryManager _memoryManager;
        private Timer _memoryPreviewTimer;
        private MemoryNodeType _currentMemNodeType;
        private string _currentMemNodeId;
        private bool _loadingMemoryTree;
        private ToolStripMenuItem _deleteFragmentMenuItem;

        public fmMemoryConfig()
        {
            InitializeComponent();
            InitializeMemoryTab();
            WireEvents();
            ApplyLocalization();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += fmMemoryConfig_Disposed;
        }

        private void WireEvents()
        {
            this.Load += fmMemoryConfig_Load;

            tlMemFileTree.FocusedNodeChanged += tlMemFileTree_FocusedNodeChanged;
            btnSaveMem.ItemClick += btnSaveMem_ItemClick;
            txtMarkDown.TextChanged += txtMarkDown_TextChanged;
            xtcMemMarkDown.SelectedPageChanged += xtcMemMarkDown_SelectedPageChanged;
        }

        private void InitializeMemoryTab()
        {
            _memoryManager = new MemoryManager(new FileSystemMemoryStore());
            _memoryPreviewTimer = new Timer { Interval = 500 };
            _memoryPreviewTimer.Tick += (s, e) => { _memoryPreviewTimer.Stop(); UpdateMemoryPreview(); };

            tlMemFileTree.OptionsBehavior.Editable = false;
            tlMemFileTree.OptionsSelection.EnableAppearanceFocusedCell = false;
            tlMemFileTree.OptionsView.ShowColumns = false;
            tlMemFileTree.OptionsView.ShowIndicator = false;
            tlMemFileTree.OptionsView.ShowVertLines = false;
            tlMemFileTree.OptionsView.ShowHorzLines = false;
            tlMemFileTree.OptionsView.ShowCheckBoxes = false;
            tlMemFileTree.KeyFieldName = "Id";
            tlMemFileTree.ParentFieldName = "ParentId";

            var imgCollection = new DevExpress.Utils.ImageCollection();
            imgCollection.AddImage(Properties.Resources.bookmark_16x16);
            imgCollection.AddImage(Properties.Resources.richeditbookmark_16x16);
            tlMemFileTree.StateImageList = imgCollection;

            var colName = tlMemFileTree.Columns.Add();
            colName.Caption = "";
            colName.FieldName = "Name";
            colName.VisibleIndex = 0;

            //txtMarkDown.Options.Behavior.Open = DocumentCapability.Hidden;
            //txtMarkDown.Options.Behavior.Save = DocumentCapability.Hidden;
            //txtMarkDown.Options.Behavior.SaveAs = DocumentCapability.Hidden;
            //txtMarkDown.Options.Behavior.CreateNew = DocumentCapability.Hidden;
            webPreview.Navigate("about:blank");

            var ctxMenu = new ContextMenuStrip();
            _deleteFragmentMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Memory.DeleteFragment"), Properties.Resources.deletelist_16x16, OnDeleteMemFragment);
            ctxMenu.Items.Add(_deleteFragmentMenuItem);
            ctxMenu.Opening += (s, args) =>
            {
                var node = tlMemFileTree.FocusedNode;
                args.Cancel = !(node?.Tag is MemTreeNodeTag tag
                    && (tag.NodeType == MemoryNodeType.Fragment || tag.NodeType == MemoryNodeType.IndexEntry));
            };
            tlMemFileTree.ContextMenuStrip = ctxMenu;
            tlMemFileTree.MouseDown += (s, args) =>
            {
                if (args.Button == MouseButtons.Right)
                {
                    var hitInfo = tlMemFileTree.CalcHitInfo(args.Location);
                    if (hitInfo.Node != null) tlMemFileTree.FocusedNode = hitInfo.Node;
                }
            };
            bsiMemInfo.Caption = LocalizationService.Get("Settings.Config.Memory.Manager");
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
            if (IsHandleCreated && !IsDisposed) _ = UpdateMemoryInfoBarAsync();
        }

        private void fmMemoryConfig_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            if (_memoryPreviewTimer != null)
            {
                _memoryPreviewTimer.Stop();
                _memoryPreviewTimer.Dispose();
                _memoryPreviewTimer = null;
            }
        }

        private void ApplyLocalization()
        {
            Text = LocalizationService.Get("Settings.Config.Memory.Manager");
            btnSaveMem.Caption = LocalizationService.Get("Settings.Config.Common.Save");
            xtpMemWebPreview.Text = LocalizationService.Get("Settings.Config.Memory.Preview");
            xtpMemMarkDown.Text = LocalizationService.Get("Settings.Config.Memory.Markdown");
            if (_deleteFragmentMenuItem != null)
                _deleteFragmentMenuItem.Text = LocalizationService.Get("Settings.Config.Memory.DeleteFragment");
            if (string.IsNullOrWhiteSpace(txtMarkDown.Text) && webPreview != null)
                UpdateMemoryPreview();
        }

        // ========== Load ==========

        private async void fmMemoryConfig_Load(object sender, EventArgs e)
        {
            bsiMemInfo.Caption = LocalizationService.Get("Settings.Config.Memory.Loading");
            await LoadMemoryTreeAsync();
        }

        private async Task LoadMemoryTreeAsync()
        {
            _loadingMemoryTree = true;
            try
            {
                await Task.Run(() => _memoryManager.EnsureEntrypointExistsAsync());
                var expandedIds = new HashSet<string>();
                foreach (TreeListNode node in tlMemFileTree.Nodes) CollectExpandedNodes(node, expandedIds);
                var entryNode = await Task.Run(() => _memoryManager.BuildTreeAsync());
                tlMemFileTree.BeginUnboundLoad();
                tlMemFileTree.Nodes.Clear();
                var rootNode = tlMemFileTree.AppendNode(new object[] { entryNode.Name }, null);
                rootNode.Tag = new MemTreeNodeTag { NodeType = MemoryNodeType.Entrypoint, NodeId = entryNode.Id };
                rootNode.StateImageIndex = 0;
                AppendTreeChildren(rootNode, entryNode.Children);
                tlMemFileTree.EndUnboundLoad();
                rootNode.Expanded = true;
                RestoreExpandedNodes(rootNode, expandedIds);
                tlMemFileTree.FocusedNode = rootNode;
                try
                {
                    var rootContent = await Task.Run(() => _memoryManager.LoadContentAsync(MemoryNodeType.Entrypoint, entryNode.Id));
                    txtMarkDown.Text = rootContent ?? string.Empty;
                    _currentMemNodeType = MemoryNodeType.Entrypoint;
                    _currentMemNodeId = entryNode.Id;
                    UpdateMemoryPreview();
                }
                catch { }
                await UpdateMemoryInfoBarAsync();
            }
            catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadMemoryTree", ex.Message), LocalizationService.Get("Settings.Config.Memory.Manager"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { _loadingMemoryTree = false; }
        }

        private static void CollectExpandedNodes(TreeListNode node, HashSet<string> expandedIds)
        {
            if (node.Expanded && node.Tag is MemTreeNodeTag tag) expandedIds.Add(tag.NodeId);
            foreach (TreeListNode child in node.Nodes) CollectExpandedNodes(child, expandedIds);
        }

        private static void RestoreExpandedNodes(TreeListNode node, HashSet<string> expandedIds)
        {
            if (node.Tag is MemTreeNodeTag tag && expandedIds.Contains(tag.NodeId)) node.Expanded = true;
            foreach (TreeListNode child in node.Nodes) RestoreExpandedNodes(child, expandedIds);
        }

        private void AppendTreeChildren(TreeListNode parentNode, List<MemoryTreeNode> children)
        {
            foreach (var child in children)
            {
                var displayName = child.NodeType == MemoryNodeType.IndexEntry ? child.Name
                    : child.NodeType == MemoryNodeType.Fragment ? child.Name : child.Name;
                var node = tlMemFileTree.AppendNode(new object[] { displayName }, parentNode);
                node.Tag = new MemTreeNodeTag { NodeType = child.NodeType, NodeId = child.Id };
                node.StateImageIndex = child.NodeType == MemoryNodeType.Entrypoint ? 0 : 1;
                AppendTreeChildren(node, child.Children);
            }
        }

        // ========== Tree Navigation ==========

        private async void tlMemFileTree_FocusedNodeChanged(object sender, FocusedNodeChangedEventArgs e)
        {
            if (e.Node?.Tag is MemTreeNodeTag tag) await LoadMemoryNodeContentAsync(tag.NodeType, tag.NodeId);
        }

        private async Task LoadMemoryNodeContentAsync(MemoryNodeType nodeType, string nodeId)
        {
            try
            {
                var content = await Task.Run(() => _memoryManager.LoadContentAsync(nodeType, nodeId));
                _currentMemNodeType = nodeType;
                _currentMemNodeId = nodeId;
                txtMarkDown.Text = content ?? string.Empty;
                UpdateMemoryPreview();
                await UpdateMemoryInfoBarAsync();
            }
            catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadMemoryContent", ex.Message), LocalizationService.Get("Settings.Config.Memory.Manager"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ========== Edit & Save ==========

        private void txtMarkDown_TextChanged(object sender, EventArgs e)
        {
            if (_loadingMemoryTree) return;
            _memoryPreviewTimer.Stop();
            _memoryPreviewTimer.Start();
        }

        private void xtcMemMarkDown_SelectedPageChanged(object sender, TabPageChangedEventArgs e)
        {
            if (e.Page == xtpMemWebPreview) UpdateMemoryPreview();
        }

        private void UpdateMemoryPreview()
        {
            try
            {
                var markdown = txtMarkDown.Text ?? string.Empty;
                if (string.IsNullOrWhiteSpace(markdown))
                {
                    webPreview.DocumentText = "<html><body style='font-family:Segoe UI,sans-serif;padding:16px;color:#999;'><p>" + System.Net.WebUtility.HtmlEncode(LocalizationService.Get("Settings.Config.Memory.EmptyPreview")) + "</p></body></html>";
                    return;
                }
                var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
                var html = Markdown.ToHtml(markdown, pipeline);
                webPreview.DocumentText = @"<!DOCTYPE html><html><head><meta charset=""utf-8""><style>
body{font-family:'Segoe UI','Microsoft YaHei',sans-serif;padding:16px;line-height:1.6;color:#333;}
h1,h2,h3{color:#1a5276;}h1{border-bottom:2px solid #1a5276;padding-bottom:8px;}
h2{border-bottom:1px solid #ddd;padding-bottom:4px;}
code{background:#f4f4f4;padding:2px 6px;border-radius:3px;}
pre{background:#2d2d2d;color:#f8f8f2;padding:16px;border-radius:6px;overflow-x:auto;}
pre code{background:none;padding:0;color:inherit;}
blockquote{border-left:4px solid #1a5276;padding-left:16px;margin-left:0;color:#666;}
table{border-collapse:collapse;width:100%;}th,td{border:1px solid #ddd;padding:8px;}th{background:#f0f0f0;}
</style></head><body>" + html + "</body></html>";
            }
            catch { }
        }

        private async void btnSaveMem_ItemClick(object sender, ItemClickEventArgs e)
        {
            await SaveCurrentMemoryNodeAsync();
            await LoadMemoryTreeAsync();
        }

        private async Task SaveCurrentMemoryNodeAsync()
        {
            if (_currentMemNodeType != MemoryNodeType.Entrypoint && _currentMemNodeType != MemoryNodeType.Fragment
                && _currentMemNodeType != MemoryNodeType.IndexEntry) return;
            try
            {
                var content = txtMarkDown.Text ?? string.Empty;
                await Task.Run(() => _memoryManager.SaveContentAsync(_currentMemNodeType, _currentMemNodeId, content));
                await UpdateMemoryInfoBarAsync();
            }
            catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveMemory", ex.Message), LocalizationService.Get("Settings.Config.Memory.Manager"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private async Task UpdateMemoryInfoBarAsync()
        {
            try
            {
                var stats = await Task.Run(() => _memoryManager.GetStatsAsync());
                var nodeLabel = _currentMemNodeType switch
                {
                    MemoryNodeType.Entrypoint => "MEMORY.md",
                    MemoryNodeType.Fragment => _currentMemNodeId + ".md",
                    MemoryNodeType.IndexEntry => _currentMemNodeId.StartsWith("index:")
                        ? _currentMemNodeId.Substring(6) + ".md" : _currentMemNodeId + ".md",
                    _ => ""
                };
                var truncated = stats.EntrypointTruncated ? LocalizationService.Get("Settings.Config.Memory.EntrypointTruncated") : string.Empty;
                if (!string.IsNullOrEmpty(nodeLabel))
                    bsiMemInfo.Caption = LocalizationService.Format("Settings.Config.Memory.InfoWithNode", nodeLabel, string.Empty, stats.EntrypointLineCount, FormatBytes(stats.EntrypointSizeBytes), stats.FragmentCount, FormatBytes(stats.TotalFragmentsSizeBytes), truncated);
                else
                    bsiMemInfo.Caption = LocalizationService.Format("Settings.Config.Memory.Info", stats.EntrypointLineCount, FormatBytes(stats.EntrypointSizeBytes), stats.FragmentCount, FormatBytes(stats.TotalFragmentsSizeBytes), truncated);
            }
            catch { bsiMemInfo.Caption = LocalizationService.Get("Settings.Config.Memory.Manager"); }
        }

        private async void OnDeleteMemFragment(object sender, EventArgs e)
        {
            var node = tlMemFileTree.FocusedNode;
            if (!(node?.Tag is MemTreeNodeTag tag)
                || (tag.NodeType != MemoryNodeType.Fragment && tag.NodeType != MemoryNodeType.IndexEntry)) return;
            var fragmentId = tag.NodeType == MemoryNodeType.IndexEntry
                ? (tag.NodeId.StartsWith("index:") ? tag.NodeId.Substring(6) : tag.NodeId) : tag.NodeId;
            if (string.IsNullOrWhiteSpace(fragmentId)) { MessageBox.Show(this, LocalizationService.Get("Settings.Config.Memory.FragmentIdMissing"), LocalizationService.Get("Settings.Config.Memory.DeleteFragment"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (MessageBox.Show(this, LocalizationService.Format("Settings.Config.Memory.ConfirmDeleteFragment", fragmentId + ".md"), LocalizationService.Get("Settings.Config.Memory.DeleteFragment"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                await Task.Run(() => _memoryManager.DeleteFragmentAndIndexAsync(fragmentId));
                if (_currentMemNodeId == tag.NodeId || (_currentMemNodeId.StartsWith("index:") && _currentMemNodeId.Substring(6) == fragmentId))
                { txtMarkDown.Text = string.Empty; }
                await LoadMemoryTreeAsync();
                if (tlMemFileTree.Nodes.Count > 0) tlMemFileTree.FocusedNode = tlMemFileTree.Nodes[0];
                await UpdateMemoryInfoBarAsync();
            }
            catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.DeleteMemory", ex.Message), LocalizationService.Get("Settings.Config.Memory.Manager"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + "B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + "KB";
            return (bytes / (1024.0 * 1024.0)).ToString("F1") + "MB";
        }

        private sealed class MemTreeNodeTag
        {
            public MemoryNodeType NodeType { get; set; }
            public string NodeId { get; set; } = string.Empty;
        }
    }
}
