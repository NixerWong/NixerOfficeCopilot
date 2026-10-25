using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Documents.Markdown;
using OfficeCopilot.Panel.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OfficeCopilot.Panel.WebView.Services.References
{
    internal sealed class GetReferenceContentTool : IAgentTool
    {
        private const string ToolName = "get_reference_content";

        public ToolDefinition Definition { get; } = new ToolDefinition
        {
            Name = ToolName,
            DisplayName = "Get reference content",
            Description = "Fetch one text chunk from an attachment. Omit chunkIndex to fetch the first unread chunk; use zero-based chunkIndex for a specific chunk, or 1-based sectionIndex for an Office/PDF section. For an Excel .xlsx/.xlsm sheet, combine sectionIndex with an A1 range such as A1:D20 to read selected cells. Use pageNumber to request a PDF, Word or PowerPoint page image when visual inspection is needed.",
            InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"referenceId\":{\"type\":\"string\"},\"chunkIndex\":{\"type\":\"integer\",\"minimum\":0},\"sectionIndex\":{\"type\":\"integer\",\"minimum\":1},\"range\":{\"type\":\"string\"},\"pageNumber\":{\"type\":\"integer\",\"minimum\":1}},\"required\":[\"referenceId\"],\"additionalProperties\":false}",
            RiskLevel = "low",
            IsReadOnly = true,
            RequiresConfirmation = false,
            RequiresBackup = false,
            AffectsDocument = false,
            TimeoutSeconds = 30,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ToolMetadataKeys.GroupName] = RuntimeToolGroups.Context
            }
        };

        public Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request)
        {
            if (context == null)
            {
                return Task.FromResult(Failure(request, "reference.context_missing", "Tool execution context is unavailable."));
            }

            var store = context.GetService<IReferenceArtifactStore>();
            if (store == null)
            {
                return Task.FromResult(Failure(request, "reference.store_unavailable", "Attachment content is unavailable in this panel."));
            }

            JObject arguments;
            try
            {
                arguments = JObject.Parse(string.IsNullOrWhiteSpace(request == null ? null : request.ArgumentsJson)
                    ? "{}"
                    : request.ArgumentsJson);
            }
            catch (JsonException)
            {
                return Task.FromResult(Failure(request, "reference.invalid_arguments", "Arguments must be a JSON object containing referenceId and optional chunkIndex or sectionIndex."));
            }

            var referenceIdToken = arguments["referenceId"];
            var referenceId = referenceIdToken == null || referenceIdToken.Type != JTokenType.String
                ? string.Empty
                : referenceIdToken.Value<string>();
            if (string.IsNullOrWhiteSpace(referenceId))
            {
                return Task.FromResult(Failure(request, "reference.id_required", "referenceId is required."));
            }

            PreparedReference reference;
            if (!store.TryGet(context.ConversationId, referenceId.Trim(), out reference) || reference == null)
            {
                return Task.FromResult(Failure(request, "reference.not_found", "No attachment with that referenceId is available in the current conversation."));
            }

            var chunkIndex = 0;
            var chunkIndexToken = arguments["chunkIndex"];
            var sectionIndexToken = arguments["sectionIndex"];
            var rangeToken = arguments["range"];
            var pageNumberToken = arguments["pageNumber"];
            string requestedRange = null;
            int? requestedSectionIndex = null;
            int? requestedPageNumber = null;
            if (pageNumberToken != null && pageNumberToken.Type != JTokenType.Null)
            {
                if (pageNumberToken.Type != JTokenType.Integer)
                {
                    return Task.FromResult(Failure(request, "reference.invalid_page_number", "pageNumber must be a positive integer."));
                }

                long requestedPage;
                if (!long.TryParse(pageNumberToken.ToString(), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out requestedPage)
                    || requestedPage < 1 || requestedPage > int.MaxValue)
                {
                    return Task.FromResult(Failure(request, "reference.invalid_page_number", "pageNumber must be a positive integer."));
                }

                if ((chunkIndexToken != null && chunkIndexToken.Type != JTokenType.Null)
                    || (sectionIndexToken != null && sectionIndexToken.Type != JTokenType.Null)
                    || (rangeToken != null && rangeToken.Type != JTokenType.Null))
                {
                    return Task.FromResult(Failure(request, "reference.invalid_arguments", "Specify pageNumber separately from chunkIndex, sectionIndex, or range."));
                }

                requestedPageNumber = (int)requestedPage;
            }

            if (rangeToken != null && rangeToken.Type != JTokenType.Null)
            {
                if (rangeToken.Type != JTokenType.String || string.IsNullOrWhiteSpace(rangeToken.Value<string>()))
                {
                    return Task.FromResult(Failure(request, "reference.invalid_range", "range must be a non-empty A1 cell address or range."));
                }

                requestedRange = rangeToken.Value<string>().Trim();
                if (chunkIndexToken != null && chunkIndexToken.Type != JTokenType.Null)
                {
                    return Task.FromResult(Failure(request, "reference.invalid_arguments", "Specify range with sectionIndex, not chunkIndex."));
                }
            }

            if (sectionIndexToken != null && sectionIndexToken.Type != JTokenType.Null)
            {
                if (chunkIndexToken != null && chunkIndexToken.Type != JTokenType.Null)
                {
                    return Task.FromResult(Failure(request, "reference.invalid_arguments", "Specify either chunkIndex or sectionIndex, not both."));
                }

                if (sectionIndexToken.Type != JTokenType.Integer)
                {
                    return Task.FromResult(Failure(request, "reference.invalid_section_index", "sectionIndex must be a positive integer."));
                }

                var requestedIndex = sectionIndexToken.Value<long>();
                if (requestedIndex < 1 || requestedIndex > int.MaxValue)
                {
                    return Task.FromResult(Failure(request, "reference.invalid_section_index", "sectionIndex must be a positive integer."));
                }

                requestedSectionIndex = (int)requestedIndex;
            }

            if (requestedRange != null && !requestedSectionIndex.HasValue)
            {
                return Task.FromResult(Failure(request, "reference.invalid_arguments", "range requires a 1-based sectionIndex identifying the Excel worksheet."));
            }

            if (chunkIndexToken == null || chunkIndexToken.Type == JTokenType.Null)
            {
                if (!requestedSectionIndex.HasValue)
                {
                    string inlinePrefixWasSent;
                    if (reference.Metadata != null
                    && reference.Metadata.TryGetValue("inlinePrefixWasSent", out inlinePrefixWasSent)
                    && string.Equals(inlinePrefixWasSent, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        string rawPrefixCount;
                        int prefixCount;
                        if (reference.Chunks != null
                        && reference.Metadata.TryGetValue("inlinePrefixCharacterCount", out rawPrefixCount)
                        && int.TryParse(rawPrefixCount, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out prefixCount))
                        {
                            var includedCharacters = 0;
                            for (var index = 0; index < reference.Chunks.Count; index++)
                            {
                                var chunkLength = reference.Chunks[index] == null
                                    ? 0
                                    : Math.Max(0, reference.Chunks[index].CharacterCount);
                                if (includedCharacters + chunkLength > prefixCount)
                                {
                                    chunkIndex = index;
                                    break;
                                }

                                includedCharacters += chunkLength;
                                chunkIndex = index + 1;
                            }
                        }
                    }
                }
            }
            else
            {
                if (chunkIndexToken.Type != JTokenType.Integer)
                {
                    return Task.FromResult(Failure(request, "reference.invalid_chunk_index", "chunkIndex must be a non-negative integer."));
                }

                var requestedIndex = chunkIndexToken.Value<long>();
                if (requestedIndex < 0 || requestedIndex > int.MaxValue)
                {
                    return Task.FromResult(Failure(request, "reference.invalid_chunk_index", "chunkIndex must be a non-negative integer."));
                }

                chunkIndex = (int)requestedIndex;
            }

            var chunks = reference.Chunks;
            if (requestedRange != null)
            {
                return Task.FromResult(FetchSpreadsheetRange(reference, requestedSectionIndex.Value, requestedRange, context.CancellationToken, request));
            }
            if (requestedPageNumber.HasValue)
            {
                return Task.FromResult(FetchPageImage(reference, requestedPageNumber.Value, context.CancellationToken, request));
            }

            if (chunks == null || chunks.Count == 0)
            {
                return Task.FromResult(Failure(request, "reference.no_text", "This attachment has no prepared text content to fetch."));
            }

            PreparedReferenceChunk chunk = null;
            for (var index = 0; index < chunks.Count; index++)
            {
                if (chunks[index] != null
                    && (requestedSectionIndex.HasValue
                        ? chunks[index].SectionIndex == requestedSectionIndex
                        : chunks[index].Index == chunkIndex))
                {
                    chunk = chunks[index];
                    break;
                }
            }

            if (chunk == null)
            {
                if (requestedSectionIndex.HasValue)
                {
                    return Task.FromResult(Failure(request, "reference.section_not_found", "sectionIndex is outside the available attachment sections."));
                }

                return Task.FromResult(Failure(request, "reference.chunk_not_found", "chunkIndex is outside the available range 0 through " + (chunks.Count - 1) + "."));
            }

            var content = "[Attachment: " + (reference.DisplayName ?? reference.ReferenceId)
                + " | chunk " + (chunk.Index + 1) + "/" + chunks.Count
                + " | zero-based index " + chunk.Index + "]\n"
                + WrapReferenceData(chunk.Content);
            var result = ToolExecutionResult.CreateSuccess(
                ToolName,
                request == null ? string.Empty : request.ToolCallId,
                content);
            result.Metadata["referenceId"] = reference.ReferenceId;
            result.Metadata["chunkIndex"] = chunk.Index.ToString();
            result.Metadata["chunkCount"] = chunks.Count.ToString();
            if (chunk.SectionIndex.HasValue)
            {
                result.Metadata["sectionIndex"] = chunk.SectionIndex.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                result.Metadata["sectionTitle"] = chunk.SectionTitle ?? string.Empty;
                var nextSectionChunk = chunks.FirstOrDefault(candidate => candidate != null
                    && candidate.Index > chunk.Index
                    && candidate.SectionIndex == chunk.SectionIndex);
                if (nextSectionChunk != null)
                {
                    result.Metadata["nextChunkIndex"] = nextSectionChunk.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            return Task.FromResult(result);
        }

        private static ToolExecutionResult FetchPageImage(
            PreparedReference reference,
            int pageNumber,
            System.Threading.CancellationToken cancellationToken,
            ToolExecutionRequest request)
        {
            var extension = (reference.FileExtension ?? string.Empty).Trim().ToLowerInvariant();
            var isPdf = string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
            var isSupportedOffice = string.Equals(extension, ".doc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".ppt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".pptx", StringComparison.OrdinalIgnoreCase);
            if (!isPdf && !isSupportedOffice)
            {
                return Failure(request, "reference.image_unsupported", "Page images are available only for PDF, Word and PowerPoint attachments; read Excel sheets with sectionIndex and range instead.");
            }

            string path;
            string expectedHash;
            if (reference.Metadata == null
                || !reference.Metadata.TryGetValue("localPath", out path)
                || !reference.Metadata.TryGetValue("sourceSha256", out expectedHash)
                || string.IsNullOrWhiteSpace(path)
                || string.IsNullOrWhiteSpace(expectedHash)
                || !File.Exists(path))
            {
                return Failure(request, "reference.image_source_unavailable", "The original attachment is unavailable for page rendering.");
            }

            var temporaryDirectory = Path.Combine(Path.GetTempPath(), "OfficeCopilot", "ReferencePageImages", Guid.NewGuid().ToString("N"));
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(ReferencePreprocessorUtilities.ComputeFileHash(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(request, "reference.source_changed", "The attachment changed after it was added; add it again before requesting a page image.");
                }

                Directory.CreateDirectory(temporaryDirectory);
                var pdfPath = path;
                if (!isPdf)
                {
                    pdfPath = Path.Combine(temporaryDirectory, "rendered.pdf");
                    string renderError;
                    if (!VisualFallbackRenderer.TryRenderToPdf(path, extension, pdfPath, out renderError, cancellationToken))
                    {
                        return Failure(request, "reference.page_render_failed", "The attachment page could not be rendered for visual inspection.");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                var pageCount = PdfToPngConverter.GetPageCount(pdfPath);
                if (pageNumber > pageCount)
                {
                    return Failure(request, "reference.page_not_found", "pageNumber is outside the attachment's available page range.");
                }

                var imageDirectory = Path.Combine(temporaryDirectory, "images");
                var images = PdfToPngConverter.ConvertPdfPagesToPng(pdfPath, new[] { pageNumber }, imageDirectory);
                cancellationToken.ThrowIfCancellationRequested();
                if (images.Count == 0 || !File.Exists(images[0]))
                {
                    return Failure(request, "reference.page_render_failed", "The requested attachment page image could not be created.");
                }

                var imageBytes = File.ReadAllBytes(images[0]);
                if (imageBytes.Length > 1500000)
                {
                    return Failure(request, "reference.page_image_too_large", "The requested page image exceeds the safe transfer limit.");
                }

                if (!string.Equals(ReferencePreprocessorUtilities.ComputeFileHash(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(request, "reference.source_changed", "The attachment changed during page rendering; add it again before retrying.");
                }

                var result = ToolExecutionResult.CreateSuccess(
                    ToolName,
                    request == null ? string.Empty : request.ToolCallId,
                    "Rendered page " + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + " of attachment " + (reference.DisplayName ?? reference.ReferenceId) + ".");
                result.AddImage("image/png", imageBytes,
                    (reference.DisplayName ?? reference.ReferenceId) + ", page " + pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
                result.Metadata["referenceId"] = reference.ReferenceId;
                result.Metadata["pageNumber"] = pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
                result.Metadata["pageCount"] = pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Failure(request, "reference.page_render_failed", "The requested attachment page could not be rendered.");
            }
            finally
            {
                try
                {
                    if (Directory.Exists(temporaryDirectory))
                    {
                        Directory.Delete(temporaryDirectory, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static ToolExecutionResult FetchSpreadsheetRange(
            PreparedReference reference,
            int sectionIndex,
            string range,
            System.Threading.CancellationToken cancellationToken,
            ToolExecutionRequest request)
        {
            var extension = (reference.FileExtension ?? string.Empty).Trim();
            if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(request, "reference.range_unsupported", "Range fetch is available only for .xlsx and .xlsm attachments.");
            }

            string path;
            string expectedHash;
            if (reference.Metadata == null
                || !reference.Metadata.TryGetValue("localPath", out path)
                || !reference.Metadata.TryGetValue("sourceSha256", out expectedHash)
                || string.IsNullOrWhiteSpace(path)
                || string.IsNullOrWhiteSpace(expectedHash)
                || !File.Exists(path))
            {
                return Failure(request, "reference.range_source_unavailable", "The original workbook snapshot is unavailable for range fetch.");
            }

            var section = reference.Chunks == null
                ? null
                : reference.Chunks.FirstOrDefault(chunk => chunk != null
                    && chunk.SectionIndex == sectionIndex
                    && !string.IsNullOrWhiteSpace(chunk.SectionTitle));
            if (section == null)
            {
                return Failure(request, "reference.section_not_found", "sectionIndex does not identify a prepared worksheet in this attachment.");
            }

            try
            {
                if (!string.Equals(ReferencePreprocessorUtilities.ComputeFileHash(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(request, "reference.source_changed", "The workbook changed after it was attached; add it again before fetching a range.");
                }

                var markdown = new SpreadsheetMarkdownConverter().Convert(path, new MarkdownConversionOptions
                {
                    WorksheetName = section.SectionTitle,
                    CellRange = range,
                    MaxTotalChars = 8000,
                    MaxSheetRows = 100,
                    MaxSheetColumns = 20
                }, cancellationToken);

                if (!string.Equals(ReferencePreprocessorUtilities.ComputeFileHash(path), expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(request, "reference.source_changed", "The workbook changed during range fetch; add it again before retrying.");
                }

                var result = ToolExecutionResult.CreateSuccess(
                    ToolName,
                    request == null ? string.Empty : request.ToolCallId,
                    "[Attachment: " + (reference.DisplayName ?? reference.ReferenceId)
                        + " | worksheet " + section.SectionTitle
                        + " | range " + range + "]\n"
                        + WrapReferenceData(markdown.ToMarkdown()));
                result.Metadata["referenceId"] = reference.ReferenceId;
                result.Metadata["sectionIndex"] = sectionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                result.Metadata["sectionTitle"] = section.SectionTitle;
                result.Metadata["range"] = range;
                result.Metadata["truncated"] = markdown.Truncated.ToString();
                return result;
            }
            catch (ArgumentException exception)
            {
                if (string.Equals(exception.ParamName, "WorksheetName", StringComparison.Ordinal))
                {
                    return Failure(request, "reference.section_not_found", "The worksheet for this sectionIndex is no longer available in the workbook.");
                }

                return Failure(request, "reference.invalid_range", "range must use valid, ordered A1 addresses within Excel worksheet bounds.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return Failure(request, "reference.range_failed", "The requested spreadsheet range could not be read.");
            }
        }

        private static string WrapReferenceData(string text)
        {
            // Attachment text is untrusted data; keep it inside a delimiter it cannot close.
            var safeText = System.Text.RegularExpressions.Regex.Replace(
                text ?? string.Empty,
                @"<(/?)reference_data\b",
                "&lt;$1reference_data",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return "<reference_data>\n" + safeText + "\n</reference_data>";
        }

        private static ToolExecutionResult Failure(ToolExecutionRequest request, string errorCode, string message)
        {
            return ToolExecutionResult.CreateFailure(
                ToolName,
                request == null ? string.Empty : request.ToolCallId,
                message,
                errorCode);
        }
    }
}