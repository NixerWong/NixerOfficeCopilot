using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils.Menu;
using OfficeCopilot.Localization;
using OfficeCopilot.Settings;
using OfficeCopilot.Settings.Abstractions;

namespace OfficeCopilot.Settings.WinForms
{
    public partial class fmMcpEditor : HostAwareForm
    {
        private BindingList<NameValueRow> _httpHeaderRows;
        private BindingList<NameValueRow> _environmentVariableRows;
        private StdioParametersModel _stdioParameters;
        private HttpParametersModel _httpParameters;

        public McpServerSettings McpServer { get; private set; }

        public fmMcpEditor()
            : this(null)
        {
        }

        private sealed class LocalizedDisplayNameAttribute : DisplayNameAttribute
        {
            private readonly string _key;

            public LocalizedDisplayNameAttribute(string key)
            {
                _key = key;
            }

            public override string DisplayName => LocalizationService.Get(_key);
        }

        public fmMcpEditor(McpServerSettings server)
        {
            InitializeComponent();

            McpServer = CreateEditableServer(server);
            WireEvents();
            ApplyLocalization();
            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            Disposed += fmMcpEditor_Disposed;
        }

        private static McpServerSettings CreateEditableServer(McpServerSettings server)
        {
            var editableServer = OfficeCopilotSettingsFactory.NormalizeMcpServer(CloneServer(server));
            if (server != null)
            {
                return editableServer;
            }

            editableServer.UseHttp();
            if (editableServer.Http == null)
            {
                editableServer.Http = new HttpMcpServerSettings();
            }

            editableServer.Http.TransportMode = HttpTransportMode.AutoDetect;
            return OfficeCopilotSettingsFactory.NormalizeMcpServer(editableServer);
        }

        private void WireEvents()
        {
            Load += fmMcpEditor_Load;
            cmbMcpType.SelectedIndexChanged += cmbMcpType_SelectedIndexChanged;
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

        private void fmMcpEditor_Load(object sender, EventArgs e)
        {
            InitializeEditor();
            BindServerToForm();
            ApplyLocalization();
        }

        private void InitializeEditor()
        {
            pgParameters.OptionsView.ShowRootCategories = false;

            cmbMcpType.Properties.Items.Clear();
            cmbMcpType.Properties.Items.AddRange(new object[] { "Stdio", "HTTP" });
            cmbMcpType.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
        }

        private void LocalizationService_LanguageChanged(object sender, EventArgs e)
        {
            ApplyLocalization();
        }

        private void fmMcpEditor_Disposed(object sender, EventArgs e)
        {
            LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        }

        private void ApplyLocalization()
        {
            Text = LocalizationService.Get(string.IsNullOrWhiteSpace(McpServer.Name)
                ? "Settings.Config.Mcp.Add"
                : "Settings.Config.Mcp.Edit");
            txtMcpName.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.Mcp.Name");
            txtDesc.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.Mcp.Description");
            cmbMcpType.Properties.NullValuePrompt = LocalizationService.Get("Settings.Config.McpEditor.TypePrompt");
            btnTest.Caption = LocalizationService.Get("Settings.Config.ModelEditor.TestConnection");
            btnSave.Caption = LocalizationService.Get("Settings.Config.Common.Save");
            btnCancel.Caption = LocalizationService.Get("Settings.Config.Common.Cancel");
            if (gvHttpHeaders != null && gvHttpHeaders.Columns["Name"] != null && gvHttpHeaders.Columns["Value"] != null)
            {
                gvHttpHeaders.Columns["Name"].Caption = cmbMcpType.SelectedIndex == 1
                    ? LocalizationService.Get("Settings.Config.McpEditor.Headers")
                    : LocalizationService.Get("Settings.Config.McpEditor.EnvironmentVariables");
                gvHttpHeaders.Columns["Value"].Caption = LocalizationService.Get("Settings.Config.McpEditor.Value");
            }
            if (pgParameters != null) pgParameters.Refresh();
        }

        private void BindServerToForm()
        {
            txtMcpName.Text = McpServer.Name ?? string.Empty;
            txtDesc.Text = McpServer.Description ?? string.Empty;

            _stdioParameters = StdioParametersModel.FromServer(McpServer.Stdio);
            _httpParameters = HttpParametersModel.FromServer(McpServer.Http);

            _environmentVariableRows = new BindingList<NameValueRow>(ToNameValueRows(McpServer.Stdio == null ? null : McpServer.Stdio.EnvironmentVariables));
            _httpHeaderRows = new BindingList<NameValueRow>(ToNameValueRows(McpServer.Http == null ? null : McpServer.Http.Headers));

            SetSelectedServerType(McpServer.McpType);
        }

        private void SetSelectedServerType(McpServerType serverType)
        {
            var selectedIndex = serverType == McpServerType.Http ? 1 : 0;
            if (cmbMcpType.SelectedIndex != selectedIndex)
            {
                cmbMcpType.SelectedIndex = selectedIndex;
                return;
            }

            ApplySelectedServerType();
        }

        private McpServerType GetSelectedServerType()
        {
            return cmbMcpType.SelectedIndex == 1 ? McpServerType.Http : McpServerType.Stdio;
        }

        private void ApplySelectedServerType()
        {
            CloseEditors();

            var serverType = GetSelectedServerType();
            var isHttp = serverType == McpServerType.Http;

            pgParameters.SelectedObject = isHttp
                ? (object)_httpParameters
                : _stdioParameters;

            gcParameters.DataSource = isHttp
                ? (object)_httpHeaderRows
                : _environmentVariableRows;

            ConfigureNameValueGrid(
                gvHttpHeaders,
                isHttp ? LocalizationService.Get("Settings.Config.McpEditor.Headers") : LocalizationService.Get("Settings.Config.McpEditor.EnvironmentVariables"),
                LocalizationService.Get("Settings.Config.McpEditor.Value"));

            gcParameters.RefreshDataSource();
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
            nameColumn.Width = 160;

            var valueColumn = view.Columns.AddVisible("Value", valueCaption);
            valueColumn.OptionsColumn.AllowSort = DevExpress.Utils.DefaultBoolean.False;
            valueColumn.VisibleIndex = 1;
            valueColumn.Width = 260;
        }

        private void cmbMcpType_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplySelectedServerType();
        }

