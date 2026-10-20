using OfficeCopilot.Host.Abstractions;
using OfficeCopilot.Host.Word.Formatting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WordApp = Microsoft.Office.Interop.Word.Application;
using WordInterop = Microsoft.Office.Interop.Word;
using Word = Microsoft.Office.Interop.Word;

namespace OfficeCopilot.Host.Word
{
    public sealed class WordHostAdapter : ICopilotHostAdapter, IDocumentBackupHostAdapter, IWordToolHost
    {
        private readonly WordApp _application;

        public WordHostAdapter()
            : this(null)
        {
        }

        public WordHostAdapter(object application)
        {
            _application = application as WordApp;
        }

        public string HostApplicationName
        {
            get { return "Word"; }
        }

        public HostContextSnapshot GetContextSnapshot()
        {
            if (_application == null)
            {
                return new HostContextSnapshot
                {
                    HostApplicationName = HostApplicationName,
                    DocumentName = "No document",
                    Summary = "Word application is not connected."
                };
            }

            var doc = _application.ActiveDocument;
            if (doc == null)
            {
                return new HostContextSnapshot
                {
                    HostApplicationName = HostApplicationName,
                    DocumentName = "No document",
                    Summary = "No active Word document."
                };
            }

            var documentName = doc.Name ?? "Untitled";
            var selection = _application.Selection;
            var selectionText = string.Empty;

            if (selection != null)
            {
                try
                {
                    selectionText = selection.Text;
                    if (selectionText != null && selectionText.Length > 500)
                    {
                        selectionText = selectionText.Substring(0, 500) + "...";
                    }
                }
                catch
                {
                    selectionText = string.Empty;
                }
            }

            var pageInfo = string.Empty;
            try
            {
                var currentPage = selection?.Information[Microsoft.Office.Interop.Word.WdInformation.wdActiveEndPageNumber];
                var totalPages = doc.ComputeStatistics(Microsoft.Office.Interop.Word.WdStatistic.wdStatisticPages);
                pageInfo = $"Page {currentPage} of {totalPages}";
            }
            catch
            {
                pageInfo = string.Empty;
            }

            var summary = string.IsNullOrWhiteSpace(documentName)
                ? "No active Word document."
                : $"Active document: {documentName}" + (string.IsNullOrEmpty(pageInfo) ? "" : $" ({pageInfo})");

            return new HostContextSnapshot
            {
                HostApplicationName = HostApplicationName,
                DocumentName = string.IsNullOrWhiteSpace(documentName) ? "Untitled" : documentName,
                SelectionPreview = selectionText,
                Summary = summary
            };
        }

