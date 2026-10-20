using System.Collections.Generic;

namespace OfficeCopilot.Host.Abstractions
{
    public interface IWordToolHost
    {
        string HostApplicationName { get; }

        WordDocumentInfo GetActiveDocumentInfo();

        WordSelectionInfo GetSelectionInfo();

        WordDocumentTextResult GetDocumentText(WordDocumentTextRequest request);

        WordInsertTextResult InsertText(WordInsertTextRequest request);

        WordFindReplaceResult FindReplace(WordFindReplaceRequest request);

        WordStyleListResult ListStyles();

        WordFormatSelectionResult FormatSelection(WordFormatSelectionRequest request);

        WordParagraphFormatResult SetParagraphFormat(WordParagraphFormatRequest request);

        WordApplyStyleResult ApplyStyle(WordApplyStyleRequest request);

        WordOutlineResult GetOutline(WordOutlineRequest request);

        WordPageSetupResult SetPageSetup(WordPageSetupRequest request);

        WordReadTableResult ReadTable(WordReadTableRequest request);

        WordAddCommentResult AddComment(WordAddCommentRequest request);

        WordTrackChangesResult SetTrackChanges(WordTrackChangesRequest request);

        WordInsertTableResult InsertTable(WordInsertTableRequest request);

        WordAddBookmarkResult AddBookmark(WordAddBookmarkRequest request);

        WordAddHyperlinkResult AddHyperlink(WordAddHyperlinkRequest request);

        WordSetHeaderFooterResult SetHeaderFooter(WordSetHeaderFooterRequest request);

        WordInsertFieldResult InsertField(WordInsertFieldRequest request);

        WordAddContentControlResult AddContentControl(WordAddContentControlRequest request);

        WordMoveToEndResult MoveToEnd();

        WordInsertParagraphResult InsertParagraph(WordInsertParagraphRequest request);

        WordSelectResult SelectDocument();

        WordSelectResult SelectParagraph(WordSelectRequest request);

        WordReadParagraphsResult ReadParagraphs(WordReadParagraphsRequest request);

        WordFormatParagraphsResult FormatParagraphs(WordFormatParagraphsRequest request);
    }

