using System.Collections.Generic;
using System.Runtime.Serialization;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelVerificationPlan
    {
        public ExcelVerificationPlan()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Basis = new ExcelVerificationBasis();
            Assertions = new List<ExcelAssertion>();
        }

        [DataMember(Name = "basis", Order = 1)]
        public ExcelVerificationBasis Basis { get; set; }

        [DataMember(Name = "assertions", Order = 2)]
        public List<ExcelAssertion> Assertions { get; set; }
    }

    [DataContract]
    public sealed class ExcelVerificationBasis
    {
        public ExcelVerificationBasis()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Source = new ExcelSourceRef();
            Filters = new List<CalcFilter>();
            GroupBy = new List<string>();
            Measures = new List<ExcelVerificationMeasure>();
            DerivedGroupBy = new List<ExcelVerificationDerivedGroup>();
            Sources = new List<ExcelVerificationSource>();
            Lookups = new List<ExcelVerificationLookup>();
            Keys = new List<string>();
            HeaderMode = "auto";
            Topology = null;
            Join = null;
            Note = null;
        }

        [DataMember(Name = "source", Order = 1)]
        public ExcelSourceRef Source { get; set; }

        [DataMember(Name = "filters", Order = 2)]
        public List<CalcFilter> Filters { get; set; }

        [DataMember(Name = "groupBy", Order = 3)]
        public List<string> GroupBy { get; set; }

        [DataMember(Name = "measures", Order = 4)]
        public List<ExcelVerificationMeasure> Measures { get; set; }

        [DataMember(Name = "note", Order = 5, EmitDefaultValue = false)]
        public string Note { get; set; }

        [DataMember(Name = "headerMode", Order = 6, EmitDefaultValue = false)]
        public string HeaderMode { get; set; }

        [DataMember(Name = "derivedGroupBy", Order = 7, EmitDefaultValue = false)]
        public List<ExcelVerificationDerivedGroup> DerivedGroupBy { get; set; }

        [DataMember(Name = "sources", Order = 8, EmitDefaultValue = false)]
        public List<ExcelVerificationSource> Sources { get; set; }

        [DataMember(Name = "topology", Order = 9, EmitDefaultValue = false)]
        public string Topology { get; set; }

        [DataMember(Name = "keys", Order = 10, EmitDefaultValue = false)]
        public List<string> Keys { get; set; }

        [DataMember(Name = "join", Order = 11, EmitDefaultValue = false)]
        public string Join { get; set; }

        [DataMember(Name = "lookups", Order = 12, EmitDefaultValue = false)]
        public List<ExcelVerificationLookup> Lookups { get; set; }
    }

    [DataContract]
    public sealed class ExcelVerificationSource
    {
        public ExcelVerificationSource()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Sheet = string.Empty;
            Range = string.Empty;
            Alias = string.Empty;
        }

        [DataMember(Name = "sheet", Order = 1)]
        public string Sheet { get; set; }

        [DataMember(Name = "range", Order = 2)]
        public string Range { get; set; }

        [DataMember(Name = "alias", Order = 3)]
        public string Alias { get; set; }
    }

    [DataContract]
    public sealed class ExcelVerificationLookup
    {
        public ExcelVerificationLookup()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Sheet = string.Empty;
            Range = string.Empty;
            TableName = string.Empty;
            Key = string.Empty;
            ForeignKey = string.Empty;
            Columns = new List<string>();
            Join = "left";
            OnDuplicateKey = "error";
        }

        [DataMember(Name = "sheet", Order = 1)]
        public string Sheet { get; set; }

        [DataMember(Name = "range", Order = 2)]
        public string Range { get; set; }

        [DataMember(Name = "key", Order = 3)]
        public string Key { get; set; }

        [DataMember(Name = "foreignKey", Order = 4)]
        public string ForeignKey { get; set; }

        [DataMember(Name = "columns", Order = 5)]
        public List<string> Columns { get; set; }

        [DataMember(Name = "join", Order = 6)]
        public string Join { get; set; }

        [DataMember(Name = "onDuplicateKey", Order = 7)]
        public string OnDuplicateKey { get; set; }

        [DataMember(Name = "tableName", Order = 8, EmitDefaultValue = false)]
        public string TableName { get; set; }
    }

    [DataContract]
    public sealed class ExcelVerificationDerivedGroup
    {
        public ExcelVerificationDerivedGroup()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Column = string.Empty;
            Transform = string.Empty;
            Alias = string.Empty;
            Size = 0d;
        }

        [DataMember(Name = "column", Order = 1)]
        public string Column { get; set; }

        [DataMember(Name = "transform", Order = 2)]
        public string Transform { get; set; }

        [DataMember(Name = "alias", Order = 3, EmitDefaultValue = false)]
        public string Alias { get; set; }

        [DataMember(Name = "size", Order = 4, EmitDefaultValue = false)]
        public double Size { get; set; }
    }

    [DataContract]
    public sealed class ExcelVerificationMeasure
    {
        public ExcelVerificationMeasure()
        {
            Column = string.Empty;
            Function = "sum";
        }

        [DataMember(Name = "column", Order = 1)]
        public string Column { get; set; }

        [DataMember(Name = "function", Order = 2)]
        public string Function { get; set; }
    }

    [DataContract]
    public sealed class ExcelAssertion
    {
        public ExcelAssertion()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Id = null;
            Kind = null;
            Target = new ExcelAssertionSide();
            Source = null;
            ExpectedValue = null;
            Tolerance = null;
            Direction = null;
            SampleCount = null;
        }

        [DataMember(Name = "id", Order = 1, EmitDefaultValue = false)]
        public string Id { get; set; }

        [DataMember(Name = "kind", Order = 2)]
        public string Kind { get; set; }

        [DataMember(Name = "target", Order = 3)]
        public ExcelAssertionSide Target { get; set; }

        [DataMember(Name = "source", Order = 4, EmitDefaultValue = false)]
        public ExcelAssertionSide Source { get; set; }

        [DataMember(Name = "expectedValue", Order = 5, EmitDefaultValue = false)]
        public double? ExpectedValue { get; set; }

        [DataMember(Name = "tolerance", Order = 6, EmitDefaultValue = false)]
        public double? Tolerance { get; set; }

        [DataMember(Name = "direction", Order = 7, EmitDefaultValue = false)]
        public string Direction { get; set; }

        [DataMember(Name = "sampleCount", Order = 8, EmitDefaultValue = false)]
        public int? SampleCount { get; set; }
    }

    [DataContract]
    public sealed class ExcelAssertionSide
    {
        public ExcelAssertionSide()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            WorksheetName = null;
            Address = null;
            TableName = null;
            Column = null;
            Columns = new List<string>();
            Function = null;
            KeyColumn = null;
            ValueColumn = null;
            Filters = new List<CalcFilter>();
            ExcludeRows = new List<int>();
            ExcludeLabelRows = false;
        }

        [DataMember(Name = "worksheetName", Order = 1, EmitDefaultValue = false)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2, EmitDefaultValue = false)]
        public string Address { get; set; }

        [DataMember(Name = "tableName", Order = 3, EmitDefaultValue = false)]
        public string TableName { get; set; }

        [DataMember(Name = "column", Order = 4, EmitDefaultValue = false)]
        public string Column { get; set; }

        [DataMember(Name = "columns", Order = 5, EmitDefaultValue = false)]
        public List<string> Columns { get; set; }

        [DataMember(Name = "function", Order = 6, EmitDefaultValue = false)]
        public string Function { get; set; }

        [DataMember(Name = "keyColumn", Order = 7, EmitDefaultValue = false)]
        public string KeyColumn { get; set; }

        [DataMember(Name = "valueColumn", Order = 8, EmitDefaultValue = false)]
        public string ValueColumn { get; set; }

        [DataMember(Name = "filters", Order = 9, EmitDefaultValue = false)]
        public List<CalcFilter> Filters { get; set; }

        [DataMember(Name = "excludeRows", Order = 10, EmitDefaultValue = false)]
        public List<int> ExcludeRows { get; set; }

        [DataMember(Name = "excludeLabelRows", Order = 11, EmitDefaultValue = false)]
        public bool ExcludeLabelRows { get; set; }
    }

    [DataContract]
    public sealed class ExcelAssertionOutcome
    {
        public ExcelAssertionOutcome()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Id = string.Empty;
            Kind = string.Empty;
            Passed = false;
            Tier = "C";
            ExpectedValue = null;
            ActualValue = null;
            Difference = null;
            Tolerance = null;
            ExpectedCount = null;
            ActualCount = null;
            ComparedCount = null;
            DiffCount = 0;
            Independent = false;
            IsSorted = null;
            TargetBackend = string.Empty;
            SourceBackend = null;
            TargetCoverage = null;
            SourceCoverage = null;
            TargetOnlyCount = null;
            SourceOnlyCount = null;
            TargetOnlySamples = new List<string>();
            SourceOnlySamples = new List<string>();
        }

        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; }

        [DataMember(Name = "kind", Order = 2)]
        public string Kind { get; set; }

        [DataMember(Name = "passed", Order = 3)]
        public bool Passed { get; set; }

        [DataMember(Name = "tier", Order = 4)]
        public string Tier { get; set; }

        [DataMember(Name = "expectedValue", Order = 5, EmitDefaultValue = false)]
        public double? ExpectedValue { get; set; }

        [DataMember(Name = "actualValue", Order = 6, EmitDefaultValue = false)]
        public double? ActualValue { get; set; }

        [DataMember(Name = "difference", Order = 7, EmitDefaultValue = false)]
        public double? Difference { get; set; }

        [DataMember(Name = "tolerance", Order = 8, EmitDefaultValue = false)]
        public double? Tolerance { get; set; }

        [DataMember(Name = "expectedCount", Order = 9, EmitDefaultValue = false)]
        public int? ExpectedCount { get; set; }

        [DataMember(Name = "actualCount", Order = 10, EmitDefaultValue = false)]
        public int? ActualCount { get; set; }

        [DataMember(Name = "comparedCount", Order = 11, EmitDefaultValue = false)]
        public int? ComparedCount { get; set; }

        [DataMember(Name = "diffCount", Order = 12)]
        public int DiffCount { get; set; }

        [DataMember(Name = "independent", Order = 13)]
        public bool Independent { get; set; }

        [DataMember(Name = "isSorted", Order = 14, EmitDefaultValue = false)]
        public bool? IsSorted { get; set; }

        [DataMember(Name = "targetBackend", Order = 15)]
        public string TargetBackend { get; set; }

        [DataMember(Name = "sourceBackend", Order = 16, EmitDefaultValue = false)]
        public string SourceBackend { get; set; }

        [DataMember(Name = "targetCoverage", Order = 17, EmitDefaultValue = false)]
        public ExcelCoverageReport TargetCoverage { get; set; }

        [DataMember(Name = "sourceCoverage", Order = 18, EmitDefaultValue = false)]
        public ExcelCoverageReport SourceCoverage { get; set; }

        [DataMember(Name = "targetOnlyCount", Order = 19, EmitDefaultValue = false)]
        public int? TargetOnlyCount { get; set; }

        [DataMember(Name = "sourceOnlyCount", Order = 20, EmitDefaultValue = false)]
        public int? SourceOnlyCount { get; set; }

        [DataMember(Name = "targetOnlySamples", Order = 21, EmitDefaultValue = false)]
        public List<string> TargetOnlySamples { get; set; }

        [DataMember(Name = "sourceOnlySamples", Order = 22, EmitDefaultValue = false)]
        public List<string> SourceOnlySamples { get; set; }
    }

    [DataContract]
    public sealed class ExcelVerificationReport
    {
        public ExcelVerificationReport()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Passed = false;
            Tier = "C";
            Items = new List<ExcelAssertionOutcome>();
            Diffs = new List<string>();
            DiffCount = 0;
            Notes = new List<string>();
        }

        [DataMember(Name = "passed", Order = 1)]
        public bool Passed { get; set; }

        [DataMember(Name = "tier", Order = 2)]
        public string Tier { get; set; }

        [DataMember(Name = "items", Order = 3)]
        public List<ExcelAssertionOutcome> Items { get; set; }

        [DataMember(Name = "diffs", Order = 4)]
        public List<string> Diffs { get; set; }

        [DataMember(Name = "diffCount", Order = 5)]
        public int DiffCount { get; set; }

        [DataMember(Name = "notes", Order = 6)]
        public List<string> Notes { get; set; }
    }

    [DataContract]
    public sealed class ExcelVerificationResult
    {
        public ExcelVerificationResult()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Tier = "C";
            Producer = ToolResultVerificationFields.ProducerUnknown;
            Operation = null;
            Notes = new List<string>();
        }

        [DataMember(Name = ToolResultVerificationFields.Tier, Order = 1)]
        public string Tier { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Passed, Order = 2)]
        public bool Passed { get; set; }

        [DataMember(Name = ToolResultVerificationFields.AssertionCount, Order = 3)]
        public int AssertionCount { get; set; }

        [DataMember(Name = ToolResultVerificationFields.CoveredRows, Order = 4)]
        public long CoveredRows { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Independent, Order = 5)]
        public bool Independent { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Sampled, Order = 6)]
        public bool Sampled { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Producer, Order = 7)]
        public string Producer { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Basis, Order = 8, EmitDefaultValue = false)]
        public ExcelVerificationBasis Basis { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Notes, Order = 9)]
        public List<string> Notes { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Details, Order = 10, EmitDefaultValue = false)]
        public ExcelVerificationReport Details { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Operation, Order = 11, EmitDefaultValue = false)]
        public string Operation { get; set; }

        public static ExcelVerificationResult FromReport(
            ExcelVerificationReport report,
            string producer,
            bool sampled,
            ExcelVerificationBasis basis)
        {
            if (report == null)
            {
                throw new System.ArgumentNullException(nameof(report));
            }

            if (!IsSupportedProducer(producer))
            {
                throw new System.ArgumentException(
                    "验证 producer 不受支持。",
                    nameof(producer));
            }

            var items = report.Items ?? new List<ExcelAssertionOutcome>();
            var coveredRows = 0L;
            var seenCoverage = new HashSet<ExcelCoverageReport>();
            var notes = report.Notes == null
                ? new List<string>()
                : new List<string>(report.Notes);
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                AddCoverage(item.TargetCoverage, seenCoverage, ref coveredRows, notes);
                AddCoverage(item.SourceCoverage, seenCoverage, ref coveredRows, notes);
            }

            return new ExcelVerificationResult
            {
                Tier = string.IsNullOrWhiteSpace(report.Tier) ? "C" : report.Tier,
                Passed = report.Passed,
                AssertionCount = items.Count,
                CoveredRows = coveredRows,
                Independent = items.Count > 0 && items.TrueForAll(
                    item => item != null && item.Independent),
                Sampled = sampled,
                Producer = producer,
                Basis = basis,
                Notes = notes,
                Details = report
            };
        }

        private static void AddCoverage(
            ExcelCoverageReport coverage,
            ISet<ExcelCoverageReport> seenCoverage,
            ref long coveredRows,
            ICollection<string> notes)
        {
            if (coverage == null || !seenCoverage.Add(coverage))
            {
                return;
            }

            if (coverage.ScannedRows < 0)
            {
                notes.Add("覆盖报告含负的 scannedRows，未计入 coveredRows。");
                return;
            }

            checked
            {
                coveredRows += coverage.ScannedRows;
            }
        }

        private static bool IsSupportedProducer(string producer)
        {
            return producer == ToolResultVerificationFields.ProducerVba
                || producer == ToolResultVerificationFields.ProducerFormula
                || producer == ToolResultVerificationFields.ProducerManaged
                || producer == ToolResultVerificationFields.ProducerModel
                || producer == ToolResultVerificationFields.ProducerUnknown;
        }
    }

    [DataContract]
    public sealed class ExcelVerificationToolResult
    {
        public ExcelVerificationToolResult()
        {
            Verification = new ExcelVerificationResult();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Verification = new ExcelVerificationResult();
        }

        [DataMember(Name = ToolResultVerificationFields.Verification, Order = 1)]
        public ExcelVerificationResult Verification { get; set; }
    }
}
