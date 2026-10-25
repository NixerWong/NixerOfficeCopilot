using System;
using DevExpress.XtraBars;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Excel.Tools;
using OfficeCopilot.Host.PowerPoint.Tools;
using OfficeCopilot.Host.Word.Tools;
using OfficeCopilot.Localization;
using OfficeCopilot.Settings.Abstractions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmToolEditor : HostAwareForm
    {
        private readonly ToolDefinition _baseDefinition;
        private readonly IServiceProvider _toolServices;
        private readonly string _toolHostApplicationName;
        private readonly bool _openTesterTab;
        private readonly bool _readOnly;
        private readonly Func<string, Task<ToolExecutionResult>> _customTestExecutor;
        private string _lastGeneratedArgumentsTemplate;

        public ToolDefinitionOverrideSettings DefinitionOverride { get; private set; }

        /// <summary>
        /// 非空时表示当前工具无法在本进程内执行（例如在 Excel 设置界面里打开 Word 工具），
        /// 定义仍可正常编辑保存，仅“调用测试”被禁用并给出该原因。
        /// </summary>
        public string TestBlockedReason { get; set; }

        public fmToolEditor()
            : this(null, null, null, null, false, false, null)
        {
        }

        public fmToolEditor(
            ToolDefinition baseDefinition,
            ToolDefinitionOverrideSettings currentOverride,
            IServiceProvider toolServices,
            string toolHostApplicationName,
            bool openTesterTab)
            : this(baseDefinition, currentOverride, toolServices, toolHostApplicationName, openTesterTab, false, null)
        {
        }

        public fmToolEditor(
            ToolDefinition baseDefinition,
            ToolDefinitionOverrideSettings currentOverride,
            IServiceProvider toolServices,
            string toolHostApplicationName,
            bool openTesterTab,
            bool readOnly,
            Func<string, Task<ToolExecutionResult>> customTestExecutor)
        {
            InitializeComponent();

            _baseDefinition = baseDefinition == null ? new ToolDefinition() : baseDefinition.Clone();
            _toolServices = toolServices;
            _toolHostApplicationName = toolHostApplicationName ?? string.Empty;
            _openTesterTab = openTesterTab;
            _readOnly = readOnly;
            _customTestExecutor = customTestExecutor;
            DefinitionOverride = CloneOverride(currentOverride);

            WireEvents();
            ApplyLocalization();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += fmToolEditor_Disposed;
        }

        private void WireEvents()
        {
            Load += fmToolEditor_Load;

            btnSave.ItemClick += btnSave_ItemClick;
            btnCancel.ItemClick += btnCancel_ItemClick;
            btnRunTest.ItemClick += btnRunTest_ItemClick;
            btnCheckSyntax.ItemClick += btnCheckSyntax_ItemClick;
            btnIndentFormat.ItemClick += btnIndentFormat_ItemClick;
            btnFormatIndent.ItemClick += btnFormatIndent_ItemClick;
            txtInputSchemaJson.EditValueChanged += txtInputSchemaJson_EditValueChanged;
        }

        private void fmToolEditor_Load(object sender, EventArgs e)
        {
            BindDefinitionToForm();

            if (_openTesterTab)
            {
                xtcToolEditor.SelectedTabPage = xtpToolTester;
            }

            ApplyReadOnlyMode();
            ApplyLocalization();

        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void fmToolEditor_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void ApplyLocalization()
        {
            var titleKey = _readOnly
                ? "Settings.Config.ToolEditor.ViewTitle"
                : "Settings.Config.ToolEditor.EditTitle";
            Text = string.IsNullOrWhiteSpace(_baseDefinition.DisplayName)
                ? LocalizationService.Get(titleKey)
                : LocalizationService.Format(titleKey + "WithName", _baseDefinition.DisplayName);
            xtpToolEditor.Text = LocalizationService.Get("Settings.Config.ToolEditor.DefinitionTab");
            xtpToolTester.Text = LocalizationService.Get("Settings.Config.ToolEditor.TestTab");
            btnRunTest.Caption = LocalizationService.Get("Settings.Config.ToolEditor.RunTest");
            btnSave.Caption = LocalizationService.Get("Settings.Config.Common.Save");
            btnCancel.Caption = LocalizationService.Get("Settings.Config.Common.Cancel");
            btnCheckSyntax.Caption = LocalizationService.Get("Settings.Config.ToolEditor.CheckSyntax");
            btnIndentFormat.Caption = LocalizationService.Get("Settings.Config.ToolEditor.FormatSchema");
            btnFormatIndent.Caption = LocalizationService.Get("Settings.Config.ToolEditor.FormatArguments");
            txtName.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.Tools.Name");
            txtDisplayName.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.ToolEditor.DisplayNamePrompt");
            txtDescription.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.Tools.Description");
        }

        private void BindDefinitionToForm()
        {
            var effectiveDefinition = _baseDefinition.Clone();
            if (DefinitionOverride != null)
            {
                effectiveDefinition.ApplyOverride(ToRuntimeOverride(DefinitionOverride));
            }

            txtName.Text = effectiveDefinition.Name ?? string.Empty;
            txtDisplayName.Text = effectiveDefinition.DisplayName ?? string.Empty;
            txtDescription.Text = effectiveDefinition.Description ?? string.Empty;
            txtInputSchemaJson.Text = FormatJsonOrOriginal(effectiveDefinition.InputSchemaJson, true);
            txtOutputSchemaJson.Text = FormatJsonOrOriginal(effectiveDefinition.OutputSchemaJson, false);
            txtOutput.Text = string.Empty;
            ApplyGeneratedArgumentsTemplate(true);

            SetInfoText(LocalizationService.Get("Settings.Config.ToolEditor.Loaded"));
        }

        private void txtInputSchemaJson_EditValueChanged(object sender, EventArgs e)
        {
            ApplyGeneratedArgumentsTemplate(false);
        }

        private void btnSave_ItemClick(object sender, ItemClickEventArgs e)
        {
            string validationMessage;
            if (!ValidateDefinitionInputs(out validationMessage))
            {
                MessageBox.Show(this, validationMessage, LocalizationService.Get("Settings.Config.Tab.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DefinitionOverride = BuildOverrideFromForm();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_ItemClick(object sender, ItemClickEventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void btnCheckSyntax_ItemClick(object sender, ItemClickEventArgs e)
        {
            string validationMessage;
            if (!ValidateDefinitionInputs(out validationMessage))
            {
                MessageBox.Show(this, validationMessage, LocalizationService.Get("Settings.Config.Tab.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetInfoText(LocalizationService.Get("Settings.Config.ToolEditor.SyntaxCheckFailed"));
                return;
            }

            MessageBox.Show(this, LocalizationService.Get("Settings.Config.ToolEditor.SyntaxCheckPassed"), LocalizationService.Get("Settings.Config.Tab.Tools"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetInfoText(LocalizationService.Get("Settings.Config.ToolEditor.SyntaxCheckPassed"));
        }

        private void btnIndentFormat_ItemClick(object sender, ItemClickEventArgs e)
        {
            txtInputSchemaJson.Text = FormatJsonOrOriginal(txtInputSchemaJson.Text, true);
            txtOutputSchemaJson.Text = FormatJsonOrOriginal(txtOutputSchemaJson.Text, false);
            SetInfoText(LocalizationService.Get("Settings.Config.ToolEditor.SchemaFormatted"));
        }

        private void btnFormatIndent_ItemClick(object sender, ItemClickEventArgs e)
        {
            txtArguments.Text = FormatJsonOrOriginal(txtArguments.Text, true);
            SetInfoText(LocalizationService.Get("Settings.Config.ToolEditor.ArgumentsFormatted"));
        }

        private async void btnRunTest_ItemClick(object sender, ItemClickEventArgs e)
        {
            string validationMessage;
            if (!ValidateDefinitionInputs(out validationMessage))
            {
                MessageBox.Show(this, validationMessage, LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateJsonText(txtArguments.Text, true, out validationMessage))
            {
                MessageBox.Show(this, validationMessage, LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_customTestExecutor == null && !string.IsNullOrWhiteSpace(TestBlockedReason))
            {
                MessageBox.Show(this, TestBlockedReason, LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_customTestExecutor == null && _toolServices == null)
            {
                MessageBox.Show(this, LocalizationService.Get("Settings.Config.ToolEditor.TestUnavailable"), LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnRunTest.Enabled = false;
            txtOutput.Text = string.Empty;
            SetInfoText(LocalizationService.Get("Settings.Config.ToolEditor.TestRunning"));

            try
            {
                var result = await ExecuteToolTestAsync();
                txtOutput.Text = FormatResult(result);
                xtcToolEditor.SelectedTabPage = xtpToolTester;
                SetInfoText(LocalizationService.Get(result != null && result.Success
                    ? "Settings.Config.ToolEditor.TestSucceeded"
                    : "Settings.Config.ToolEditor.TestCompleted"));
            }
            catch (Exception ex)
            {
                txtOutput.Text = ex.ToString();
                SetInfoText(LocalizationService.Get("Settings.Config.ToolEditor.TestFailed"));
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.ToolEditor.Error.TestFailed", ex.Message), LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnRunTest.Enabled = true;
            }
        }

        private async Task<ToolExecutionResult> ExecuteToolTestAsync()
        {
            if (_customTestExecutor != null)
            {
                return await _customTestExecutor(string.IsNullOrWhiteSpace(txtArguments.Text) ? "{}" : txtArguments.Text.Trim());
            }

            var definitionOverride = BuildRuntimeOverrideFromForm();
            IAgentTool[] tools;

            if (string.Equals(_toolHostApplicationName, "Word", StringComparison.OrdinalIgnoreCase))
            {
                var options = new WordToolCatalogOptions();
                if (definitionOverride != null)
                {
                    options.DefinitionOverrides.Add(definitionOverride);
                }

                tools = WordBuiltInToolCatalog.CreateAllAvailableTools(options);
            }
            else if (string.Equals(_toolHostApplicationName, "PowerPoint", StringComparison.OrdinalIgnoreCase))
            {
                var options = new PowerPointToolCatalogOptions();
                if (definitionOverride != null)
                {
                    options.DefinitionOverrides.Add(definitionOverride);
                }

                tools = PowerPointBuiltInTools.CreateAllAvailableTools(options);
            }
            else
            {
                var options = new ExcelToolCatalogOptions();
                if (definitionOverride != null)
                {
                    options.DefinitionOverrides.Add(definitionOverride);
                }

                tools = ExcelBuiltInTools.CreateAllAvailableTools(options);
            }

            var tool = tools.FirstOrDefault(p =>
                p != null
                && p.Definition != null
                && string.Equals(p.Definition.Name, _baseDefinition.Name, StringComparison.OrdinalIgnoreCase));

            if (tool == null)
            {
                throw new InvalidOperationException(LocalizationService.Get("Settings.Config.ToolEditor.RuntimeInstanceMissing"));
            }

            var context = new ToolExecutionContext
            {
                HostApplicationName = _toolHostApplicationName,
                Services = _toolServices
            };

            var request = new ToolExecutionRequest
            {
                ToolCallId = Guid.NewGuid().ToString("N"),
                ToolName = tool.Definition.Name,
                ArgumentsJson = string.IsNullOrWhiteSpace(txtArguments.Text) ? "{}" : txtArguments.Text.Trim()
            };

            return await tool.ExecuteAsync(context, request);
        }

        private void ApplyReadOnlyMode()
        {
            if (!_readOnly)
            {
                return;
            }

            txtName.Properties.ReadOnly = true;
            txtDisplayName.Properties.ReadOnly = true;
            txtDescription.Properties.ReadOnly = true;
            txtInputSchemaJson.Properties.ReadOnly = true;
            txtOutputSchemaJson.Properties.ReadOnly = true;
            btnSave.Visibility = BarItemVisibility.Never;
        }

        private bool ValidateDefinitionInputs(out string validationMessage)
        {
            validationMessage = string.Empty;

            if (!ValidateJsonText(txtInputSchemaJson.Text, true, out validationMessage))
            {
                return false;
            }

            if (!ValidateJsonText(txtOutputSchemaJson.Text, false, out validationMessage))
            {
                return false;
            }

            return true;
        }

        private void ApplyGeneratedArgumentsTemplate(bool force)
        {
            var template = BuildArgumentsTemplateText(txtInputSchemaJson.Text);
            var currentValue = txtArguments.Text ?? string.Empty;
            if (force
                || string.IsNullOrWhiteSpace(currentValue)
                || string.Equals(currentValue.Trim(), "{}", StringComparison.Ordinal)
                || string.Equals(currentValue, _lastGeneratedArgumentsTemplate, StringComparison.Ordinal))
            {
                txtArguments.Text = template;
            }

            _lastGeneratedArgumentsTemplate = template;
        }

        private static bool ValidateJsonText(string json, bool required, out string validationMessage)
        {
            validationMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                if (required)
                {
                    validationMessage = LocalizationService.Get("Settings.Config.ToolEditor.Validation.JsonRequired");
                    return false;
                }

                return true;
            }

            try
            {
                JToken.Parse(json);
                return true;
            }
            catch (Exception ex)
            {
                validationMessage = LocalizationService.Format("Settings.Config.ToolEditor.Validation.JsonInvalid", ex.Message);
                return false;
            }
        }

        private static string BuildArgumentsTemplateText(string inputSchemaJson)
        {
            if (string.IsNullOrWhiteSpace(inputSchemaJson))
            {
                return "{}";
            }

            try
            {
                var schema = JToken.Parse(inputSchemaJson);
                var sample = BuildSampleToken(schema, null);
                if (sample == null || sample.Type == JTokenType.Null)
                {
                    return "{}";
                }

                return sample.ToString(Formatting.Indented);
            }
            catch
            {
                return "{}";
            }
        }

        private static JToken BuildSampleToken(JToken schema, string propertyName)
        {
            if (schema == null)
            {
                return CreateStringToken(propertyName, null);
            }

            var explicitValue = GetExplicitSchemaValue(schema);
            if (explicitValue != null)
            {
                return explicitValue;
            }

            var schemaType = GetSchemaType(schema);
            if (string.IsNullOrWhiteSpace(schemaType))
            {
                if (schema["properties"] != null)
                {
                    schemaType = "object";
                }
                else if (schema["items"] != null)
                {
                    schemaType = "array";
                }
                else
                {
                    schemaType = "string";
                }
            }

            switch (schemaType)
            {
                case "object":
                    return BuildSampleObject(schema as JObject);
                case "array":
                    return BuildSampleArray(schema, propertyName);
                case "integer":
                    return new JValue(Convert.ToInt32(GetNumericSample(schema, propertyName)));
                case "number":
                    return new JValue(GetNumericSample(schema, propertyName));
                case "boolean":
                    return new JValue(GetBooleanSample(propertyName));
                case "string":
                default:
                    return CreateStringToken(propertyName, schema);
            }
        }

        private static JToken GetExplicitSchemaValue(JToken schema)
        {
            var constToken = schema["const"];
            if (constToken != null)
            {
                return constToken.DeepClone();
            }

            var defaultToken = schema["default"];
            if (defaultToken != null)
            {
                return defaultToken.DeepClone();
            }

            var examplesToken = schema["examples"] as JArray;
            if (examplesToken != null && examplesToken.Count > 0)
            {
                return examplesToken[0].DeepClone();
            }

            var enumToken = schema["enum"] as JArray;
            if (enumToken != null && enumToken.Count > 0)
            {
                return enumToken[0].DeepClone();
            }

            return null;
        }

        private static string GetSchemaType(JToken schema)
        {
            if (schema == null)
            {
                return string.Empty;
            }

            var typeToken = schema["type"];
            if (typeToken == null)
            {
                return string.Empty;
            }

            if (typeToken.Type == JTokenType.String)
            {
                return typeToken.Value<string>() ?? string.Empty;
            }

            var typeArray = typeToken as JArray;
            if (typeArray == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < typeArray.Count; i++)
            {
                var candidate = typeArray[i] == null ? string.Empty : typeArray[i].Value<string>();
                if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "null", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static JObject BuildSampleObject(JObject schema)
        {
            var result = new JObject();
            if (schema == null)
            {
                return result;
            }

            var properties = schema["properties"] as JObject;
            if (properties == null)
            {
                return result;
            }

            var requiredNames = (schema["required"] as JArray)
                ?.Select(p => p == null ? string.Empty : p.Value<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList() ?? new System.Collections.Generic.List<string>();

            foreach (var name in requiredNames)
            {
                JToken propertySchema;
                if (!properties.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out propertySchema))
                {
                    continue;
                }

                result[name] = BuildSampleToken(propertySchema, name);
            }

            foreach (var property in properties.Properties())
            {
                if (result.Property(property.Name) != null)
                {
                    continue;
                }

                result[property.Name] = BuildSampleToken(property.Value, property.Name);
            }

            return result;
        }

        private static JArray BuildSampleArray(JToken schema, string propertyName)
        {
            var result = new JArray();
            if (schema == null)
            {
                return result;
            }

            var itemsSchema = schema["items"];
            if (itemsSchema == null)
            {
                result.Add(CreateStringToken(propertyName, null));
                return result;
            }

            result.Add(BuildSampleToken(itemsSchema, propertyName));
            return result;
        }

        private static JValue CreateStringToken(string propertyName, JToken schema)
        {
            var normalizedName = NormalizePropertyName(propertyName);
            var format = schema == null ? string.Empty : (schema["format"] == null ? string.Empty : schema["format"].Value<string>() ?? string.Empty);

            if (string.Equals(format, "date-time", StringComparison.OrdinalIgnoreCase))
            {
                return new JValue(DateTime.UtcNow.ToString("o"));
            }

            if (string.Equals(format, "date", StringComparison.OrdinalIgnoreCase))
            {
                return new JValue(DateTime.Today.ToString("yyyy-MM-dd"));
            }

            if (string.Equals(format, "uri", StringComparison.OrdinalIgnoreCase))
            {
                return new JValue("https://example.com");
            }

            switch (normalizedName)
            {
                case "worksheetname":
                    return new JValue("Sheet1");
                case "address":
                    return new JValue("A1");
                case "value":
                    return new JValue(LocalizationService.Get("Settings.Config.ToolEditor.Json.SampleValue"));
                case "displayname":
                    return new JValue(LocalizationService.Get("Settings.Config.ToolEditor.Json.SampleDisplayName"));
                case "description":
                    return new JValue(LocalizationService.Get("Settings.Config.ToolEditor.Json.SampleDescription"));
                case "code":
                    return new JValue("Sub Main()\r\n    MsgBox \"Hello\"\r\nEnd Sub");
                case "modulename":
                    return new JValue("Module1");
                case "entrypoint":
                    return new JValue("Main");
                default:
                    return new JValue(string.IsNullOrWhiteSpace(propertyName)
                        ? LocalizationService.Get("Settings.Config.ToolEditor.Json.SampleText")
                        : LocalizationService.Format("Settings.Config.ToolEditor.Json.SamplePrefix", propertyName));
            }
        }

        private static double GetNumericSample(JToken schema, string propertyName)
        {
            var minimumToken = schema == null ? null : schema["minimum"];
            if (minimumToken != null)
            {
                double minimum;
                if (double.TryParse(minimumToken.ToString(), out minimum))
                {
                    return minimum;
                }
            }

            var normalizedName = NormalizePropertyName(propertyName);
            if (normalizedName.Contains("row") || normalizedName.Contains("column") || normalizedName.Contains("count"))
            {
                return 10d;
            }

            return 1d;
        }

        private static bool GetBooleanSample(string propertyName)
        {
            var normalizedName = NormalizePropertyName(propertyName);
            if (normalizedName.Contains("useselection") || normalizedName.Contains("remove"))
            {
                return true;
            }

            return false;
        }

        private static string NormalizePropertyName(string propertyName)
        {
            return string.IsNullOrWhiteSpace(propertyName)
                ? string.Empty
                : propertyName.Replace("_", string.Empty).Trim().ToLowerInvariant();
        }

        private ToolDefinitionOverrideSettings BuildOverrideFromForm()
        {
            var runtimeOverride = BuildRuntimeOverrideFromForm();
            if (runtimeOverride == null)
            {
                return null;
            }

            return new ToolDefinitionOverrideSettings
            {
                ToolName = runtimeOverride.ToolName,
                DisplayName = runtimeOverride.DisplayName,
                Description = runtimeOverride.Description,
                InputSchemaJson = runtimeOverride.InputSchemaJson,
                OutputSchemaJson = runtimeOverride.OutputSchemaJson,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        private ToolDefinitionOverride BuildRuntimeOverrideFromForm()
        {
            var displayName = NormalizeText(txtDisplayName.Text);
            var description = NormalizeText(txtDescription.Text);
            var inputSchemaJson = NormalizeJsonForSave(txtInputSchemaJson.Text, true);
            var outputSchemaJson = NormalizeJsonForSave(txtOutputSchemaJson.Text, false);

            var hasOverride = !string.Equals(displayName, NormalizeText(_baseDefinition.DisplayName), StringComparison.Ordinal)
                || !string.Equals(description, NormalizeText(_baseDefinition.Description), StringComparison.Ordinal)
                || !string.Equals(inputSchemaJson, NormalizeJsonForSave(_baseDefinition.InputSchemaJson, true), StringComparison.Ordinal)
                || !string.Equals(outputSchemaJson, NormalizeJsonForSave(_baseDefinition.OutputSchemaJson, false), StringComparison.Ordinal);

            if (!hasOverride)
            {
                return null;
            }

            return new ToolDefinitionOverride
            {
                ToolName = _baseDefinition.Name ?? string.Empty,
                DisplayName = displayName,
                Description = description,
                InputSchemaJson = inputSchemaJson,
                OutputSchemaJson = outputSchemaJson,
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeJsonForSave(string json, bool required)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return required ? "{}" : string.Empty;
            }

            try
            {
                return JToken.Parse(json).ToString(Formatting.None);
            }
            catch
            {
                return json.Trim();
            }
        }

        private static string FormatJsonOrOriginal(string json, bool required)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return required ? "{}" : string.Empty;
            }

            try
            {
                return JToken.Parse(json).ToString(Formatting.Indented);
            }
            catch
            {
                return json;
            }
        }

        private static ToolDefinitionOverrideSettings CloneOverride(ToolDefinitionOverrideSettings definitionOverride)
        {
            if (definitionOverride == null)
            {
                return null;
            }

            return new ToolDefinitionOverrideSettings
            {
                ToolName = definitionOverride.ToolName,
                DisplayName = definitionOverride.DisplayName,
                Description = definitionOverride.Description,
                RequiresConfirmation = definitionOverride.RequiresConfirmation,
                RequiresBackup = definitionOverride.RequiresBackup,
                InputSchemaJson = definitionOverride.InputSchemaJson,
                OutputSchemaJson = definitionOverride.OutputSchemaJson,
                UpdatedAtUtc = definitionOverride.UpdatedAtUtc
            };
        }

        private static ToolDefinitionOverride ToRuntimeOverride(ToolDefinitionOverrideSettings definitionOverride)
        {
            if (definitionOverride == null)
            {
                return null;
            }

            return new ToolDefinitionOverride
            {
                ToolName = definitionOverride.ToolName,
                DisplayName = definitionOverride.DisplayName,
                Description = definitionOverride.Description,
                RequiresConfirmation = definitionOverride.RequiresConfirmation,
                RequiresBackup = definitionOverride.RequiresBackup,
                InputSchemaJson = definitionOverride.InputSchemaJson,
                OutputSchemaJson = definitionOverride.OutputSchemaJson,
                UpdatedAtUtc = definitionOverride.UpdatedAtUtc
            };
        }

        private static string FormatResult(ToolExecutionResult result)
        {
            if (result == null)
            {
                return "<null>";
            }

            var builder = new StringBuilder();
            builder.AppendLine("Success: " + (result.Success ? "true" : "false"));
            builder.AppendLine("ToolName: " + (result.ToolName ?? string.Empty));
            builder.AppendLine("ToolCallId: " + (result.ToolCallId ?? string.Empty));

            if (!string.IsNullOrWhiteSpace(result.ErrorCode))
            {
                builder.AppendLine("ErrorCode: " + result.ErrorCode);
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                builder.AppendLine("ErrorMessage: " + result.ErrorMessage);
            }

            builder.AppendLine();
            builder.AppendLine("Content:");
            builder.AppendLine(result.Content ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(result.StructuredContentJson))
            {
                builder.AppendLine();
                builder.AppendLine("StructuredContentJson:");
                builder.AppendLine(FormatJsonOrOriginal(result.StructuredContentJson, false));
            }

            return builder.ToString();
        }

        private void SetInfoText(string message)
        {
            bsiInformation.Caption = message ?? string.Empty;
        }
    }
}
