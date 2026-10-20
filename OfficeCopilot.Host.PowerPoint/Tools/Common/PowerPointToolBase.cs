using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.PowerPoint.Tools
{
    public abstract class PowerPointToolBase : IAgentTool
    {
        private static readonly JsonSerializerSettings ResultJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        public abstract ToolDefinition Definition { get; }
        public abstract Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request);

        protected static IPowerPointToolHost GetHost(ToolExecutionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.GetService<IPowerPointToolHost>()
                ?? throw new InvalidOperationException("IPowerPointToolHost not registered.");
        }

        /// <summary>解析工具参数；空或非法 JSON 视为空对象。</summary>
        protected static JObject ParseArguments(ToolExecutionRequest request)
        {
            var json = request == null ? null : request.ArgumentsJson;
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JObject();
            }

            try
            {
                return JToken.Parse(json) as JObject ?? new JObject();
            }
            catch (JsonException)
            {
                return new JObject();
            }
        }

        protected static string ToJson(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.None, ResultJsonSettings);
        }

        protected ToolExecutionResult Success(ToolExecutionRequest req, string content, string json = null)
            => ToolExecutionResult.CreateSuccess(Definition.Name, req?.ToolCallId ?? "", content, json);

        protected ToolExecutionResult Fail(ToolExecutionRequest req, string msg, string code = null)
            => ToolExecutionResult.CreateFailure(Definition.Name, req?.ToolCallId ?? "", msg, code);

        /// <summary>异常统一出口：PowerPoint 忙碌映射为 ppt.host_busy，其余使用调用方给定的错误码。</summary>
        protected ToolExecutionResult Fail(ToolExecutionRequest req, Exception ex, string code)
            => ex is PowerPointHostBusyException
                ? Fail(req, ex.Message, "ppt.host_busy")
                : Fail(req, ex.Message, code);

        /// <summary>
        /// 把截图压缩后附到结果上，由运行时在模型支持图像输入时转交。编码失败只丢图，不影响工具结果。
        /// </summary>
        protected static void AttachImage(ToolExecutionResult result, byte[] imageBytes, string description)
        {
            if (result == null || imageBytes == null || imageBytes.Length == 0)
            {
                return;
            }

            try
            {
                result.AddImage("image/jpeg", ModelImageEncoder.Encode(imageBytes), description);
            }
            catch (Exception)
            {
            }
        }
    }
}
