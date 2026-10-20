using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Client;

namespace OfficeCopilot.Agent.Tests.Client
{
    /// <summary>
    /// Broker 命名管道协议是"一行一个 JSON 信封"的行分隔格式（见 NamedPipeBrokerHost）。
    /// 这里锁定信封 DTO 的默认 <see cref="JsonSerializer"/> 往返行为与消息类型常量的稳定性。
    /// </summary>
    [TestClass]
    public class BrokerEnvelopeTests
    {
        [TestMethod]
        public void RequestEnvelope_InitializesMetadataAndDefaults()
        {
            var envelope = new AgentBrokerRequestEnvelope();

            Assert.IsNotNull(envelope.Metadata, "Metadata 必须预初始化，调用方直接索引赋值");
            Assert.AreEqual(0, envelope.Metadata.Count);
        }

        [TestMethod]
        public void ResponseEnvelope_InitializesMetadata()
        {
            var envelope = new AgentBrokerResponseEnvelope();

            Assert.IsNotNull(envelope.Metadata);
            Assert.IsFalse(envelope.Success, "Success 默认应为 false，避免误判失败响应为成功");
        }

        [TestMethod]
        public void EventEnvelope_InitializesMetadata()
        {
            Assert.IsNotNull(new AgentBrokerEventEnvelope().Metadata);
        }

        [TestMethod]
        public void RequestEnvelope_RoundTripsThroughJson()
        {
            var original = new AgentBrokerRequestEnvelope
            {
                RequestId = "req-1",
                MessageType = AgentBrokerMessageTypes.ExecuteTurnRequest,
                PayloadJson = "{\"prompt\":\"生成报表\"}"
            };
            original.Metadata["hostApplicationName"] = "Excel";
            original.Metadata["runtime.maxToolRoundCount"] = "50";

            var restored = JsonSerializer.Deserialize<AgentBrokerRequestEnvelope>(JsonSerializer.Serialize(original));

            Assert.AreEqual(original.RequestId, restored.RequestId);
            Assert.AreEqual(original.MessageType, restored.MessageType);
            Assert.AreEqual(original.PayloadJson, restored.PayloadJson);
            Assert.AreEqual(2, restored.Metadata.Count);
            Assert.AreEqual("Excel", restored.Metadata["hostApplicationName"]);
            Assert.AreEqual("50", restored.Metadata["runtime.maxToolRoundCount"]);
        }

        [TestMethod]
        public void ResponseEnvelope_RoundTripsErrorFields()
        {
            var original = new AgentBrokerResponseEnvelope
            {
                RequestId = "req-2",
                MessageType = AgentBrokerMessageTypes.Error,
                Success = false,
                ErrorCode = "broker.request.invalid",
                ErrorMessage = "请求负载无法解析"
            };

            var restored = JsonSerializer.Deserialize<AgentBrokerResponseEnvelope>(JsonSerializer.Serialize(original));

            Assert.IsFalse(restored.Success);
            Assert.AreEqual("broker.request.invalid", restored.ErrorCode);
            Assert.AreEqual("请求负载无法解析", restored.ErrorMessage);
            Assert.AreEqual("{}", restored.PayloadJson, "未显式赋值时应保持构造函数的空对象默认值");
        }

        [TestMethod]
        public void TurnResult_ContentBearingIncompleteTurnIsNotAnOperationalFailure()
        {
            var result = new AgentBrokerTurnResult
            {
                Success = false,
                Content = "验证未完成：尚未确认目标工作表。",
                FinishReason = "pending_step_unresolved"
            };

            Assert.IsFalse(AgentBrokerTurnResult.IsOperationalFailure(result));
        }

        [TestMethod]
        public void TurnResult_StreamedContentPreventsIncompleteTurnFromBecomingOperationalFailure()
        {
            var result = new AgentBrokerTurnResult
            {
                Success = false,
                FinishReason = "pending_step_unresolved"
            };

            Assert.IsFalse(AgentBrokerTurnResult.IsOperationalFailure(result, true));
        }

        [TestMethod]
        public void TurnResult_ExplicitRuntimeErrorIsAnOperationalFailure()
        {
            var result = new AgentBrokerTurnResult
            {
                Success = false,
                Content = "部分回答",
                ErrorCode = "runtime.turn_failed",
                ErrorMessage = "Broker 执行失败。",
                FinishReason = "exception"
            };

            Assert.IsTrue(AgentBrokerTurnResult.IsOperationalFailure(result));
            Assert.IsTrue(AgentBrokerTurnResult.IsOperationalFailure(result, true));
        }

        [TestMethod]
        public void TurnResult_EmptyUnsuccessfulResultIsAnOperationalFailure()
        {
            var result = new AgentBrokerTurnResult
            {
                Success = false
            };

            Assert.IsTrue(AgentBrokerTurnResult.IsOperationalFailure(result));
        }

        [TestMethod]
        public void EventEnvelope_RoundTripsThroughJson()
        {
            var original = new AgentBrokerEventEnvelope
            {
                RequestId = "req-3",
                MessageType = AgentBrokerMessageTypes.TurnEvent,
                PayloadJson = "{\"kind\":\"delta\"}"
            };
            original.Metadata["seq"] = "7";

            var restored = JsonSerializer.Deserialize<AgentBrokerEventEnvelope>(JsonSerializer.Serialize(original));

            Assert.AreEqual("req-3", restored.RequestId);
            Assert.AreEqual(AgentBrokerMessageTypes.TurnEvent, restored.MessageType);
            Assert.AreEqual("7", restored.Metadata["seq"]);
        }

