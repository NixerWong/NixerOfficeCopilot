using System.Collections.Generic;
using System.Runtime.Serialization;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelVbaExpectation
    {
        public ExcelVbaExpectation()
        {
            Initialize(default(StreamingContext));
        }

        // DataContractJsonSerializer 不走构造函数，必须显式设默认值，
        // 否则未提供的 maxRows 会被当成 0，导致任何非空结果都判定失败。
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Columns = new List<string>();
            Conserve = new List<ExcelVbaConserveRule>();
            Basis = null;
            Assertions = new List<ExcelAssertion>();
            MinRows = -1;
            MaxRows = -1;
        }

        [DataMember(Name = "minRows", Order = 1)]
        public int MinRows { get; set; }

        [DataMember(Name = "maxRows", Order = 2)]
        public int MaxRows { get; set; }

        [DataMember(Name = "columns", Order = 3)]
        public List<string> Columns { get; set; }

        /// <summary>守恒断言：把输出表的聚合结果与源区域或给定值比对。</summary>
        [DataMember(Name = "conserve", Order = 4)]
        public List<ExcelVbaConserveRule> Conserve { get; set; }

        [DataMember(Name = "basis", Order = 5, EmitDefaultValue = false)]
        public ExcelVerificationBasis Basis { get; set; }

        [DataMember(Name = "assertions", Order = 6, EmitDefaultValue = false)]
        public List<ExcelAssertion> Assertions { get; set; }
    }

    /// <summary>
    /// 一条守恒规则。<see cref="CompareTo"/> 与 <see cref="ExpectedValue"/> 二选一。
    /// </summary>
    [DataContract]
    public sealed class ExcelVbaConserveRule
    {
        public ExcelVbaConserveRule()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Function = "sum";
            Column = string.Empty;
            Tolerance = 0d;
            ExpectedValue = null;
            CompareTo = null;
        }

        /// <summary>sum / count / countDistinct / average / min / max。</summary>
        [DataMember(Name = "function", Order = 1)]
        public string Function { get; set; }

        /// <summary>输出表上参与聚合的列；function 为 count 时可留空。</summary>
        [DataMember(Name = "column", Order = 2)]
        public string Column { get; set; }

        [DataMember(Name = "compareTo", Order = 3)]
        public ExcelVbaConserveSource CompareTo { get; set; }

        [DataMember(Name = "expectedValue", Order = 4)]
        public double? ExpectedValue { get; set; }

        /// <summary>允许的绝对误差；不给或非正数时按 1e-6 处理。</summary>
        [DataMember(Name = "tolerance", Order = 5)]
        public double Tolerance { get; set; }
    }

    /// <summary>守恒规则的比对源，通常是本次计算的原始明细区域。</summary>
    [DataContract]
    public sealed class ExcelVbaConserveSource
    {
        public ExcelVbaConserveSource()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            TableName = string.Empty;
            Function = string.Empty;
            Column = string.Empty;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "tableName", Order = 3)]
        public string TableName { get; set; }

        /// <summary>留空表示沿用规则本身的 function。</summary>
        [DataMember(Name = "function", Order = 4)]
        public string Function { get; set; }

        [DataMember(Name = "column", Order = 5)]
        public string Column { get; set; }
    }

    /// <summary>
    /// 金丝雀执行选项：先把源表前 N 行复制到一张样本表，同一段代码先在样本上跑，
    /// expect 通过了才对全量跑。它把“列名写错”这类错误从一张被搞坏的表降级成一次廉价的样本失败。
    /// </summary>
    [DataContract]
    public sealed class ExcelVbaProbeOptions
    {
        public ExcelVbaProbeOptions()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Rows = 0;
            SourceWorksheetName = string.Empty;
        }

        /// <summary>样本行数（不含表头）；0 或缺省表示不做金丝雀。</summary>
        [DataMember(Name = "rows", Order = 1)]
        public int Rows { get; set; }

        /// <summary>被采样的源工作表名。代码必须以引号字面量引用这个名字，否则无法重定向到样本表。</summary>
        [DataMember(Name = "sourceWorksheetName", Order = 2)]
        public string SourceWorksheetName { get; set; }
    }

    [DataContract]
    public sealed class ExcelVbaDeclaredWriteTarget
    {
        public ExcelVbaDeclaredWriteTarget()
        {
            WorksheetName = string.Empty;
            Range = string.Empty;
        }

        [DataMember(Name = "sheet", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "range", Order = 2, EmitDefaultValue = false)]
        public string Range { get; set; }

        [DataMember(Name = "isNewSheet", Order = 3)]
        public bool IsNewSheet { get; set; }
    }

    [DataContract]
    public sealed class ExcelVbaStageRequest
    {
        public ExcelVbaStageRequest()
        {
            Code = string.Empty;
            ModuleName = string.Empty;
            EntryPoint = string.Empty;
            RemoveModuleAfterRun = true;
            StageName = string.Empty;
            OutputSheet = string.Empty;
            DeclaredWriteTargets = new List<ExcelVbaDeclaredWriteTarget>();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Code = string.Empty;
            ModuleName = string.Empty;
            EntryPoint = string.Empty;
            RemoveModuleAfterRun = true;
            StageName = string.Empty;
            OutputSheet = string.Empty;
            DeclaredWriteTargets = new List<ExcelVbaDeclaredWriteTarget>();
        }

        [DataMember(Name = "code", Order = 1)]
        public string Code { get; set; }

        [DataMember(Name = "moduleName", Order = 2)]
        public string ModuleName { get; set; }

        [DataMember(Name = "entryPoint", Order = 3)]
        public string EntryPoint { get; set; }

        [DataMember(Name = "removeModuleAfterRun", Order = 4)]
        public bool RemoveModuleAfterRun { get; set; }

        [DataMember(Name = "stageName", Order = 5)]
        public string StageName { get; set; }

        /// <summary>本阶段结果写入的工作表名。给出后会自动回读该表画像。</summary>
        [DataMember(Name = "outputSheet", Order = 6)]
        public string OutputSheet { get; set; }

        [DataMember(Name = "declaredWriteTargets", Order = 7)]
        public List<ExcelVbaDeclaredWriteTarget> DeclaredWriteTargets { get; set; }

        [DataMember(Name = "expect", Order = 8)]
        public ExcelVbaExpectation Expect { get; set; }

        [DataMember(Name = "probe", Order = 9)]
        public ExcelVbaProbeOptions Probe { get; set; }

        /// <summary>执行看门狗秒数；≤ 0 表示用宕主侧默认值。</summary>
        [DataMember(Name = "timeoutSeconds", Order = 10)]
        public int TimeoutSeconds { get; set; }

        [IgnoreDataMember]
        public bool StagedOnly { get; set; }

        [IgnoreDataMember]
        public IList<string> ProtectionAllowedWorksheetNames { get; set; }
    }

    [DataContract]
    public sealed class ExcelVbaStageResult
    {
        public ExcelVbaStageResult()
        {
            Message = string.Empty;
            ModuleName = string.Empty;
            EntryPoint = string.Empty;
            StageName = string.Empty;
            OutputSheet = string.Empty;
            LogEntries = new List<string>();
            ExpectationFailures = new List<string>();
            ConserveChecks = new List<string>();
            ProbeChecks = new List<string>();
            ScopeChecks = new List<string>();
            ExpectationsPassed = true;
            Verification = null;
        }

        [DataMember(Name = "success", Order = 1)]
        public bool Success { get; set; }

        [DataMember(Name = "message", Order = 2)]
        public string Message { get; set; }

        [DataMember(Name = "moduleName", Order = 3)]
        public string ModuleName { get; set; }

        [DataMember(Name = "entryPoint", Order = 4)]
        public string EntryPoint { get; set; }

        [DataMember(Name = "stageName", Order = 5)]
        public string StageName { get; set; }

        [DataMember(Name = "outputSheet", Order = 6)]
        public string OutputSheet { get; set; }

        [DataMember(Name = "elapsedMilliseconds", Order = 7)]
        public long ElapsedMilliseconds { get; set; }

        [DataMember(Name = "logEntries", Order = 8)]
        public List<string> LogEntries { get; set; }

        [DataMember(Name = "expectationsPassed", Order = 9)]
        public bool ExpectationsPassed { get; set; }

        [DataMember(Name = "expectationFailures", Order = 10)]
        public List<string> ExpectationFailures { get; set; }

        [DataMember(Name = "outputProfile", Order = 11)]
        public ExcelProfileRangeResult OutputProfile { get; set; }

        /// <summary>每条守恒断言的实际比对明细，通过与否都记录。</summary>
        [DataMember(Name = "conserveChecks", Order = 12)]
        public List<string> ConserveChecks { get; set; }

        /// <summary>金丝雀阶段的执行与校验明细。</summary>
        [DataMember(Name = "probeChecks", Order = 13)]
        public List<string> ProbeChecks { get; set; }

        /// <summary>金丝雀已执行且通过（未开启时为 false）。</summary>
        [DataMember(Name = "probePassed", Order = 14)]
        public bool ProbePassed { get; set; }

        /// <summary>本次执行是否因看门狗超时被中断。</summary>
        [DataMember(Name = "timedOut", Order = 15)]
        public bool TimedOut { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Verification, Order = 16, EmitDefaultValue = false)]
        public ExcelVerificationResult Verification { get; set; }

        [DataMember(Name = "errorCode", Order = 17, EmitDefaultValue = false)]
        public string ErrorCode { get; set; }

        [DataMember(Name = "scopeChecks", Order = 18, EmitDefaultValue = false)]
        public List<string> ScopeChecks { get; set; }
    }

    [DataContract]
    public sealed class ExcelCleanupWorkspaceRequest
    {
        public ExcelCleanupWorkspaceRequest()
        {
            Prefix = StagedWriteConfirmationPolicy.WipPrefix;
            RegisteredWorksheetNames = new List<string>();
        }

        [DataMember(Name = "prefix", Order = 1)]
        public string Prefix { get; set; }

        [DataMember(Name = "dryRun", Order = 2)]
        public bool DryRun { get; set; }

        [IgnoreDataMember]
        public IList<string> RegisteredWorksheetNames { get; set; }
    }

    [DataContract]
    public sealed class ExcelCleanupWorkspaceResult
    {
        public ExcelCleanupWorkspaceResult()
        {
            Prefix = string.Empty;
            RemovedSheets = new List<string>();
            MatchedSheets = new List<string>();
            Message = string.Empty;
        }

        [DataMember(Name = "prefix", Order = 1)]
        public string Prefix { get; set; }

        [DataMember(Name = "matchedSheets", Order = 2)]
        public List<string> MatchedSheets { get; set; }

        [DataMember(Name = "removedSheets", Order = 3)]
        public List<string> RemovedSheets { get; set; }

        [DataMember(Name = "dryRun", Order = 4)]
        public bool DryRun { get; set; }

        [DataMember(Name = "message", Order = 5)]
        public string Message { get; set; }
    }
}
