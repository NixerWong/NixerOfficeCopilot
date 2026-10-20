namespace OfficeCopilot.Diagnostics.Transcripts
{
    public interface IModelTranscriptLogger
    {
        void Write(ModelTranscriptEntry entry);
    }
}