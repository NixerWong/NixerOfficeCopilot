using System.Runtime.Serialization;
using System.Collections.Generic;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelConnectionInfo
    {
        public ExcelConnectionInfo()
        {
            Name = string.Empty;
            Type = string.Empty;
            Description = string.Empty;
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string Type { get; set; }

        [DataMember(Order = 3)]
        public string Description { get; set; }

        [DataMember(Order = 4)]
        public bool IsPowerQuery { get; set; }

        [DataMember(Order = 5)]
        public bool BackgroundQuery { get; set; }

        [DataMember(Order = 6)]
        public bool RefreshOnFileOpen { get; set; }
    }

    [DataContract]
    public sealed class ExcelConnectionListResult
    {
        public ExcelConnectionListResult()
        {
            WorkbookName = string.Empty;
            Connections = new List<ExcelConnectionInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelConnectionInfo> Connections { get; set; }
    }

    [DataContract]
    public sealed class ExcelConnectionViewRequest
    {
        public ExcelConnectionViewRequest()
        {
            ConnectionName = string.Empty;
        }

        [DataMember(Name = "connectionName", Order = 1)]
        public string ConnectionName { get; set; }
    }

    [DataContract]
    public sealed class ExcelConnectionViewResult
    {
        public ExcelConnectionViewResult()
        {
            WorkbookName = string.Empty;
            ConnectionName = string.Empty;
            Type = string.Empty;
            Description = string.Empty;
            ConnectionString = string.Empty;
            CommandText = string.Empty;
            CommandType = string.Empty;
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string ConnectionName { get; set; }

        [DataMember(Order = 3)]
        public string Type { get; set; }

        [DataMember(Order = 4)]
        public string Description { get; set; }

        [DataMember(Order = 5)]
        public string ConnectionString { get; set; }

        [DataMember(Order = 6)]
        public string CommandText { get; set; }

        [DataMember(Order = 7)]
        public string CommandType { get; set; }

        [DataMember(Order = 8)]
        public bool IsPowerQuery { get; set; }

        [DataMember(Order = 9)]
        public bool BackgroundQuery { get; set; }

        [DataMember(Order = 10)]
        public bool RefreshOnFileOpen { get; set; }
    }
}