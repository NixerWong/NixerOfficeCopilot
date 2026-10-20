using DevExpress.XtraBars;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    public partial class fmModelConfig : HostAwareForm
    {
        private readonly ISettingsStore _settingsStore;
        private OfficeCopilotSettings _settings;
        private BindingList<ModelProviderSettings> _models;
        private bool _loadingSettings;

        public fmModelConfig(ISettingsStore settingsStore)
        {
            InitializeComponent();
            _settingsStore = settingsStore ?? new JsonSettingsStore();
            WireEvents();
        }

        private void WireEvents()
        {
            this.Load += fmModelConfig_Load;
            this.FormClosing += fmModelConfig_FormClosing;

            btnAddModel.ItemClick += btnAddModel_ItemClick;
            btnEditModel.ItemClick += gvModels_DoubleClick;
            btnDeleteModel.ItemClick += btnDeleteModel_ItemClick;
            btnExportListToJson.ItemClick += btnExportListToJson_ItemClick;
            btnImportList.ItemClick += btnImportList_ItemClick;
            btnShowPreview.ItemClick += btnShowPreview_ItemClick;

            gvModels.DoubleClick += gvModels_DoubleClick;
            gvModels.CustomColumnDisplayText += gvModels_CustomColumnDisplayText;

            var ctxModels = new ContextMenuStrip();
            ctxModels.Items.Add("编辑", Properties.Resources.loadpagesetup_16x16, (s, ev) => EditFocusedModel());
            ctxModels.Items.Add("删除", Properties.Resources.deletelist_16x16, (s, ev) => btnDeleteModel_ItemClick(s, null));
            ctxModels.Opening += (s, ev) => { ev.Cancel = GetFocusedModel() == null; };
            GridContextMenuHelper.AttachToDataRows(gcModels, gvModels, ctxModels);
        }

        // ========== Load / Save ==========

        private void fmModelConfig_Load(object sender, EventArgs e)
        {
            LoadSettings();
            gvModels.OptionsBehavior.Editable = true;
            gvModels.OptionsSelection.EnableAppearanceFocusedCell = false;
            gvModels.OptionsView.ShowPreview = false;
            RefreshPreviewButtonCaption();
        }

        private void fmModelConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadingSettings || e == null) return;
            // Commit a pending checkbox edit in the grid before reading the bound rows.
            gvModels.PostEditor();
            try { SaveSettings(); }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, "保存模型配置失败：" + ex.Message + "\n\n是否放弃修改并关闭？", "模型配置",
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
                MessageBox.Show(this, "加载配置失败：" + ex.Message, "模型配置", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _settings = OfficeCopilotSettingsFactory.CreateDefault();
            }
            if (_settings.ModelProviders == null) _settings.ModelProviders = new List<ModelProviderSettings>();
            RebindModels(_settings.ModelProviders);
            _loadingSettings = false;
        }

        private void RebindModels(IEnumerable<ModelProviderSettings> models)
        {
            var list = models == null ? new List<ModelProviderSettings>()
                : models.Where(p => p != null).OrderBy(p => p.Id).ToList();
            _models = new BindingList<ModelProviderSettings>(list);
            gcModels.DataSource = _models;
        }

        private void SaveSettings()
        {
            if (_settings == null) _settings = OfficeCopilotSettingsFactory.CreateDefault();
            _settings.ModelProviders = _models == null ? new List<ModelProviderSettings>()
                : _models.Where(p => p != null).Select(OfficeCopilotSettingsFactory.NormalizeModelProvider)
                    .OrderBy(p => p.Id).ToList();
            if (_settings.DefaultModelProviderId.HasValue)
            {
                if (!_settings.ModelProviders.Any(m => m.Id == _settings.DefaultModelProviderId.Value))
                    _settings.DefaultModelProviderId = null;
            }
            _settings.UpdatedAtUtc = DateTime.UtcNow;
            _settingsStore.Save(_settings);
        }

        // ========== Model CRUD ==========

        private ModelProviderSettings GetFocusedModel()
        {
            if (gvModels.FocusedRowHandle < 0) return null;
            return gvModels.GetRow(gvModels.FocusedRowHandle) as ModelProviderSettings;
        }

        private int GetNextModelId()
        {
            if (_models == null || _models.Count == 0) return 1;
            return _models.Max(m => m.Id) + 1;
        }

        private void btnAddModel_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var form = new fmModelEditor())
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Model == null) return;
                if (form.Model.Id <= 0) form.Model.Id = GetNextModelId();
                var list = _models == null ? new List<ModelProviderSettings>() : _models.ToList();
                list.Add(form.Model);
                RebindModels(list);
                SaveSettings();
                gvModels.RefreshData();
            }
        }

        private void gvModels_DoubleClick(object sender, EventArgs e)
        {
            if (!IsDoubleClickOnRowCell(sender)) return;
            EditFocusedModel();
        }

        private void EditFocusedModel()
        {
            var current = GetFocusedModel();
            if (current == null) return;
            var list = _models.ToList();
            var index = list.FindIndex(m => m != null && m.Id == current.Id);
            if (index < 0) return;
            using (var form = new fmModelEditor(current))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.Model == null) return;
                if (form.Model.Id <= 0) form.Model.Id = current.Id;
                list[index] = form.Model;
                RebindModels(list);
                SaveSettings();
                gvModels.RefreshData();
            }
        }

        private void btnDeleteModel_ItemClick(object sender, ItemClickEventArgs e)
        {
            var current = GetFocusedModel();
            if (current == null) return;
            if (MessageBox.Show(this, string.Format("确定要删除模型 \"{0}\" 吗？", current.Name),
                "模型配置", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var list = _models.ToList();
            list.RemoveAll(m => m != null && m.Id == current.Id);
            if (_settings != null && _settings.DefaultModelProviderId.HasValue &&
                _settings.DefaultModelProviderId.Value == current.Id)
                _settings.DefaultModelProviderId = null;
            RebindModels(list);
            SaveSettings();
            gvModels.RefreshData();
        }

        // ========== Import / Export ==========

        private void btnExportListToJson_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new SaveFileDialog { Title = "导出模型列表", Filter = "JSON 文件|*.json|所有文件|*.*", FileName = "model-providers.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var json = JsonConvert.SerializeObject(_models.OrderBy(m => m.Id).ToList(), Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json, System.Text.Encoding.UTF8);
                    MessageBox.Show(this, "导出完成。", "模型配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, "模型配置", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void btnImportList_ItemClick(object sender, ItemClickEventArgs e)
        {
            using (var dialog = new OpenFileDialog { Title = "导入模型定义", Filter = "JSON 文件|*.json|所有文件|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this, "导入将覆盖当前模型列表，是否继续？", "模型配置",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    var imported = ReadModelsFromFile(dialog.FileName);
                    if (_settings == null) _settings = OfficeCopilotSettingsFactory.CreateDefault();
                    _settings.ModelProviders = imported;
                    if (_settings.DefaultModelProviderId.HasValue && !imported.Any(m => m.Id == _settings.DefaultModelProviderId.Value))
                        _settings.DefaultModelProviderId = null;
                    RebindModels(imported);
                    SaveSettings();
                    gvModels.RefreshData();
                    MessageBox.Show(this, "导入完成。", "模型配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { MessageBox.Show(this, "导入失败：" + ex.Message, "模型配置", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        // ========== Preview ==========

        private void btnShowPreview_ItemClick(object sender, ItemClickEventArgs e)
        {
            gvModels.OptionsView.ShowPreview = !gvModels.OptionsView.ShowPreview;
            RefreshPreviewButtonCaption();
        }

        private void RefreshPreviewButtonCaption()
        {
            btnShowPreview.Caption = gvModels.OptionsView.ShowPreview ? "隐藏提示词预览" : "显示提示词预览";
        }

        // ========== Display ==========

        private void gvModels_CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (e?.Column == null) return;
            var fn = e.Column.FieldName;
            if (string.IsNullOrWhiteSpace(fn)) return;
            if (fn == "IsEnabled" || fn == "SupportsImageInput" || fn == "SupportsFunctionCalling" || fn == "SupportsDeepThinking")
            {
                if (TryConvertToBoolean(e.Value, out bool bv)) e.DisplayText = bv ? "是" : "否";
            }
            else if (fn == "ProviderKind")
            {
                if (TryConvertToProviderKind(e.Value, out ModelProviderKind pk)) e.DisplayText = GetProviderKindDisplayText(pk);
            }
        }

        private static bool TryConvertToBoolean(object value, out bool result)
        {
            if (value is bool) { result = (bool)value; return true; }
            return bool.TryParse(Convert.ToString(value), out result);
        }

        private static bool TryConvertToProviderKind(object value, out ModelProviderKind result)
        {
            if (value is ModelProviderKind) { result = (ModelProviderKind)value; return true; }
            return Enum.TryParse(Convert.ToString(value), true, out result);
        }

        private static string GetProviderKindDisplayText(ModelProviderKind kind)
        {
            switch (kind)
            {
                case ModelProviderKind.OpenAI: return "OpenAI";
                case ModelProviderKind.AzureOpenAI: return "Azure OpenAI";
                case ModelProviderKind.Ollama: return "Ollama";
                case ModelProviderKind.LMStudio: return "LM Studio";
                case ModelProviderKind.Anthropic: return "Anthropic";
                case ModelProviderKind.DeepSeek: return "DeepSeek";
                default: return "Custom";
            }
        }

        private static bool IsDoubleClickOnRowCell(object sender)
        {
            var view = sender as GridView;
            if (view == null) return false;
            return view.CalcHitInfo(view.GridControl.PointToClient(Control.MousePosition)).InRowCell;
        }

        // ========== Import Helpers ==========

        private static List<ModelProviderSettings> ReadModelsFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) return new List<ModelProviderSettings>();
            var token = JToken.Parse(json);
            List<ModelProviderSettings> models = null;
            if (token.Type == JTokenType.Array)
            {
                var array = token as JArray;
                MigrateModelProviderIds(array);
                models = array?.ToObject<List<ModelProviderSettings>>();
            }
            else if (token.Type == JTokenType.Object)
            {
                var providers = (token as JObject)?["ModelProviders"] as JArray;
                if (providers != null) { MigrateModelProviderIds(providers); models = providers.ToObject<List<ModelProviderSettings>>(); }
            }
            models = models ?? new List<ModelProviderSettings>();
            NormalizeImportedModels(models);
            return models.OrderBy(m => m.Id).ToList();
        }

        private static void MigrateModelProviderIds(JArray providers)
        {
            if (providers == null || providers.Count == 0) return;
            var maxId = providers.OfType<JObject>().Max(j => TryReadPositiveInt(j["Id"]));
            var nextId = maxId <= 0 ? 1 : maxId + 1;
            var usedIds = new HashSet<int>();
            foreach (var item in providers.OfType<JObject>())
            {
                var cid = TryReadPositiveInt(item["Id"]);
                if (cid <= 0 || usedIds.Contains(cid)) cid = nextId++;
                item["Id"] = cid;
                usedIds.Add(cid);
            }
        }

        private static void NormalizeImportedModels(List<ModelProviderSettings> models)
        {
            if (models == null) return;
            var maxId = models.Where(m => m != null && m.Id > 0).Select(m => m.Id).DefaultIfEmpty(0).Max();
            var nextId = maxId <= 0 ? 1 : maxId + 1;
            var usedIds = new HashSet<int>();
            foreach (var model in models.Where(m => m != null))
            {
                if (model.Id <= 0 || usedIds.Contains(model.Id)) model.Id = nextId++;
                usedIds.Add(model.Id);
                if (model.SystemPrompt == null) model.SystemPrompt = string.Empty;
                OfficeCopilotSettingsFactory.NormalizeModelProvider(model);
            }
        }

        private static int TryReadPositiveInt(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return 0;
            if (token.Type == JTokenType.Integer) { var v = token.Value<int>(); return v > 0 ? v : 0; }
            return int.TryParse(token.ToString(), out int p) && p > 0 ? p : 0;
        }
    }
}
