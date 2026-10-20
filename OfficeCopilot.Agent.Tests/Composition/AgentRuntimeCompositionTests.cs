using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Agent.Persistence;
using OfficeCopilot.Agent.Runtime;
using OfficeCopilot.Agent.Runtime.Composition;
using System;

namespace OfficeCopilot.Agent.Tests.Composition
{
    /// <summary>
    /// 装配根的冒烟测试：DI 图配错只会在 Broker 启动时暴露，这里把它提前到构建期。
    /// </summary>
    [TestClass]
    public class AgentRuntimeCompositionTests
    {
        private static ServiceCollection CreateServices()
        {
            return new ServiceCollection();
        }

        [TestMethod]
        public void AddOfficeCopilotAgentRuntime_ResolvesAgentRuntime()
        {
            var services = CreateServices();
            services.AddOfficeCopilotAgentRuntime(new AgentRuntimeOptions
            {
                StorageMode = ConversationStorageMode.InMemory
            });

            using (var provider = services.BuildServiceProvider())
            {
                var runtime = provider.GetRequiredService<IAgentRuntime>();
                Assert.IsNotNull(runtime);
                Assert.AreEqual(ConversationStorageMode.InMemory, runtime.Options.StorageMode);
            }
        }

        [TestMethod]
        public void AddOfficeCopilotAgentRuntime_RegistersSingletons()
        {
            var services = CreateServices();
            services.AddOfficeCopilotAgentRuntime(null);

            using (var provider = services.BuildServiceProvider())
            {
                Assert.AreSame(
                    provider.GetRequiredService<IAgentRuntime>(),
                    provider.GetRequiredService<IAgentRuntime>());
            }
        }

        [TestMethod]
        public void AddOfficeCopilotAgentRuntime_KeepsCallerRegistrations()
        {
            var services = CreateServices();
            var store = new InMemoryConversationStore();
            services.AddSingleton<IConversationStore>(store);

            services.AddOfficeCopilotAgentRuntime(new AgentRuntimeOptions());

            using (var provider = services.BuildServiceProvider())
            {
                Assert.AreSame(store, provider.GetRequiredService<IConversationStore>());
            }
        }

        [TestMethod]
        public void AddOfficeCopilotAgentRuntime_NullServices_Throws()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => AgentRuntimeServiceCollectionExtensions.AddOfficeCopilotAgentRuntime(null, new AgentRuntimeOptions()));
        }

        [TestMethod]
        public void AddOfficeCopilotAgentRuntime_NullOptions_UsesDefaults()
        {
            var services = CreateServices();
            services.AddOfficeCopilotAgentRuntime(null);

            using (var provider = services.BuildServiceProvider())
            {
                Assert.IsNotNull(provider.GetRequiredService<IAgentRuntime>().Options);
            }
        }
    }
}
