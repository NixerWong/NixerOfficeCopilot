using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel.Tools;

namespace OfficeCopilot.Agent.Tests.Excel.Verification
{
    [TestClass]
    public sealed class ExcelCompareKeySetsToolTests
    {
        [TestMethod]
        public void ExcelCompareKeySets_IsRegisteredReadOnlyWithBoundedSamples()
        {
            var tool = ExcelBuiltInToolCatalog.CreateDataTools()
                .Single(candidate => candidate.Definition.Name == "excel_compare_key_sets");

            Assert.IsTrue(tool.Definition.IsReadOnly);
            Assert.IsFalse(tool.Definition.RequiresConfirmation);
            using (var schema = JsonDocument.Parse(tool.Definition.InputSchemaJson))
            {
                var root = schema.RootElement;
                var properties = root.GetProperty("properties");
                CollectionAssert.AreEqual(
                    new[] { "target", "source" },
                    root.GetProperty("required")
                        .EnumerateArray()
                        .Select(value => value.GetString())
                        .ToArray());
                Assert.AreEqual(
                    10,
                    properties.GetProperty("sampleCount").GetProperty("maximum").GetInt32());
                Assert.AreEqual(
                    1,
                    properties.GetProperty("sampleCount").GetProperty("minimum").GetInt32());
                var sideProperties = root.GetProperty("definitions")
                    .GetProperty("side").GetProperty("properties");
                Assert.IsTrue(sideProperties.TryGetProperty("keyColumn", out _));
                Assert.IsTrue(sideProperties.TryGetProperty("filters", out _));
            }
        }

        [TestMethod]
        public void RequestDeserializationDefaultsToThreeSamplesAndAutoHeaders()
        {
            const string json = "{\"target\":{\"worksheetName\":\"Orders\","
                + "\"keyColumn\":\"CustomerID\"},\"source\":{\"worksheetName\":\"Customers\","
                + "\"keyColumn\":\"CustomerID\"}}";
            var serializer = new DataContractJsonSerializer(
                typeof(ExcelKeySetComparisonRequest));
            ExcelKeySetComparisonRequest request;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                request = (ExcelKeySetComparisonRequest)serializer.ReadObject(stream);
            }

            Assert.AreEqual(3, request.SampleCount);
            Assert.AreEqual("auto", request.HeaderMode);
            Assert.AreEqual("CustomerID", request.Target.KeyColumn);
            Assert.AreEqual("CustomerID", request.Source.KeyColumn);
        }

        [TestMethod]
        public void ResultSerializationKeepsCountsAndCanonicalVerificationEnvelope()
        {
            var result = new ExcelKeySetComparisonResult
            {
                TargetOnlyCount = 150,
                SourceOnlyCount = 150,
                MatchedCount = 2850,
                TotalUnmatchedCount = 300,
                TargetOnlySamples = new System.Collections.Generic.List<string>
                {
                    "C2851", "C2925", "C3000"
                },
                SourceOnlySamples = new System.Collections.Generic.List<string>
                {
                    "C3001", "C3075", "C3150"
                },
                Verification = new ExcelVerificationResult
                {
                    Tier = "A",
                    Passed = true,
                    AssertionCount = 0,
                    CoveredRows = 6000,
                    Independent = true,
                    Sampled = false,
                    Producer = "model",
                    Operation = "keySetDifference"
                }
            };
            var serializer = new DataContractJsonSerializer(
                typeof(ExcelKeySetComparisonResult));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, result);
                stream.Position = 0;
                using (var document = JsonDocument.Parse(stream))
                {
                    var root = document.RootElement;
                    Assert.AreEqual(150, root.GetProperty("targetOnlyCount").GetInt32());
                    Assert.AreEqual(150, root.GetProperty("sourceOnlyCount").GetInt32());
                    Assert.AreEqual(2850, root.GetProperty("matchedCount").GetInt32());
                    Assert.AreEqual(300, root.GetProperty("totalUnmatchedCount").GetInt32());
                    Assert.AreEqual(
                        "A",
                        root.GetProperty("verification").GetProperty("tier").GetString());
                    Assert.AreEqual(
                        "keySetDifference",
                        root.GetProperty("verification").GetProperty("operation").GetString());
                    Assert.AreEqual(
                        "C2925",
                        root.GetProperty("targetOnlySamples")[1].GetString());
                }
            }
        }
    }
}