    public sealed class WordDocumentInfo
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsReadOnly { get; set; }
        public int PageCount { get; set; }
        public int ParagraphCount { get; set; }
    }

    public sealed class WordSelectionInfo
    {
        public string Text { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public int PageNumber { get; set; }
    }

    public sealed class WordDocumentTextRequest
    {
        public WordDocumentTextRequest()
        {
            MaxCharacters = 20000;
        }

        /// <summary>Maximum number of characters to return. Use 0 to return everything.</summary>
        public int MaxCharacters { get; set; }
    }

    public sealed class WordDocumentTextResult
    {
        public WordDocumentTextResult()
        {
            DocumentName = string.Empty;
            Text = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string DocumentName { get; set; }
        public string Text { get; set; }
        public int TotalCharacters { get; set; }
        public bool Truncated { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordInsertTextRequest
    {
        public WordInsertTextRequest()
        {
            Text = string.Empty;
        }

        public string Text { get; set; }

        /// <summary>When true, the current selection is replaced; otherwise text is inserted at the cursor.</summary>
        public bool ReplaceSelection { get; set; }

        /// <summary>When true, the cursor is moved to the end of the document first.</summary>
        public bool InsertAtEnd { get; set; }
    }

    public sealed class WordInsertTextResult
    {
        public WordInsertTextResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int InsertedCharacters { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordFindReplaceRequest
    {
        public WordFindReplaceRequest()
        {
            FindText = string.Empty;
            ReplaceWith = string.Empty;
            ReplaceAll = true;
        }

        public string FindText { get; set; }
        public string ReplaceWith { get; set; }
        public bool MatchCase { get; set; }
        public bool ReplaceAll { get; set; }
    }

    public sealed class WordFindReplaceResult
    {
        public WordFindReplaceResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int ReplacementCount { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordStyleListResult
    {
        public WordStyleListResult()
        {
            Styles = new List<WordStyleInfo>();
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public IList<WordStyleInfo> Styles { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordStyleInfo
    {
        public WordStyleInfo()
        {
            Name = string.Empty;
            NameLocal = string.Empty;
        }

        public string Name { get; set; }
        public string NameLocal { get; set; }
        public bool IsBuiltIn { get; set; }
        public int StyleType { get; set; }
    }

    public sealed class WordFormatSelectionRequest
    {
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public bool? Underline { get; set; }
        public string Color { get; set; }
        public double? Size { get; set; }
        public string FontName { get; set; }
        public string HighlightColor { get; set; }
        public bool? Subscript { get; set; }
        public bool? Superscript { get; set; }

        /// <summary>When set, formatting applies to find matches instead of the selection.</summary>
        public string FindText { get; set; }

        public bool ReplaceAll { get; set; }
    }

    public sealed class WordFormatSelectionResult
    {
        public WordFormatSelectionResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int AppliedCount { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordParagraphFormatRequest
    {
        public string Alignment { get; set; }
        public string LineSpacingRule { get; set; }
        public double? LineSpacing { get; set; }
        public double? SpaceBefore { get; set; }
        public double? SpaceAfter { get; set; }
        public double? FirstLineIndent { get; set; }
        public double? LeftIndent { get; set; }
        public double? RightIndent { get; set; }
    }

    public sealed class WordParagraphFormatResult
    {
        public WordParagraphFormatResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int AppliedCount { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordApplyStyleRequest
    {
        public string StyleName { get; set; }
    }

    public sealed class WordApplyStyleResult
    {
        public WordApplyStyleResult()
        {
            AppliedStyle = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string AppliedStyle { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordOutlineRequest
    {
        public WordOutlineRequest()
        {
            MaxLevel = 9;
        }

        public int MaxLevel { get; set; }
    }

    public sealed class WordOutlineResult
    {
        public WordOutlineResult()
        {
            Items = new List<WordOutlineItem>();
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public IList<WordOutlineItem> Items { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordOutlineItem
    {
        public WordOutlineItem()
        {
            Text = string.Empty;
        }

        public int Level { get; set; }
        public string Text { get; set; }
    }

    public sealed class WordPageSetupRequest
    {
        public double? TopMargin { get; set; }
        public double? BottomMargin { get; set; }
        public double? LeftMargin { get; set; }
        public double? RightMargin { get; set; }
        public double? PageWidth { get; set; }
        public double? PageHeight { get; set; }
        public string Orientation { get; set; }
    }

    public sealed class WordPageSetupResult
    {
        public WordPageSetupResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordReadTableRequest
    {
        public WordReadTableRequest()
        {
            TableIndex = 1;
            MaxRows = 200;
        }

        public int TableIndex { get; set; }
        public int MaxRows { get; set; }
    }

    public sealed class WordReadTableResult
    {
        public WordReadTableResult()
        {
            Rows = new List<WordTableRow>();
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int TableCount { get; set; }
        public IList<WordTableRow> Rows { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordTableRow
    {
        public WordTableRow()
        {
            Cells = new List<string>();
        }

        public IList<string> Cells { get; set; }
    }

    public sealed class WordAddCommentRequest
    {
        public string Text { get; set; }
    }

    public sealed class WordAddCommentResult
    {
        public WordAddCommentResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int CommentCount { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordTrackChangesRequest
    {
        public bool Enable { get; set; }
    }

    public sealed class WordTrackChangesResult
    {
        public WordTrackChangesResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public bool Enabled { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordInsertTableRequest
    {
        public WordInsertTableRequest()
        {
            Rows = 1;
            Columns = 1;
            Data = new List<IList<string>>();
        }

        public int Rows { get; set; }
        public int Columns { get; set; }
        public IList<IList<string>> Data { get; set; }
        public bool InsertAtEnd { get; set; }
    }

    public sealed class WordInsertTableResult
    {
        public WordInsertTableResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int TableCount { get; set; }
        public int Rows { get; set; }
        public int Columns { get; set; }
        public bool EscapedNestedTable { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordAddBookmarkRequest
    {
        public string Name { get; set; }
    }

    public sealed class WordAddBookmarkResult
    {
        public WordAddBookmarkResult()
        {
            BookmarkName = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string BookmarkName { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordAddHyperlinkRequest
    {
        public string Address { get; set; }
        public string Text { get; set; }
        public string SubAddress { get; set; }
    }

    public sealed class WordAddHyperlinkResult
    {
        public WordAddHyperlinkResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordSetHeaderFooterRequest
    {
        public WordSetHeaderFooterRequest()
        {
            SectionIndex = 1;
            Target = "primary";
        }

        public string HeaderText { get; set; }
        public string FooterText { get; set; }
        public string Target { get; set; }
        public int SectionIndex { get; set; }
    }

    public sealed class WordSetHeaderFooterResult
    {
        public WordSetHeaderFooterResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordInsertFieldRequest
    {
        public string FieldType { get; set; }
    }

    public sealed class WordInsertFieldResult
    {
        public WordInsertFieldResult()
        {
            FieldName = string.Empty;
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string FieldName { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordAddContentControlRequest
    {
        public WordAddContentControlRequest()
        {
            Type = "text";
        }

        public string Type { get; set; }
        public string Title { get; set; }
        public string Tag { get; set; }
    }

    public sealed class WordAddContentControlResult
    {
        public WordAddContentControlResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordMoveToEndResult
    {
        public WordMoveToEndResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public int PageNumber { get; set; }
        public string Preview { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordInsertParagraphRequest
    {
        public WordInsertParagraphRequest()
        {
            Count = 1;
        }

        public int Count { get; set; }
    }

    public sealed class WordInsertParagraphResult
    {
        public WordInsertParagraphResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int InsertedParagraphs { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordSelectRequest
    {
        public WordSelectRequest()
        {
            ParagraphIndex = 1;
        }

        public int ParagraphIndex { get; set; }
    }

    public sealed class WordSelectResult
    {
        public WordSelectResult()
        {
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public int ParagraphIndex { get; set; }
        public string SelectedText { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordReadParagraphsRequest
    {
        public WordReadParagraphsRequest()
        {
            MaxCount = 200;
        }

        /// <summary>Maximum number of paragraphs to return. Use 0 to return all.</summary>
        public int MaxCount { get; set; }
    }

    public sealed class WordReadParagraphsResult
    {
        public WordReadParagraphsResult()
        {
            Paragraphs = new List<WordParagraphInfo>();
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int TotalParagraphs { get; set; }
        public IList<WordParagraphInfo> Paragraphs { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class WordParagraphInfo
    {
        public WordParagraphInfo()
        {
            Text = string.Empty;
        }

        public int Index { get; set; }
        public string Text { get; set; }
    }

    /// <summary>One formatting role (e.g. title / h1 / body) shared by many paragraphs.</summary>
    public sealed class WordParagraphRoleFormat
    {
        /// <summary>Built-in style id such as Title / Heading1 / Normal; works in any UI language.</summary>
        public string BuiltinStyle { get; set; }

        /// <summary>Localized or custom style name; used only when BuiltinStyle is empty.</summary>
        public string StyleName { get; set; }

        public string FontName { get; set; }
        public string LatinFontName { get; set; }
        public double? Size { get; set; }
        public bool? Bold { get; set; }
        public bool? Italic { get; set; }
        public string Color { get; set; }
        public string Alignment { get; set; }
        public string LineSpacingRule { get; set; }
        public double? LineSpacing { get; set; }
        public double? SpaceBefore { get; set; }
        public double? SpaceAfter { get; set; }
        public double? FirstLineIndent { get; set; }
        public double? FirstLineIndentChars { get; set; }
        public double? LeftIndent { get; set; }
        public double? RightIndent { get; set; }
    }

    public sealed class WordParagraphRoleAssignment
    {
        public int Index { get; set; }
        public string Role { get; set; }

        /// <summary>Expected leading text; the paragraph is skipped when it does not match.</summary>
        public string Expect { get; set; }
    }

    public sealed class WordFormatParagraphsRequest
    {
        public WordFormatParagraphsRequest()
        {
            Roles = new Dictionary<string, WordParagraphRoleFormat>(System.StringComparer.OrdinalIgnoreCase);
            Paragraphs = new List<WordParagraphRoleAssignment>();
        }

        public IDictionary<string, WordParagraphRoleFormat> Roles { get; set; }
        public IList<WordParagraphRoleAssignment> Paragraphs { get; set; }

        /// <summary>Role applied to non-empty, non-table paragraphs that are not listed explicitly.</summary>
        public string DefaultRole { get; set; }

        /// <summary>When true, role formatting is written into the style definitions instead of each paragraph.</summary>
        public bool UpdateStyleDefinitions { get; set; }

        /// <summary>When true, existing direct font/paragraph formatting is reset before the role is applied.</summary>
        public bool ClearDirectFormatting { get; set; }
    }

    public sealed class WordParagraphFormatSkip
    {
        public int Index { get; set; }
        public string Reason { get; set; }
    }

    public sealed class WordFormatParagraphsResult
    {
        public WordFormatParagraphsResult()
        {
            Skipped = new List<WordParagraphFormatSkip>();
            RoleCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            UpdatedStyles = new List<string>();
            ErrorMessage = string.Empty;
        }

        public bool Success { get; set; }
        public int TotalParagraphs { get; set; }
        public int AppliedCount { get; set; }
        public IList<WordParagraphFormatSkip> Skipped { get; set; }
        public IDictionary<string, int> RoleCounts { get; set; }
        public IList<string> UpdatedStyles { get; set; }
        public bool UndoGrouped { get; set; }
        public string ErrorMessage { get; set; }
    }
}