        [TestMethod]
        public void SerializedEnvelope_NeverContainsRawNewline()
        {
            // 管道按行读取；负载里的换行必须被 JSON 转义，否则会把一条消息拆成两行。
            var envelope = new AgentBrokerRequestEnvelope
            {
                RequestId = "req-4",
                MessageType = AgentBrokerMessageTypes.CompleteRequest,
                PayloadJson = "line-1\r\nline-2\n\tline-3"
            };
            envelope.Metadata["note"] = "with\nnewline";

            var json = JsonSerializer.Serialize(envelope);

            Assert.IsFalse(json.Contains("\n"), "序列化结果不得含裸换行");
            Assert.IsFalse(json.Contains("\r"), "序列化结果不得含裸回车");

            var restored = JsonSerializer.Deserialize<AgentBrokerRequestEnvelope>(json);
            Assert.AreEqual(envelope.PayloadJson, restored.PayloadJson);
            Assert.AreEqual("with\nnewline", restored.Metadata["note"]);
        }

        [TestMethod]
        public void SerializedEnvelope_SurvivesNonAsciiPayload()
        {
            var envelope = new AgentBrokerRequestEnvelope
            {
                RequestId = "req-5",
                MessageType = AgentBrokerMessageTypes.SeedConversationRequest,
                PayloadJson = "{\"text\":\"中文 · Emoji 🚀 · \\\"quoted\\\"\"}"
            };

            var restored = JsonSerializer.Deserialize<AgentBrokerRequestEnvelope>(JsonSerializer.Serialize(envelope));

            Assert.AreEqual(envelope.PayloadJson, restored.PayloadJson);
        }

        [TestMethod]
        public void Deserialize_KeepsConstructorDefaultsForMissingFields()
        {
            var restored = JsonSerializer.Deserialize<AgentBrokerRequestEnvelope>("{\"RequestId\":\"req-6\"}");

            Assert.AreEqual("req-6", restored.RequestId);
            Assert.AreEqual(string.Empty, restored.MessageType);
            Assert.IsNotNull(restored.Metadata);
        }

        // ---- 消息类型常量 ----

        private static IReadOnlyList<FieldInfo> MessageTypeConstants()
        {
            return typeof(AgentBrokerMessageTypes)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .ToList();
        }

        [TestMethod]
        public void MessageTypes_AreAllNonEmpty()
        {
            var fields = MessageTypeConstants();

            Assert.IsTrue(fields.Count >= 10, "消息类型常量数量异常：" + fields.Count);
            foreach (var field in fields)
            {
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace((string)field.GetRawConstantValue()),
                    field.Name + " 不能为空");
            }
        }

        [TestMethod]
        public void MessageTypes_AreMutuallyDistinct()
        {
            var values = MessageTypeConstants()
                .Select(f => (string)f.GetRawConstantValue())
                .ToList();

            CollectionAssert.AllItemsAreUnique(values, "消息类型常量存在重复，会导致路由歧义");
        }

        [TestMethod]
        public void MessageTypes_UseLowerCaseDottedNames()
        {
            foreach (var field in MessageTypeConstants())
            {
                var value = (string)field.GetRawConstantValue();
                Assert.AreEqual(value.ToLowerInvariant(), value, field.Name + " 应为全小写");
                Assert.IsTrue(value.IndexOf('.') > 0, field.Name + " 应为点分命名：" + value);
                Assert.AreEqual(value.Trim(), value, field.Name + " 不应含首尾空白");
            }
        }

        [TestMethod]
        public void MessageTypes_RequestResponsePairsAreConsistent()
        {
            var pairs = new[]
            {
                Tuple.Create(AgentBrokerMessageTypes.ExecuteTurnRequest, AgentBrokerMessageTypes.ExecuteTurnResponse),
                Tuple.Create(AgentBrokerMessageTypes.CancelTurnRequest, AgentBrokerMessageTypes.CancelTurnResponse),
                Tuple.Create(AgentBrokerMessageTypes.SeedConversationRequest, AgentBrokerMessageTypes.SeedConversationResponse),
                Tuple.Create(AgentBrokerMessageTypes.ClearExecutionModePreferenceRequest, AgentBrokerMessageTypes.ClearExecutionModePreferenceResponse),
                Tuple.Create(AgentBrokerMessageTypes.ListConversationsRequest, AgentBrokerMessageTypes.ListConversationsResponse),
                Tuple.Create(AgentBrokerMessageTypes.LoadConversationRequest, AgentBrokerMessageTypes.LoadConversationResponse),
                Tuple.Create(AgentBrokerMessageTypes.DeleteConversationRequest, AgentBrokerMessageTypes.DeleteConversationResponse),
                Tuple.Create(AgentBrokerMessageTypes.CompleteRequest, AgentBrokerMessageTypes.CompleteResponse),
                Tuple.Create(AgentBrokerMessageTypes.PingRequest, AgentBrokerMessageTypes.PingResponse),
                Tuple.Create(AgentBrokerMessageTypes.ShutdownRequest, AgentBrokerMessageTypes.ShutdownResponse)
            };

            foreach (var pair in pairs)
            {
                Assert.IsTrue(pair.Item1.EndsWith(".request", StringComparison.Ordinal), pair.Item1);
                Assert.IsTrue(pair.Item2.EndsWith(".response", StringComparison.Ordinal), pair.Item2);
                Assert.AreEqual(
                    pair.Item1.Substring(0, pair.Item1.Length - ".request".Length),
                    pair.Item2.Substring(0, pair.Item2.Length - ".response".Length),
                    "请求/响应常量前缀应一致");
            }
        }
    }
}
