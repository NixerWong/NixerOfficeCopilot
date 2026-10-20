using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Excel;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelAssertionContractsTests
    {
        [TestMethod]
        public void VerificationPlanSerializesAllAssertionShapesAndBasis()
        {
            var plan = new ExcelVerificationPlan
            {
                Basis = new ExcelVerificationBasis
                {
                    Source = new ExcelSourceRef
                    {
                        WorksheetName = "Sales",
                        TableName = "Transactions"
                    },
                    HeaderMode = "no",
                    Note = "Exclude returns"
                }
            };
            plan.Basis.Filters.Add(new CalcFilter
            {
                Column = "Year",
                Operator = "eq",
                Value = "2025"
            });
            plan.Basis.GroupBy.Add("Department");
            plan.Basis.Measures.Add(new ExcelVerificationMeasure
            {
                Column = "Amount",
                Function = "sum"
            });
            plan.Basis.DerivedGroupBy.Add(new ExcelVerificationDerivedGroup
            {
                Column = "Birthday",
                Transform = "month",
                Alias = "BirthMonth"
            });
            plan.Assertions.Add(new ExcelAssertion
            {
                Id = "total",
                Kind = "scalar",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "_wip_summary",
                    Column = "Total",
                    Function = "sum"
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Sales",
                    Column = "Amount",
                    Function = "sum",
                    ExcludeRows = { 12 }
                },
                Tolerance = 0.01d
            });
            plan.Assertions.Add(new ExcelAssertion
            {
                Id = "byDepartment",
                Kind = "groupMatch",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "_wip_summary",
                    KeyColumn = "Department",
                    ValueColumn = "Total"
                },
                Source = new ExcelAssertionSide
                {
                    WorksheetName = "Sales",
                    KeyColumn = "Department",
                    ValueColumn = "Amount",
                    Function = "sum",
                    ExcludeLabelRows = true
                }
            });
            plan.Assertions.Add(new ExcelAssertion
            {
                Id = "ordered",
                Kind = "sorted",
                Target = new ExcelAssertionSide
                {
                    WorksheetName = "_wip_summary",
                    Column = "Total"
                },
                Direction = "descending"
            });

            var json = Serialize(plan);
            var copy = Deserialize(json);

            StringAssert.Contains(json, "\"assertions\"");
            StringAssert.Contains(json, "\"groupMatch\"");
            StringAssert.Contains(json, "\"keyColumn\"");
            Assert.AreEqual("Transactions", copy.Basis.Source.TableName);
            Assert.AreEqual("no", copy.Basis.HeaderMode);
            Assert.AreEqual("2025", copy.Basis.Filters[0].Value);
            Assert.AreEqual("Department", copy.Basis.GroupBy[0]);
            Assert.AreEqual("sum", copy.Basis.Measures[0].Function);
            Assert.AreEqual("Birthday", copy.Basis.DerivedGroupBy[0].Column);
            Assert.AreEqual("month", copy.Basis.DerivedGroupBy[0].Transform);
            Assert.AreEqual("BirthMonth", copy.Basis.DerivedGroupBy[0].Alias);
            Assert.AreEqual(3, copy.Assertions.Count);
            Assert.AreEqual(0.01d, copy.Assertions[0].Tolerance.Value, 0d);
            Assert.AreEqual("Amount", copy.Assertions[1].Source.ValueColumn);
            Assert.AreEqual(12, copy.Assertions[0].Source.ExcludeRows[0]);
            Assert.IsTrue(copy.Assertions[1].Source.ExcludeLabelRows);
            Assert.AreEqual("descending", copy.Assertions[2].Direction);
        }

        [TestMethod]
        public void DeserializingOmittedCollectionsInitializesSafeDefaults()
        {
            const string json = "{\"assertions\":[{\"kind\":\"rowCount\",\"target\":{\"worksheetName\":\"Output\"}}]}";

            var plan = Deserialize(json);

            Assert.IsNotNull(plan.Basis);
            Assert.IsNotNull(plan.Basis.Filters);
            Assert.IsNotNull(plan.Basis.GroupBy);
            Assert.IsNotNull(plan.Basis.Measures);
            Assert.IsNotNull(plan.Basis.DerivedGroupBy);
            Assert.AreEqual("auto", plan.Basis.HeaderMode);
            Assert.AreEqual(1, plan.Assertions.Count);
            Assert.IsNotNull(plan.Assertions[0].Target.Columns);
            Assert.IsNotNull(plan.Assertions[0].Target.Filters);
            Assert.IsNotNull(plan.Assertions[0].Target.ExcludeRows);
            Assert.IsFalse(plan.Assertions[0].Target.ExcludeLabelRows);
            Assert.AreEqual("Output", plan.Assertions[0].Target.WorksheetName);
            Assert.IsNull(plan.Assertions[0].Source);
            Assert.IsNull(plan.Assertions[0].Tolerance);
        }

        [TestMethod]
        public void AggregateVerificationBasis_PreservesDerivedGroupSemantics()
        {
            var request = new ExcelAggregateRequest
            {
                HeaderMode = "yes",
                DerivedGroupBy =
                {
                    new ExcelAggregateDerivedGroup
                    {
                        Column = "Salary",
                        Transform = "numberBucket",
                        Alias = "SalaryBand",
                        Size = 1000d
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                WorksheetName = "Employees"
            };

            var basis = ExcelHostAdapter.BuildAggregateVerificationBasis(request, result);

            Assert.AreEqual("Employees", basis.Source.WorksheetName);
            Assert.AreEqual("yes", basis.HeaderMode);
            Assert.AreEqual(1, basis.DerivedGroupBy.Count);
            Assert.AreEqual("Salary", basis.DerivedGroupBy[0].Column);
            Assert.AreEqual("numberBucket", basis.DerivedGroupBy[0].Transform);
            Assert.AreEqual("SalaryBand", basis.DerivedGroupBy[0].Alias);
            Assert.AreEqual(1000d, basis.DerivedGroupBy[0].Size);
        }

        [TestMethod]
        public void AggregateVerificationBasis_RecordsUnionSourcesAndTopology()
        {
            var request = new ExcelAggregateRequest
            {
                Sources =
                {
                    new ExcelAggregateSource
                    {
                        WorksheetName = "Jan",
                        Address = "A1:B4",
                        Alias = "jan"
                    },
                    new ExcelAggregateSource
                    {
                        WorksheetName = "Feb",
                        Address = "A1:B5",
                        Alias = "feb"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                SourceKind = "union",
                SourceSummaries =
                {
                    new ExcelAggregateSourceSummary
                    {
                        Alias = "jan",
                        WorksheetName = "Jan",
                        Address = "$A$1:$B$4",
                        TotalRowCount = 3
                    },
                    new ExcelAggregateSourceSummary
                    {
                        Alias = "feb",
                        WorksheetName = "Feb",
                        Address = "$A$1:$B$5",
                        TotalRowCount = 4
                    }
                }
            };

            var basis = ExcelHostAdapter.BuildAggregateVerificationBasis(request, result);

            Assert.IsNull(basis.Source);
            Assert.AreEqual("union", basis.Topology);
            Assert.AreEqual(2, basis.Sources.Count);
            Assert.AreEqual("feb", basis.Sources[1].Alias);
            Assert.AreEqual("$A$1:$B$5", basis.Sources[1].Range);
        }

        [TestMethod]
        public void AggregateVerificationBasis_RecordsLookupProvenanceAndEffectiveJoins()
        {
            var request = new ExcelAggregateRequest
            {
                Lookups =
                {
                    new ExcelAggregateLookup
                    {
                        Source = new ExcelSourceRef { TableName = "CustomerTable" },
                        Key = "CustomerId",
                        ForeignKey = "CustomerId",
                        Columns = { "Region" }
                    },
                    new ExcelAggregateLookup
                    {
                        Source = new ExcelSourceRef
                        {
                            WorksheetName = "Tiers",
                            Address = "A1:B5"
                        },
                        Key = "TierId",
                        ForeignKey = "TierId",
                        Columns = { "TierName" },
                        Join = "inner",
                        OnDuplicateKey = "first"
                    }
                }
            };
            var result = new ExcelAggregateResult
            {
                LookupSummaries =
                {
                    new ExcelAggregateLookupSummary
                    {
                        WorksheetName = "Customers",
                        Address = "$A$1:$C$20",
                        TableName = "CustomerTable"
                    },
                    new ExcelAggregateLookupSummary
                    {
                        WorksheetName = "Tiers",
                        Address = "$A$1:$B$5"
                    }
                }
            };

            var basis = ExcelHostAdapter.BuildAggregateVerificationBasis(request, result);

            Assert.AreEqual("lookup", basis.Topology);
            Assert.IsNull(basis.Join);
            Assert.AreEqual(2, basis.Lookups.Count);
            Assert.AreEqual("Customers", basis.Lookups[0].Sheet);
            Assert.AreEqual("$A$1:$C$20", basis.Lookups[0].Range);
            Assert.AreEqual("CustomerTable", basis.Lookups[0].TableName);
            Assert.AreEqual("CustomerId", basis.Lookups[0].Key);
            Assert.AreEqual("CustomerId", basis.Lookups[0].ForeignKey);
            Assert.AreEqual("Region", basis.Lookups[0].Columns[0]);
            Assert.AreEqual("left", basis.Lookups[0].Join);
            Assert.AreEqual("error", basis.Lookups[0].OnDuplicateKey);
            Assert.AreEqual("inner", basis.Lookups[1].Join);
            Assert.AreEqual("first", basis.Lookups[1].OnDuplicateKey);

            using (var document = System.Text.Json.JsonDocument.Parse(SerializeDataContract(basis)))
            {
                var firstLookup = document.RootElement.GetProperty("lookups")[0];
                Assert.AreEqual("Customers", firstLookup.GetProperty("sheet").GetString());
                Assert.AreEqual("left", firstLookup.GetProperty("join").GetString());
            }
        }

        [TestMethod]
        public void VerificationEnvelopeSerializesCanonicalMetadataAndDetails()
        {
            var coverage = new ExcelCoverageReport
            {
                TotalRows = 2,
                ScannedRows = 2,
                Backend = "com"
            };
            var report = new ExcelVerificationReport
            {
                Passed = true,
                Tier = "A"
            };
            report.Items.Add(new ExcelAssertionOutcome
            {
                Id = "total",
                Kind = "scalar",
                Passed = true,
                Tier = "A",
                Independent = true,
                TargetBackend = "com",
                TargetCoverage = coverage,
                SourceCoverage = coverage
            });

            var verification = ExcelVerificationResult.FromReport(
                report,
                OfficeCopilot.HostBridge.Contracts.ToolResultVerificationFields.ProducerVba,
                false,
                new ExcelVerificationBasis
                {
                    Source = new ExcelSourceRef { WorksheetName = "Sales" },
                    Sources =
                    {
                        new ExcelVerificationSource
                        {
                            Sheet = "Sales",
                            Range = "A1:C20",
                            Alias = "sales"
                        },
                        new ExcelVerificationSource
                        {
                            Sheet = "Returns",
                            Range = "A1:C8",
                            Alias = "returns"
                        }
                    },
                    Topology = "union",
                    Keys = { "OrderId" },
                    Join = "left"
                });
            var envelope = new ExcelVerificationToolResult
            {
                Verification = verification
            };
            var json = SerializeVerificationEnvelope(envelope);
            var copy = DeserializeVerificationEnvelope(json);

            using (var document = System.Text.Json.JsonDocument.Parse(json))
            {
                var element = document.RootElement.GetProperty("verification");
                Assert.AreEqual("A", element.GetProperty("tier").GetString());
                Assert.IsTrue(element.GetProperty("passed").GetBoolean());
                Assert.AreEqual(1, element.GetProperty("assertionCount").GetInt32());
                Assert.AreEqual(2, element.GetProperty("coveredRows").GetInt64());
                Assert.IsTrue(element.GetProperty("independent").GetBoolean());
                Assert.IsFalse(element.GetProperty("sampled").GetBoolean());
                Assert.AreEqual("vba", element.GetProperty("producer").GetString());
                Assert.AreEqual("Sales", element.GetProperty("basis")
                    .GetProperty("source").GetProperty("worksheetName").GetString());
                var basis = element.GetProperty("basis");
                Assert.AreEqual("union", basis.GetProperty("topology").GetString());
                Assert.AreEqual("OrderId", basis.GetProperty("keys")[0].GetString());
                Assert.AreEqual("left", basis.GetProperty("join").GetString());
                Assert.AreEqual("returns", basis.GetProperty("sources")[1]
                    .GetProperty("alias").GetString());
                Assert.IsTrue(element.TryGetProperty("details", out _));
            }

            Assert.AreEqual(1, copy.Verification.AssertionCount);
            Assert.AreEqual("vba", copy.Verification.Producer);
            Assert.AreEqual(2, copy.Verification.Basis.Sources.Count);
            Assert.AreEqual("total", copy.Verification.Details.Items[0].Id);

            var omitted = DeserializeVerificationEnvelope("{\"verification\":{}}");
            Assert.AreEqual("C", omitted.Verification.Tier);
            Assert.AreEqual("unknown", omitted.Verification.Producer);
            Assert.IsNotNull(omitted.Verification.Notes);
        }

        [TestMethod]
        public void AggregateAndReconcileResultsExposeTheCanonicalVerificationField()
        {
            var verification = ExcelVerificationResult.FromReport(
                new ExcelVerificationReport { Passed = true, Tier = "B" },
                OfficeCopilot.HostBridge.Contracts.ToolResultVerificationFields.ProducerManaged,
                false,
                new ExcelVerificationBasis());
            var aggregateJson = SerializeDataContract(new ExcelAggregateResult
            {
                Verification = verification
            });
            var reconcileJson = SerializeDataContract(new ExcelReconcileResult
            {
                Verification = verification
            });

            using (var document = System.Text.Json.JsonDocument.Parse(aggregateJson))
            {
                Assert.IsTrue(document.RootElement.GetProperty("verification")
                    .GetProperty("passed").GetBoolean());
            }

            using (var document = System.Text.Json.JsonDocument.Parse(reconcileJson))
            {
                Assert.AreEqual("managed", document.RootElement.GetProperty("verification")
                    .GetProperty("producer").GetString());
            }
        }

        private static string Serialize(ExcelVerificationPlan plan)
        {
            var serializer = new DataContractJsonSerializer(typeof(ExcelVerificationPlan));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, plan);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static string SerializeVerificationEnvelope(ExcelVerificationToolResult result)
        {
            return SerializeDataContract(result);
        }

        private static string SerializeDataContract<T>(T result)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, result);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static ExcelVerificationToolResult DeserializeVerificationEnvelope(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(ExcelVerificationToolResult));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (ExcelVerificationToolResult)serializer.ReadObject(stream);
            }
        }

        private static ExcelVerificationPlan Deserialize(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(ExcelVerificationPlan));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (ExcelVerificationPlan)serializer.ReadObject(stream);
            }
        }
    }
}
