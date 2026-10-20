using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OfficeCopilot.Agent.Client;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Persistence.Jsonl;
using OfficeCopilot.Agent.Planning;
using OfficeCopilot.Agent.Runtime.Context;
using OfficeCopilot.Agent.Runtime.Turn;
using OfficeCopilot.HostBridge.Client;
using OfficeCopilot.Skills;
using System;

namespace OfficeCopilot.Agent.Runtime.Composition
{
    /// <summary>
    /// Broker 运行时的唯一装配入口。所有依赖在此显式登记，
    /// 取代 <see cref="AgentRuntimeService"/> 过去的 4 个构造重载与内部 <c>new</c> 兜底。
    /// </summary>
    public static class AgentRuntimeServiceCollectionExtensions
    {
        /// <summary>
        /// 登记 Broker 运行时的默认实现。调用方可在此之前 <c>AddSingleton</c> 覆盖任一契约
        /// （全部使用 <c>TryAdd</c>），例如注入真实的 <see cref="IHostBridgeClient"/> 或测试替身。
        /// </summary>
        public static IServiceCollection AddOfficeCopilotAgentRuntime(
            this IServiceCollection services,
            AgentRuntimeOptions options)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton(options ?? new AgentRuntimeOptions());
            services.TryAddSingleton<IHostBridgeClient>(_ => new NullHostBridgeClient());
            services.TryAddSingleton<IHostContextReader>(sp =>
                new HostBridgeContextReader(sp.GetRequiredService<IHostBridgeClient>()));
            services.TryAddSingleton<IHostToolInvoker>(sp =>
                new HostBridgeToolInvoker(sp.GetRequiredService<IHostBridgeClient>()));
            services.TryAddSingleton<IModelClient>(_ => new HttpModelClient());
            services.TryAddSingleton<IUserInteractionHandler>(_ => new NullUserInteractionHandler());
            services.TryAddSingleton<BrokerEventDispatcher>();
            services.TryAddSingleton<IConversationStore>(sp =>
                CreateConversationStore(sp.GetRequiredService<AgentRuntimeOptions>()));
            services.TryAddSingleton<ISkillRegistry>(sp =>
            {
                var hostApplicationName = sp.GetRequiredService<AgentRuntimeOptions>().HostApplicationName;
                return new ReloadingSkillRegistry(
                    () => LoadSkillRegistry(hostApplicationName),
                    SkillStorageLocations.GetHostDirectory(hostApplicationName));
            });

            // IMemoryStore / IMcpRegistry 允许缺席：只在可用时登记，消费方用 GetService 取。
            // 登记 null 会让 MEDI 抛异常，故不能用工厂返回 null 的写法。
            if (!services.IsRegistered(typeof(OfficeCopilot.Memory.IMemoryStore)))
            {
                var memoryStore = CreateMemoryStore();
                if (memoryStore != null)
                {
                    services.AddSingleton(memoryStore);
                }
            }

            services.TryAddSingleton(sp => CreateContextWindowPolicy(
                sp.GetRequiredService<AgentRuntimeOptions>(),
                sp.GetRequiredService<IModelClient>()));

            services.TryAddSingleton(sp => new BrokerTurnExecutor(
                sp.GetRequiredService<AgentRuntimeOptions>(),
                sp.GetRequiredService<BrokerEventDispatcher>(),
                sp.GetRequiredService<IHostContextReader>(),
                sp.GetRequiredService<IHostBridgeClient>(),
                sp.GetRequiredService<IHostToolInvoker>(),
                sp.GetRequiredService<IModelClient>(),
                sp.GetRequiredService<IContextWindowPolicy>(),
                sp.GetRequiredService<ISkillRegistry>(),
                sp.GetService<OfficeCopilot.Memory.IMemoryStore>(),
                sp.GetService<OfficeCopilot.Mcp.IMcpRegistry>(),
                sp.GetRequiredService<IUserInteractionHandler>()));

            services.TryAddSingleton<IAgentRuntime>(sp => new AgentRuntimeService(
                sp.GetRequiredService<AgentRuntimeOptions>(),
                sp.GetRequiredService<IHostContextReader>(),
                sp.GetRequiredService<IConversationStore>(),
                sp.GetRequiredService<IModelClient>(),
                sp.GetRequiredService<BrokerTurnExecutor>(),
                sp.GetService<OfficeCopilot.Mcp.IMcpRegistry>()));

            return services;
        }

        private static bool IsRegistered(this IServiceCollection services, Type serviceType)
        {
            for (var i = 0; i < services.Count; i++)
            {
                if (services[i].ServiceType == serviceType)
                {
                    return true;
                }
            }

            return false;
        }

