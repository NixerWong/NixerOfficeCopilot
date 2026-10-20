using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Agent.Runtime.Turn
{
    /// <summary>
    /// D-2：Runtime 工具目录直接复用线上模型 <see cref="HostBridgeToolDescriptor"/>，
    /// 本工厂只负责补齐原 <c>RuntimeToolDefinition</c> 构造函数里的归一化逻辑。
    /// </summary>
    internal static class RuntimeToolDescriptors
    {
        public static HostBridgeToolDescriptor Create(
            string name,
            string description,
            bool isReadOnly,
            bool requiresConfirmation,
            bool requiresBackup,
            string riskLevel,
            string inputSchemaJson,
            bool requiresUserInteraction = false,
            string maxWriteEffect = null)
        {
            return new HostBridgeToolDescriptor
            {
                Name = name ?? string.Empty,
                Description = description ?? string.Empty,
                IsReadOnly = isReadOnly,
                RequiresConfirmation = requiresConfirmation,
                RequiresBackup = requiresBackup,
                RequiresUserInteraction = requiresUserInteraction,
                RiskLevel = string.IsNullOrWhiteSpace(riskLevel) ? (isReadOnly ? "low" : "high") : riskLevel,
                MaxWriteEffect = string.IsNullOrWhiteSpace(maxWriteEffect)
                    ? (isReadOnly
                        ? OfficeCopilot.Agent.Abstractions.ToolWriteEffectLevels.None
                        : OfficeCopilot.Agent.Abstractions.ToolWriteEffectLevels.Modify)
                    : maxWriteEffect,
                InputSchemaJson = DefaultToolDispatcher.NormalizeToolSchemaJson(
                    inputSchemaJson,
                    DefaultToolDispatcher.EmptyObjectSchemaJson)
            };
        }
    }
}