        public WordDocumentInfo GetActiveDocumentInfo()
        {
            if (_application == null) return new WordDocumentInfo { IsAvailable = false };
            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null) return new WordDocumentInfo { IsAvailable = false };
                return new WordDocumentInfo
                {
                    Name = doc.Name ?? string.Empty,
                    FullName = doc.FullName ?? string.Empty,
                    IsAvailable = true,
                    IsReadOnly = doc.ReadOnly,
                    PageCount = doc.ComputeStatistics(Microsoft.Office.Interop.Word.WdStatistic.wdStatisticPages),
                    ParagraphCount = doc.Paragraphs.Count
                };
            }
            catch { return new WordDocumentInfo { IsAvailable = false }; }
        }

        public WordSelectionInfo GetSelectionInfo()
        {
            if (_application == null) return null;
            try
            {
                var sel = _application.Selection;
                if (sel == null) return null;
                var text = sel.Text ?? string.Empty;
                if (text.Length > 2000) text = text.Substring(0, 2000) + "...";
                return new WordSelectionInfo
                {
                    Text = text,
                    StartPosition = sel.Start,
                    EndPosition = sel.End,
                    PageNumber = (int)(sel.Information[Microsoft.Office.Interop.Word.WdInformation.wdActiveEndPageNumber] ?? 1)
                };
            }
            catch { return null; }
        }

        public void InsertText(string text)
        {
            if (_application == null)
            {
                return;
            }

            var selection = _application.Selection;
            if (selection == null)
            {
                return;
            }

            selection.TypeText(text ?? string.Empty);
        }

        public WordDocumentTextResult GetDocumentText(WordDocumentTextRequest request)
        {
            request = request ?? new WordDocumentTextRequest();
            var result = new WordDocumentTextResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                result.DocumentName = doc.Name ?? string.Empty;
                var fullText = doc.Content.Text ?? string.Empty;
                result.TotalCharacters = fullText.Length;

                var max = request.MaxCharacters;
                if (max <= 0 || fullText.Length <= max)
                {
                    result.Text = fullText;
                    result.Truncated = false;
                }
                else
                {
                    result.Text = fullText.Substring(0, max);
                    result.Truncated = true;
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordInsertTextResult InsertText(WordInsertTextRequest request)
        {
            request = request ?? new WordInsertTextRequest();
            var result = new WordInsertTextResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection or document.";
                    return result;
                }

                var text = request.Text ?? string.Empty;

                if (request.InsertAtEnd)
                {
                    var endRange = doc.Content;
                    endRange.Collapse(WordInterop.WdCollapseDirection.wdCollapseEnd);
                    endRange.Select();
                    selection = _application.Selection;
                }

                if (request.ReplaceSelection && !string.IsNullOrEmpty(selection.Text))
                {
                    // word_select_paragraph selects the trailing paragraph mark; replacing it would
                    // merge this paragraph with the next one and shift every later index.
                    if (WordParagraphFormatPlanner.ShouldKeepTrailingParagraphMark(selection.Text, text))
                    {
                        selection.SetRange(selection.Start, selection.End - 1);
                    }

                    selection.Text = text;
                }
                else
                {
                    // Never destroy an existing selection when appending: collapse
                    // to its end, then insert plain text (no inherited formatting).
                    selection.SetRange(selection.End, selection.End);
                    ClearDirectCharacterFormatting(selection);
                    selection.TypeText(text);
                }

                result.Success = true;
                result.InsertedCharacters = text.Length;
                result.StartPosition = selection.Start;
                result.EndPosition = selection.End;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordFindReplaceResult FindReplace(WordFindReplaceRequest request)
        {
            request = request ?? new WordFindReplaceRequest();
            var result = new WordFindReplaceResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var findText = request.FindText ?? string.Empty;
                if (string.IsNullOrEmpty(findText))
                {
                    result.ErrorMessage = "Find text cannot be empty.";
                    return result;
                }

                var replaceWith = request.ReplaceWith ?? string.Empty;
                var matchCase = request.MatchCase;

                var count = CountMatches(doc, findText, matchCase);
                if (count > 0)
                {
                    var find = doc.Content.Find;
                    find.ClearFormatting();
                    find.Replacement.ClearFormatting();
                    find.Text = findText;
                    find.Replacement.Text = replaceWith;
                    find.MatchCase = matchCase;
                    find.Forward = true;
                    find.Wrap = WordInterop.WdFindWrap.wdFindContinue;
                    find.Execute(Replace: request.ReplaceAll
                        ? WordInterop.WdReplace.wdReplaceAll
                        : WordInterop.WdReplace.wdReplaceOne);
                }

                result.Success = true;
                result.ReplacementCount = count;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordStyleListResult ListStyles()
        {
            var result = new WordStyleListResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                foreach (WordInterop.Style style in doc.Styles)
                {
                    if (style == null)
                    {
                        continue;
                    }

                    result.Styles.Add(new WordStyleInfo
                    {
                        Name = style.NameLocal ?? string.Empty,
                        NameLocal = style.NameLocal ?? string.Empty,
                        IsBuiltIn = style.BuiltIn,
                        StyleType = (int)style.Type
                    });
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordFormatSelectionResult FormatSelection(WordFormatSelectionRequest request)
        {
            request = request ?? new WordFormatSelectionRequest();
            var result = new WordFormatSelectionResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                int applied = 0;
                if (!string.IsNullOrWhiteSpace(request.FindText))
                {
                    var range = doc.Content;
                    var find = range.Find;
                    find.ClearFormatting();
                    find.Text = request.FindText;
                    find.Forward = true;
                    find.Wrap = WordInterop.WdFindWrap.wdFindStop;

                    find.Execute();
                    while (find.Found)
                    {
                        ApplyFont(range.Font, request);
                        ApplyHighlight(range, request.HighlightColor);
                        applied++;
                        if (!request.ReplaceAll)
                        {
                            break;
                        }

                        range.Collapse(WordInterop.WdCollapseDirection.wdCollapseEnd);
                        find.Execute();
                    }
                }
                else
                {
                    var selection = _application.Selection;
                    if (selection == null)
                    {
                        result.ErrorMessage = "No active selection.";
                        return result;
                    }

                    // Format visible characters only, excluding the trailing paragraph
                    // mark(s), so the direct formatting does not leak into the next
                    // paragraph.
                    var formatRange = GetCharacterFormatRange(selection.Range);
                    ApplyFont(formatRange.Font, request);
                    ApplyHighlight(formatRange, request.HighlightColor);
                    applied = 1;

                    // Leave the cursor collapsed at the end of the formatted range.
                    selection.SetRange(formatRange.End, formatRange.End);
                }

                result.Success = true;
                result.AppliedCount = applied;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordParagraphFormatResult SetParagraphFormat(WordParagraphFormatRequest request)
        {
            request = request ?? new WordParagraphFormatRequest();
            var result = new WordParagraphFormatResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection.";
                    return result;
                }

                ApplyParagraphFormat(selection.ParagraphFormat, request);

                // Collapse the cursor to the end of the formatted range.
                selection.SetRange(selection.End, selection.End);

                result.Success = true;
                result.AppliedCount = 1;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordApplyStyleResult ApplyStyle(WordApplyStyleRequest request)
        {
            request = request ?? new WordApplyStyleRequest();
            var result = new WordApplyStyleResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection.";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(request.StyleName))
                {
                    result.ErrorMessage = "StyleName is required.";
                    return result;
                }

                object styleName = ResolveStyleKey(request.StyleName);
                selection.set_Style(ref styleName);

                // Collapse the cursor to the end of the styled range.
                selection.SetRange(selection.End, selection.End);

                result.Success = true;
                result.AppliedStyle = request.StyleName;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordOutlineResult GetOutline(WordOutlineRequest request)
        {
            request = request ?? new WordOutlineRequest();
            var result = new WordOutlineResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                int maxLevel = request.MaxLevel <= 0 || request.MaxLevel > 9 ? 9 : request.MaxLevel;
                foreach (WordInterop.Paragraph para in doc.Paragraphs)
                {
                    if (para == null)
                    {
                        continue;
                    }

                    int level;
                    try
                    {
                        level = (int)para.OutlineLevel;
                    }
                    catch
                    {
                        continue;
                    }

                    if (level < 1 || level > maxLevel)
                    {
                        continue;
                    }

                    var text = (para.Range.Text ?? string.Empty).Trim();
                    if (text.Length == 0)
                    {
                        continue;
                    }

                    if (text.Length > 500)
                    {
                        text = text.Substring(0, 500);
                    }

                    result.Items.Add(new WordOutlineItem { Level = level, Text = text });
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordPageSetupResult SetPageSetup(WordPageSetupRequest request)
        {
            request = request ?? new WordPageSetupRequest();
            var result = new WordPageSetupResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var pageSetup = doc.PageSetup;
                if (request.TopMargin.HasValue) pageSetup.TopMargin = (float)request.TopMargin.Value;
                if (request.BottomMargin.HasValue) pageSetup.BottomMargin = (float)request.BottomMargin.Value;
                if (request.LeftMargin.HasValue) pageSetup.LeftMargin = (float)request.LeftMargin.Value;
                if (request.RightMargin.HasValue) pageSetup.RightMargin = (float)request.RightMargin.Value;
                if (request.PageWidth.HasValue) pageSetup.PageWidth = (float)request.PageWidth.Value;
                if (request.PageHeight.HasValue) pageSetup.PageHeight = (float)request.PageHeight.Value;
                if (!string.IsNullOrWhiteSpace(request.Orientation))
                {
                    pageSetup.Orientation = request.Orientation.Trim().Equals("landscape", StringComparison.OrdinalIgnoreCase)
                        ? WordInterop.WdOrientation.wdOrientLandscape
                        : WordInterop.WdOrientation.wdOrientPortrait;
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordReadTableResult ReadTable(WordReadTableRequest request)
        {
            request = request ?? new WordReadTableRequest();
            var result = new WordReadTableResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                result.TableCount = doc.Tables.Count;
                if (result.TableCount == 0)
                {
                    result.Success = true;
                    return result;
                }

                int tableIndex = request.TableIndex < 1 || request.TableIndex > result.TableCount
                    ? 1
                    : request.TableIndex;
                var table = doc.Tables[tableIndex];

                int maxRows = request.MaxRows <= 0 ? 200 : request.MaxRows;
                int rowCount = 0;
                foreach (WordInterop.Row row in table.Rows)
                {
                    if (row == null)
                    {
                        continue;
                    }

                    if (rowCount >= maxRows)
                    {
                        break;
                    }

                    var tableRow = new WordTableRow();
                    foreach (WordInterop.Cell cell in row.Cells)
                    {
                        if (cell == null)
                        {
                            continue;
                        }

                        var text = (cell.Range.Text ?? string.Empty).TrimEnd('\r', '\a').Trim();
                        tableRow.Cells.Add(text);
                    }

                    result.Rows.Add(tableRow);
                    rowCount++;
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordAddCommentResult AddComment(WordAddCommentRequest request)
        {
            request = request ?? new WordAddCommentRequest();
            var result = new WordAddCommentResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection.";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    result.ErrorMessage = "Comment text is required.";
                    return result;
                }

                object text = request.Text;
                doc.Comments.Add(selection.Range, ref text);
                result.Success = true;
                result.CommentCount = doc.Comments.Count;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordTrackChangesResult SetTrackChanges(WordTrackChangesRequest request)
        {
            request = request ?? new WordTrackChangesRequest();
            var result = new WordTrackChangesResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                doc.TrackRevisions = request.Enable;
                result.Success = true;
                result.Enabled = doc.TrackRevisions;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordInsertTableResult InsertTable(WordInsertTableRequest request)
        {
            request = request ?? new WordInsertTableRequest();
            var result = new WordInsertTableResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                int rows = request.Rows < 1 ? 1 : request.Rows;
                int columns = request.Columns < 1 ? 1 : request.Columns;

                // Derive the table dimensions from the supplied data when the caller
                // only provided `data` and left rows/columns at their defaults (1).
                if (request.Data != null && request.Data.Count > 0)
                {
                    int dataRows = request.Data.Count;
                    int dataCols = 0;
                    for (int i = 0; i < dataRows; i++)
                    {
                        var dataRow = request.Data[i];
                        if (dataRow != null && dataRow.Count > dataCols)
                        {
                            dataCols = dataRow.Count;
                        }
                    }

                    if (dataRows > rows)
                    {
                        rows = dataRows;
                    }

                    if (dataCols > columns)
                    {
                        columns = dataCols;
                    }
                }

                result.Rows = rows;
                result.Columns = columns;

                WordInterop.Range range;
                if (request.InsertAtEnd)
                {
                    range = doc.Content;
                    range.Collapse(WordInterop.WdCollapseDirection.wdCollapseEnd);
                }
                else
                {
                    var selection = _application.Selection;
                    if (selection == null)
                    {
                        result.ErrorMessage = "No active selection.";
                        return result;
                    }

                    // Guard against nesting: inserting a table while the cursor is
                    // already inside a table would create a table-in-table. Escape to
                    // the end of the document instead and flag it in the result.
                    if (IsInsideTable(selection))
                    {
                        result.EscapedNestedTable = true;
                        range = doc.Content;
                        range.Collapse(WordInterop.WdCollapseDirection.wdCollapseEnd);
                    }
                    else
                    {
                        // Never replace a non-collapsed selection: insert after it.
                        selection.SetRange(selection.End, selection.End);
                        range = selection.Range;
                    }
                }

                object defaultBehavior = WordInterop.WdDefaultTableBehavior.wdWord9TableBehavior;
                object autoFit = WordInterop.WdAutoFitBehavior.wdAutoFitWindow;
                var table = doc.Tables.Add(range, rows, columns, ref defaultBehavior, ref autoFit);

                if (request.Data != null)
                {
                    for (int r = 0; r < rows && r < request.Data.Count; r++)
                    {
                        var rowData = request.Data[r];
                        if (rowData == null)
                        {
                            continue;
                        }

                        for (int c = 0; c < columns && c < rowData.Count; c++)
                        {
                            table.Cell(r + 1, c + 1).Range.Text = rowData[c] ?? string.Empty;
                        }
                    }
                }

                // Move the cursor to just after the new table so subsequent inserts
                // (text or another table) land below it, not inside its cells.
                var afterTable = table.Range;
                afterTable.Collapse(WordInterop.WdCollapseDirection.wdCollapseEnd);
                var selectionAfter = _application.Selection;
                if (selectionAfter != null)
                {
                    selectionAfter.SetRange(afterTable.Start, afterTable.End);
                    result.StartPosition = selectionAfter.Start;
                    result.EndPosition = selectionAfter.End;
                }
                else
                {
                    result.StartPosition = afterTable.Start;
                    result.EndPosition = afterTable.End;
                }

                result.Success = true;
                result.TableCount = doc.Tables.Count;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordAddBookmarkResult AddBookmark(WordAddBookmarkRequest request)
        {
            request = request ?? new WordAddBookmarkRequest();
            var result = new WordAddBookmarkResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection.";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(request.Name))
                {
                    result.ErrorMessage = "Name is required.";
                    return result;
                }

                object range = selection.Range;
                doc.Bookmarks.Add(request.Name, ref range);
                result.Success = true;
                result.BookmarkName = request.Name;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordAddHyperlinkResult AddHyperlink(WordAddHyperlinkRequest request)
        {
            request = request ?? new WordAddHyperlinkRequest();
            var result = new WordAddHyperlinkResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection.";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(request.Address))
                {
                    result.ErrorMessage = "Address is required.";
                    return result;
                }

                object anchor = selection.Range;
                object address = request.Address;
                object subAddress = string.IsNullOrEmpty(request.SubAddress) ? Type.Missing : (object)request.SubAddress;
                object screenTip = Type.Missing;
                object textToDisplay = string.IsNullOrEmpty(request.Text) ? Type.Missing : (object)request.Text;
                object target = Type.Missing;

                doc.Hyperlinks.Add(anchor, ref address, ref subAddress, ref screenTip, ref textToDisplay, ref target);
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordSetHeaderFooterResult SetHeaderFooter(WordSetHeaderFooterRequest request)
        {
            request = request ?? new WordSetHeaderFooterRequest();
            var result = new WordSetHeaderFooterResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                int sectionIndex = request.SectionIndex < 1 ? 1 : request.SectionIndex;
                if (sectionIndex > doc.Sections.Count)
                {
                    result.ErrorMessage = "Section index out of range.";
                    return result;
                }

                var section = doc.Sections[sectionIndex];
                var target = ParseHeaderFooterTarget(request.Target);

                if (!string.IsNullOrWhiteSpace(request.HeaderText))
                {
                    section.Headers[target].Range.Text = request.HeaderText;
                }

                if (!string.IsNullOrWhiteSpace(request.FooterText))
                {
                    section.Footers[target].Range.Text = request.FooterText;
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordInsertFieldResult InsertField(WordInsertFieldRequest request)
        {
            request = request ?? new WordInsertFieldRequest();
            var result = new WordInsertFieldResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection.";
                    return result;
                }

                var fieldType = ParseFieldType(request.FieldType);
                if (!fieldType.HasValue)
                {
                    result.ErrorMessage = "Unknown field type: " + (request.FieldType ?? string.Empty);
                    return result;
                }

                object type = fieldType.Value;
                object text = Type.Missing;
                object preserve = true;
                selection.Fields.Add(selection.Range, ref type, ref text, ref preserve);

                result.Success = true;
                result.FieldName = request.FieldType;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordAddContentControlResult AddContentControl(WordAddContentControlRequest request)
        {
            request = request ?? new WordAddContentControlRequest();
            var result = new WordAddContentControlResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection.";
                    return result;
                }

                var ccType = ParseContentControlType(request.Type);
                if (!ccType.HasValue)
                {
                    result.ErrorMessage = "Unknown content control type: " + request.Type;
                    return result;
                }

                object range = selection.Range;
                var cc = doc.ContentControls.Add(ccType.Value, ref range);
                if (!string.IsNullOrWhiteSpace(request.Title)) cc.Title = request.Title;
                if (!string.IsNullOrWhiteSpace(request.Tag)) cc.Tag = request.Tag;

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordMoveToEndResult MoveToEnd()
        {
            var result = new WordMoveToEndResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var endRange = doc.Content;
                endRange.Collapse(WordInterop.WdCollapseDirection.wdCollapseEnd);
                endRange.Select();

                var selection = _application.Selection;
                result.StartPosition = selection.Start;
                result.EndPosition = selection.End;
                result.PageNumber = (int)(selection.Information[WordInterop.WdInformation.wdActiveEndPageNumber] ?? 1);

                var docText = doc.Content.Text ?? string.Empty;
                int tailStart = docText.Length > 200 ? docText.Length - 200 : 0;
                result.Preview = docText.Substring(tailStart);

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordInsertParagraphResult InsertParagraph(WordInsertParagraphRequest request)
        {
            request = request ?? new WordInsertParagraphRequest();
            var result = new WordInsertParagraphResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                var selection = _application.Selection;
                if (selection == null)
                {
                    result.ErrorMessage = "No active selection or document.";
                    return result;
                }

                int count = request.Count < 1 ? 1 : (request.Count > 100 ? 100 : request.Count);

                // Never destroy an existing selection: collapse to its end first.
                selection.SetRange(selection.End, selection.End);

                for (int i = 0; i < count; i++)
                {
                    selection.TypeParagraph();
                }

                // The new paragraph inherits the previous run's direct character
                // formatting (e.g. a bold/large title). Clear it so body text
                // stays plain.
                ClearDirectCharacterFormatting(selection);

                result.Success = true;
                result.InsertedParagraphs = count;
                result.StartPosition = selection.Start;
                result.EndPosition = selection.End;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordSelectResult SelectDocument()
        {
            var result = new WordSelectResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                doc.Content.Select();

                var selection = _application.Selection;
                result.StartPosition = selection.Start;
                result.EndPosition = selection.End;
                result.SelectedText = TruncateText(selection.Text, 500);
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordSelectResult SelectParagraph(WordSelectRequest request)
        {
            request = request ?? new WordSelectRequest();
            var result = new WordSelectResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                int index = request.ParagraphIndex < 1 ? 1 : request.ParagraphIndex;
                if (index > doc.Paragraphs.Count)
                {
                    result.ErrorMessage = "Paragraph index out of range (document has " + doc.Paragraphs.Count + " paragraphs).";
                    return result;
                }

                var paragraphRange = doc.Paragraphs[index].Range;
                paragraphRange.Select();

                var selection = _application.Selection;
                result.ParagraphIndex = index;
                result.StartPosition = selection.Start;
                result.EndPosition = selection.End;
                result.SelectedText = TruncateText(selection.Text, 500);
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordReadParagraphsResult ReadParagraphs(WordReadParagraphsRequest request)
        {
            request = request ?? new WordReadParagraphsRequest();
            var result = new WordReadParagraphsResult();

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            try
            {
                var doc = _application.ActiveDocument;
                if (doc == null)
                {
                    result.ErrorMessage = "No active Word document.";
                    return result;
                }

                result.TotalParagraphs = doc.Paragraphs.Count;
                int maxCount = request.MaxCount <= 0 ? int.MaxValue : request.MaxCount;

                int index = 0;
                foreach (WordInterop.Paragraph para in doc.Paragraphs)
                {
                    if (para == null)
                    {
                        continue;
                    }

                    if (index >= maxCount)
                    {
                        break;
                    }

                    var text = (para.Range.Text ?? string.Empty).TrimEnd('\r', '\a', '\v', '\f').Trim();
                    result.Paragraphs.Add(new WordParagraphInfo
                    {
                        Index = index + 1,
                        Text = text
                    });
                    index++;
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public WordFormatParagraphsResult FormatParagraphs(WordFormatParagraphsRequest request)
        {
            var result = new WordFormatParagraphsResult();
            var validationError = WordParagraphFormatPlanner.Validate(request);
            if (validationError != null)
            {
                result.ErrorMessage = validationError;
                return result;
            }

            if (_application == null)
            {
                result.ErrorMessage = "Word application is not connected.";
                return result;
            }

            WordInterop.Document doc;
            try
            {
                doc = _application.ActiveDocument;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "No active Word document: " + ex.Message;
                return result;
            }

            if (doc == null)
            {
                result.ErrorMessage = "No active Word document.";
                return result;
            }

            // Paragraphs[i] is O(i) in Word; capture every paragraph once.
            var paragraphs = new List<WordInterop.Paragraph>();
            var snapshots = new List<WordParagraphSnapshot>();
            try
            {
                var tableRanges = new List<KeyValuePair<int, int>>();
                foreach (WordInterop.Table table in doc.Tables)
                {
                    tableRanges.Add(new KeyValuePair<int, int>(table.Range.Start, table.Range.End));
                }

                foreach (WordInterop.Paragraph para in doc.Paragraphs)
                {
                    paragraphs.Add(para);
                    var range = para.Range;
                    var start = range.Start;
                    snapshots.Add(new WordParagraphSnapshot
                    {
                        Index = paragraphs.Count,
                        Text = range.Text ?? string.Empty,
                        InTable = tableRanges.Any(t => start >= t.Key && start < t.Value)
                    });
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Failed to read paragraphs: " + ex.Message;
                return result;
            }

            result.TotalParagraphs = paragraphs.Count;

            var styles = new Dictionary<string, WordInterop.Style>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in request.Roles)
            {
                var styleKey = !string.IsNullOrWhiteSpace(pair.Value.BuiltinStyle) ? pair.Value.BuiltinStyle : pair.Value.StyleName;
                if (string.IsNullOrWhiteSpace(styleKey))
                {
                    continue;
                }

                try
                {
                    object key = ResolveStyleKey(styleKey);
                    styles[pair.Key] = doc.Styles[ref key];
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = "Role '" + pair.Key + "': style '" + styleKey + "' not found (" + ex.Message
                        + "). Use builtinStyle (Title / Heading1 / Normal ...) or a name returned by word_list_styles. No changes were made.";
                    return result;
                }
            }

            var plan = WordParagraphFormatPlanner.BuildPlan(request, snapshots);
            foreach (var skip in plan.Skipped)
            {
                result.Skipped.Add(skip);
            }

            WordInterop.UndoRecord undo = null;
            bool? screenUpdating = null;
            try
            {
                try
                {
                    undo = _application.UndoRecord;
                    if (undo != null && !undo.IsRecordingCustomRecord)
                    {
                        undo.StartCustomRecord("AI 批量排版");
                        result.UndoGrouped = true;
                    }
                    else
                    {
                        undo = null;
                    }
                }
                catch
                {
                    undo = null;
                }

                try
                {
                    screenUpdating = _application.ScreenUpdating;
                    _application.ScreenUpdating = false;
                }
                catch
                {
                    screenUpdating = null;
                }

                if (request.UpdateStyleDefinitions)
                {
                    foreach (var pair in styles)
                    {
                        var role = request.Roles[pair.Key];
                        ApplyRoleFont(pair.Value.Font, role);
                        ApplyRoleParagraphFormat(pair.Value.ParagraphFormat, role);
                        result.UpdatedStyles.Add(SafeStyleName(pair.Value));
                    }
                }

                foreach (var item in plan.Items)
                {
                    var role = request.Roles[item.Role];
                    try
                    {
                        var range = paragraphs[item.Index - 1].Range;
                        if (request.ClearDirectFormatting)
                        {
                            range.ParagraphFormat.Reset();
                            GetCharacterFormatRange(range).Font.Reset();
                        }

                        WordInterop.Style style;
                        var hasStyle = styles.TryGetValue(item.Role, out style);
                        if (hasStyle)
                        {
                            object styleObject = style;
                            range.set_Style(ref styleObject);
                        }

                        if (!hasStyle || !request.UpdateStyleDefinitions)
                        {
                            ApplyRoleParagraphFormat(range.ParagraphFormat, role);
                            ApplyRoleFont(GetCharacterFormatRange(range).Font, role);
                        }

                        result.AppliedCount++;
                        int count;
                        result.RoleCounts[item.Role] = result.RoleCounts.TryGetValue(item.Role, out count) ? count + 1 : 1;
                    }
                    catch (Exception ex)
                    {
                        result.Skipped.Add(new WordParagraphFormatSkip { Index = item.Index, Reason = "error: " + ex.Message });
                    }
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
            finally
            {
                if (screenUpdating.HasValue)
                {
                    try { _application.ScreenUpdating = screenUpdating.Value; } catch { }
                }

                if (undo != null)
                {
                    try { undo.EndCustomRecord(); } catch { }
                }
            }
        }

        /// <summary>Built-in style aliases become WdBuiltinStyle so they work in every Word UI language.</summary>
        private static object ResolveStyleKey(string styleName)
        {
            int builtinId;
            if (WordParagraphFormatPlanner.TryResolveBuiltinStyle(styleName, out builtinId))
            {
                return (WordInterop.WdBuiltinStyle)builtinId;
            }

            return styleName;
        }

        private static string SafeStyleName(WordInterop.Style style)
        {
            try
            {
                return style.NameLocal;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void ApplyRoleFont(WordInterop.Font font, WordParagraphRoleFormat role)
        {
            if (font == null || role == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(role.FontName))
            {
                font.Name = role.FontName;
                font.NameFarEast = role.FontName;
            }

            if (!string.IsNullOrWhiteSpace(role.LatinFontName))
            {
                font.NameAscii = role.LatinFontName;
                font.NameOther = role.LatinFontName;
            }

            if (role.Size.HasValue) font.Size = (float)role.Size.Value;
            if (role.Bold.HasValue) font.Bold = role.Bold.Value ? 1 : 0;
            if (role.Italic.HasValue) font.Italic = role.Italic.Value ? 1 : 0;
            if (!string.IsNullOrWhiteSpace(role.Color)) font.Color = ParseColor(role.Color);
        }

        private static void ApplyRoleParagraphFormat(WordInterop.ParagraphFormat pf, WordParagraphRoleFormat role)
        {
            if (pf == null || role == null)
            {
                return;
            }

            ApplyParagraphFormat(pf, new WordParagraphFormatRequest
            {
                Alignment = role.Alignment,
                LineSpacingRule = role.LineSpacingRule,
                LineSpacing = role.LineSpacing,
                SpaceBefore = role.SpaceBefore,
                SpaceAfter = role.SpaceAfter,
                FirstLineIndent = role.FirstLineIndentChars.HasValue ? null : role.FirstLineIndent,
                LeftIndent = role.LeftIndent,
                RightIndent = role.RightIndent
            });

            if (role.FirstLineIndentChars.HasValue)
            {
                if (role.FirstLineIndentChars.Value == 0)
                {
                    pf.FirstLineIndent = 0;
                }

                pf.CharacterUnitFirstLineIndent = (float)role.FirstLineIndentChars.Value;
            }
        }

        private static void ClearDirectCharacterFormatting(WordInterop.Selection selection)
        {
            if (selection == null)
            {
                return;
            }

            try
            {
                selection.Font.Reset();
            }
            catch
            {
            }

            try
            {
                selection.Range.HighlightColorIndex = WordInterop.WdColorIndex.wdNoHighlight;
            }
            catch
            {
            }
        }

        private static WordInterop.Range GetCharacterFormatRange(WordInterop.Range range)
        {
            if (range == null)
            {
                return null;
            }

            var formatRange = range.Duplicate;
            var text = formatRange.Text ?? string.Empty;

            int trim = 0;
            while (trim < text.Length)
            {
                char last = text[text.Length - 1 - trim];
                if (last == '\r' || last == '\a' || last == '\v' || last == '\f')
                {
                    trim++;
                }
                else
                {
                    break;
                }
            }

            if (trim > 0)
            {
                int newEnd = formatRange.End - trim;
                if (newEnd >= formatRange.Start)
                {
                    formatRange.End = newEnd;
                }
            }

            return formatRange;
        }

        private static bool IsInsideTable(WordInterop.Selection selection)
        {
            if (selection == null)
            {
                return false;
            }

            try
            {
                object info = selection.Information[WordInterop.WdInformation.wdWithInTable];
                return info != null && Convert.ToBoolean(info);
            }
            catch
            {
                return false;
            }
        }

        private static string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length > maxLength ? text.Substring(0, maxLength) + "..." : text;
        }

        private static WordInterop.WdHeaderFooterIndex ParseHeaderFooterTarget(string target)
        {
            var t = (target ?? string.Empty).Trim().ToLowerInvariant();
            switch (t)
            {
                case "first": return WordInterop.WdHeaderFooterIndex.wdHeaderFooterFirstPage;
                case "even": return WordInterop.WdHeaderFooterIndex.wdHeaderFooterEvenPages;
                default: return WordInterop.WdHeaderFooterIndex.wdHeaderFooterPrimary;
            }
        }

        private static WordInterop.WdFieldType? ParseFieldType(string fieldType)
        {
            var t = (fieldType ?? string.Empty).Trim().ToLowerInvariant();
            switch (t)
            {
                case "page": return WordInterop.WdFieldType.wdFieldPage;
                case "numpages": return WordInterop.WdFieldType.wdFieldNumPages;
                case "date": return WordInterop.WdFieldType.wdFieldDate;
                case "time": return WordInterop.WdFieldType.wdFieldTime;
                case "author": return WordInterop.WdFieldType.wdFieldAuthor;
                case "title": return WordInterop.WdFieldType.wdFieldTitle;
                case "subject": return WordInterop.WdFieldType.wdFieldSubject;
                case "filename": return WordInterop.WdFieldType.wdFieldFileName;
                case "numwords": return WordInterop.WdFieldType.wdFieldNumWords;
                case "numchars": return WordInterop.WdFieldType.wdFieldNumChars;
                case "edittime": return WordInterop.WdFieldType.wdFieldEditTime;
                case "lastsavedby": return WordInterop.WdFieldType.wdFieldLastSavedBy;
                case "revnum": return WordInterop.WdFieldType.wdFieldRevisionNum;
                case "template": return WordInterop.WdFieldType.wdFieldTemplate;
                default: return null;
            }
        }

        private static WordInterop.WdContentControlType? ParseContentControlType(string type)
        {
            var t = (type ?? string.Empty).Trim().ToLowerInvariant();
            switch (t)
            {
                case "text": return WordInterop.WdContentControlType.wdContentControlText;
                case "richtext": return WordInterop.WdContentControlType.wdContentControlRichText;
                case "checkbox": return WordInterop.WdContentControlType.wdContentControlCheckBox;
                case "dropdown": return WordInterop.WdContentControlType.wdContentControlDropdownList;
                case "combo": return WordInterop.WdContentControlType.wdContentControlComboBox;
                case "date": return WordInterop.WdContentControlType.wdContentControlDate;
                default: return null;
            }
        }

        private static void ApplyFont(WordInterop.Font font, WordFormatSelectionRequest request)
        {
            if (font == null)
            {
                return;
            }

            if (request.Bold.HasValue) font.Bold = request.Bold.Value ? 1 : 0;
            if (request.Italic.HasValue) font.Italic = request.Italic.Value ? 1 : 0;
            if (request.Underline.HasValue)
            {
                font.Underline = request.Underline.Value
                    ? WordInterop.WdUnderline.wdUnderlineSingle
                    : WordInterop.WdUnderline.wdUnderlineNone;
            }

            if (!string.IsNullOrWhiteSpace(request.FontName)) font.Name = request.FontName;
            if (request.Size.HasValue) font.Size = (float)request.Size.Value;
            if (!string.IsNullOrWhiteSpace(request.Color)) font.Color = ParseColor(request.Color);
            if (request.Subscript.HasValue && request.Subscript.Value) font.Subscript = 1;
            if (request.Superscript.HasValue && request.Superscript.Value) font.Superscript = 1;
        }

        private static void ApplyHighlight(WordInterop.Range range, string highlightColor)
        {
            if (range == null || string.IsNullOrWhiteSpace(highlightColor))
            {
                return;
            }

            var index = ParseHighlightColor(highlightColor);
            if (index.HasValue)
            {
                range.HighlightColorIndex = index.Value;
            }
        }

        private static WordInterop.WdColorIndex? ParseHighlightColor(string color)
        {
            var c = color.Trim();
            if (c.Equals("none", StringComparison.OrdinalIgnoreCase)
                || c.Equals("noHighlight", StringComparison.OrdinalIgnoreCase)
                || c.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                return WordInterop.WdColorIndex.wdNoHighlight;
            }

            try
            {
                return (WordInterop.WdColorIndex)Enum.Parse(typeof(WordInterop.WdColorIndex), "wd" + c, true);
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyParagraphFormat(WordInterop.ParagraphFormat pf, WordParagraphFormatRequest request)
        {
            if (pf == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(request.Alignment))
            {
                switch (request.Alignment.ToLowerInvariant())
                {
                    case "left": pf.Alignment = WordInterop.WdParagraphAlignment.wdAlignParagraphLeft; break;
                    case "center": pf.Alignment = WordInterop.WdParagraphAlignment.wdAlignParagraphCenter; break;
                    case "right": pf.Alignment = WordInterop.WdParagraphAlignment.wdAlignParagraphRight; break;
                    case "justify": pf.Alignment = WordInterop.WdParagraphAlignment.wdAlignParagraphJustify; break;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.LineSpacingRule))
            {
                switch (request.LineSpacingRule.ToLowerInvariant())
                {
                    case "single": pf.LineSpacingRule = WordInterop.WdLineSpacing.wdLineSpaceSingle; break;
                    case "1.5":
                    case "oneandhalf": pf.LineSpacingRule = WordInterop.WdLineSpacing.wdLineSpace1pt5; break;
                    case "double": pf.LineSpacingRule = WordInterop.WdLineSpacing.wdLineSpaceDouble; break;
                    case "atleast": pf.LineSpacingRule = WordInterop.WdLineSpacing.wdLineSpaceAtLeast; break;
                    case "exactly": pf.LineSpacingRule = WordInterop.WdLineSpacing.wdLineSpaceExactly; break;
                    case "multiple": pf.LineSpacingRule = WordInterop.WdLineSpacing.wdLineSpaceMultiple; break;
                }
            }

            if (request.LineSpacing.HasValue) pf.LineSpacing = (float)request.LineSpacing.Value;
            if (request.SpaceBefore.HasValue) pf.SpaceBefore = (float)request.SpaceBefore.Value;
            if (request.SpaceAfter.HasValue) pf.SpaceAfter = (float)request.SpaceAfter.Value;
            if (request.FirstLineIndent.HasValue) pf.FirstLineIndent = (float)request.FirstLineIndent.Value;
            if (request.LeftIndent.HasValue) pf.LeftIndent = (float)request.LeftIndent.Value;
            if (request.RightIndent.HasValue) pf.RightIndent = (float)request.RightIndent.Value;
        }

        private static WordInterop.WdColor ParseColor(string color)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return WordInterop.WdColor.wdColorAutomatic;
            }

            var trimmed = color.Trim();
            if (trimmed.StartsWith("#"))
            {
                trimmed = trimmed.Substring(1);
            }

            if (trimmed.Length == 6)
            {
                try
                {
                    int r = Convert.ToInt32(trimmed.Substring(0, 2), 16);
                    int g = Convert.ToInt32(trimmed.Substring(2, 2), 16);
                    int b = Convert.ToInt32(trimmed.Substring(4, 2), 16);
                    // Word stores colors in BGR order.
                    return (WordInterop.WdColor)(r | (g << 8) | (b << 16));
                }
                catch
                {
                    return WordInterop.WdColor.wdColorAutomatic;
                }
            }

            try
            {
                return (WordInterop.WdColor)Enum.Parse(typeof(WordInterop.WdColor), "wdColor" + trimmed, true);
            }
            catch
            {
                return WordInterop.WdColor.wdColorAutomatic;
            }
        }

        private static int CountMatches(WordInterop.Document doc, string findText, bool matchCase)
        {
            var range = doc.Content;
            var find = range.Find;
            find.ClearFormatting();
            find.Text = findText;
            find.MatchCase = matchCase;
            find.Forward = true;
            find.Wrap = WordInterop.WdFindWrap.wdFindStop;

            var count = 0;
            find.Execute();
            while (find.Found)
            {
                count++;
                range.Collapse(WordInterop.WdCollapseDirection.wdCollapseEnd);
                find.Execute();
            }

            return count;
        }

        public DocumentBackupResult CreateBackup(DocumentBackupRequest request)
        {
            request = request ?? new DocumentBackupRequest();

            try
            {
                if (_application == null)
                {
                    return CreateFailure(request, "Word application is not connected.");
                }

                var document = _application.ActiveDocument;
                if (document == null)
                {
                    return CreateFailure(request, "No active Word document.");
                }

                var documentName = document.Name;
                var originalPath = document.FullName;
                var documentPath = document.Path;

                // 未保存到磁盘的文档：FullName 可能只是“Document1”之类的名字，
                // 而 Path 为空。此时跳过文件级备份，直接放行写操作——内存中的文档
                // 仍可被 COM 正常修改。
                if (string.IsNullOrWhiteSpace(documentPath))
                {
                    return CreateBackupSkipped(request, documentName);
                }

                // 已保存且源文件在本地磁盘：刷盘后直接复制。
                if (!string.IsNullOrWhiteSpace(originalPath) && File.Exists(originalPath))
                {
                    if (!document.Saved)
                    {
                        document.Save();
                    }

                    var backupPath = BuildBackupPath(HostApplicationName, originalPath, documentName, request, ".docx");
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath) ?? string.Empty);
                    File.Copy(originalPath, backupPath, true);

                    return CreateBackupSuccess(request, documentName, originalPath, backupPath);
                }

                // 已保存但源文件不在本地磁盘（如 OneDrive/SharePoint 云占位文件、
                // 或源文件被移动/删除）。尝试用 SaveCopyAs 生成一份本地副本；
                // 若仍失败（如受保护文档），则跳过备份，不阻塞写操作。
                try
                {
                    var fallbackBackupPath = BuildBackupPath(
                        HostApplicationName,
                        string.IsNullOrWhiteSpace(originalPath) ? documentName : originalPath,
                        documentName,
                        request,
                        ".docx");

                    var backupDirectory = Path.GetDirectoryName(fallbackBackupPath);
                    if (!string.IsNullOrWhiteSpace(backupDirectory))
                    {
                        Directory.CreateDirectory(backupDirectory);
                    }

                    object backupFileName = fallbackBackupPath;
                    document.SaveCopyAs(ref backupFileName);

                    return CreateBackupSuccess(
                        request,
                        documentName,
                        string.IsNullOrWhiteSpace(originalPath) ? string.Empty : originalPath,
                        fallbackBackupPath);
                }
                catch
                {
                    return CreateBackupSkipped(request, documentName);
                }
            }
            catch (Exception ex)
            {
                return CreateFailure(request, ex.Message);
            }
        }

        private static DocumentBackupResult CreateFailure(
            DocumentBackupRequest request,
            string errorMessage,
            string documentName = null,
            string originalPath = null)
        {
            return new DocumentBackupResult
            {
                Success = false,
                HostApplicationName = "Word",
                DocumentName = documentName ?? string.Empty,
                OriginalPath = originalPath ?? string.Empty,
                ErrorMessage = errorMessage ?? string.Empty,
                SessionId = request == null ? string.Empty : request.SessionId ?? string.Empty,
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                ToolName = request == null ? string.Empty : request.ToolName ?? string.Empty,
                RiskLevel = request == null ? string.Empty : request.RiskLevel ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        private static DocumentBackupResult CreateBackupSuccess(
            DocumentBackupRequest request,
            string documentName,
            string originalPath,
            string backupPath)
        {
            return new DocumentBackupResult
            {
                Success = true,
                HostApplicationName = "Word",
                DocumentName = documentName ?? string.Empty,
                OriginalPath = originalPath ?? string.Empty,
                BackupPath = backupPath ?? string.Empty,
                SessionId = request == null ? string.Empty : request.SessionId ?? string.Empty,
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                ToolName = request == null ? string.Empty : request.ToolName ?? string.Empty,
                RiskLevel = request == null ? string.Empty : request.RiskLevel ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        private static DocumentBackupResult CreateBackupSkipped(
            DocumentBackupRequest request,
            string documentName)
        {
            return new DocumentBackupResult
            {
                Success = true,
                HostApplicationName = "Word",
                DocumentName = documentName ?? string.Empty,
                OriginalPath = string.Empty,
                BackupPath = string.Empty,
                SessionId = request == null ? string.Empty : request.SessionId ?? string.Empty,
                ConversationId = request == null ? string.Empty : request.ConversationId ?? string.Empty,
                MessageId = request == null ? string.Empty : request.MessageId ?? string.Empty,
                ToolName = request == null ? string.Empty : request.ToolName ?? string.Empty,
                RiskLevel = request == null ? string.Empty : request.RiskLevel ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow
            };
        }

        private static string BuildBackupPath(
            string hostApplicationName,
            string originalPath,
            string documentName,
            DocumentBackupRequest request,
            string fallbackExtension)
        {
            var backupsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "Backups",
                string.IsNullOrWhiteSpace(hostApplicationName) ? "Unknown" : hostApplicationName);

            var docBase = SanitizeFileName(Path.GetFileNameWithoutExtension(
                string.IsNullOrWhiteSpace(documentName) ? "document" : documentName));
            if (docBase.Length > 16)
                docBase = docBase.Substring(0, 16);

            var pathHash4 = ComputeShortHash(string.IsNullOrWhiteSpace(originalPath) ? documentName : originalPath, 4);

            var folder = Path.Combine(backupsRoot, docBase + "_" + pathHash4);

            var extension = Path.GetExtension(originalPath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = fallbackExtension;

            var fileName = DateTime.Now.ToString("HHmmss") + extension;

            return Path.Combine(folder, fileName);
        }

        private static string ComputeShortHash(string value, int length)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(length);
                for (var i = 0; i < length && i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static string SanitizeFileName(string value)
        {
            var text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "session";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var current = text[i];
                var isInvalid = false;
                for (var j = 0; j < invalidChars.Length; j++)
                {
                    if (invalidChars[j] == current)
                    {
                        isInvalid = true;
                        break;
                    }
                }

                builder.Append(isInvalid ? '_' : current);
            }

            return builder.ToString().Trim('_');
        }
    }
}
