using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Settings;

namespace OfficeCopilot.Agent.Tests.Planning
{
    [TestClass]
    public class HostSystemPromptStoreTests
    {
        [TestMethod]
        public void IsUnmodifiedSeed_MatchesSeedIgnoringLineEndingsAndOuterWhitespace()
        {
            var seed = HostSystemPromptStore.ComputeSeedHash("第一行\n第二行");

            Assert.IsTrue(HostSystemPromptStore.IsUnmodifiedSeed("第一行\r\n第二行\r\n", seed));
            Assert.IsTrue(HostSystemPromptStore.IsUnmodifiedSeed("第一行\n第二行", seed.ToUpperInvariant() + "\n"));
        }

        [TestMethod]
        public void IsUnmodifiedSeed_EditedContentOrMissingSeed_IsTreatedAsCustom()
        {
            var seed = HostSystemPromptStore.ComputeSeedHash("第一行\n第二行");

            Assert.IsFalse(HostSystemPromptStore.IsUnmodifiedSeed("第一行\n第二行（用户修改）", seed));
            Assert.IsFalse(HostSystemPromptStore.IsUnmodifiedSeed("第一行\n第二行", null), "没有 .seed 的旧文件按用户自定义处理。");
            Assert.IsFalse(HostSystemPromptStore.IsUnmodifiedSeed("第一行\n第二行", " "));
        }
    }
}
