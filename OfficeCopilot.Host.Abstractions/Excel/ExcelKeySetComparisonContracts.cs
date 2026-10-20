using System.Collections.Generic;
using System.Runtime.Serialization;
using OfficeCopilot.HostBridge.Contracts;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelKeySetComparisonRequest
    {
        public ExcelKeySetComparisonRequest()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Target = new ExcelAssertionSide();
            Source = new ExcelAssertionSide();
            HeaderMode = "auto";
            SampleCount = 3;
        }

        [DataMember(Name = "target", Order = 1)]
        public ExcelAssertionSide Target { get; set; }

        [DataMember(Name = "source", Order = 2)]
        public ExcelAssertionSide Source { get; set; }

        [DataMember(Name = "headerMode", Order = 3, EmitDefaultValue = false)]
        public string HeaderMode { get; set; }

        [DataMember(Name = "sampleCount", Order = 4, EmitDefaultValue = false)]
        public int SampleCount { get; set; }
    }

    [DataContract]
    public sealed class ExcelKeySetComparisonResult
    {
        public ExcelKeySetComparisonResult()
        {
            Initialize(default(StreamingContext));
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            TargetOnlyCount = null;
            SourceOnlyCount = null;
            MatchedCount = null;
            TotalUnmatchedCount = null;
            TargetOnlySamples = null;
            SourceOnlySamples = null;
            Verification = new ExcelVerificationResult();
        }

        [DataMember(Name = "targetOnlyCount", Order = 1, EmitDefaultValue = false)]
        public int? TargetOnlyCount { get; set; }

        [DataMember(Name = "sourceOnlyCount", Order = 2, EmitDefaultValue = false)]
        public int? SourceOnlyCount { get; set; }

        [DataMember(Name = "matchedCount", Order = 3, EmitDefaultValue = false)]
        public int? MatchedCount { get; set; }

        [DataMember(Name = "totalUnmatchedCount", Order = 4, EmitDefaultValue = false)]
        public int? TotalUnmatchedCount { get; set; }

        [DataMember(Name = "targetOnlySamples", Order = 5, EmitDefaultValue = false)]
        public List<string> TargetOnlySamples { get; set; }

        [DataMember(Name = "sourceOnlySamples", Order = 6, EmitDefaultValue = false)]
        public List<string> SourceOnlySamples { get; set; }

        [DataMember(Name = ToolResultVerificationFields.Verification, Order = 7)]
        public ExcelVerificationResult Verification { get; set; }
    }
}
