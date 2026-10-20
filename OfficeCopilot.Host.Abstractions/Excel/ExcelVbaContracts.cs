using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelVbaModuleInfo
    {
        public ExcelVbaModuleInfo()
        {
            Name = string.Empty;
            Type = string.Empty;
            Procedures = new List<string>();
        }

        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public string Type { get; set; }

        [DataMember(Order = 3)]
        public int LineCount { get; set; }

        [DataMember(Order = 4)]
        public List<string> Procedures { get; set; }
    }

    [DataContract]
    public sealed class ExcelVbaModuleListResult
    {
        public ExcelVbaModuleListResult()
        {
            WorkbookName = string.Empty;
            Modules = new List<ExcelVbaModuleInfo>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public List<ExcelVbaModuleInfo> Modules { get; set; }
    }

    [DataContract]
    public sealed class ExcelVbaModuleViewRequest
    {
        public ExcelVbaModuleViewRequest()
        {
            ModuleName = string.Empty;
        }

        [DataMember(Name = "moduleName", Order = 1)]
        public string ModuleName { get; set; }
    }

    [DataContract]
    public sealed class ExcelVbaModuleViewResult
    {
        public ExcelVbaModuleViewResult()
        {
            WorkbookName = string.Empty;
            ModuleName = string.Empty;
            ModuleType = string.Empty;
            Code = string.Empty;
            Procedures = new List<string>();
        }

        [DataMember(Order = 1)]
        public string WorkbookName { get; set; }

        [DataMember(Order = 2)]
        public string ModuleName { get; set; }

        [DataMember(Order = 3)]
        public string ModuleType { get; set; }

        [DataMember(Order = 4)]
        public string Code { get; set; }

        [DataMember(Order = 5)]
        public int LineCount { get; set; }

        [DataMember(Order = 6)]
        public List<string> Procedures { get; set; }
    }
}