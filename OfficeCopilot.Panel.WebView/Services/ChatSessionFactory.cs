using System;
using System.Collections.Generic;
using OfficeCopilot.Panel.WebView.Models;

namespace OfficeCopilot.Panel.WebView.Services
{
    /// <summary>
    /// 新建会话的工厂。会话持久化由 Agent.Broker 的 JSONL 存储负责，Panel 侧不落盘。
    /// </summary>
    public static class ChatSessionFactory
    {
        public const string DefaultNewSessionTitle = "新会话";

        public static ChatSessionState CreateNewSession()
        {
            return new ChatSessionState
            {
                Id = Guid.NewGuid(),
                Title = DefaultNewSessionTitle,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Messages = new List<ChatMessageState>()
            };
        }
    }
}
