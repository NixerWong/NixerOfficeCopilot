using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfficeCopilot.Host.Abstractions
{
    [DataContract]
    public sealed class ExcelRangeWriteRequest
    {
        public ExcelRangeWriteRequest()
        {
            WorksheetName = string.Empty;
            Address = string.Empty;
            Value = string.Empty;
            Values = new List<string[]>();
            UseSelectionWhenAddressEmpty = true;
        }

        [DataMember(Name = "worksheetName", Order = 1)]
        public string WorksheetName { get; set; }

        [DataMember(Name = "address", Order = 2)]
        public string Address { get; set; }

        [DataMember(Name = "value", Order = 3)]
        public string Value { get; set; }

        [DataMember(Name = "numberValue", Order = 4, EmitDefaultValue = false)]
        public double? NumberValue { get; set; }

        [DataMember(Name = "booleanValue", Order = 5, EmitDefaultValue = false)]
        public bool? BooleanValue { get; set; }

        [DataMember(Name = "values", Order = 6)]
        public List<string[]> Values { get; set; }

        [DataMember(Name = "useSelectionWhenAddressEmpty", Order = 7)]
        public bool UseSelectionWhenAddressEmpty { get; set; }
    }
}
