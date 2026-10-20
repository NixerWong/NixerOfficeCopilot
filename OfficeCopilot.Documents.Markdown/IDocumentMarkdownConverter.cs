using System.Threading;

namespace OfficeCopilot.Documents.Markdown
{
    public interface IDocumentMarkdownConverter
    {
        bool CanConvert(string extension);

        MarkdownDocument Convert(string path, MarkdownConversionOptions options);
    }

    public interface ICancellableDocumentMarkdownConverter : IDocumentMarkdownConverter
    {
        MarkdownDocument Convert(string path, MarkdownConversionOptions options, CancellationToken cancellationToken);
    }
}
