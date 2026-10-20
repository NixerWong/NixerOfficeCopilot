using DevExpress.XtraBars;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OfficeCopilot.Panel.WebView.Presentation
{
    /// <summary>
    /// F1：从 <c>CopilotPanelControl</c> 抽出的模型下拉菜单展示层，
    /// 独占「可用模型列表 + 当前选中模型 + 菜单项渲染」三件事。
    /// 会话侧的读写通过构造函数注入的回调回到面板，避免presenter 反向依赖会话状态。
    /// </summary>
    internal sealed class PanelModelMenuPresenter
    {
        private const string NoModelCaption = "请先设置模型";
        private const int ModelMenuGroupIndex = 100;

        private readonly BarManager _barManager;
        private readonly BarSubItem _menu;
        private readonly Action _syncSelectionFromSession;
        private readonly Action _syncSessionFromSelection;
        private readonly Action<int?> _persistDefaultModelProviderId;

        private List<ModelProviderSettings> _availableModels = new List<ModelProviderSettings>();

        public PanelModelMenuPresenter(
            BarManager barManager,
            BarSubItem menu,
            Action syncSelectionFromSession,
            Action syncSessionFromSelection,
            Action<int?> persistDefaultModelProviderId)
        {
            if (barManager == null) throw new ArgumentNullException(nameof(barManager));
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            if (syncSelectionFromSession == null) throw new ArgumentNullException(nameof(syncSelectionFromSession));
            if (syncSessionFromSelection == null) throw new ArgumentNullException(nameof(syncSessionFromSelection));
            if (persistDefaultModelProviderId == null) throw new ArgumentNullException(nameof(persistDefaultModelProviderId));

            _barManager = barManager;
            _menu = menu;
            _syncSelectionFromSession = syncSelectionFromSession;
            _syncSessionFromSelection = syncSessionFromSelection;
            _persistDefaultModelProviderId = persistDefaultModelProviderId;
        }

        public int? SelectedModelProviderId { get; set; }

        public bool HasModels
        {
            get { return _availableModels.Count > 0; }
        }

        public void SetAvailableModels(IEnumerable<ModelProviderSettings> models)
        {
            _availableModels = models == null
                ? new List<ModelProviderSettings>()
                : models.Where(m => m != null).ToList();
        }

        public void Refresh()
        {
            _menu.ClearLinks();

            if (!HasModels)
            {
                SelectedModelProviderId = null;
                _menu.Caption = NoModelCaption;
                _persistDefaultModelProviderId(null);
                AddEmptyMenuItem();
                return;
            }

            _syncSelectionFromSession();

            ModelProviderSettings selectedModel = null;
            if (SelectedModelProviderId.HasValue)
            {
                selectedModel = FindModel(SelectedModelProviderId.Value);
            }

            if (selectedModel == null)
            {
                selectedModel = _availableModels[0];
                SelectedModelProviderId = selectedModel.Id;
                _syncSessionFromSelection();
            }

            _menu.Caption = GetDisplayName(selectedModel);

            foreach (var model in _availableModels)
            {
                var item = new BarCheckItem();
                item.Caption = GetDisplayName(model);
                item.Tag = model.Id;
                item.GroupIndex = ModelMenuGroupIndex;
                item.Checked = model.Id == SelectedModelProviderId;
                item.ItemClick += MenuItem_ItemClick;

                _barManager.Items.Add(item);
                _menu.AddItem(item);
            }
        }

        public void Select(int modelId, bool persist)
        {
            var model = FindModel(modelId);
            if (model == null)
            {
                Refresh();
                return;
            }

            SelectedModelProviderId = model.Id;
            _syncSessionFromSelection();
            _menu.Caption = GetDisplayName(model);

            foreach (BarItemLink link in _menu.ItemLinks)
            {
                var checkItem = link.Item as BarCheckItem;
                if (checkItem == null || checkItem.Tag == null)
                {
                    continue;
                }

                int currentId;
                if (int.TryParse(checkItem.Tag.ToString(), out currentId))
                {
                    checkItem.Checked = currentId == model.Id;
                }
            }

            if (persist)
            {
                _persistDefaultModelProviderId(model.Id);
            }
        }

        /// <summary>
        /// 按「会话指定 → 全局默认 → 第一个可用」的顺序解析出一个仍然有效的模型 ID。
        /// </summary>
        public int? ResolveModelProviderId(int? sessionModelProviderId, int? defaultModelProviderId)
        {
            if (!HasModels)
            {
                return null;
            }

            if (sessionModelProviderId.HasValue)
            {
                var sessionModel = FindModel(sessionModelProviderId.Value);
                if (sessionModel != null)
                {
                    return sessionModel.Id;
                }
            }

            if (defaultModelProviderId.HasValue)
            {
                var defaultModel = FindModel(defaultModelProviderId.Value);
                if (defaultModel != null)
                {
                    return defaultModel.Id;
                }
            }

            return _availableModels[0].Id;
        }

        /// <summary>
        /// 取本轮实际生效的模型：优先会话绑定，其次菜单选中。
        /// </summary>
        public ModelProviderSettings ResolveEffectiveModel(int? sessionModelProviderId)
        {
            if (!HasModels)
            {
                return null;
            }

            if (sessionModelProviderId.HasValue)
            {
                var sessionModel = FindModel(sessionModelProviderId.Value);
                if (sessionModel != null)
                {
                    return sessionModel;
                }
            }

            return SelectedModelProviderId.HasValue
                ? FindModel(SelectedModelProviderId.Value)
                : null;
        }

        public static string GetDisplayName(ModelProviderSettings model)
        {
            if (model == null)
            {
                return NoModelCaption;
            }

            if (!string.IsNullOrWhiteSpace(model.Name))
            {
                return model.Name.Trim();
            }

            return "模型 " + model.Id;
        }

        private ModelProviderSettings FindModel(int modelId)
        {
            return _availableModels.FirstOrDefault(m => m.Id == modelId);
        }

        private void AddEmptyMenuItem()
        {
            var item = new BarButtonItem();
            item.Caption = NoModelCaption;
            item.Enabled = false;

            _barManager.Items.Add(item);
            _menu.AddItem(item);
        }

        private void MenuItem_ItemClick(object sender, ItemClickEventArgs e)
        {
            int modelId;
            if (e == null || e.Item == null || e.Item.Tag == null || !int.TryParse(e.Item.Tag.ToString(), out modelId))
            {
                return;
            }

            Select(modelId, true);
        }
    }
}
