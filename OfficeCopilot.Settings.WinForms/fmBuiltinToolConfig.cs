using DevExpress.XtraEditors.Repository;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Localization;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmBuiltinToolConfig : HostAwareForm
    {
        private readonly ISettingsStore _settingsStore;
        private readonly IToolRegistry _toolRegistry;
        private readonly IServiceProvider _toolServices;
        private readonly string _toolHostApplicationName;
        private readonly BuiltinToolSettingsStore _builtinToolStore;
        private OfficeCopilotSettings _settings;
        private BuiltinToolSettings _builtinToolSettings;
        private BindingList<ToolGroupRow> _toolGroups;
        private List<ToolRow> _allToolRows;
        private BindingList<ToolRow> _visibleToolRows;
        private RepositoryItemCheckEdit _groupRegistrationEditor;
        private RepositoryItemCheckEdit _toolRegistrationEditor;
        private RepositoryItemCheckEdit _toolAuthorizationEditor;
        private RepositoryItemButtonEdit _toolTestButtonEditor;
        private ToolStripMenuItem _editMenuItem;
        private bool _loadingSettings;

        public fmBuiltinToolConfig(
            ISettingsStore settingsStore,
            IToolRegistry toolRegistry,
            IServiceProvider toolServices,
            string toolHostApplicationName)
        {
            InitializeComponent();
            _settingsStore = settingsStore ?? new JsonSettingsStore();
            _toolRegistry = toolRegistry;
            _toolServices = toolServices;
            _toolHostApplicationName = toolHostApplicationName ?? string.Empty;
            _builtinToolStore = new BuiltinToolSettingsStore();
            InitializeToolViews();
            WireEvents();
            ApplyLocalization();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += fmBuiltinToolConfig_Disposed;
        }

        private void WireEvents()
        {
            this.Load += fmBuiltinToolConfig_Load;
            this.FormClosing += fmBuiltinToolConfig_FormClosing;

            gvToolsGroup.FocusedRowChanged += gvToolsGroup_FocusedRowChanged;
            gvToolsGroup.CellValueChanged += gvToolsGroup_CellValueChanged;
            gvTools.CellValueChanged += gvTools_CellValueChanged;
            gvTools.DoubleClick += gvTools_DoubleClick;
        }

        private void InitializeToolViews()
        {
            colToolsGroupName.FieldName = "DisplayName";
            colToolsGroupEnable.FieldName = "IsRegistered";
            colToolName.FieldName = "DisplayName";
            colDescription.FieldName = "Description";
            colEnableTool.FieldName = "IsRegistered";
            colNeedConfirm.FieldName = "IsAutomaticAuthorization";
            colTestTool.FieldName = "TestActionText";

            gvToolsGroup.OptionsBehavior.Editable = true;
            gvToolsGroup.OptionsSelection.EnableAppearanceFocusedCell = false;
            gvTools.OptionsBehavior.Editable = true;
            gvTools.OptionsSelection.EnableAppearanceFocusedCell = false;

            colToolsGroupName.OptionsColumn.AllowEdit = false;
            colToolName.OptionsColumn.AllowEdit = false;
            colDescription.OptionsColumn.AllowEdit = false;
            colToolsGroupEnable.OptionsColumn.AllowEdit = true;
            colEnableTool.OptionsColumn.AllowEdit = true;
            colNeedConfirm.OptionsColumn.AllowEdit = true;
            colTestTool.OptionsColumn.AllowEdit = true;

            _groupRegistrationEditor = CreateCheckEdit();
            _toolRegistrationEditor = CreateCheckEdit();
            _toolAuthorizationEditor = CreateCheckEdit();
            _toolTestButtonEditor = CreateTestButtonEditor();

            gcToolsGroup.RepositoryItems.Add(_groupRegistrationEditor);
            gcTools.RepositoryItems.Add(_toolRegistrationEditor);
            gcTools.RepositoryItems.Add(_toolAuthorizationEditor);
            gcTools.RepositoryItems.Add(_toolTestButtonEditor);

            colToolsGroupEnable.ColumnEdit = _groupRegistrationEditor;
            colEnableTool.ColumnEdit = _toolRegistrationEditor;
            colNeedConfirm.ColumnEdit = _toolAuthorizationEditor;
            colTestTool.ColumnEdit = _toolTestButtonEditor;

            // 右键菜单
            var ctxTools = new ContextMenuStrip();
            _editMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Edit"), Properties.Resources.edit_16x16, (s, ev) => OpenToolEditor(false));
            ctxTools.Items.Add(_editMenuItem);
            ctxTools.Opening += (s, ev) => { ev.Cancel = GetFocusedTool() == null; };
            GridContextMenuHelper.AttachToDataRows(gcTools, gvTools, ctxTools);
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void fmBuiltinToolConfig_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void ApplyLocalization()
        {
            Text = LocalizationService.Get("Settings.Config.Tab.Tools");
            colToolsGroupName.Caption = LocalizationService.Get("Settings.Config.Tools.Group");
            colToolsGroupEnable.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");
            colToolName.Caption = LocalizationService.Get("Settings.Config.Tools.Name");
            colDescription.Caption = LocalizationService.Get("Settings.Config.Tools.Description");
            colEnableTool.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");
            colNeedConfirm.Caption = LocalizationService.Get("Settings.Config.Tools.NoConfirmation");
            colTestTool.Caption = LocalizationService.Get("Settings.Config.Tools.Details");
            if (_toolTestButtonEditor != null && _toolTestButtonEditor.Buttons.Count > 0)
                _toolTestButtonEditor.Buttons[0].Caption = LocalizationService.Get("Settings.Config.Tools.Details");
            if (_editMenuItem != null) _editMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Edit");

            if (_toolGroups != null)
                foreach (var row in _toolGroups) row.DisplayName = GetGroupDisplayName(row.GroupName);
            gcToolsGroup?.RefreshDataSource();
        }

        private static RepositoryItemCheckEdit CreateCheckEdit()
            => new RepositoryItemCheckEdit { ValueChecked = true, ValueUnchecked = false, NullStyle = StyleIndeterminate.Unchecked };

        private RepositoryItemButtonEdit CreateTestButtonEditor()
        {
            var editor = new RepositoryItemButtonEdit { TextEditStyle = TextEditStyles.HideTextEditor };
            editor.Buttons.Clear();
            editor.Buttons.Add(new EditorButton(ButtonPredefines.Glyph) { Caption = LocalizationService.Get("Settings.Config.Tools.Details"), Kind = ButtonPredefines.Glyph, IsDefaultButton = true });
            editor.ButtonClick += (s, e) => OpenToolEditor(true);
            return editor;
        }

        // ========== Load / Save ==========

        private void fmBuiltinToolConfig_Load(object sender, EventArgs e)
        {
            LoadSettings();
        }

        private void fmBuiltinToolConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadingSettings || e == null) return;
            try { SaveSettings(); }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveOnCloseFailed", ex.Message), LocalizationService.Get("Settings.Config.Tab.Tools"),
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

                // Load built-in tool settings from the new dedicated store (with legacy migration)
                _builtinToolSettings = _builtinToolStore.LoadMerged(_toolHostApplicationName, _settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadSettings", ex.Message), LocalizationService.Get("Settings.Config.Tab.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }
            LoadToolSettings();
            _loadingSettings = false;
        }

        private void LoadToolSettings()
        {
            var toolRows = BuildToolRows();
            var groupRows = BuildToolGroupRows(toolRows);
            _toolGroups = new BindingList<ToolGroupRow>(groupRows);
            _allToolRows = toolRows;
            gcToolsGroup.DataSource = _toolGroups;
            if (_toolGroups.Count > 0) gvToolsGroup.FocusedRowHandle = 0;
            RefreshVisibleToolRows();
        }

        private List<ToolRow> BuildToolRows()
        {
            // Get definitions from the tool registry (populated by current app's TaskPaneManager)
            // + runtime built-in tools (always available in Broker)
            var allDefinitions = new List<ToolDefinition>();

            if (_toolRegistry != null)
            {
                var registryDefs = _toolRegistry.GetDefinitions();
                if (registryDefs != null)
                    allDefinitions.AddRange(registryDefs);
            }

            var runtimeDefs = RuntimeBuiltInTools.GetAllDefinitions();
            if (runtimeDefs != null)
                allDefinitions.AddRange(runtimeDefs);

            // Deduplicate by name (registry wins over runtime)
            allDefinitions = allDefinitions
                .Where(d => d != null && !string.IsNullOrWhiteSpace(d.Name))
                .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var rows = new List<ToolRow>();
            foreach (var def in allDefinitions)
            {
                var gn = GetToolGroupName(def);
                // Read registration state from the new store (fallback to legacy settings)
                var isRegistered = GetToolRegistration(def.Name)
                    ?? IsGroupRegisteredByDefault(gn);

                rows.Add(new ToolRow
                {
                    GroupName = gn,
                    ToolName = def.Name,
                    DisplayName = string.IsNullOrWhiteSpace(def.DisplayName) ? def.Name : def.DisplayName,
                    Description = def.Description ?? string.Empty,
                    IsRegistered = isRegistered,
                    IsAutomaticAuthorization = GetToolAutoApproval(def.Name)
                });
            }
            return rows.OrderBy(p => GetGroupOrder(p.GroupName)).ThenBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private bool? GetToolRegistration(string toolName)
        {
            // New store first
            if (_builtinToolSettings?.ToolRegistrations != null)
            {
                var entry = _builtinToolSettings.ToolRegistrations
                    .FirstOrDefault(r => r != null && string.Equals(r.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
                if (entry != null) return entry.IsRegistered;
            }

            // Fallback to legacy settings
            return _settings?.GetToolRegistration(toolName);
        }

        private bool GetToolAutoApproval(string toolName)
        {
            // New store first
            if (_builtinToolSettings?.ToolConfirmationPolicies != null)
            {
                if (_builtinToolSettings.ToolConfirmationPolicies.Any(p =>
                    p != null && string.Equals(p.ToolName, toolName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(p.Mode, "allow-always", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            // Fallback to legacy settings
            return _settings?.IsToolAutoApproved(toolName) == true;
        }

        private bool? GetGroupRegistration(string groupName)
        {
            if (_builtinToolSettings?.ToolGroupRegistrations != null)
            {
                var entry = _builtinToolSettings.ToolGroupRegistrations
                    .FirstOrDefault(g => g != null && string.Equals(g.GroupName, groupName, StringComparison.OrdinalIgnoreCase));
                if (entry != null) return entry.IsRegistered;
            }

            return _settings?.GetToolGroupRegistration(groupName);
        }

        private List<ToolGroupRow> BuildToolGroupRows(IEnumerable<ToolRow> toolRows)
        {
            var rows = new List<ToolGroupRow>();
            var gns = toolRows?.Where(p => p != null).Select(p => p.GroupName ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase)
                ?? Enumerable.Empty<string>();
            foreach (var gn in gns.OrderBy(GetGroupOrder).ThenBy(p => p, StringComparer.OrdinalIgnoreCase))
                rows.Add(new ToolGroupRow { GroupName = gn, DisplayName = GetGroupDisplayName(gn),
                    IsRegistered = GetGroupRegistration(gn) ?? IsGroupRegisteredByDefault(gn) });
            return rows;
        }

        private void RefreshVisibleToolRows()
        {
            var group = GetFocusedToolGroup();
            var rows = _allToolRows?.Where(p => p != null)
                .Where(p => group == null || string.Equals(p.GroupName, group.GroupName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList()
                ?? new List<ToolRow>();
            _visibleToolRows = new BindingList<ToolRow>(rows);
            gcTools.DataSource = _visibleToolRows;
        }

        // ========== Grid Events ==========

        private ToolGroupRow GetFocusedToolGroup()
        {
            if (gvToolsGroup?.FocusedRowHandle < 0) return null;
            return gvToolsGroup.GetRow(gvToolsGroup.FocusedRowHandle) as ToolGroupRow;
        }

        private ToolRow GetFocusedTool()
        {
            if (gvTools?.FocusedRowHandle < 0) return null;
            return gvTools.GetRow(gvTools.FocusedRowHandle) as ToolRow;
        }

        private void gvToolsGroup_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e) => RefreshVisibleToolRows();

        private void gvToolsGroup_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_loadingSettings || e?.Column != colToolsGroupEnable) return;
            SaveToolSettings();
        }

        private void gvTools_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_loadingSettings || e == null) return;
            if (e.Column != colEnableTool && e.Column != colNeedConfirm) return;
            SaveToolSettings();
        }

        private void gvTools_DoubleClick(object sender, EventArgs e)
        {
            if (!IsDoubleClickOnRowCell(sender)) return;
            OpenToolEditor(false);
        }

        // ========== Tool Editor ==========

        private void OpenToolEditor(bool openTesterTab)
        {
            var tool = GetFocusedTool();
            if (tool == null) return;

            // Find base definition from registry or runtime built-in tools
            ToolDefinition baseDef = null;
            if (_toolRegistry != null && _toolRegistry.TryGetTool(tool.ToolName, out var agentTool))
                baseDef = agentTool?.Definition;
            if (baseDef == null)
                baseDef = RuntimeBuiltInTools.GetAllDefinitions()
                    .FirstOrDefault(p => p != null && string.Equals(p.Name, tool.ToolName, StringComparison.OrdinalIgnoreCase));

            if (baseDef == null) { MessageBox.Show(this, LocalizationService.Get("Settings.Config.Tools.BaseDefinitionMissing"), LocalizationService.Get("Settings.Config.Tab.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var currentOverride = _settings?.GetToolDefinitionOverride(tool.ToolName);
            using (var form = new fmToolEditor(baseDef, currentOverride, _toolServices, _toolHostApplicationName, openTesterTab))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (_settings == null) _settings = OfficeCopilotSettingsFactory.CreateDefault();
                    if (form.DefinitionOverride == null)
                        _settings.RemoveToolDefinitionOverride(tool.ToolName);
                    else
                        _settings.SetToolDefinitionOverride(form.DefinitionOverride);
                    _settings.UpdatedAtUtc = DateTime.UtcNow;
                    _settings = OfficeCopilotSettingsFactory.Normalize(_settings);
                    _settingsStore.Save(_settings);
                    RebuildToolRegistry();
                    LoadToolSettings();
                }
                catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveToolDefinition", ex.Message), LocalizationService.Get("Settings.Config.Tab.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        // ========== Save ==========

        private void SaveToolSettings()
        {
            if (_loadingSettings) return;
            try { ApplyToolSettings(); RebuildToolRegistry(); }
            catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveBuiltInTools", ex.Message), LocalizationService.Get("Settings.Config.Tab.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SaveSettings()
        {
            ApplyToolSettings();
            RebuildToolRegistry();
        }

        private void ApplyToolSettings()
        {
            if (_builtinToolSettings == null) _builtinToolSettings = new BuiltinToolSettings();

            // Separate shared (runtime.*) from app-specific tools
            var shared = new BuiltinToolSettings();
            var app = new BuiltinToolSettings();

            if (_toolGroups != null)
            {
                foreach (var g in _toolGroups.Where(p => p != null))
                {
                    if (IsRuntimeGroup(g.GroupName))
                        shared.ToolGroupRegistrations.Add(new ToolGroupRegistrationSettings { GroupName = g.GroupName, IsRegistered = g.IsRegistered, UpdatedAtUtc = DateTime.UtcNow });
                    else
                        app.ToolGroupRegistrations.Add(new ToolGroupRegistrationSettings { GroupName = g.GroupName, IsRegistered = g.IsRegistered, UpdatedAtUtc = DateTime.UtcNow });
                }
            }

            if (_allToolRows != null)
            {
                foreach (var t in _allToolRows.Where(p => p != null))
                {
                    var target = IsRuntimeTool(t.ToolName) ? shared : app;
                    target.ToolRegistrations.Add(new ToolRegistrationSettings { GroupName = t.GroupName, ToolName = t.ToolName, IsRegistered = t.IsRegistered, UpdatedAtUtc = DateTime.UtcNow });
                    if (t.IsAutomaticAuthorization)
                    {
                        target.ToolConfirmationPolicies.Add(new ToolConfirmationPolicySettings { ToolName = t.ToolName, Mode = "allow-always", UpdatedAtUtc = DateTime.UtcNow });
                    }
                }
            }

            // Save to new dedicated files
            var appName = _toolHostApplicationName;
            if (!string.IsNullOrWhiteSpace(appName))
            {
                _builtinToolStore.SaveApp(appName, app);
            }
            _builtinToolStore.SaveShared(shared);

            // Without a host name, app-specific tool settings have no file to go to; surface it instead of dropping them.
            if (string.IsNullOrWhiteSpace(appName) &&
                (app.ToolRegistrations.Count > 0 || app.ToolGroupRegistrations.Count > 0))
            {
                throw new InvalidOperationException(LocalizationService.Get("Settings.Config.Tools.UnknownHostSaveWarning"));
            }

            // Update in-memory copy
            _builtinToolSettings = _builtinToolStore.LoadMerged(appName, _settings);
        }

        private void RebuildToolRegistry()
        {
            // Registry is managed by the TaskPaneManager — tools are registered at startup.
            // After saving settings, we just need to signal a context refresh.
            // The TaskPaneManager will re-evaluate tool registration based on updated settings.
        }

        // ========== Static Helpers ==========

        private static bool IsRuntimeTool(string toolName)
        {
            return toolName != null && toolName.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRuntimeGroup(string groupName)
        {
            return !string.IsNullOrWhiteSpace(groupName)
                && groupName.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetToolGroupName(ToolDefinition def)
        {
            if (def?.Metadata == null) return string.Empty;
            return def.Metadata.TryGetValue(ToolMetadataKeys.GroupName, out var gn) ? (gn ?? string.Empty) : string.Empty;
        }

        private static bool IsGroupRegisteredByDefault(string gn)
        {
            // Runtime groups and non-advanced app groups are registered by default
            if (IsRuntimeGroup(gn)) return true;
            if (gn.EndsWith(".advanced", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static int GetGroupOrder(string gn)
        {
            // App groups (end with .context/.read/.write/.advanced) come first, runtime groups after
            if (string.Equals(gn, RuntimeToolGroups.Context, StringComparison.OrdinalIgnoreCase)) return 14;
            if (gn.EndsWith(".context", StringComparison.OrdinalIgnoreCase)) return 0;
            if (gn.EndsWith(".read", StringComparison.OrdinalIgnoreCase)) return 1;
            if (gn.EndsWith(".write", StringComparison.OrdinalIgnoreCase)) return 2;
            if (gn.EndsWith(".advanced", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(gn, RuntimeToolGroups.Shell, StringComparison.OrdinalIgnoreCase)) return 10;
            if (string.Equals(gn, RuntimeToolGroups.Interaction, StringComparison.OrdinalIgnoreCase)) return 11;
            if (string.Equals(gn, RuntimeToolGroups.Planning, StringComparison.OrdinalIgnoreCase)) return 12;
            if (string.Equals(gn, RuntimeToolGroups.Notification, StringComparison.OrdinalIgnoreCase)) return 13;
            return int.MaxValue;
        }

        private static string GetGroupDisplayName(string gn)
        {
            return string.IsNullOrWhiteSpace(gn)
                ? LocalizationService.Get("Settings.Config.Tools.Group.Ungrouped")
                : gn.Trim();
        }

        private static bool IsDoubleClickOnRowCell(object sender)
        {
            var view = sender as GridView;
            if (view == null) return false;
            return view.CalcHitInfo(view.GridControl.PointToClient(Control.MousePosition)).InRowCell;
        }

        // ========== Row Classes ==========

        private sealed class ToolGroupRow
        {
            public string GroupName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public bool IsRegistered { get; set; } = true;
        }

        private sealed class ToolRow
        {
            public string GroupName { get; set; } = string.Empty;
            public string ToolName { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public bool IsRegistered { get; set; } = true;
            public bool IsAutomaticAuthorization { get; set; }
            public string TestActionText => LocalizationService.Get("Settings.Config.Tools.Details");
        }
    }
}
