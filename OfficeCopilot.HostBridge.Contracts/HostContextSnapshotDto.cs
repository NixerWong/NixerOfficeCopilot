namespace OfficeCopilot.HostBridge.Contracts
{
    public sealed class HostContextSnapshotDto
    {
        public HostContextSnapshotDto()
        {
            HostApplicationName = string.Empty;
            DocumentName = string.Empty;
            WorksheetName = string.Empty;
            SelectionAddress = string.Empty;
            SelectionPreview = string.Empty;
            Summary = string.Empty;
            CurrentLocalTime = string.Empty;
            WorkbookMapSummary = string.Empty;
            WorkbookMap = new WorkbookMapSnapshotDto();
        }

        public string HostApplicationName { get; set; }

        public string DocumentName { get; set; }

        public string WorksheetName { get; set; }

        public string SelectionAddress { get; set; }

        public string SelectionPreview { get; set; }

        public string Summary { get; set; }

        /// <summary>
        /// 由 Broker 在每轮采集上下文时写入的本地时间行（如 "2026-10-12 19:06（星期一），时区 UTC+08:00"），宿主不需要提供。
        /// </summary>
        public string CurrentLocalTime { get; set; }

        public string WorkbookMapSummary { get; set; }

        public WorkbookMapSnapshotDto WorkbookMap { get; set; }
    }
}
