using System;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelRangeReadRequest
    {
        public ExcelRangeReadRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            MaxRows = 20;
            MaxColumns = 10;
            UseSelectionWhenAddressEmpty = true;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "maxRows", Order = 3)]
        public int MaxRows { get; set; }

        [DataMember(Name = "maxColumns", Order = 4)]
        public int MaxColumns { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 5)]
        public bool UseSelectionWhenAddressEmpty { get; set; }
    }
}