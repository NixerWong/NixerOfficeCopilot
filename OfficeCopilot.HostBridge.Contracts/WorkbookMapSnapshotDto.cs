using System.Collections.Generic;

namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class WorkbookMapSnapshotDto
    {
        public WorkbookMapSnapshotDto()
        {
            WorkbookName = string.Empty;
            Worksheets = new List<WorkbookWorksheetSnapshotDto>();
            WorksheetGroups = new List<WorkbookWorksheetHeaderGroupSnapshotDto>();
            SharedColumnCandidates = new List<WorkbookSharedColumnCandidateSnapshotDto>();
        }

        public string WorkbookName { get; set; }

        public int TotalWorksheetCount { get; set; }

        public bool IsTruncated { get; set; }

        public List<WorkbookWorksheetSnapshotDto> Worksheets { get; set; }

        public List<WorkbookWorksheetHeaderGroupSnapshotDto> WorksheetGroups { get; set; }

        public List<WorkbookSharedColumnCandidateSnapshotDto> SharedColumnCandidates { get; set; }
    }

    public sealed class WorkbookWorksheetSnapshotDto
    {
        public WorkbookWorksheetSnapshotDto()
        {
            Name = string.Empty;
            CodeName = string.Empty;
            UsedRangeAddress = string.Empty;
            HeaderSignature = string.Empty;
            HeaderColumns = new List<string>();
            RoleGuess = "other";
            InspectionError = string.Empty;
        }

        public string Name { get; set; }

        public string CodeName { get; set; }

        public int Index { get; set; }

        public bool IsVisible { get; set; }

        public string UsedRangeAddress { get; set; }

        public int UsedRangeRows { get; set; }

        public int UsedRangeColumns { get; set; }

        public string HeaderSignature { get; set; }

        public List<string> HeaderColumns { get; set; }

        public bool HasFormulas { get; set; }

        public bool IsProtected { get; set; }

        public string RoleGuess { get; set; }

        public string InspectionError { get; set; }
    }

    public sealed class WorkbookWorksheetHeaderGroupSnapshotDto
    {
        public WorkbookWorksheetHeaderGroupSnapshotDto()
        {
            GroupId = string.Empty;
            WorksheetNames = new List<string>();
        }

        public string GroupId { get; set; }

        public decimal MinimumHeaderSimilarity { get; set; }

        public List<string> WorksheetNames { get; set; }
    }

    public sealed class WorkbookSharedColumnCandidateSnapshotDto
    {
        public WorkbookSharedColumnCandidateSnapshotDto()
        {
            ColumnName = string.Empty;
            WorksheetNames = new List<string>();
        }

        public string ColumnName { get; set; }

        public List<string> WorksheetNames { get; set; }

        public bool UniquenessVerified { get; set; }
    }
}
