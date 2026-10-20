using System;
using System.Collections.Generic;

namespace OfficeCopilot.Panel.Abstractions
{
    public enum ReferenceItemKind
    {
        Unknown = 0,
        LocalFile = 1,
        ImageFile = 2,
        ExcelRange = 3,
        ExcelWorksheet = 4,
        ExcelWorkbook = 5,
        TextSnippet = 6,
        WebLink = 7
    }

    public enum ReferenceDeliveryMode
    {
        Native = 0,
        InlineText = 1,
        ToolFetch = 2,
        Rejected = 3
    }

    public sealed class ReferenceItem
    {
        public ReferenceItem()
        {
            ReferenceId = Guid.NewGuid().ToString("N");
            Kind = ReferenceItemKind.Unknown;
            DisplayName = string.Empty;
            MimeType = string.Empty;
            LocalPath = string.Empty;
            FileExtension = string.Empty;
            InlineText = string.Empty;
            Summary = string.Empty;
            Source = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string ReferenceId { get; set; }

        public ReferenceItemKind Kind { get; set; }

        public string DisplayName { get; set; }

        public string MimeType { get; set; }

        public string LocalPath { get; set; }

        public string FileExtension { get; set; }

        public long? SizeBytes { get; set; }

        public string InlineText { get; set; }

        public string Summary { get; set; }

        public string Source { get; set; }

        public IDictionary<string, string> Metadata { get; set; }
    }

    public sealed class PreparedReferenceChunk
    {
        public int Index { get; set; }

        public int? SectionIndex { get; set; }

        public string SectionTitle { get; set; }

        public string Content { get; set; }

        public int CharacterCount { get; set; }
    }

    public sealed class PreparedReference
    {
        public PreparedReference()
        {
            ReferenceId = string.Empty;
            DisplayName = string.Empty;
            MimeType = string.Empty;
            FileExtension = string.Empty;
            Summary = string.Empty;
            InlineText = string.Empty;
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Chunks = new List<PreparedReferenceChunk>();
        }

        public string ReferenceId { get; set; }

        public ReferenceItemKind Kind { get; set; }

        public string DisplayName { get; set; }

        public string MimeType { get; set; }

        public string FileExtension { get; set; }

        public long? SizeBytes { get; set; }

        public string Summary { get; set; }

        public string InlineText { get; set; }

        public bool NativeInputCandidate { get; set; }

        public bool InlineTextCandidate { get; set; }

        public bool ToolFetchCandidate { get; set; }

        public IDictionary<string, string> Metadata { get; set; }

        public IList<PreparedReferenceChunk> Chunks { get; set; }
    }

    public sealed class ReferenceDeliveryDecision
    {
        public ReferenceDeliveryDecision()
        {
            ReferenceId = string.Empty;
            DisplayName = string.Empty;
            Reason = string.Empty;
        }

        public string ReferenceId { get; set; }

        public string DisplayName { get; set; }

        public ReferenceDeliveryMode DeliveryMode { get; set; }

        public string Reason { get; set; }
    }

    public sealed class ReferenceSendPlan
    {
        public ReferenceSendPlan()
        {
            PreparedReferences = new List<PreparedReference>();
            Decisions = new List<ReferenceDeliveryDecision>();
            NativeReferences = new List<PreparedReference>();
            InlineReferences = new List<PreparedReference>();
            ToolFetchReferences = new List<PreparedReference>();
            Warnings = new List<string>();
            Errors = new List<string>();
            InlineContextText = string.Empty;
            ManifestText = string.Empty;
        }

        public IList<PreparedReference> PreparedReferences { get; set; }

        public IList<ReferenceDeliveryDecision> Decisions { get; set; }

        public IList<PreparedReference> NativeReferences { get; set; }

        public IList<PreparedReference> InlineReferences { get; set; }

        public IList<PreparedReference> ToolFetchReferences { get; set; }

        public IList<string> Warnings { get; set; }

        public IList<string> Errors { get; set; }

        public string InlineContextText { get; set; }

        public string ManifestText { get; set; }

        public bool HasErrors
        {
            get { return Errors != null && Errors.Count > 0; }
        }
    }

    public interface IReferencePreprocessor
    {
        bool CanProcess(ReferenceItem item);

        PreparedReference Prepare(ReferenceItem item);
    }

    public interface IReferenceArtifactStore
    {
        void Save(string conversationId, IEnumerable<PreparedReference> references);

        bool TryGet(string conversationId, string referenceId, out PreparedReference reference);

        IReadOnlyList<PreparedReference> GetAll(string conversationId);
    }
}
