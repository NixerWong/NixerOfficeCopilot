using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelPowerQueryValidationRequest
    {
        public ExcelPowerQueryValidationRequest()
        {
            MCode = string.Empty;
        }

        [DataMember(Name = "mCode", Order = 1)]
        public string MCode { get; set; }
    }

    [DataContract]
    public sealed class ExcelPowerQueryValidationResult
    {
        public ExcelPowerQueryValidationResult()
        {
            WorkbookName = string.Empty;
            MCode = string.Empty;
            ValidationErrors = new List<string>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public bool IsValid { get; set; }

        [DataMember(Order = 3)]
        public List<string> ValidationErrors { get; set; }

        [DataMember(Order = 4)]
        public string MCode { get; set; }
    }
}