        private static IContextWindowPolicy CreateContextWindowPolicy(AgentRuntimeOptions options, IModelClient modelClient)
        {
            return options.AutoCompactEnabled || options.MicroCompactEnabled
                ? (IContextWindowPolicy)new CompositeContextWindowPolicy(
                    new RoughTokenEstimator(),
                    options.ContextWindowSize,
                    modelClient)
                : NullContextWindowPolicy.Instance;
        }

        private static IConversationStore CreateConversationStore(AgentRuntimeOptions options)
        {
            switch (options.StorageMode)
            {
                case ConversationStorageMode.Jsonl:
                    var hostScope = ConversationScope.ResolveHostScope(options.HostApplicationName);
                    if (hostScope == ConversationScope.Unscoped)
                    {
                        Console.Error.WriteLine(
                            "[agent-runtime] 宿主名为空或不合法，会话使用 _unscoped 作用域。HostApplicationName='"
                            + (options.HostApplicationName ?? string.Empty)
                            + "'");
                    }

                    return new JsonlConversationStore(options.JsonlRootDirectory, hostScope);
                case ConversationStorageMode.InMemory:
                default:
                    return new InMemoryConversationStore();
            }
        }

        private static OfficeCopilot.Memory.IMemoryStore CreateMemoryStore()
        {
            try
            {
                return new OfficeCopilot.Memory.FileSystemMemoryStore();
            }
            catch (Exception ex)
            {
                SkillLog.Write($"[AgentRuntime] Memory store unavailable, long-term memory disabled: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 三层 Skill 注册：C# 硬编码默认值 → 随安装包分发的 BuiltInSkills → 用户文件系统 SKILL.md。
        /// 同名后加载覆盖先加载。由 <see cref="ReloadingSkillRegistry"/> 在用户 SKILL.md 变化时重新调用。
        /// </summary>
        private static InMemorySkillRegistry LoadSkillRegistry(string hostApplicationName)
        {
            var totalTimer = System.Diagnostics.Stopwatch.StartNew();
            var registry = new InMemorySkillRegistry(OfficeSkillCatalog.CreateDefaultSkills());
            SkillLog.Write($"[AgentRuntime] Layer1 hardcoded: {registry.GetSkills().Count} skills");

            var layerTimer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var builtInLoader = new BuiltInSkillLoader();
                var builtInFiles = builtInLoader.Load();

                if (builtInFiles != null)
                {
                    foreach (var parsed in builtInFiles)
                    {
                        var definition = parsed.ToSkillDefinition();
                        if (definition != null)
                        {
                            LogIfOverride(registry, definition, "built-in");
                            registry.AddOrUpdate(definition);
                            SkillLog.Write($"[AgentRuntime] Layer2 built-in: registered '{definition.Name}'");
                        }
                    }
                }

                SkillLog.Write($"[AgentRuntime] Layer2 built-in: {builtInFiles?.Count ?? 0} skills from {builtInLoader.SourceDescription} in {layerTimer.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                SkillLog.Write($"[AgentRuntime] Layer2 built-in skill load FAILED, shipped skills are unavailable: {ex}");
            }

            layerTimer.Restart();
            try
            {
                foreach (var legacyFolder in SkillStorageLocations.FindLegacyFlatSkillFolders())
                {
                    SkillLog.Write($"[AgentRuntime] WARNING: user skill folder '{legacyFolder}' sits in the shared skills root and is ignored; move it under a host folder (excel / word / powerpoint).");
                }

                var loader = new FileSystemSkillLoader(
                    SkillStorageLocations.GetHostDirectory(hostApplicationName),
                    SkillStorageLocations.GetHostFolderName(hostApplicationName));
                var parsedFiles = loader.Load();

                if (parsedFiles != null)
                {
                    foreach (var parsed in parsedFiles)
                    {
                        var definition = parsed.ToSkillDefinition();
                        if (definition != null)
                        {
                            LogIfOverride(registry, definition, "user");
                            registry.AddOrUpdate(definition);
                        }
                    }
                }

                SkillLog.Write($"[AgentRuntime] Layer3 user: {parsedFiles?.Count ?? 0} skills from {loader.SourceDescription} in {layerTimer.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                SkillLog.Write($"[AgentRuntime] Layer3 user skill load FAILED, user SKILL.md files are unavailable: {ex}");
            }

            SkillLog.Write($"[AgentRuntime] Skill registry ready: {registry.GetSkills().Count} skills in {totalTimer.ElapsedMilliseconds} ms");
            return registry;
        }

        private static void LogIfOverride(ISkillRegistry registry, SkillDefinition definition, string layer)
        {
            if (!string.IsNullOrWhiteSpace(definition.Name) && registry.FindByName(definition.Name) != null)
            {
                SkillLog.Write($"[AgentRuntime] WARNING: {layer} skill '{definition.Name}' overrides an earlier skill with the same name.");
            }
        }
    }
}
