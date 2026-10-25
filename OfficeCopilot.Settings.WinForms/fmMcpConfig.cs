using DevExpress.XtraBars;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Localization;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmMcpConfig : HostAwareForm
    {
        private readonly ISettingsStore _settingsStore;
        private OfficeCopilotSettings _settings;
        private BindingList<McpServerRow> _mcpServers;
        private RepositoryItemCheckEdit _mcpServerRegistrationEditor;
        private bool _loadingSettings;
        private ToolStripMenuItem _editMenuItem;
        private ToolStripMenuItem _deleteMenuItem;

        public fmMcpConfig(ISettingsStore settingsStore)
        {
            InitializeComponent();
            _settingsStore = settingsStore ?? new JsonSettingsStore();
            WireEvents();
            ApplyLocalization();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += fmMcpConfig_Disposed;
        }

        private void WireEvents()
        {
            this.Load += fmMcpConfig_Load;
            this.FormClosing += fmMcpConfig_FormClosing;

            btnNewMcp.ItemClick += btnNewMcp_ItemClick;
            btnEditMcp.ItemClick += btnEditMcp_ItemClick;
            btnDelMcp.ItemClick += btnDelMcp_ItemClick;
            btnExportMcp.ItemClick += btnExportMcp_ItemClick;
            btnImportMcp.ItemClick += btnImportMcp_ItemClick;

            gvMcpServer.FocusedRowChanged += gvMcpServer_FocusedRowChanged;
            gvMcpServer.CellValueChanged += gvMcpServer_CellValueChanged;
            gvMcpServer.DoubleClick += gvMcpServer_DoubleClick;

            // Init columns
            colMcpName.FieldName = "DisplayName";
            colEnableMcp.FieldName = "IsEnabled";
            gvMcpServer.OptionsBehavior.Editable = true;
            gvMcpServer.OptionsSelection.EnableAppearanceFocusedCell = false;
            colMcpName.OptionsColumn.AllowEdit = false;
            colMcpDesc.OptionsColumn.AllowEdit = false;
            colEnableMcp.OptionsColumn.AllowEdit = true;

            _mcpServerRegistrationEditor = new RepositoryItemCheckEdit
            {
                ValueChecked = true,
                ValueUnchecked = false,
                NullStyle = StyleIndeterminate.Unchecked
            };
            gcMcpServer.RepositoryItems.Add(_mcpServerRegistrationEditor);
            colEnableMcp.ColumnEdit = _mcpServerRegistrationEditor;

            // 右键菜单
            var ctxMcp = new ContextMenuStrip();
            _editMenuItem = new ToolStripMenuItem(
                LocalizationService.Get("Settings.Config.Context.Edit"),
                Properties.Resources.edit_16x16,
                (s, ev) => EditFocusedMcpServer());
            _deleteMenuItem = new ToolStripMenuItem(
                LocalizationService.Get("Settings.Config.Context.Delete"),
                Properties.Resources.deletelist_16x16,
                (s, ev) => btnDelMcp_ItemClick(s, null));
            ctxMcp.Items.AddRange(new ToolStripItem[] { _editMenuItem, _deleteMenuItem });
            ctxMcp.Opening += (s, ev) => { ev.Cancel = GetFocusedMcpServer() == null; };
            GridContextMenuHelper.AttachToDataRows(gcMcpServer, gvMcpServer, ctxMcp);
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void fmMcpConfig_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void ApplyLocalization()
        {
            Text = LocalizationService.Get("Settings.Config.DialogTitle.Mcp");
            colMcpId.Caption = LocalizationService.Get("Settings.Config.Mcp.Id");
            colMcpName.Caption = LocalizationService.Get("Settings.Config.Mcp.Name");
            colMcpDesc.Caption = LocalizationService.Get("Settings.Config.Mcp.Description");
            colEnableMcp.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");
            btnNewMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Add");
            btnEditMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Edit");
            btnDelMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Delete");
            btnExportMcp.Caption = LocalizationService.Get("Settings.Config.Common.ExportList");
            btnImportMcp.Caption = LocalizationService.Get("Settings.Config.Mcp.Import");

            if (_editMenuItem != null)
            {
                _editMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Edit");
                _deleteMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Delete");
            }
        }

        // ========== Load / Save ==========

        private void fmMcpConfig_Load(object sender, EventArgs e)
        {
            ApplyLocalization();
            LoadSettings();
        }

        private void fmMcpConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadingSettings || e == null) return;
            try { SaveMcpSettings(); }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveOnCloseFailed", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Mcp"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Error) != DialogResult.Yes)
                    e.Cancel = true;
            }
        }

        private void LoadSettings()
        {
            _loadingSettings = true;
            try
            {
                _settings = OfficeCopilotSettingsFactory.Normalize(
                    _settingsStore.Load() ?? OfficeCopilotSettingsFactory.CreateDefault());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadSettings", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }
            LoadMcpSettings();
            _loadingSettings = false;
        }

        private void LoadMcpSettings()
        {
            var servers = _settings?.McpServers == null
                ? new List<McpServerSettings>()
                : _settings.McpServers.Select(OfficeCopilotSettingsFactory.NormalizeMcpServer).ToList();
            _mcpServers = new BindingList<McpServerRow>(BuildMcpServerRows(servers));
            gcMcpServer.DataSource = _mcpServers;
            if (_mcpServers.Count > 0) gvMcpServer.FocusedRowHandle = 0;
        }

        private static List<McpServerRow> BuildMcpServerRows(IEnumerable<McpServerSettings> servers)
        {
            return servers?.Where(p => p != null).Select(p => new McpServerRow
            {
                ServerId = p.Id,
                DisplayName = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name,
                Description = p.Description ?? string.Empty,
                IsEnabled = p.IsEnabled,
                ServerType = p.McpType
            }).OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList()
                ?? new List<McpServerRow>();
        }

        // ========== MCP CRUD ==========

        private McpServerRow GetFocusedMcpServer()
        {
            if (gvMcpServer?.FocusedRowHandle < 0) return null;
            return gvMcpServer.GetRow(gvMcpServer.FocusedRowHandle) as McpServerRow;
        }

        private McpServerSettings FindMcpServer(string serverId)
        {
            if (string.IsNullOrWhiteSpace(serverId) || _settings?.McpServers == null) return null;
            var server = _settings.McpServers.FirstOrDefault(p =>
                p != null && string.Equals(p.Id, serverId, StringComparison.OrdinalIgnoreCase));
            return server == null ? null : CloneMcpServer(server);
        }

        private void EnsureSettings()
        {
            if (_settings == null) _settings = OfficeCopilotSettingsFactory.CreateDefault();
            if (_settings.McpServers == null) _settings.McpServers = new List<McpServerSettings>();
        }

        private void UpsertMcpServer(McpServerSettings server)
        {
            if (server == null) return;
            EnsureSettings();
            var idx = _settings.McpServers.FindIndex(p =>
                p != null && string.Equals(p.Id, server.Id, StringComparison.OrdinalIgnoreCase));
            if (idx < 0)
                _settings.McpServers.Add(OfficeCopilotSettingsFactory.NormalizeMcpServer(server));
            else
                _settings.McpServers[idx] = OfficeCopilotSettingsFactory.NormalizeMcpServer(server);
        }

        private void btnNewMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var form = new fmMcpEditor())
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.McpServer == null) return;
                EnsureSettings();
                UpsertMcpServer(form.McpServer);
                _settings.UpdatedAtUtc = DateTime.UtcNow;
                _settingsStore.Save(_settings);
                LoadMcpSettings();
            }
        }

        private void btnEditMcp_ItemClick(object sender, ItemClickEventArgs e) => EditFocusedMcpServer();

        private void gvMcpServer_DoubleClick(object sender, EventArgs e)
        {
            if (!IsDoubleClickOnRowCell(sender)) return;
            EditFocusedMcpServer();
        }

        private void EditFocusedMcpServer()
        {
            var server = GetFocusedMcpServer();
            if (server == null) return;
            using (var form = new fmMcpEditor(FindMcpServer(server.ServerId)))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.McpServer == null) return;
                EnsureSettings();
                UpsertMcpServer(form.McpServer);
                _settings.UpdatedAtUtc = DateTime.UtcNow;
                _settingsStore.Save(_settings);
                LoadMcpSettings();
            }
        }

        private void btnDelMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            var server = GetFocusedMcpServer();
            if (server == null) return;
            if (MessageBox.Show(this, LocalizationService.Format("Settings.Config.Mcp.ConfirmDelete", server.DisplayName),
                LocalizationService.Get("Settings.Config.DialogTitle.Mcp"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            EnsureSettings();
            _settings.McpServers.RemoveAll(p =>
                p != null && string.Equals(p.Id, server.ServerId, StringComparison.OrdinalIgnoreCase));
            _settings.UpdatedAtUtc = DateTime.UtcNow;
            _settingsStore.Save(_settings);
            LoadMcpSettings();
        }

        // ========== Import / Export ==========

        private void btnExportMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new SaveFileDialog
            {
                Title = LocalizationService.Get("Settings.Config.Mcp.ExportDialogTitle"),
                Filter = LocalizationService.Get("Settings.Config.FileFilter.Json"),
                FileName = "mcp-servers.json"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    EnsureSettings();
                    ApplyMcpSettings();
                    File.WriteAllText(dialog.FileName,
                        JsonConvert.SerializeObject(_settings.McpServers, Formatting.Indented), System.Text.Encoding.UTF8);
                    MessageBox.Show(this, LocalizationService.Get("Settings.Config.Common.ExportComplete"),
                        LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ExportFailed", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void btnImportMcp_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new OpenFileDialog
            {
                Title = LocalizationService.Get("Settings.Config.Mcp.ImportDialogTitle"),
                Filter = LocalizationService.Get("Settings.Config.FileFilter.Json")
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this, LocalizationService.Get("Settings.Config.Mcp.ConfirmImport"),
                    LocalizationService.Get("Settings.Config.DialogTitle.Mcp"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    EnsureSettings();
                    _settings.McpServers = ReadMcpServersFromFile(dialog.FileName);
                    SaveMcpSettings();
                    LoadMcpSettings();
                    MessageBox.Show(this, LocalizationService.Get("Settings.Config.Common.ImportComplete"),
                        LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ImportFailed", ex.Message),
                        LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ========== Grid Events ==========

        private void gvMcpServer_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e) { }

        private void gvMcpServer_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_loadingSettings || e?.Column != colEnableMcp) return;
            SaveMcpSettings();
        }

        // ========== Save ==========

        private void SaveMcpSettings()
        {
            if (_loadingSettings) return;
            try
            {
                ApplyMcpSettings();
                _settingsStore.Save(_settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveMcp", ex.Message),
                    LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadSettings();
            }
        }

        private void ApplyMcpSettings()
        {
            if (_settings == null) _settings = OfficeCopilotSettingsFactory.CreateDefault();
            var servers = new List<McpServerSettings>();
            if (_mcpServers != null)
            {
                foreach (var row in _mcpServers.Where(p => p != null))
                {
                    var server = FindMcpServer(row.ServerId);
                    if (server == null) continue;
                    server.Name = row.DisplayName ?? string.Empty;
                    server.Description = row.Description ?? string.Empty;
                    server.IsEnabled = row.IsEnabled;
                    servers.Add(OfficeCopilotSettingsFactory.NormalizeMcpServer(server));
                }
            }
            _settings.McpServers = servers.OrderBy(p => p.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase).ToList();
            _settings.UpdatedAtUtc = DateTime.UtcNow;
            _settings = OfficeCopilotSettingsFactory.Normalize(_settings);
        }

        // ========== Helpers ==========

        private static bool IsDoubleClickOnRowCell(object sender)
        {
            var view = sender as GridView;
            if (view == null) return false;
            return view.CalcHitInfo(view.GridControl.PointToClient(Control.MousePosition)).InRowCell;
        }

        private static McpServerSettings CloneMcpServer(McpServerSettings source)
        {
            source = OfficeCopilotSettingsFactory.NormalizeMcpServer(source);
            return new McpServerSettings
            {
                Id = source.Id, Name = source.Name, Description = source.Description, IsEnabled = source.IsEnabled,
                Stdio = source.Stdio == null ? null : new StdioMcpServerSettings
                {
                    Command = source.Stdio.Command, WorkingDirectory = source.Stdio.WorkingDirectory,
                    Arguments = source.Stdio.Arguments == null ? new List<string>() : new List<string>(source.Stdio.Arguments),
                    EnvironmentVariables = source.Stdio.EnvironmentVariables?.Select(CloneNameValueItem).ToList() ?? new List<NameValueItem>()
                },
                Http = source.Http == null ? null : new HttpMcpServerSettings
                {
                    Endpoint = source.Http.Endpoint, TransportMode = source.Http.TransportMode,
                    Headers = source.Http.Headers?.Select(CloneNameValueItem).ToList() ?? new List<NameValueItem>()
                },
                Tools = source.Tools?.Select(CloneMcpTool).ToList() ?? new List<McpToolSettings>()
            };
        }

        private static NameValueItem CloneNameValueItem(NameValueItem item)
            => item == null ? new NameValueItem() : new NameValueItem { Name = item.Name, Value = item.Value };

        private static McpToolSettings CloneMcpTool(McpToolSettings tool)
            => tool == null ? new McpToolSettings() : new McpToolSettings
            {
                Name = tool.Name, DisplayName = tool.DisplayName, Description = tool.Description,
                SourceName = tool.SourceName, InputSchemaJson = tool.InputSchemaJson, OutputSchemaJson = tool.OutputSchemaJson,
                IsReadOnly = tool.IsReadOnly, IsEnabled = tool.IsEnabled,
                Metadata = tool.Metadata?.Select(CloneNameValueItem).ToList() ?? new List<NameValueItem>()
            };

        private static List<McpServerSettings> ReadMcpServersFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return new List<McpServerSettings>();
            var token = JToken.Parse(json);
            List<McpServerSettings> servers = null;
            if (token.Type == JTokenType.Array)
                servers = token.ToObject<List<McpServerSettings>>();
            else if (token.Type == JTokenType.Object)
                servers = (token as JObject)?["McpServers"]?.ToObject<List<McpServerSettings>>();
            return (servers ?? new List<McpServerSettings>()).Select(OfficeCopilotSettingsFactory.NormalizeMcpServer).ToList();
        }

        // ========== Row Classes ==========

        private sealed class McpServerRow
        {
            public string ServerId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool IsEnabled { get; set; } = true;
            public McpServerType ServerType { get; set; } = McpServerType.Stdio;
        }
    }
}
