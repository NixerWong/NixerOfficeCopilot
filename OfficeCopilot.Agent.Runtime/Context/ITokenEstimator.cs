using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OfficeCopilot.Agent.Persistence;

namespace OfficeCopilot.Agent.Runtime.Context
{
    /// <summary>
    /// Token 估算器接口。阶段 1.6 引入 <c>Microsoft.ML.Tokenizers</c> 后提供精确实现。
    /// 当前默认实现为 CJK 感知的粗估器，见 <see cref="RoughTokenEstimator"/>。
    /// </summary>
    internal interface ITokenEstimator
    {
        /// <summary>
        /// 估算一段文本的 token 数。
        /// </summary>
        int EstimateTokens(string text);

        /// <summary>
        /// 估算单条消息的 token 数（含 role + content 开销）。
        /// </summary>
        int EstimateMessageTokens(BrokerConversationMessage message);

        /// <summary>
        /// 估算整个会话的 token 占用。
        /// </summary>
        int EstimateConversationTokens(BrokerConversationState conversation);
    }

    /// <summary>
    /// CJK 感知的粗估器：拉丁/ASCII 文本按 len/4 估算，CJK 与全角字符按每字符 1 token 估算。
    /// 主流 BPE 分词器下一个汉字通常占 0.6~1 token，沿用 len/4 会系统性低估 2~3 倍，
    /// 导致该压缩时不压缩、最终撞上下文上限。此处取偏保守（偏高）的系数。
    /// </summary>
    internal sealed class RoughTokenEstimator : ITokenEstimator
    {
        private const double LatinCharsPerToken = 4.0;
        private const double CjkTokensPerChar = 1.0;

        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var cjkCount = 0;
            var otherCount = 0;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];

                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    // 补充平面（含 CJK 扩展 B 及以上）按一个表意字符计。
                    cjkCount++;
                    i++;
                    continue;
                }

                if (IsWideCharacter(c))
                {
                    cjkCount++;
                }
                else
                {
                    otherCount++;
                }
            }

            var tokens = (cjkCount * CjkTokensPerChar) + (otherCount / LatinCharsPerToken);
            return (int)Math.Round(tokens, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// 判定字符是否属于 CJK 表意文字、假名、谚文或全角符号。
        /// </summary>
        private static bool IsWideCharacter(char c)
        {
            if (c < 0x2E80)
            {
                return false;
            }

            return (c >= 0x2E80 && c <= 0x303F)   // CJK 部首扩展 / 康熙部首 / CJK 符号与标点
                || (c >= 0x3040 && c <= 0x30FF)   // 平假名 / 片假名
                || (c >= 0x3100 && c <= 0x318F)   // 注音符号 / 谚文兼容字母
                || (c >= 0x31A0 && c <= 0x31BF)   // 注音符号扩展
                || (c >= 0x3400 && c <= 0x4DBF)   // CJK 扩展 A
                || (c >= 0x4E00 && c <= 0x9FFF)   // CJK 统一表意文字
                || (c >= 0xA000 && c <= 0xA4CF)   // 彝文
                || (c >= 0xAC00 && c <= 0xD7AF)   // 谚文音节
                || (c >= 0xF900 && c <= 0xFAFF)   // CJK 兼容表意文字
                || (c >= 0xFE30 && c <= 0xFE4F)   // CJK 兼容形式
                || (c >= 0xFF00 && c <= 0xFF60)   // 全角 ASCII 变体
                || (c >= 0xFFE0 && c <= 0xFFE6);  // 全角货币等符号
        }

        public int EstimateMessageTokens(BrokerConversationMessage message)
        {
            if (message == null)
            {
                return 0;
            }

            var tokens = 0;
            tokens += EstimateTokens(message.Content ?? string.Empty);

            // Tool name (for tool_call messages this is the tool identifier)
            if (!string.IsNullOrWhiteSpace(message.Name))
            {
                tokens += EstimateTokens(message.Name);
            }

            // Structured content (tool results, etc.)
            if (!string.IsNullOrWhiteSpace(message.StructuredContentJson))
            {
                tokens += EstimateTokens(message.StructuredContentJson);
            }

            // Per-message overhead (role + formatting): ~4 tokens
            return tokens + 4;
        }

        public int EstimateConversationTokens(BrokerConversationState conversation)
        {
            if (conversation == null || conversation.Messages == null)
            {
                return 0;
            }

            var tokens = 0;
            foreach (var message in conversation.Messages)
            {
                if (message == null)
                {
                    continue;
                }

                tokens += EstimateMessageTokens(message);
            }

            return tokens;
        }
    }
}
