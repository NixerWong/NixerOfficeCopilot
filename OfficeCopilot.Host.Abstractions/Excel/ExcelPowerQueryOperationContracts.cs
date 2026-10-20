using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelPowerQueryRefreshRequest
    {
        public ExcelPowerQueryRefreshRequest()
        {
            Name = string.Empty;
        }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }
    }

    [DataContract]
    public sealed class ExcelPowerQueryRefreshResult
    {
        public ExcelPowerQueryRefreshResult()
        {
            WorkbookName = string.Empty;
            QueryName = string.Empty;
            ErrorMessages = new List<string>();
            LoadedToSheet = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string QueryName { get; set; }

        [DataMember(Order = 3)]
        public bool HasErrors { get; set; }

        [DataMember(Order = 4)]
        public List<string> ErrorMessages { get; set; }

        [DataMember(Order = 5)]
        public DateTime RefreshTime { get; set; }

        [DataMember(Order = 6)]
        public bool IsConnectionOnly { get; set; }

        [DataMember(Order = 7)]
        public string LoadedToSheet { get; set; }
    }

    [DataContract]
    public sealed class ExcelPowerQueryErrorCheckRequest
    {
        public ExcelPowerQueryErrorCheckRequest()
        {
            Name = string.Empty;
        }

        [DataMember(Name = "name", Order = 1)]
        public string Name { get; set; }
    }

    [DataContract]
    public sealed class ExcelPowerQueryErrorCheckResult
    {
        public ExcelPowerQueryErrorCheckResult()
        {
            WorkbookName = string.Empty;
            QueryName = string.Empty;
            ErrorMessages = new List<string>();
            ErrorCategory = string.Empty;
            Message = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string QueryName { get; set; }

        [DataMember(Order = 3)]
        public bool HasErrors { get; set; }

        [DataMember(Order = 4)]
        public List<string> ErrorMessages { get; set; }

        [DataMember(Order = 5)]
        public string ErrorCategory { get; set; }

        [DataMember(Order = 6)]
        public bool IsConnectionOnly { get; set; }

        [DataMember(Order = 7)]
        public string Message { get; set; }
    }
}