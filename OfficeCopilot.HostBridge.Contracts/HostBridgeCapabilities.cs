using System;
using System.Collections.Generic;

namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class HostBridgeCapabilities
    {
        public HostBridgeCapabilities()
        {
            HostApplicationName = string.Empty;
            HostVersion = string.Empty;
            ToolNames = new List<string>();
            Tools = new List<HostBridgeToolDescriptor>();
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SupportsWriteEffectPreflight = false;
        }

        public string HostApplicationName { get; set; }

        public string HostVersion { get; set; }

        public bool CanInsertText { get; set; }

        public bool CanExecuteCode { get; set; }

        public bool CanCreateDocumentBackup { get; set; }

        public IList<string> ToolNames { get; set; }

        public IList<HostBridgeToolDescriptor> Tools { get; set; }

        public bool SupportsWriteEffectPreflight { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }

    public sealed class HostBridgeToolDescriptor
    {
        public HostBridgeToolDescriptor()
        {
            Name = string.Empty;
            DisplayName = string.Empty;
            Description = string.Empty;
            InputSchemaJson = "{}";
            RiskLevel = "low";
            MaxWriteEffect = string.Empty;
            RequiresWriteEffectPreflight = false;
        }

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Description { get; set; }

        public string InputSchemaJson { get; set; }

        public bool IsReadOnly { get; set; }

        public bool RequiresConfirmation { get; set; }

        public bool RequiresBackup { get; set; }

        /// <summary>调度器遇到此标记会暂停 Agent 循环，转而向 Panel 发起交互请求。</summary>
        public bool RequiresUserInteraction { get; set; }

        public string RiskLevel { get; set; }

        public string MaxWriteEffect { get; set; }

        public bool RequiresWriteEffectPreflight { get; set; }

        public int TimeoutSeconds { get; set; }
    }
}