        private void btnSave_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            string validationMessage;
            if (!ValidateInputs(out validationMessage))
            {
                MessageBox.Show(this, validationMessage, LocalizationService.Get("Settings.Config.DialogTitle.Mcp"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            McpServer = BuildServerFromForm();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnCancel_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private async void btnTest_ItemClick(object sender, DevExpress.XtraBars.ItemClickEventArgs e)
        {
            string validationMessage;
            if (!ValidateInputs(out validationMessage))
            {
                MessageBox.Show(this, validationMessage, LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var server = BuildServerFromForm();
            btnTest.Enabled = false;

            try
            {
                if (server.McpType == McpServerType.Http)
                {
                    await TestHttpServerAsync(server);
                }
                else
                {
                    await TestStdioServerAsync(server);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, LocalizationService.Format("Settings.Config.McpEditor.Error.TestFailed", ex.Message), LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTest.Enabled = true;
            }
        }

        private bool ValidateInputs(out string message)
        {
            CloseEditors();

            if (string.IsNullOrWhiteSpace(txtMcpName.Text))
            {
                message = LocalizationService.Get("Settings.Config.McpEditor.Validation.NameRequired");
                return false;
            }

            if (GetSelectedServerType() == McpServerType.Http)
            {
                if (_httpParameters == null)
                {
                    message = LocalizationService.Get("Settings.Config.McpEditor.Validation.HttpParametersMissing");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(_httpParameters.Endpoint))
                {
                    message = LocalizationService.Get("Settings.Config.McpEditor.Validation.EndpointRequired");
                    return false;
                }

                Uri endpointUri;
                if (!Uri.TryCreate(_httpParameters.Endpoint.Trim(), UriKind.Absolute, out endpointUri)
                    || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
                {
                    message = LocalizationService.Get("Settings.Config.McpEditor.Validation.EndpointInvalid");
                    return false;
                }

                List<NameValueItem> headers;
                if (!TryBuildNameValueItems(_httpHeaderRows, LocalizationService.Get("Settings.Config.McpEditor.Headers"), out headers, out message))
                {
                    return false;
                }
            }
            else
            {
                if (_stdioParameters == null)
                {
                    message = LocalizationService.Get("Settings.Config.McpEditor.Validation.StdioParametersMissing");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(_stdioParameters.Command))
                {
                    message = LocalizationService.Get("Settings.Config.McpEditor.Validation.CommandRequired");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(_stdioParameters.WorkingDirectory)
                    && !System.IO.Directory.Exists(_stdioParameters.WorkingDirectory.Trim()))
                {
                    message = LocalizationService.Get("Settings.Config.McpEditor.Validation.WorkingDirectoryMissing");
                    return false;
                }

                List<NameValueItem> environmentVariables;
                if (!TryBuildNameValueItems(_environmentVariableRows, LocalizationService.Get("Settings.Config.McpEditor.EnvironmentVariables"), out environmentVariables, out message))
                {
                    return false;
                }
            }

            message = null;
            return true;
        }

        private void CloseEditors()
        {
            if (pgParameters != null)
            {
                pgParameters.PostEditor();
                pgParameters.CloseEditor();
            }

            if (gvHttpHeaders != null)
            {
                gvHttpHeaders.CloseEditor();
                gvHttpHeaders.UpdateCurrentRow();
            }
        }

        private McpServerSettings BuildServerFromForm()
        {
            var server = CloneServer(McpServer);
            server.Name = (txtMcpName.Text ?? string.Empty).Trim();
            server.Description = txtDesc.Text ?? string.Empty;

            if (GetSelectedServerType() == McpServerType.Http)
            {
                server.UseHttp();
                _httpParameters.ApplyTo(server.Http);

                List<NameValueItem> headers;
                string message;
                if (!TryBuildNameValueItems(_httpHeaderRows, LocalizationService.Get("Settings.Config.McpEditor.Headers"), out headers, out message))
                {
                    throw new InvalidOperationException(message);
                }

                server.Http.Headers = headers;
                server.Stdio = null;
            }
            else
            {
                server.UseStdio();
                _stdioParameters.ApplyTo(server.Stdio);

                List<NameValueItem> environmentVariables;
                string message;
                if (!TryBuildNameValueItems(_environmentVariableRows, LocalizationService.Get("Settings.Config.McpEditor.EnvironmentVariables"), out environmentVariables, out message))
                {
                    throw new InvalidOperationException(message);
                }

                server.Stdio.EnvironmentVariables = environmentVariables;
                server.Http = null;
            }

            return OfficeCopilotSettingsFactory.NormalizeMcpServer(server);
        }

        private async System.Threading.Tasks.Task TestHttpServerAsync(McpServerSettings server)
        {
            // .NET Framework 进程的默认协议可能不含 TLS 1.2，握手会失败，这里补上。
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);

                var endpoint = server.Http.Endpoint.TrimEnd('/');
                string sessionId = null;

                // Step 1: initialize
                var initId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var initBody = BuildJsonRpcRequest(initId, "initialize", new Dictionary<string, object>
                {
                    ["protocolVersion"] = "2024-11-05",
                    ["capabilities"] = new Dictionary<string, object>(),
                    ["clientInfo"] = new Dictionary<string, object>
                    {
                        ["name"] = "OfficeCopilot-McpTester",
                        ["version"] = "1.0"
                    }
                });

                var initReply = await PostJsonRpcAsync(client, endpoint, initBody, server.Http.Headers, initId, null);
                sessionId = initReply.SessionId;
                var initResult = ParseJsonRpcResult(initReply.Body);
                if (initResult.error != null)
                {
                    throw new InvalidOperationException(LocalizationService.Format("Settings.Config.McpEditor.Error.Initialize", initResult.error));
                }

                string serverName = LocalizationService.Get("Settings.Config.McpEditor.UnknownServer");
                string serverVersion = "?";
                var initObj = initResult.result as JObject;
                if (initObj != null)
                {
                    var si = initObj["serverInfo"] as JObject;
                    if (si != null)
                    {
                        serverName = (string)si["name"] ?? "?";
                        serverVersion = (string)si["version"] ?? "?";
                    }
                }

                // 发送 initialized 通知
                var initNotif = BuildJsonRpcNotification("notifications/initialized", null);
                await PostJsonRpcAsync(client, endpoint, initNotif, server.Http.Headers, null, sessionId);

                // Step 2: tools/list
                var listId = Guid.NewGuid().ToString("N").Substring(0, 8);
                var listBody = BuildJsonRpcRequest(listId, "tools/list", null);

                var listReply = await PostJsonRpcAsync(client, endpoint, listBody, server.Http.Headers, listId, sessionId);
                var listResult = ParseJsonRpcResult(listReply.Body);
                if (listResult.error != null)
                {
                    throw new InvalidOperationException(LocalizationService.Format("Settings.Config.McpEditor.Error.ListTools", listResult.error));
                }

                var toolNames = new List<string>();
                int toolCount = 0;
                var listObj = listResult.result as JObject;
                if (listObj != null)
                {
                    var toolsArr = listObj["tools"] as JArray;
                    if (toolsArr != null)
                    {
                        foreach (JObject tool in toolsArr)
                        {
                            var tn = (string)tool["name"];
                            if (!string.IsNullOrEmpty(tn))
                                toolNames.Add(tn);
                        }
                        toolCount = toolNames.Count;
                    }
                }

                var resultMsg = LocalizationService.Format("Settings.Config.McpEditor.TestSuccess",
                    serverName, serverVersion, toolCount, "2024-11-05") + "\n"
                    + FormatToolList(toolNames);

                MessageBox.Show(
                    this,
                    resultMsg,
                    LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private async System.Threading.Tasks.Task TestStdioServerAsync(McpServerSettings server)
        {
            var command = server.Stdio == null ? string.Empty : server.Stdio.Command ?? string.Empty;
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new InvalidOperationException(LocalizationService.Get("Settings.Config.McpEditor.Validation.CommandRequired"));
            }

            var workingDirectory = server.Stdio == null ? string.Empty : server.Stdio.WorkingDirectory ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(workingDirectory) && !System.IO.Directory.Exists(workingDirectory))
            {
                throw new InvalidOperationException(LocalizationService.Get("Settings.Config.McpEditor.Validation.WorkingDirectoryMissing"));
            }

            var args = server.Stdio?.Arguments != null
                ? string.Join(" ", server.Stdio.Arguments.Where(a => !string.IsNullOrWhiteSpace(a)))
                : string.Empty;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OfficeCopilot.Agent.Abstractions.McpCommandResolver.Resolve(command, workingDirectory),
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            if (server.Stdio?.EnvironmentVariables != null)
            {
                foreach (var env in server.Stdio.EnvironmentVariables)
                {
                    if (env != null && !string.IsNullOrWhiteSpace(env.Name))
                        psi.Environment[env.Name] = env.Value ?? string.Empty;
                }
            }

            using (var process = System.Diagnostics.Process.Start(psi))
            {
                if (process == null)
                {
                    throw new InvalidOperationException(LocalizationService.Get("Settings.Config.McpEditor.Error.ProcessStartFailed"));
                }

                // 异步读取 stderr（避免阻塞）
                var stderrTask = process.StandardError.ReadToEndAsync();

                try
                {
                    // Step 1: 发送 initialize 请求
                    var initId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    var initRequest = BuildJsonRpcRequest(initId, "initialize", new Dictionary<string, object>
                    {
                        ["protocolVersion"] = "2024-11-05",
                        ["capabilities"] = new Dictionary<string, object>(),
                        ["clientInfo"] = new Dictionary<string, object>
                        {
                            ["name"] = "OfficeCopilot-McpTester",
                            ["version"] = "1.0"
                        }
                    });

                    await process.StandardInput.WriteLineAsync(initRequest);
                    await process.StandardInput.FlushAsync();

                    // 读取 initialize 响应（超时 15 秒）
                    var initResponse = await ReadJsonRpcResponseAsync(process, initId, timeoutMs: 15000);
                    if (initResponse == null)
                    {
                        throw new InvalidOperationException(LocalizationService.Get("Settings.Config.McpEditor.Error.InitializeTimeout"));
                    }

                    var initResult = ParseJsonRpcResult(initResponse);
                    if (initResult.error != null)
                    {
                        throw new InvalidOperationException(LocalizationService.Format("Settings.Config.McpEditor.Error.Initialize", initResult.error));
                    }

                    // 提取 serverInfo
                    string serverName = LocalizationService.Get("Settings.Config.McpEditor.UnknownServer");
                    string serverVersion = "?";
                    var initObj = initResult.result as JObject;
                    if (initObj != null)
                    {
                        var si = initObj["serverInfo"] as JObject;
                        if (si != null)
                        {
                            serverName = (string)si["name"] ?? "?";
                            serverVersion = (string)si["version"] ?? "?";
                        }
                    }

                    // 发送 initialized 通知（MCP 协议要求）
                    var initializedNotification = BuildJsonRpcNotification("notifications/initialized", null);
                    await process.StandardInput.WriteLineAsync(initializedNotification);
                    await process.StandardInput.FlushAsync();

                    // Step 2: 发送 tools/list
                    var listId = Guid.NewGuid().ToString("N").Substring(0, 8);
                    var listRequest = BuildJsonRpcRequest(listId, "tools/list", null);

                    await process.StandardInput.WriteLineAsync(listRequest);
                    await process.StandardInput.FlushAsync();

                    var listResponse = await ReadJsonRpcResponseAsync(process, listId, timeoutMs: 15000);
                    if (listResponse == null)
                    {
                        throw new InvalidOperationException(LocalizationService.Get("Settings.Config.McpEditor.Error.ListToolsTimeout"));
                    }

                    var listResult = ParseJsonRpcResult(listResponse);
                    if (listResult.error != null)
                    {
                        throw new InvalidOperationException(LocalizationService.Format("Settings.Config.McpEditor.Error.ListTools", listResult.error));
                    }

                    // 提取工具列表
                    var toolNames = new List<string>();
                    int toolCount = 0;
                    var listObj = listResult.result as JObject;
                    if (listObj != null)
                    {
                        var toolsArr = listObj["tools"] as JArray;
                        if (toolsArr != null)
                        {
                            foreach (JObject tool in toolsArr)
                            {
                                var tn = (string)tool["name"];
                                if (!string.IsNullOrEmpty(tn))
                                    toolNames.Add(tn);
                            }
                            toolCount = toolNames.Count;
                        }
                    }

                    // 成功！
                    var resultMsg = LocalizationService.Format("Settings.Config.McpEditor.TestSuccess",
                        serverName, serverVersion, toolCount, "2024-11-05") + "\n"
                        + FormatToolList(toolNames);

                    MessageBox.Show(
                        this,
                        resultMsg,
                        LocalizationService.Get("Settings.Config.ModelEditor.TestConnection"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (InvalidOperationException)
                {
                    throw; // 重新抛出业务异常
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(LocalizationService.Format("Settings.Config.McpEditor.Error.ProtocolTest", ex.Message), ex);
                }
                finally
                {
                    // 清理：关闭 stdin，等待进程自然退出或强制 kill
                    try { process.StandardInput.Close(); } catch { }
                    if (!process.HasExited)
                    {
                        try { process.Kill(); } catch { }
                    }
                }
            }
        }

        private static string TruncateError(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            text = text.Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }

        private static void ApplyHeaders(HttpRequestMessage request, IList<NameValueItem> headers)
        {
            if (request == null || headers == null)
            {
                return;
            }

            foreach (var header in headers)
            {
                if (header == null || string.IsNullOrWhiteSpace(header.Name))
                {
                    continue;
                }

                request.Headers.Remove(header.Name);
                request.Headers.TryAddWithoutValidation(header.Name, header.Value ?? string.Empty);
            }
        }

        private static bool TryBuildNameValueItems(IEnumerable<NameValueRow> rows, string displayName, out List<NameValueItem> items, out string message)
        {
            items = new List<NameValueItem>();

            foreach (var row in EnumerateRows(rows))
            {
                var name = (row.Name ?? string.Empty).Trim();
                var value = row.Value ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    message = LocalizationService.Format("Settings.Config.McpEditor.Validation.NameValueRequired", displayName);
                    return false;
                }

                if (items.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    message = LocalizationService.Format("Settings.Config.McpEditor.Validation.NameValueDuplicate", displayName, name);
                    return false;
                }

                items.Add(new NameValueItem
                {
                    Name = name,
                    Value = value
                });
            }

            message = null;
            return true;
        }

        private void NameValueGrid_PopupMenuShowing(object sender, DevExpress.XtraGrid.Views.Grid.PopupMenuShowingEventArgs e)
        {
            var view = sender as DevExpress.XtraGrid.Views.Grid.GridView;
            if (view == null || e == null || e.HitInfo == null || !e.HitInfo.InRowCell)
            {
                return;
            }

            var rowHandle = e.HitInfo.RowHandle;
            if (rowHandle < 0 || view.IsNewItemRow(rowHandle) || e.Menu == null)
            {
                return;
            }

            view.FocusedRowHandle = rowHandle;
            e.Menu.Items.Add(new DXMenuItem(LocalizationService.Get("Settings.Config.Context.DeleteCurrentRow"), delegate
            {
                DeleteRow(view, rowHandle);
            }));
        }

        private void NameValueGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e == null || e.KeyCode != Keys.Delete)
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
            if (view == null || rowHandle < 0 || view.IsNewItemRow(rowHandle))
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

        private static List<NameValueRow> ToNameValueRows(IEnumerable<NameValueItem> source)
        {
            var rows = new List<NameValueRow>();
            if (source == null)
            {
                return rows;
            }

            foreach (var item in source)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                rows.Add(new NameValueRow
                {
                    Name = item.Name,
                    Value = item.Value ?? string.Empty
                });
            }

            return rows;
        }

        private static IEnumerable<NameValueRow> EnumerateRows(IEnumerable<NameValueRow> rows)
        {
            return rows == null ? Enumerable.Empty<NameValueRow>() : rows.Where(p => p != null);
        }

        private static McpServerSettings CloneServer(McpServerSettings source)
        {
            var server = source == null ? new McpServerSettings() : new McpServerSettings
            {
                Id = source.Id,
                Name = source.Name,
                Description = source.Description,
                IsEnabled = source.IsEnabled,
                Stdio = source.Stdio == null ? null : new StdioMcpServerSettings
                {
                    Command = source.Stdio.Command,
                    WorkingDirectory = source.Stdio.WorkingDirectory,
                    Arguments = source.Stdio.Arguments == null ? new List<string>() : new List<string>(source.Stdio.Arguments),
                    EnvironmentVariables = CloneNameValueItems(source.Stdio.EnvironmentVariables)
                },
                Http = source.Http == null ? null : new HttpMcpServerSettings
                {
                    Endpoint = source.Http.Endpoint,
                    TransportMode = source.Http.TransportMode,
                    Headers = CloneNameValueItems(source.Http.Headers)
                },
                Tools = CloneTools(source.Tools)
            };

            return OfficeCopilotSettingsFactory.NormalizeMcpServer(server);
        }

        private static List<NameValueItem> CloneNameValueItems(IEnumerable<NameValueItem> items)
        {
            return items == null
                ? new List<NameValueItem>()
                : items.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new NameValueItem
                    {
                        Name = p.Name,
                        Value = p.Value
                    })
                    .ToList();
        }

        private static List<McpToolSettings> CloneTools(IEnumerable<McpToolSettings> tools)
        {
            return tools == null
                ? new List<McpToolSettings>()
                : tools.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new McpToolSettings
                    {
                        Name = p.Name,
                        DisplayName = p.DisplayName,
                        Description = p.Description,
                        SourceName = p.SourceName,
                        InputSchemaJson = p.InputSchemaJson,
                        OutputSchemaJson = p.OutputSchemaJson,
                        IsReadOnly = p.IsReadOnly,
                        IsEnabled = p.IsEnabled,
                        Metadata = CloneNameValueItems(p.Metadata)
                    })
                    .ToList();
        }

        // ═══════════════════════════════════════════════════
        //  MCP JSON-RPC 测试辅助方法
        // ═══════════════════════════════════════════════════

        private const int MaxToolDisplay = 10;

        private static string FormatToolList(List<string> toolNames)
        {
            if (toolNames == null || toolNames.Count == 0)
                return string.Empty;

            var shown = toolNames.Take(MaxToolDisplay).ToList();
            var result = "  • " + string.Join("\n  • ", shown);

            if (toolNames.Count > MaxToolDisplay)
                result += "\n" + LocalizationService.Format("Settings.Config.McpEditor.ToolListMore", toolNames.Count - MaxToolDisplay);

            return result;
        }

        private static string BuildJsonRpcRequest(string id, string method, object ps)
        {
            var req = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method
            };
            if (ps != null)
                req["params"] = ps;
            return JsonConvert.SerializeObject(req);
        }

        private static string BuildJsonRpcNotification(string method, object ps)
        {
            var req = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method
            };
            if (ps != null)
                req["params"] = ps;
            return JsonConvert.SerializeObject(req);
        }

        // 按 id 匹配应答：服务端可能在应答之前推送通知（如 notifications/tools/list_changed）或非 JSON 日志，这些行需要跳过。
        private static async Task<string> ReadJsonRpcResponseAsync(Process process, string expectedId, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    return null; // 超时

                var readTask = process.StandardOutput.ReadLineAsync();
                var delayTask = Task.Delay(remaining);
                var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
                if (completed == delayTask)
                    return null; // 超时

                var line = await readTask.ConfigureAwait(false);
                if (line == null)
                    throw new InvalidOperationException(LocalizationService.Get("Settings.Config.McpEditor.Error.ProcessExited"));

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                JObject msg;
                try
                {
                    msg = JObject.Parse(line);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    continue; // 非 JSON 输出，忽略
                }

                if (msg["method"] != null)
                    continue; // 通知或服务端请求，不是应答

                if (string.Equals((string)msg["id"], expectedId, StringComparison.Ordinal))
                    return line;
            }
        }

        private static (object result, string error) ParseJsonRpcResult(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return (null, LocalizationService.Get("Settings.Config.McpEditor.Error.EmptyResponse"));

            try
            {
                var root = JObject.Parse(line);

                var err = root["error"] as JObject;
                if (err != null)
                {
                    var msg = (string)err["message"] ?? LocalizationService.Get("Settings.Config.McpEditor.UnknownError");
                    return (null, msg);
                }

                var res = root["result"];
                if (res != null)
                {
                    // 返回 JToken，让调用方自行解析
                    return (res, null);
                }

                return (null, LocalizationService.Get("Settings.Config.McpEditor.Error.ResponseMissingResult"));
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                return (null, LocalizationService.Format("Settings.Config.McpEditor.Error.JsonParse", ex.Message));
            }
        }

        private sealed class HttpJsonRpcReply
        {
            public string Body { get; set; }

            public string SessionId { get; set; }
        }

        // Streamable HTTP 服务端可能以 application/json 或 text/event-stream 应答，后者需要从 data: 行中取出应答。
        private static async Task<HttpJsonRpcReply> PostJsonRpcAsync(
            HttpClient client, string endpoint, string jsonBody, IList<NameValueItem> headers,
            string expectedId, string sessionId)
        {
            using (var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"))
            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content })
            {
                bool hasUserAccept = false;
                if (headers != null)
                {
                    foreach (var h in headers)
                    {
                        if (h != null && !string.IsNullOrWhiteSpace(h.Name))
                        {
                            if (string.Equals(h.Name, "Accept", StringComparison.OrdinalIgnoreCase))
                                hasUserAccept = true;
                            request.Headers.TryAddWithoutValidation(h.Name, h.Value ?? string.Empty);
                        }
                    }
                }

                if (!hasUserAccept)
                    request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");

                if (!string.IsNullOrEmpty(sessionId))
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);

                using (var response = await SendHttpRequestAsync(client, request).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(
                            $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {TruncateError(body, 500)}");
                    }

                    IEnumerable<string> sessionValues;
                    string newSessionId = null;
                    if (response.Headers.TryGetValues("Mcp-Session-Id", out sessionValues))
                        newSessionId = sessionValues.FirstOrDefault();

                    var contentType = response.Content.Headers.ContentType?.MediaType;
                    return new HttpJsonRpcReply
                    {
                        Body = ExtractJsonRpcMessage(body, contentType, expectedId),
                        SessionId = newSessionId ?? sessionId
                    };
                }
            }
        }

