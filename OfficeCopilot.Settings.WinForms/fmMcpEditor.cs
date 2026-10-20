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

        public fmMcpEditor(McpServerSettings server)
        {
            InitializeComponent();

            McpServer = CreateEditableServer(server);
            WireEvents();
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
        }

        private void InitializeEditor()
        {
            Text = string.IsNullOrWhiteSpace(McpServer.Name) ? "新增MCP服务" : "编辑MCP服务";

            pgParameters.OptionsView.ShowRootCategories = false;

            cmbMcpType.Properties.Items.Clear();
            cmbMcpType.Properties.Items.AddRange(new object[] { "Stdio", "HTTP" });
            cmbMcpType.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
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
                isHttp ? "请求头" : "变量名",
                "值");

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
                MessageBox.Show(this, validationMessage, "MCP服务", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show(this, validationMessage, "测试MCP服务", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                MessageBox.Show(this, "测试失败：" + ex.Message, "测试MCP服务", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                message = "请填写 MCP 名称。";
                return false;
            }

            if (GetSelectedServerType() == McpServerType.Http)
            {
                if (_httpParameters == null)
                {
                    message = "HTTP 参数未初始化。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(_httpParameters.Endpoint))
                {
                    message = "请填写 MCP 服务 URL。";
                    return false;
                }

                Uri endpointUri;
                if (!Uri.TryCreate(_httpParameters.Endpoint.Trim(), UriKind.Absolute, out endpointUri)
                    || (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
                {
                    message = "MCP 服务 URL 格式不正确，必须是 http 或 https。";
                    return false;
                }

                List<NameValueItem> headers;
                if (!TryBuildNameValueItems(_httpHeaderRows, "HTTP 请求头", out headers, out message))
                {
                    return false;
                }
            }
            else
            {
                if (_stdioParameters == null)
                {
                    message = "Stdio 参数未初始化。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(_stdioParameters.Command))
                {
                    message = "请填写命令。";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(_stdioParameters.WorkingDirectory)
                    && !System.IO.Directory.Exists(_stdioParameters.WorkingDirectory.Trim()))
                {
                    message = "工作目录不存在。";
                    return false;
                }

                List<NameValueItem> environmentVariables;
                if (!TryBuildNameValueItems(_environmentVariableRows, "环境变量", out environmentVariables, out message))
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
                if (!TryBuildNameValueItems(_httpHeaderRows, "HTTP 请求头", out headers, out message))
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
                if (!TryBuildNameValueItems(_environmentVariableRows, "环境变量", out environmentVariables, out message))
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
                    throw new InvalidOperationException($"MCP initialize 失败: {initResult.error}");
                }

                string serverName = "未知";
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
                    throw new InvalidOperationException($"tools/list 失败: {listResult.error}");
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

                var resultMsg = $"✅ MCP 连接测试成功！\n\n"
                    + $"服务器: {serverName} v{serverVersion}\n"
                    + $"协议版本: 2024-11-05\n"
                    + $"发现工具: {toolCount} 个\n"
                    + FormatToolList(toolNames);

                MessageBox.Show(
                    this,
                    resultMsg,
                    "测试MCP服务",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private async System.Threading.Tasks.Task TestStdioServerAsync(McpServerSettings server)
        {
            var command = server.Stdio == null ? string.Empty : server.Stdio.Command ?? string.Empty;
            if (string.IsNullOrWhiteSpace(command))
            {
                throw new InvalidOperationException("未配置命令。");
            }

            var workingDirectory = server.Stdio == null ? string.Empty : server.Stdio.WorkingDirectory ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(workingDirectory) && !System.IO.Directory.Exists(workingDirectory))
            {
                throw new InvalidOperationException("工作目录不存在。");
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
                    throw new InvalidOperationException("无法启动进程，请检查命令是否在 PATH 中。");
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
                        throw new InvalidOperationException("MCP 握手超时：initialize 无响应。");
                    }

                    var initResult = ParseJsonRpcResult(initResponse);
                    if (initResult.error != null)
                    {
                        throw new InvalidOperationException($"MCP initialize 失败: {initResult.error}");
                    }

                    // 提取 serverInfo
                    string serverName = "未知";
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
                        throw new InvalidOperationException("MCP 握手超时：tools/list 无响应。");
                    }

                    var listResult = ParseJsonRpcResult(listResponse);
                    if (listResult.error != null)
                    {
                        throw new InvalidOperationException($"tools/list 失败: {listResult.error}");
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
                    var resultMsg = $"✅ MCP 连接测试成功！\n\n"
                        + $"服务器: {serverName} v{serverVersion}\n"
                        + $"协议版本: 2024-11-05\n"
                        + $"发现工具: {toolCount} 个\n"
                        + FormatToolList(toolNames);

                    MessageBox.Show(
                        this,
                        resultMsg,
                        "测试MCP服务",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (InvalidOperationException)
                {
                    throw; // 重新抛出业务异常
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"MCP 协议测试异常: {ex.Message}", ex);
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
                    message = displayName + "名称不能为空。";
                    return false;
                }

                if (items.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    message = string.Format("{0}“{1}”重复，请检查。", displayName, name);
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
            e.Menu.Items.Add(new DXMenuItem("删除当前行", delegate
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
                result += $"\n  ... 还有 {toolNames.Count - MaxToolDisplay} 个工具未展示";

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
                    throw new InvalidOperationException("服务进程已退出，未返回应答。");

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
                return (null, "空响应");

            try
            {
                var root = JObject.Parse(line);

                var err = root["error"] as JObject;
                if (err != null)
                {
                    var msg = (string)err["message"] ?? "未知错误";
                    return (null, msg);
                }

                var res = root["result"];
                if (res != null)
                {
                    // 返回 JToken，让调用方自行解析
                    return (res, null);
                }

                return (null, "响应中既无 result 也无 error");
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                return (null, $"JSON 解析失败: {ex.Message}");
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

                throw new InvalidOperationException("发送请求失败：" + detail, ex);
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
            [DisplayName("命令")]
            public string Command { get; set; }

            [Category("Stdio")]
            [DisplayName("命令行参数")]
            public string ArgumentsText { get; set; }

            [Category("Stdio")]
            [DisplayName("工作目录")]
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
            [DisplayName("URL")]
            public string Endpoint { get; set; }

            [Category("HTTP")]
            [DisplayName("类型")]
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
