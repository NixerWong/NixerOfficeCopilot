using Microsoft.VisualStudio.TestTools.UnitTesting;
using OfficeCopilot.Host.Abstractions;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace OfficeCopilot.Agent.Tests.Excel
{
    [TestClass]
    public sealed class ExcelCalcContractsTests
    {
        [TestMethod]
        public void CalcQueryAndResultRoundTripThroughDataContractSerializer()
        {
            var query = new CalcQuery
            {
                Source = new ExcelSourceRef
                {
                    WorksheetName = "Data",
                    Address = "A1:C20",
                    TableName = "Sales"
                },
                Function = "sum",
                GroupByColumn = "Region",
                SortDirection = "descending"
            };
            query.Columns.Add("Amount");
            query.Filters.Add(new CalcFilter
            {
                Column = "Status",
                Operator = "eq",
                Value = "Closed"
            });
            query.ExcludeRows.Add(15);
            query.ExcludeLabelRows = true;
            var result = new CalcResult
            {
                Scalar = 42d
            };
            result.Groups.Add("West", 42d);
            result.Coverage.TotalRows = 20;
            result.Coverage.ScannedRows = 20;
            result.Coverage.CellCountsAvailable = false;
            result.Notes.Add("managed");
            result.Keys.Add("C-01");
            result.IsSorted = true;

            var queryCopy = RoundTrip(query);
            var resultCopy = RoundTrip(result);

            Assert.AreEqual("Sales", queryCopy.Source.TableName);
            Assert.AreEqual("Amount", queryCopy.Columns[0]);
            Assert.AreEqual("Closed", queryCopy.Filters[0].Value);
            Assert.AreEqual("descending", queryCopy.SortDirection);
            Assert.AreEqual(15, queryCopy.ExcludeRows[0]);
            Assert.IsTrue(queryCopy.ExcludeLabelRows);
            Assert.AreEqual(42d, resultCopy.Scalar.Value, 0d);
            Assert.AreEqual(42d, resultCopy.Groups["West"], 0d);
            Assert.AreEqual(20, resultCopy.Coverage.ScannedRows);
            Assert.AreEqual(false, resultCopy.Coverage.CellCountsAvailable);
            Assert.AreEqual("managed", resultCopy.Notes[0]);
            Assert.AreEqual("C-01", resultCopy.Keys[0]);
            Assert.AreEqual(true, resultCopy.IsSorted);
        }

        [TestMethod]
        public void ProfileRangeQualityFieldsDefaultWhenReadingOlderContracts()
        {
            const string legacyXml =
                "<ExcelProfileRangeResult xmlns=\"http://schemas.datacontract.org/2004/07/OfficeCopilot.Host.Abstractions\" />";
            var serializer = new DataContractSerializer(typeof(ExcelProfileRangeResult));
            using (var reader = XmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(legacyXml))))
            {
                var result = (ExcelProfileRangeResult)serializer.ReadObject(reader);
                Assert.IsNotNull(result.DataQualityFlags);
                Assert.AreEqual(0, result.DataQualityFlags.Count);
                Assert.IsNotNull(result.DataQualityNotes);
                Assert.AreEqual(0, result.DataQualityNotes.Count);
                Assert.AreEqual("single", result.Topology);
                Assert.IsNotNull(result.SourceProfiles);
            }
        }

        [TestMethod]
        public void MultiSourceProfileContractsRoundTripHeaderConsistency()
        {
            var request = new ExcelProfileRangeRequest
            {
                HeaderMode = "yes",
                Sources =
                {
                    new ExcelProfileRangeSource
                    {
                        WorksheetName = "Jan",
                        Address = "A1:C20",
                        Alias = "jan"
                    },
                    new ExcelProfileRangeSource
                    {
                        WorksheetName = "Feb",
                        Address = "A1:C18",
                        Alias = "feb"
                    }
                }
            };
            var result = new ExcelProfileRangeResult
            {
                Topology = "multiSource",
                HeaderConsistency = new ExcelProfileHeaderConsistency
                {
                    NamesConsistent = true,
                    OrderConsistent = false,
                    TypesConsistent = true,
                    HeaderInferenceConsistent = true
                }
            };
            result.SourceProfiles.Add(new ExcelProfileSourceResult
            {
                Alias = "jan",
                WorksheetName = "Jan",
                Address = "$A$1:$C$20",
                ColumnCount = 3
            });

            var requestCopy = RoundTrip(request);
            var resultCopy = RoundTrip(result);

            Assert.AreEqual(2, requestCopy.Sources.Count);
            Assert.AreEqual("feb", requestCopy.Sources[1].Alias);
            Assert.AreEqual("multiSource", resultCopy.Topology);
            Assert.AreEqual(1, resultCopy.SourceProfiles.Count);
            Assert.IsTrue(resultCopy.HeaderConsistency.NamesConsistent);
            Assert.IsFalse(resultCopy.HeaderConsistency.OrderConsistent);
        }

        [TestMethod]
        public void MultiSourceCalcQueryRoundTripsAllSources()
        {
            var query = new CalcQuery
            {
                Source = null,
                Function = "sum",
                Columns = { "Amount" },
                Sources =
                {
                    new ExcelVerificationSource
                    {
                        Sheet = "Jan",
                        Range = "$A$1:$B$10",
                        Alias = "jan"
                    },
                    new ExcelVerificationSource
                    {
                        Sheet = "Feb",
                        Range = "$A$1:$B$12",
                        Alias = "feb"
                    }
                }
            };

            var copy = RoundTrip(query);

            Assert.IsNull(copy.Source);
            Assert.AreEqual(2, copy.Sources.Count);
            Assert.AreEqual("Feb", copy.Sources[1].Sheet);
            Assert.AreEqual("feb", copy.Sources[1].Alias);
        }

        private static T RoundTrip<T>(T value)
        {
            var serializer = new DataContractSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