        // HttpClient 的外层异常只有通用文案，原因在内层异常中，需要展开后展示。
        private static async Task<HttpResponseMessage> SendHttpRequestAsync(HttpClient client, HttpRequestMessage request)
        {
            try
            {
                return await client.SendAsync(request).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                var detail = ex.Message;
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                    detail += " → " + inner.Message;

                throw new InvalidOperationException(LocalizationService.Format("Settings.Config.McpEditor.Error.RequestFailed", detail), ex);
            }
        }

        // 从 SSE 正文中取出 id 匹配的应答；若无法按 id 匹配，则取第一条应答消息。
        private static string ExtractJsonRpcMessage(string body, string contentType, string expectedId)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            if (contentType == null || !contentType.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
                return body;

            string fallback = null;
            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var payload = line.Substring(5).Trim();
                if (payload.Length == 0)
                    continue;

                JObject msg;
                try
                {
                    msg = JObject.Parse(payload);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    continue;
                }

                if (msg["method"] != null)
                    continue; // 通知或服务端请求

                if (expectedId != null && string.Equals((string)msg["id"], expectedId, StringComparison.Ordinal))
                    return payload;

                if (fallback == null)
                    fallback = payload;
            }

            return fallback ?? string.Empty;
        }

        private sealed class NameValueRow
        {
            public string Name { get; set; }

            public string Value { get; set; }
        }

        private sealed class StdioParametersModel
        {
            [Category("Stdio")]
            [LocalizedDisplayName("Settings.Config.McpEditor.Property.Command")]
            public string Command { get; set; }

            [Category("Stdio")]
            [LocalizedDisplayName("Settings.Config.McpEditor.Property.Arguments")]
            public string ArgumentsText { get; set; }

            [Category("Stdio")]
            [LocalizedDisplayName("Settings.Config.McpEditor.Property.WorkingDirectory")]
            public string WorkingDirectory { get; set; }

            public static StdioParametersModel FromServer(StdioMcpServerSettings settings)
            {
                settings = settings ?? new StdioMcpServerSettings();
                return new StdioParametersModel
                {
                    Command = settings.Command ?? string.Empty,
                    ArgumentsText = JoinArguments(settings.Arguments),
                    WorkingDirectory = settings.WorkingDirectory ?? string.Empty
                };
            }

            public void ApplyTo(StdioMcpServerSettings settings)
            {
                if (settings == null)
                {
                    return;
                }

                settings.Command = (Command ?? string.Empty).Trim();
                settings.WorkingDirectory = (WorkingDirectory ?? string.Empty).Trim();
                settings.Arguments = SplitArguments(ArgumentsText);
            }

            private static string JoinArguments(IEnumerable<string> arguments)
            {
                if (arguments == null)
                {
                    return string.Empty;
                }

                return string.Join(" ", arguments.Where(p => !string.IsNullOrWhiteSpace(p)).Select(QuoteArgument));
            }

            private static string QuoteArgument(string argument)
            {
                if (string.IsNullOrEmpty(argument))
                {
                    return string.Empty;
                }

                return argument.IndexOf(' ') >= 0 ? string.Format("\"{0}\"", argument.Replace("\"", "\\\"")) : argument;
            }

            private static List<string> SplitArguments(string text)
            {
                var result = new List<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return result;
                }

                var buffer = new System.Text.StringBuilder();
                var inQuotes = false;

                foreach (var ch in text.Trim())
                {
                    if (ch == '"')
                    {
                        inQuotes = !inQuotes;
                        continue;
                    }

                    if (char.IsWhiteSpace(ch) && !inQuotes)
                    {
                        if (buffer.Length > 0)
                        {
                            result.Add(buffer.ToString());
                            buffer.Clear();
                        }

                        continue;
                    }

                    buffer.Append(ch);
                }

                if (buffer.Length > 0)
                {
                    result.Add(buffer.ToString());
                }

                return result;
            }
        }

        private sealed class HttpParametersModel
        {
            [Category("HTTP")]
            [LocalizedDisplayName("Settings.Config.McpEditor.Property.Endpoint")]
            public string Endpoint { get; set; }

            [Category("HTTP")]
            [LocalizedDisplayName("Settings.Config.McpEditor.Property.TransportMode")]
            public HttpTransportMode TransportMode { get; set; }

            public static HttpParametersModel FromServer(HttpMcpServerSettings settings)
            {
                settings = settings ?? new HttpMcpServerSettings();
                return new HttpParametersModel
                {
                    Endpoint = settings.Endpoint ?? string.Empty,
                    TransportMode = settings.TransportMode
                };
            }

            public void ApplyTo(HttpMcpServerSettings settings)
            {
                if (settings == null)
                {
                    return;
                }

                settings.Endpoint = (Endpoint ?? string.Empty).Trim();
                settings.TransportMode = TransportMode;
            }
        }
    }
}
