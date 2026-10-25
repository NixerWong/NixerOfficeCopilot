using DevExpress.XtraBars;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using Newtonsoft.Json;
using OfficeCopilot.Localization;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using OfficeCopilot.Skills;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmSkillsConfig : HostAwareForm
    {
        private readonly ISettingsStore _settingsStore;
        private readonly string _skillsDirectory;
        private OfficeCopilotSettings _settings;
        private BindingList<SkillRow> _skillRows;
        private RepositoryItemCheckEdit _skillEnabledEditor;
        private ToolStripMenuItem _editMenuItem;
        private ToolStripMenuItem _deleteMenuItem;
        private bool _loadingSettings;

        private static readonly string BuiltInSkillsDirectory = GetBuiltInSkillsDirectory();

        /// <param name="hostApplicationName">
        /// 当前宿主（Excel / Word / PowerPoint）。自定义 Skill 只读写该宿主专属目录，与其他宿主隔离。
        /// </param>
        public fmSkillsConfig(ISettingsStore settingsStore, string hostApplicationName)
        {
            InitializeComponent();
            _settingsStore = settingsStore ?? new JsonSettingsStore();
            _skillsDirectory = SkillStorageLocations.GetHostDirectory(HostApplicationNames.Normalize(hostApplicationName));
            WireEvents();
            ApplyLocalization();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += fmSkillsConfig_Disposed;
        }

        private void WireEvents()
        {
            this.Load += fmSkillsConfig_Load;
            this.FormClosing += fmSkillsConfig_FormClosing;

            btnNewSkill.ItemClick += btnNewSkill_ItemClick;
            btnEditSkill.ItemClick += btnEditSkill_ItemClick;
            btnDeleteSkill.ItemClick += btnDeleteSkill_ItemClick;
            btnExportSkills.ItemClick += btnExportSkills_ItemClick;
            btnImportSkills.ItemClick += btnImportSkills_ItemClick;

            gvSkills.FocusedRowChanged += gvSkills_FocusedRowChanged;
            gvSkills.CellValueChanged += gvSkills_CellValueChanged;
            gvSkills.DoubleClick += gvSkills_DoubleClick;

            // Init grid
            gvSkills.OptionsBehavior.Editable = true;
            gvSkills.OptionsSelection.EnableAppearanceFocusedCell = false;
            colSkillName.OptionsColumn.AllowEdit = false;
            colSkillDesc.OptionsColumn.AllowEdit = false;
            colSkillEnabled.OptionsColumn.AllowEdit = true;

            _skillEnabledEditor = new RepositoryItemCheckEdit
            {
                ValueChecked = true, ValueUnchecked = false,
                NullStyle = StyleIndeterminate.Unchecked
            };
            gcSkills.RepositoryItems.Add(_skillEnabledEditor);
            colSkillEnabled.ColumnEdit = _skillEnabledEditor;

            // 右键菜单
            var ctxSkills = new ContextMenuStrip();
            _editMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Edit"), Properties.Resources.edit_16x16, (s, ev) => EditFocusedSkill());
            _deleteMenuItem = new ToolStripMenuItem(LocalizationService.Get("Settings.Config.Context.Delete"), Properties.Resources.deletelist_16x16, (s, ev) => btnDeleteSkill_ItemClick(s, null));
            ctxSkills.Items.AddRange(new ToolStripItem[] { _editMenuItem, _deleteMenuItem });
            ctxSkills.Opening += (s, ev) => { ev.Cancel = GetFocusedSkillRow() == null; };
            GridContextMenuHelper.AttachToDataRows(gcSkills, gvSkills, ctxSkills);
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void fmSkillsConfig_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void ApplyLocalization()
        {
            Text = LocalizationService.Get("Settings.Config.Tab.Skills");
            btnNewSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Add");
            btnEditSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Edit");
            btnDeleteSkill.Caption = LocalizationService.Get("Settings.Config.Skills.Delete");
            btnExportSkills.Caption = LocalizationService.Get("Settings.Config.Common.ExportList");
            btnImportSkills.Caption = LocalizationService.Get("Settings.Config.Skills.Import");
            colSkillId.Caption = LocalizationService.Get("Settings.Config.Skills.Id");
            colSkillName.Caption = LocalizationService.Get("Settings.Config.Skills.Name");
            colSkillDesc.Caption = LocalizationService.Get("Settings.Config.Skills.Description");
            colSkillEnabled.Caption = LocalizationService.Get("Settings.Config.Common.Enabled");
            if (_editMenuItem != null) _editMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Edit");
            if (_deleteMenuItem != null) _deleteMenuItem.Text = LocalizationService.Get("Settings.Config.Context.Delete");
        }

        // ========== Load / Save ==========

        private void fmSkillsConfig_Load(object sender, EventArgs e)
        {
            ApplyLocalization();
            LoadSettings();
        }

        private void fmSkillsConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadingSettings || e == null) return;
            if (!SyncSkillEnabledStates())
            {
                e.Cancel = true;
                return;
            }
            try { _settingsStore.Save(_settings); }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveOnCloseFailed", ex.Message), LocalizationService.Get("Settings.Config.Tab.Skills"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Error) != DialogResult.Yes)
                    e.Cancel = true;
            }
        }

        private void LoadSettings()
        {
            _loadingSettings = true;
            try
            {
                try
                {
                    _settings = OfficeCopilotSettingsFactory.Normalize(
                        _settingsStore.Load() ?? OfficeCopilotSettingsFactory.CreateDefault());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.LoadSettings", ex.Message), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _settings = OfficeCopilotSettingsFactory.CreateDefault();
                }
                LoadSkillSettings();
            }
            finally
            {
                _loadingSettings = false;
            }
        }

        // 关闭前提交未结束的网格编辑，并把与 SKILL.md 不一致的启用状态写回文件
        private bool SyncSkillEnabledStates()
        {
            if (_skillRows == null) return true;
            gvSkills.PostEditor();
            foreach (var row in _skillRows)
            {
                try
                {
                    if (SkillFrontmatterText.ReadEnabled(row.FilePath) == row.IsEnabled) continue;
                    SkillFrontmatterText.SetEnabled(row.FilePath, row.IsEnabled);
                }
                catch (Exception ex)
                {
                    if (MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveSkillEnabledState", row.Name + ": " + ex.Message),
                        LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.YesNo, MessageBoxIcon.Error) != DialogResult.Yes)
                        return false;
                }
            }
            return true;
        }

        // ========== Skills Loading ==========

        private void LoadSkillSettings()
        {
            var rows = new List<SkillRow>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Layer 1: 内置 Skill
            if (!string.IsNullOrWhiteSpace(BuiltInSkillsDirectory) && Directory.Exists(BuiltInSkillsDirectory))
            {
                foreach (var subDir in Directory.GetDirectories(BuiltInSkillsDirectory))
                {
                    try
                    {
                        var skillMdPath = Path.Combine(subDir, "SKILL.md");
                        if (!File.Exists(skillMdPath)) continue;
                        var (name, description, category) = ParseSkillFrontmatter(skillMdPath);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            rows.Add(new SkillRow
                            {
                                Id = Path.GetFileName(subDir), Name = name,
                                Description = description ?? string.Empty, Category = category ?? string.Empty,
                                SourceKind = "内置", FilePath = skillMdPath, IsEnabled = true, IsReadOnly = true
                            });
                            seenNames.Add(name);
                        }
                    }
                    catch { /* skip */ }
                }
            }

            // Layer 2: 用户 Skill
            if (Directory.Exists(_skillsDirectory))
            {
                foreach (var subDir in Directory.GetDirectories(_skillsDirectory))
                {
                    try
                    {
                        var skillMdPath = Path.Combine(subDir, "SKILL.md");
                        if (!File.Exists(skillMdPath)) continue;
                        var (name, description, category) = ParseSkillFrontmatter(skillMdPath);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            if (seenNames.Contains(name))
                                rows.RemoveAll(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase) && r.IsReadOnly);
                            rows.Add(new SkillRow
                            {
                                Id = Path.GetFileName(subDir), Name = name,
                                Description = description ?? string.Empty, Category = category ?? string.Empty,
                                SourceKind = "文件系统", FilePath = skillMdPath, IsEnabled = true
                            });
                            seenNames.Add(name);
                        }
                    }
                    catch { /* skip */ }
                }
            }

            // 启用状态以各 SKILL.md 的 enabled 字段为准
            foreach (var row in rows)
            {
                row.IsEnabled = SkillFrontmatterText.ReadEnabled(row.FilePath);
            }

            _skillRows = new BindingList<SkillRow>(rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            gcSkills.DataSource = _skillRows;
            if (_skillRows.Count > 0) gvSkills.FocusedRowHandle = 0;
        }

        private static (string name, string description, string category) ParseSkillFrontmatter(string filePath)
        {
            return (SkillFrontmatterText.ReadScalar(filePath, "name"),
                    SkillFrontmatterText.ReadScalar(filePath, "description"),
                    SkillFrontmatterText.ReadScalar(filePath, "category"));
        }

        // ========== Skills CRUD ==========

        private SkillRow GetFocusedSkillRow()
        {
            if (gvSkills?.FocusedRowHandle < 0) return null;
            return gvSkills.GetRow(gvSkills.FocusedRowHandle) as SkillRow;
        }

        private void btnNewSkill_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var editor = new fmSkillEditor(_skillsDirectory))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                LoadSkillSettings();
            }
        }

        private void btnEditSkill_ItemClick(object sender, ItemClickEventArgs e) => EditFocusedSkill();

        private void gvSkills_DoubleClick(object sender, EventArgs e)
        {
            if (!IsDoubleClickOnRowCell(sender)) return;
            EditFocusedSkill();
        }

        private void EditFocusedSkill()
        {
            var row = GetFocusedSkillRow();
            if (row == null) { MessageBox.Show(this, LocalizationService.Get("Settings.Config.Skills.SelectionRequired"), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (row.IsReadOnly) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Skills.BuiltinEditNotAllowed", row.Name), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (!File.Exists(row.FilePath)) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Skills.FileMissing", row.FilePath), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            using (var editor = new fmSkillEditor(row.FilePath, isNew: false))
            {
                if (editor.ShowDialog(this) == DialogResult.OK) LoadSkillSettings();
            }
        }

        private void btnDeleteSkill_ItemClick(object sender, ItemClickEventArgs e)
        {
            var row = GetFocusedSkillRow();
            if (row == null) { MessageBox.Show(this, LocalizationService.Get("Settings.Config.Skills.SelectionRequired"), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (row.IsReadOnly) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Skills.BuiltinDeleteNotAllowed", row.Name), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (string.IsNullOrWhiteSpace(row.FilePath)) { MessageBox.Show(this, LocalizationService.Get("Settings.Config.Skills.FilePathMissing"), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (MessageBox.Show(this, LocalizationService.Format("Settings.Config.Skills.ConfirmDelete", row.Name, Path.GetDirectoryName(row.FilePath)),
                LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                var dir = Path.GetDirectoryName(row.FilePath);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                LoadSkillSettings();
            }
            catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.DeleteSkill", ex.Message), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ========== Import / Export ==========

        private void btnExportSkills_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new SaveFileDialog { Title = LocalizationService.Get("Settings.Config.Skills.ExportDialogTitle"), Filter = LocalizationService.Get("Settings.Config.Skills.ExportFilter"), FileName = "skills-export.nsk", FilterIndex = 1 })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (dialog.FileName.EndsWith(".nsk", StringComparison.OrdinalIgnoreCase))
                        ExportAsNskPackage(dialog.FileName);
                    else
                        ExportAsJson(dialog.FileName);
                    MessageBox.Show(this, LocalizationService.Get("Settings.Config.Common.ExportComplete"), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ExportFailed", ex.Message), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ExportAsNskPackage(string filePath)
        {
            using (var zip = ZipFile.Open(filePath, ZipArchiveMode.Create))
            {
                var exportList = _skillRows.Select(r => new { r.Id, r.Name, r.Description, r.Category, r.SourceKind, r.IsEnabled }).ToList();
                var manifestJson = JsonConvert.SerializeObject(exportList, Formatting.Indented);
                var manifestEntry = zip.CreateEntry("manifest.json");
                using (var writer = new StreamWriter(manifestEntry.Open())) { writer.Write(manifestJson); }
                foreach (var row in _skillRows)
                {
                    if (row.IsReadOnly || string.IsNullOrWhiteSpace(row.FilePath) || !File.Exists(row.FilePath)) continue;
                    var skillDir = Path.GetDirectoryName(row.FilePath);
                    if (!Directory.Exists(skillDir)) continue;
                    var entryPrefix = "skills/" + Path.GetFileName(skillDir) + "/";
                    foreach (var file in Directory.GetFiles(skillDir, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(file, entryPrefix + GetRelativePath(skillDir, file).Replace('\\', '/'));
                }
            }
        }

        private void ExportAsJson(string filePath)
        {
            var exportList = _skillRows.Select(r => new { r.Id, r.Name, r.Description, r.FilePath, r.IsEnabled }).ToList();
            File.WriteAllText(filePath, JsonConvert.SerializeObject(exportList, Formatting.Indented), System.Text.Encoding.UTF8);
        }

        private void btnImportSkills_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new OpenFileDialog { Title = LocalizationService.Get("Settings.Config.Skills.ImportDialogTitle"), Filter = LocalizationService.Get("Settings.Config.Skills.ImportFilter"), FilterIndex = 1 })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (dialog.FileName.EndsWith(".nsk", StringComparison.OrdinalIgnoreCase))
                        ImportFromNskPackage(dialog.FileName);
                    else
                        ImportFromJson(dialog.FileName);
                    LoadSkillSettings();
                }
                catch (Exception ex) { MessageBox.Show(this, LocalizationService.Format("Settings.Config.Common.ImportFailed", ex.Message), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ImportFromNskPackage(string filePath)
        {
            var manifestEntries = new List<SkillRow>();
            int extractedCount = 0, skippedCount = 0;
            using (var zip = ZipFile.OpenRead(filePath))
            {
                var manifestEntry = zip.GetEntry("manifest.json");
                if (manifestEntry != null)
                    using (var reader = new StreamReader(manifestEntry.Open()))
                        manifestEntries = JsonConvert.DeserializeObject<List<SkillRow>>(reader.ReadToEnd()) ?? new List<SkillRow>();
                if (!Directory.Exists(_skillsDirectory)) Directory.CreateDirectory(_skillsDirectory);
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name) || entry.FullName == "manifest.json") continue;
                    if (!entry.FullName.StartsWith("skills/", StringComparison.OrdinalIgnoreCase)) continue;
                    var relativePath = entry.FullName.Substring("skills/".Length);
                    var targetPath = Path.Combine(_skillsDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
                    if (!targetPath.StartsWith(_skillsDirectory, StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(targetPath))
                    {
                        var sn = relativePath.Split(new[] { '/', '\\' }, 2)[0];
                        if (manifestEntries.Any(m => string.Equals(m.Id, sn, StringComparison.OrdinalIgnoreCase))) { skippedCount++; continue; }
                    }
                    var targetDir = Path.GetDirectoryName(targetPath);
                    if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                    entry.ExtractToFile(targetPath, overwrite: true);
                    extractedCount++;
                }
            }
            foreach (var item in manifestEntries)
            {
                if (string.IsNullOrWhiteSpace(item.Id)) continue;
                var localSkillMd = Path.Combine(_skillsDirectory, item.Id, "SKILL.md");
                if (!File.Exists(localSkillMd)) continue;
                item.FilePath = localSkillMd;
                SaveSkillEnabledState(item);
            }
            var skipped = skippedCount > 0 ? LocalizationService.Format("Settings.Config.Skills.ExistingFilesSkipped", skippedCount) : string.Empty;
            MessageBox.Show(this, LocalizationService.Format("Settings.Config.Skills.NskImportComplete", extractedCount, skipped),
                LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ImportFromJson(string filePath)
        {
            if (MessageBox.Show(this, LocalizationService.Get("Settings.Config.Skills.JsonImportWarning"), LocalizationService.Get("Settings.Config.Tab.Skills"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            var importList = JsonConvert.DeserializeObject<List<SkillRow>>(json);
            if (importList != null)
            {
                int count = 0;
                foreach (var item in importList)
                {
                    if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
                    { SaveSkillEnabledState(item); count++; }
                }
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Skills.JsonImportComplete", count), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ========== Grid Events ==========

        private void gvSkills_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e) { }

        private void gvSkills_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_loadingSettings || e.Column != colSkillEnabled) return;
            var row = gvSkills.GetRow(e.RowHandle) as SkillRow;
            if (row != null) SaveSkillEnabledState(row);
        }

        private void SaveSkillEnabledState(SkillRow row)
        {
            if (row == null) return;
            try
            {
                SkillFrontmatterText.SetEnabled(row.FilePath, row.IsEnabled);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.Error.SaveSkillEnabledState", ex.Message), LocalizationService.Get("Settings.Config.Tab.Skills"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ========== Helpers ==========

        private static bool IsDoubleClickOnRowCell(object sender)
        {
            var view = sender as GridView;
            if (view == null) return false;
            return view.CalcHitInfo(view.GridControl.PointToClient(Control.MousePosition)).InRowCell;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString())) basePath += Path.DirectorySeparatorChar;
            return Uri.UnescapeDataString(new Uri(basePath).MakeRelativeUri(new Uri(fullPath)).ToString());
        }

        private static string GetBuiltInSkillsDirectory()
        {
            var searchDirs = new List<string>();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir)) searchDirs.Add(baseDir);
            var entryAsm = System.Reflection.Assembly.GetEntryAssembly();
            if (entryAsm != null) { var d = Path.GetDirectoryName(entryAsm.Location); if (!string.IsNullOrWhiteSpace(d)) searchDirs.Add(d); }
            var thisAsm = System.Reflection.Assembly.GetExecutingAssembly();
            if (thisAsm != null) { var d = Path.GetDirectoryName(thisAsm.Location); if (!string.IsNullOrWhiteSpace(d)) searchDirs.Add(d); }
            var allCandidates = new List<string>(searchDirs);
            foreach (var dir in searchDirs)
            {
                try
                {
                    var parent = Path.GetDirectoryName(dir);
                    if (!string.IsNullOrWhiteSpace(parent) && !allCandidates.Exists(c => string.Equals(c, parent, StringComparison.OrdinalIgnoreCase)))
                        allCandidates.Add(parent);
                }
                catch { }
            }
            foreach (var dir in allCandidates)
            {
                var path = Path.Combine(dir, "BuiltInSkills");
                if (Directory.Exists(path)) return path;
            }
            return null;
        }

        // ========== Row Class ==========

        private sealed class SkillRow
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string SourceKind { get; set; } = "文件系统";
            public string FilePath { get; set; } = string.Empty;
            public bool IsEnabled { get; set; } = true;
            public bool IsReadOnly { get; set; }
        }
    }
}
