using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace OfficeCopilot.Mcp.TestServer
{
    /// <summary>
    /// 极简 stdio MCP 测试服务器，零外部依赖，纯 JSON-RPC 2.0 over stdin/stdout。
    /// 用于在 fmMcpEditor 中配置后验证 MCP 连接、工具发现、工具调用全链路。
    ///
    /// 提供的工具：
    ///   echo         - 回显文本
    ///   get_time     - 返回当前 UTC 时间
    ///   add_numbers  - 两数相加
    ///   get_env_var  - 读取环境变量
    ///   delete_item  - 【测试】模拟删除（名称推导为 high，不实际修改数据）
    ///   run_command  - 【测试】模拟执行（名称推导为 critical，不实际执行）
    ///   slow_task    - 【测试】阻塞指定秒数（验证单工具超时）
    ///   get_image    - 【测试】返回 1x1 PNG 图片块（验证非文本结果）
    ///
    /// 配置示例（fmMcpEditor 或 .mcp.json）：
    ///   transport: stdio
    ///   command:   dotnet
    ///   args:      run --project OfficeCopilot.Mcp.TestServer/OfficeCopilot.Mcp.TestServer.csproj
    /// </summary>
    public static class Program
    {
        private const string ServerName = "officecopilot-test-server";
        private const string ServerVersion = "1.0.0";

        private static string _logPath;

        public static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.InputEncoding = System.Text.Encoding.UTF8;

            _logPath = Path.Combine(Path.GetTempPath(), "mcp-test-server.log");
            Log($"START (PID={Environment.ProcessId})");

            try
            {
                using var stdin = Console.OpenStandardInput();
                using var reader = new StreamReader(stdin);

                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    Log($">>> {line}");

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (!root.TryGetProperty("method", out var methodProp))
                            continue; // 不是请求，跳过

                        var method = methodProp.GetString();
                        var id = root.TryGetProperty("id", out var idProp)
                            ? SerializeElement(idProp)
                            : null;

                        var response = Dispatch(method, id, root);
                        if (response != null)
                        {
                            Log($"<<< {response}");
                            Console.WriteLine(response);
                        }
                    }
                    catch (JsonException ex)
                    {
                        Log($"PARSE ERROR: {ex.Message}");
                        var err = BuildError(null, -32700, $"Parse error: {ex.Message}");
                        Console.WriteLine(err);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"FATAL: {ex}");
            }
            finally
            {
                Log("EXIT");
            }
        }

        // ═══════════════════════════════════════
        //  Dispatch
        // ═══════════════════════════════════════

        private static string Dispatch(string method, string id, JsonElement root)
        {
            // notification（无 id）不回复
            if (id == null)
            {
                Log($"NOTIFICATION: {method} (ignored)");
                return null;
            }

            switch (method)
            {
                case "initialize":
                    return HandleInitialize(id);

                case "tools/list":
                    return HandleToolsList(id);

                case "tools/call":
                    return HandleToolsCall(id, root);

                // ping 是常见扩展，礼貌回应
                case "ping":
                    return BuildResult(id, new Dictionary<string, object> { ["pong"] = true });

                default:
                    return BuildError(id, -32601, $"Method not found: {method}");
            }
        }

        // ═══════════════════════════════════════
        //  initialize
        // ═══════════════════════════════════════

        private static string HandleInitialize(string id)
        {
            return BuildResult(id, new Dictionary<string, object>
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new Dictionary<string, object>
                {
                    ["tools"] = new Dictionary<string, object>()
                },
                ["serverInfo"] = new Dictionary<string, object>
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion
                }
            });
        }

        // ═══════════════════════════════════════
        //  tools/list
        // ═══════════════════════════════════════

        private static string HandleToolsList(string id)
        {
            return BuildResult(id, new Dictionary<string, object>
            {
                ["tools"] = new[]
                {
                    ToolDef("echo", "回显输入的文本，验证 MCP 通信是否正常",
                        Props(("text", "string", "要回显的文本内容")),
                        Required("text")),

                    ToolDef("get_time", "返回当前 UTC 时间，验证无参数工具调用",
                        Props(),
                        null), // no required

                    ToolDef("add_numbers", "计算两数之和，验证带参数工具调用和数值传递",
                        Props(("a", "number", "第一个加数"), ("b", "number", "第二个加数")),
                        Required("a", "b")),

                    ToolDef("get_env_var", "读取指定环境变量，验证环境变量传递是否生效",
                        Props(("name", "string", "环境变量名称")),
                        Required("name")),

                    // 测试专用：名字触发风险推导，验证确认门（不会真正删除或执行任何东西）
                    ToolDef("delete_item", "【测试】模拟删除一条记录，名称含 delete 推导为 high 风险",
                        Props(("id", "string", "要删除的记录 ID")),
                        Required("id")),

                    ToolDef("run_command", "【测试】模拟执行命令，名称含 run_command 推导为 critical 风险（不会真正执行）",
                        Props(("command", "string", "要模拟执行的命令")),
                        Required("command")),

                    ToolDef("slow_task", "【测试】阻塞指定秒数后返回，用于验证单工具超时（最长 120 秒）",
                        Props(("seconds", "number", "阻塞秒数")),
                        Required("seconds")),

                    ToolDef("get_image", "【测试】返回一张 1x1 像素 PNG 图片，用于验证非文本结果的处理",
                        Props(),
                        null)
                }
            });
        }

        // ═══════════════════════════════════════
        //  tools/call
        // ═══════════════════════════════════════

        private static string HandleToolsCall(string id, JsonElement root)
        {
            if (!root.TryGetProperty("params", out var ps))
                return BuildError(id, -32602, "Missing params");

            if (!ps.TryGetProperty("name", out var nm))
                return BuildError(id, -32602, "Missing tool name");

            var toolName = nm.GetString();
            JsonElement args = ps.TryGetProperty("arguments", out var a)
                ? a : default;

            if (toolName == "get_image")
            {
                return BuildResult(id, new Dictionary<string, object>
                {
                    ["content"] = new[] { ImageContent(OnePixelPngBase64, "image/png") }
                });
            }

            try
            {
                var text = toolName switch
                {
                    "echo"        => ExecuteEcho(args),
                    "get_time"    => ExecuteGetTime(),
                    "add_numbers" => ExecuteAddNumbers(args),
                    "get_env_var" => ExecuteGetEnvVar(args),
                    "delete_item" => ExecuteDeleteItem(args),
                    "run_command" => ExecuteRunCommand(args),
                    "slow_task"   => ExecuteSlowTask(args),
                    _             => $"Unknown tool: {toolName}"
                };

                return BuildResult(id, new Dictionary<string, object>
                {
                    ["content"] = new[] { TextContent(text) }
                });
            }
            catch (Exception ex)
            {
                Log($"TOOL ERROR [{toolName}]: {ex.Message}");
                return BuildResult(id, new Dictionary<string, object>
                {
                    ["content"] = new[] { TextContent($"Error: {ex.Message}") },
                    ["isError"] = true
                });
            }
        }

        // ═══════════════════════════════════════
        //  Tool implementations
        // ═══════════════════════════════════════

        private static string ExecuteEcho(JsonElement args)
        {
            var text = "（无输入）";
            if (args.ValueKind == JsonValueKind.Object &&
                args.TryGetProperty("text", out var t))
            {
                text = t.GetString() ?? "(null)";
            }
            return $"[echo] {text}";
        }

        private static string ExecuteGetTime()
        {
            var now = DateTime.UtcNow;
            return $"UTC: {now:yyyy-MM-dd HH:mm:ss.fff}\n"
                 + $"Unix: {new DateTimeOffset(now).ToUnixTimeSeconds()}\n"
                 + $"Local: {now.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}";
        }

        private static string ExecuteAddNumbers(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("arguments 必须是 JSON 对象");

            if (!args.TryGetProperty("a", out var ae) || !args.TryGetProperty("b", out var be))
                throw new ArgumentException("缺少必填参数 a 和 b");

            double a = ToDouble(ae), b = ToDouble(be);
            return $"{a} + {b} = {a + b}";
        }

        private static string ExecuteGetEnvVar(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("name", out var ne))
                throw new ArgumentException("缺少必填参数 name");

            var name = ne.GetString() ?? "";
            var value = Environment.GetEnvironmentVariable(name);
            return value != null
                ? $"环境变量 '{name}' = {value}"
                : $"环境变量 '{name}' 未设置";
        }

        private static string ExecuteDeleteItem(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("id", out var ie))
                throw new ArgumentException("缺少必填参数 id");

            return $"[模拟] 已删除记录 '{ie.GetString()}'（未实际修改任何数据）";
        }

        private static string ExecuteRunCommand(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("command", out var ce))
                throw new ArgumentException("缺少必填参数 command");

            return $"[模拟] 未执行命令: {ce.GetString()}";
        }

        private static string ExecuteSlowTask(JsonElement args)
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("seconds", out var se))
                throw new ArgumentException("缺少必填参数 seconds");

            var seconds = Math.Min(Math.Max(ToDouble(se), 0), 120);
            System.Threading.Thread.Sleep(TimeSpan.FromSeconds(seconds));
            return $"slow_task 完成，阻塞 {seconds} 秒";
        }

        // ═══════════════════════════════════════
        //  JSON-RPC helpers
        // ═══════════════════════════════════════

        private static string BuildResult(string id, object result)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id != null ? JsonSerializer.Deserialize<JsonElement>(id) : null,
                ["result"] = result
            });
        }

        private static string BuildError(string id, int code, string message)
        {
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id != null ? JsonSerializer.Deserialize<JsonElement>(id) : null,
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = code,
                    ["message"] = message
                }
            });
        }

        private static string SerializeElement(JsonElement el)
        {
            return JsonSerializer.Serialize(el);
        }

        private static double ToDouble(JsonElement el)
        {
            return el.ValueKind == JsonValueKind.Number
                ? el.GetDouble()
                : double.TryParse(el.GetString(), out var d) ? d : 0;
        }

        // ═══════════════════════════════════════
        //  Tool schema builders
        // ═══════════════════════════════════════

        private static readonly HashSet<string> ReadOnlyToolNames = new HashSet<string>
        {
            "echo", "get_time", "add_numbers", "get_env_var", "get_image"
        };

        private static Dictionary<string, object> ToolDef(
            string name, string desc,
            Dictionary<string, object> props,
            string[] required)
        {
            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = props
            };
            if (required != null && required.Length > 0)
                schema["required"] = required;

            var def = new Dictionary<string, object>
            {
                ["name"] = name,
                ["description"] = desc,
                ["inputSchema"] = schema
            };
            // 只读工具声明 readOnlyHint，用于验证客户端按声明跳过确认
            if (ReadOnlyToolNames.Contains(name))
                def["annotations"] = new Dictionary<string, object> { ["readOnlyHint"] = true };
            return def;
        }

        private static Dictionary<string, object> Props(
            params (string name, string type, string desc)[] props)
        {
            var d = new Dictionary<string, object>();
            foreach (var (name, type, desc) in props)
                d[name] = new Dictionary<string, object>
                {
                    ["type"] = type,
                    ["description"] = desc
                };
            return d;
        }

        private static string[] Required(params string[] names) => names;

        private static Dictionary<string, object> TextContent(string text)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "text",
                ["text"] = text
            };
        }

        private const string OnePixelPngBase64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        private static Dictionary<string, object> ImageContent(string base64, string mimeType)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "image",
                ["data"] = base64,
                ["mimeType"] = mimeType
            };
        }

        private static void Log(string msg)
        {
            // 同时可能有多个实例（如编辑器“测试”与启动连接并存），日志必须允许共享写；
            // 写日志失败不能影响 stdio 通信，所以忽略 IO 异常。
            try
            {
                using var fs = new FileStream(_logPath, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                using var sw = new StreamWriter(fs, new System.Text.UTF8Encoding(false));
                sw.WriteLine($"[{DateTime.UtcNow:O}] {msg}");
            }
            catch (IOException)
            {
            }
        }
    }
}
