using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Tests.HostBridge
{
    /// <summary>
    /// D-3：Broker↔Office 的 HostBridge 协议与 Panel↔Broker 协议同为「一行一个 JSON 信封」，
    /// 这里按 <c>BrokerEnvelopeTests</c> 的同一套标准锁定信封往返、错误码常量与操作名常量。
    /// </summary>
    [TestClass]
    public class HostBridgeEnvelopeTests
    {
        [TestMethod]
        public void Request_InitializesDefaults()
        {
            var request = new HostBridgeRequest();

            Assert.IsFalse(string.IsNullOrWhiteSpace(request.RequestId));
            Assert.AreEqual("{}", request.PayloadJson);
            Assert.IsNotNull(request.Metadata);
        }

        [TestMethod]
        public void Response_InitializesDefaults()
        {
            var response = new HostBridgeResponse();

            Assert.IsFalse(response.Success, "Success 默认应为 false，避免误判失败响应为成功");
            Assert.AreEqual(string.Empty, response.Operation);
            Assert.AreEqual("{}", response.PayloadJson);
            Assert.IsNotNull(response.Metadata);
        }

        [TestMethod]
        public void Request_RoundTripsThroughJson()
        {
            var original = new HostBridgeRequest
            {
                RequestId = "req-1",
                SessionId = "sess-1",
                Operation = HostBridgeOperationNames.InvokeTool,
                PayloadJson = "{\"toolName\":\"excel_write_range\"}"
            };
            original.Metadata[HostBridgeMetadataKeys.ConfirmationGranted] = HostBridgeMetadataKeys.TrueValue;

            var restored = JsonConvert.DeserializeObject<HostBridgeRequest>(JsonConvert.SerializeObject(original));

            Assert.AreEqual(original.RequestId, restored.RequestId);
            Assert.AreEqual(original.SessionId, restored.SessionId);
            Assert.AreEqual(original.Operation, restored.Operation);
            Assert.AreEqual(original.PayloadJson, restored.PayloadJson);
            Assert.AreEqual(
                HostBridgeMetadataKeys.TrueValue,
                restored.Metadata[HostBridgeMetadataKeys.ConfirmationGranted]);
        }

        [TestMethod]
        public void Response_EchoesOperation()
        {
            var success = HostBridgeResponse.CreateSuccess("req-2", HostBridgeOperationNames.GetCapabilities, "{\"a\":1}");
            var failure = HostBridgeResponse.CreateFailure(
                "req-3",
                HostBridgeOperationNames.ExecuteCode,
                HostBridgeErrorCodes.CodeExecutionFailed,
                "boom");

            Assert.AreEqual(HostBridgeOperationNames.GetCapabilities, success.Operation);
            Assert.IsTrue(success.Success);
            Assert.AreEqual(HostBridgeOperationNames.ExecuteCode, failure.Operation);
            Assert.IsFalse(failure.Success);
            Assert.AreEqual(HostBridgeErrorCodes.CodeExecutionFailed, failure.ErrorCode);
        }

        [TestMethod]
        public void Response_LegacyOverloadsLeaveOperationEmpty()
        {
            Assert.AreEqual(string.Empty, HostBridgeResponse.CreateSuccess("req-4", "{}").Operation);
            Assert.AreEqual(string.Empty, HostBridgeResponse.CreateFailure("req-5", "code", "msg").Operation);
        }

        [TestMethod]
        public void SerializedEnvelope_NeverContainsRawNewline()
        {
            // 管道按行读取；负载里的换行必须被 JSON 转义，否则会把一条消息拆成两行。
            var request = new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.ExecuteCode,
                PayloadJson = "line-1\r\nline-2\n\tline-3"
            };

            var json = JsonConvert.SerializeObject(request);

            Assert.IsFalse(json.Contains("\n"), "序列化结果不得含裸换行");
            Assert.IsFalse(json.Contains("\r"), "序列化结果不得含裸回车");
            Assert.AreEqual(
                request.PayloadJson,
                JsonConvert.DeserializeObject<HostBridgeRequest>(json).PayloadJson);
        }

        [TestMethod]
        public void SerializedEnvelope_SurvivesNonAsciiPayload()
        {
            var request = new HostBridgeRequest
            {
                Operation = HostBridgeOperationNames.InsertText,
                PayloadJson = "{\"text\":\"中文 · Emoji 🚀 · \\\"quoted\\\"\"}"
            };

            var restored = JsonConvert.DeserializeObject<HostBridgeRequest>(JsonConvert.SerializeObject(request));

            Assert.AreEqual(request.PayloadJson, restored.PayloadJson);
        }

        // ---- 常量 ----

        private static IReadOnlyList<FieldInfo> StringConstants(Type type)
        {
            return type
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .ToList();
        }

        [TestMethod]
        public void OperationNames_UseLowerCaseDottedNames()
        {
            var fields = StringConstants(typeof(HostBridgeOperationNames));

            Assert.IsTrue(fields.Count >= 5, "操作名常量数量异常：" + fields.Count);
            foreach (var field in fields)
            {
                var value = (string)field.GetRawConstantValue();
                Assert.AreEqual(value.ToLowerInvariant(), value, field.Name + " 应为全小写");
                Assert.IsTrue(value.StartsWith("host.", StringComparison.Ordinal), field.Name + " 应以 host. 开头：" + value);
                Assert.AreEqual(value.Trim(), value, field.Name + " 不应含首尾空白");
            }
        }

        [TestMethod]
        public void OperationNames_AreMutuallyDistinct()
        {
            var values = StringConstants(typeof(HostBridgeOperationNames))
                .Select(f => (string)f.GetRawConstantValue())
                .ToList();

            CollectionAssert.AllItemsAreUnique(values, "操作名常量存在重复，会导致路由歧义");
        }

        [TestMethod]
        public void ErrorCodes_UseLowerCaseDottedNames()
        {
            var fields = StringConstants(typeof(HostBridgeErrorCodes));

            Assert.IsTrue(fields.Count >= 10, "错误码常量数量异常：" + fields.Count);
            foreach (var field in fields)
            {
                var value = (string)field.GetRawConstantValue();
                Assert.AreEqual(value.ToLowerInvariant(), value, field.Name + " 应为全小写");
                Assert.IsTrue(
                    value.StartsWith("host_bridge.", StringComparison.Ordinal),
                    field.Name + " 应以 host_bridge. 开头：" + value);
                Assert.AreEqual(value.Trim(), value, field.Name + " 不应含首尾空白");
            }
        }

        [TestMethod]
        public void ErrorCodes_AreMutuallyDistinct()
        {
            var values = StringConstants(typeof(HostBridgeErrorCodes))
                .Select(f => (string)f.GetRawConstantValue())
                .ToList();

            CollectionAssert.AllItemsAreUnique(values, "错误码常量存在重复，会让调用方无法区分失败原因");
        }
    }
}
