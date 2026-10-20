using DevExpress.XtraBars;
using DevExpress.XtraGrid.Columns;
using DevExpress.Utils.Menu;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmModelEditor : HostAwareForm
    {
        private BindingList<NameValueRow> _additionalParameterRows;
        private BindingList<NameValueRow> _httpHeaderRows;
        private BasicParametersModel _basicParameters;

        public ModelProviderSettings Model { get; private set; }

        public fmModelEditor()
            : this(null)
        {
        }

        public fmModelEditor(ModelProviderSettings model)
        {
            InitializeComponent();

            Model = OfficeCopilotSettingsFactory.NormalizeModelProvider(CloneModel(model));

            WireEvents();
        }

        private void WireEvents()
        {
            this.Load += fmModelEditor_Load;

            btnSave.ItemClick += btnSave_ItemClick;
            btnCancel.ItemClick += btnCancel_ItemClick;
            btnTestConn.ItemClick += btnTestConn_ItemClick;

            WireNameValueGridEvents(gvAdditionalParams);
            WireNameValueGridEvents(gvHttpHeaders);
        }

        private void WireNameValueGridEvents(DevExpress.XtraGrid.Views.Grid.GridView view)
        {
            if (view == null)
            {
                return;
            }

            view.PopupMenuShowing += NameValueGrid_PopupMenuShowing;
            view.KeyDown += NameValueGrid_KeyDown;
        }

        private void fmModelEditor_Load(object sender, EventArgs e)
        {
            InitializeEditor();
            BindModelToForm();
        }

        private void InitializeEditor()
        {
            Text = string.IsNullOrWhiteSpace(Model.Name) ? "新增模型" : "编辑模型";

            pgcBasicParams.OptionsView.ShowRootCategories = false;

            ConfigureNameValueGrid(gvAdditionalParams, "参数名", "参数值");
            ConfigureNameValueGrid(gvHttpHeaders, "请求头", "值");
        }

        private void BindModelToForm()
        {
            txtModelName.Text = Model.Name ?? string.Empty;
            txtSystemPrompt.Text = Model.SystemPrompt ?? string.Empty;
            bciEnableModel.Checked = Model.IsEnabled;

            _basicParameters = BasicParametersModel.FromModel(Model);
            pgcBasicParams.SelectedObject = _basicParameters;

            _additionalParameterRows = new BindingList<NameValueRow>(ToNameValueRows(Model.AdditionalParameters));
            gcAdditionalParams.DataSource = _additionalParameterRows;

            _httpHeaderRows = new BindingList<NameValueRow>(ToNameValueRows(Model.HttpHeaders));
            gcHttpHeaders.DataSource = _httpHeaderRows;

            gvAdditionalParams.BestFitColumns();
            gvHttpHeaders.BestFitColumns();
        }

        private static void ConfigureNameValueGrid(DevExpress.XtraGrid.Views.Grid.GridView view, string nameCaption, string valueCaption)
        {
            if (view == null)
            {
                return;
            }

            view.Columns.Clear();

            var nameColumn = view.Columns.AddVisible("Name", nameCaption);
            nameColumn.OptionsColumn.AllowSort = DevExpress.Utils.DefaultBoolean.False;
            nameColumn.VisibleIndex = 0;
            nameColumn.Width = 180;

            var valueColumn = view.Columns.AddVisible("Value", valueCaption);
            valueColumn.OptionsColumn.AllowSort = DevExpress.Utils.DefaultBoolean.False;
            valueColumn.VisibleIndex = 1;
            valueColumn.Width = 280;
        }

        private static List<NameValueRow> ToNameValueRows(IDictionary<string, object> source)
        {
            var rows = new List<NameValueRow>();
            if (source == null)
            {
                return rows;
            }

            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                rows.Add(new NameValueRow
                {
                    Name = pair.Key,
                    Value = FormatValue(pair.Value)
                });
            }

            return rows;
        }

        private static List<NameValueRow> ToNameValueRows(IDictionary<string, string> source)
        {
            var rows = new List<NameValueRow>();
            if (source == null)
            {
                return rows;
            }

            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                rows.Add(new NameValueRow
                {
                    Name = pair.Key,
                    Value = pair.Value ?? string.Empty
                });
            }

            return rows;
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is string)
            {
                return (string)value;
            }

            if (value is bool)
            {
                return ((bool)value) ? "true" : "false";
            }

            if (value is IFormattable)
            {
                return ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture);
            }

            return JsonConvert.SerializeObject(value);
        }

        private void btnSave_ItemClick(object sender, ItemClickEventArgs e)
        {
            string validationMessage;
            if (!ValidateInputs(out validationMessage))
            {
                MessageBox.Show(this, validationMessage, "模型配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ApplyFormToModel();

            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_ItemClick(object sender, ItemClickEventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private async void btnTestConn_ItemClick(object sender, ItemClickEventArgs e)
        {
            string validationMessage;
            if (!ValidateInputs(out validationMessage))
            {
                MessageBox.Show(this, validationMessage, "测试连接", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var probeModel = BuildModelFromForm();
            btnTestConn.Enabled = false;

            try
            {
                using (var client = CreateHttpClient(probeModel))
                using (var request = new HttpRequestMessage(HttpMethod.Get, probeModel.Endpoint))
                {
                    ApplyHeaders(request, probeModel);

                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                    {
                        // 只要收到 HTTP 响应（无论状态码，404/405 等一律忽略），就说明服务 URL 可达。
                        MessageBox.Show(this, "服务URL可达。", "测试连接", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "连接失败：" + ex.Message, "测试连接", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTestConn.Enabled = true;
            }
        }

        private static HttpClient CreateHttpClient(ModelProviderSettings model)
        {
            var client = new HttpClient();
            //以后用系统级别参数来统一控制超时时间，这里先设置一个较大的值，避免测试连接时因为默认超时过短而误报连接失败
            client.Timeout = TimeSpan.FromSeconds(600);
            return client;
        }

        private bool ValidateInputs(out string message)
        {
            CloseEditors();

            if (string.IsNullOrWhiteSpace(txtModelName.Text))
            {
                message = "请填写模型名称。";
                return false;
            }

            if (_basicParameters == null)
            {
                message = "基本参数未初始化。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_basicParameters.Endpoint))
            {
                message = "请填写 API 地址。";
                return false;
            }

            Uri endpointUri;
            if (!Uri.TryCreate(_basicParameters.Endpoint.Trim(), UriKind.Absolute, out endpointUri) ||
                (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
            {
                message = "API 地址格式不正确，必须是 http 或 https。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_basicParameters.ModelId))
            {
                message = "请填写模型 ID。";
                return false;
            }

            Dictionary<string, object> additionalParameters;
            if (!TryBuildAdditionalParameters(out additionalParameters, out message))
            {
                return false;
            }

            Dictionary<string, string> httpHeaders;
            if (!TryBuildHttpHeaders(out httpHeaders, out message))
            {
                return false;
            }

            message = null;
            return true;
        }

        private void ApplyFormToModel()
        {
            Model = BuildModelFromForm();
        }

        private ModelProviderSettings BuildModelFromForm()
        {
            Dictionary<string, object> additionalParameters;
            string message;
            if (!TryBuildAdditionalParameters(out additionalParameters, out message))
            {
                throw new InvalidOperationException(message);
            }

            Dictionary<string, string> httpHeaders;
            if (!TryBuildHttpHeaders(out httpHeaders, out message))
            {
                throw new InvalidOperationException(message);
            }

            var model = CloneModel(Model);
            if (model.Id == 0)
            {
                model.Id = int.Parse(Guid.NewGuid().ToString("N").Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            model.Name = (txtModelName.Text ?? string.Empty).Trim();
            model.SystemPrompt = txtSystemPrompt.Text ?? string.Empty;
            model.IsEnabled = bciEnableModel.Checked;

            _basicParameters.ApplyTo(model);
            model.AdditionalParameters = additionalParameters;
            model.HttpHeaders = httpHeaders;

            return OfficeCopilotSettingsFactory.NormalizeModelProvider(model);
        }

        private void CloseEditors()
        {
            if (pgcBasicParams != null)
            {
                pgcBasicParams.PostEditor();
                pgcBasicParams.CloseEditor();
            }

            gvAdditionalParams.CloseEditor();
            gvAdditionalParams.UpdateCurrentRow();

            gvHttpHeaders.CloseEditor();
            gvHttpHeaders.UpdateCurrentRow();
        }

        private void NameValueGrid_PopupMenuShowing(object sender, DevExpress.XtraGrid.Views.Grid.PopupMenuShowingEventArgs e)
        {
            var view = sender as DevExpress.XtraGrid.Views.Grid.GridView;
            if (view == null)
            {
                return;
            }

            if (e.HitInfo == null || !e.HitInfo.InRowCell)
            {
                return;
            }

            var rowHandle = e.HitInfo.RowHandle;
            if (rowHandle < 0 || view.IsNewItemRow(rowHandle))
            {
                return;
            }

            view.FocusedRowHandle = rowHandle;

            if (e.Menu == null)
            {
                return;
            }

            e.Menu.Items.Add(new DXMenuItem("删除当前行", delegate
            {
                DeleteRow(view, rowHandle);
            }));
        }

        private void NameValueGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete)
            {
                return;
            }

            var view = sender as DevExpress.XtraGrid.Views.Grid.GridView;
            if (view == null)
            {
                return;
            }

            if (DeleteRow(view, view.FocusedRowHandle))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private static bool DeleteRow(DevExpress.XtraGrid.Views.Grid.GridView view, int rowHandle)
        {
            if (view == null)
            {
                return false;
            }

            if (rowHandle < 0 || view.IsNewItemRow(rowHandle))
            {
                return false;
            }

            view.CloseEditor();
            view.UpdateCurrentRow();

            if (!view.IsValidRowHandle(rowHandle) || view.IsNewItemRow(rowHandle))
            {
                return false;
            }

            view.DeleteRow(rowHandle);
            return true;
        }

        private bool TryBuildAdditionalParameters(out Dictionary<string, object> additionalParameters, out string message)
        {
            additionalParameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in EnumerateRows(_additionalParameterRows))
            {
                var name = (row.Name ?? string.Empty).Trim();
                var rawValue = row.Value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(rawValue))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    message = "附加参数名称不能为空。";
                    return false;
                }

                if (additionalParameters.ContainsKey(name))
                {
                    message = string.Format("附加参数“{0}”重复，请检查。", name);
                    return false;
                }

                object parsedValue;
                if (!TryParseValue(rawValue, out parsedValue, out message))
                {
                    message = string.Format("附加参数“{0}”的值无效：{1}", name, message);
                    return false;
                }

                additionalParameters[name] = parsedValue;
            }

            message = null;
            return true;
        }

        private bool TryBuildHttpHeaders(out Dictionary<string, string> httpHeaders, out string message)
        {
            httpHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in EnumerateRows(_httpHeaderRows))
            {
                var name = (row.Name ?? string.Empty).Trim();
                var value = row.Value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    message = "HTTP 请求头名称不能为空。";
                    return false;
                }

                if (httpHeaders.ContainsKey(name))
                {
                    message = string.Format("HTTP 请求头“{0}”重复，请检查。", name);
                    return false;
                }

                httpHeaders[name] = value;
            }

            message = null;
            return true;
        }

        private static IEnumerable<NameValueRow> EnumerateRows(IEnumerable<NameValueRow> rows)
        {
            return rows == null ? Enumerable.Empty<NameValueRow>() : rows.Where(r => r != null);
        }

        private static bool TryParseValue(string rawValue, out object parsedValue, out string message)
        {
            var text = rawValue == null ? string.Empty : rawValue.Trim();
            if (text.Length == 0)
            {
                parsedValue = string.Empty;
                message = null;
                return true;
            }

            bool booleanValue;
            if (bool.TryParse(text, out booleanValue))
            {
                parsedValue = booleanValue;
                message = null;
                return true;
            }

            int intValue;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                parsedValue = intValue;
                message = null;
                return true;
            }

            long longValue;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out longValue))
            {
                parsedValue = longValue;
                message = null;
                return true;
            }

            double doubleValue;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                parsedValue = doubleValue;
                message = null;
                return true;
            }

            if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
            {
                parsedValue = null;
                message = null;
                return true;
            }

            if (LooksLikeJson(text))
            {
                try
                {
                    parsedValue = JToken.Parse(text).ToObject<object>();
                    message = null;
                    return true;
                }
                catch (Exception ex)
                {
                    parsedValue = null;
                    message = ex.Message;
                    return false;
                }
            }

            parsedValue = rawValue ?? string.Empty;
            message = null;
            return true;
        }

        private static bool LooksLikeJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            return (text.StartsWith("{") && text.EndsWith("}")) ||
                   (text.StartsWith("[") && text.EndsWith("]"));
        }

        private static void ApplyHeaders(HttpRequestMessage request, ModelProviderSettings model)
        {
            if (request == null || model == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(model.ApiKey))
            {
                if (model.ProviderKind == ModelProviderKind.AzureOpenAI)
                {
                    request.Headers.Remove("api-key");
                    request.Headers.Add("api-key", model.ApiKey);
                }
                else if (model.ProviderKind == ModelProviderKind.Anthropic)
                {
                    request.Headers.Remove("x-api-key");
                    request.Headers.Add("x-api-key", model.ApiKey);
                }
                else
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", model.ApiKey);
                }
            }

            if (model.HttpHeaders == null)
            {
                return;
            }

            foreach (var header in model.HttpHeaders)
            {
                if (string.IsNullOrWhiteSpace(header.Key))
                {
                    continue;
                }

                request.Headers.Remove(header.Key);
                request.Headers.TryAddWithoutValidation(header.Key, header.Value ?? string.Empty);
            }
        }

        private static ModelProviderSettings CloneModel(ModelProviderSettings source)
        {
            if (source == null)
            {
                return new ModelProviderSettings();
            }

            return new ModelProviderSettings
            {
                Id = source.Id,
                Name = source.Name,
                SupportedInputFileTypes = source.SupportedInputFileTypes,
                IsEnabled = source.IsEnabled,
                ProviderKind = source.ProviderKind,
                Endpoint = source.Endpoint,
                ApiKey = source.ApiKey,
                ModelId = source.ModelId,
                MaxContextTokens = source.MaxContextTokens,
                SupportsImageInput = source.SupportsImageInput,
                SystemPrompt = source.SystemPrompt,
                AdditionalParameters = source.AdditionalParameters == null
                    ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, object>(source.AdditionalParameters, StringComparer.OrdinalIgnoreCase),
                HttpHeaders = source.HttpHeaders == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(source.HttpHeaders, StringComparer.OrdinalIgnoreCase)
            };
        }

        private sealed class NameValueRow
        {
            public string Name { get; set; }

            public string Value { get; set; }
        }

        private sealed class BasicParametersModel
        {
            [Category("连接")]
            [DisplayName("模型 ID")]
            public string ModelId { get; set; }

            [Category("连接")]
            [DisplayName("最大上下文长度")]
            public int MaxContextTokens { get; set; }

            [Category("连接")]
            [DisplayName("API 地址")]
            public string Endpoint { get; set; }

            [Category("连接")]
            [DisplayName("API 密钥")]
            [PasswordPropertyText(true)]
            public string ApiKey { get; set; }

            [Category("连接")]
            [DisplayName("API 协议")]
            public ModelProviderKind ProviderKind { get; set; }

            [Category("能力")]
            [DisplayName("支持多模态")]
            public bool SupportsImageInput { get; set; }

            [Category("能力")]
            [DisplayName("支持文件类型")]
            public string SupportedInputFileTypes { get; set; }

            public static BasicParametersModel FromModel(ModelProviderSettings model)
            {
                model = model ?? new ModelProviderSettings();
                return new BasicParametersModel
                {
                    ModelId = model.ModelId ?? string.Empty,
                    MaxContextTokens = model.MaxContextTokens > 0 ? model.MaxContextTokens : ModelProviderSettings.DefaultMaxContextTokens,
                    Endpoint = model.Endpoint ?? string.Empty,
                    ApiKey = model.ApiKey ?? string.Empty,
                    ProviderKind = model.ProviderKind,
                    SupportsImageInput = model.SupportsImageInput,
                    SupportedInputFileTypes = model.SupportedInputFileTypes ?? ModelProviderSettings.DefaultSupportedInputFileTypes
                };
            }

            public void ApplyTo(ModelProviderSettings model)
            {
                if (model == null)
                {
                    return;
                }

                model.ModelId = (ModelId ?? string.Empty).Trim();
                model.MaxContextTokens = MaxContextTokens > 0 ? MaxContextTokens : ModelProviderSettings.DefaultMaxContextTokens;
                model.Endpoint = (Endpoint ?? string.Empty).Trim();
                model.ApiKey = (ApiKey ?? string.Empty).Trim();
                model.ProviderKind = ProviderKind;
                model.SupportsImageInput = SupportsImageInput;
                model.SupportedInputFileTypes = string.IsNullOrWhiteSpace(SupportedInputFileTypes)
                    ? ModelProviderSettings.DefaultSupportedInputFileTypes
                    : SupportedInputFileTypes.Trim();
            }
        }
    }
}
