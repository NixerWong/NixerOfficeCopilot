using OfficeCopilot.Host.Abstractions;
using System;
using System.Collections.Generic;
using PowerPointApp = Microsoft.Office.Interop.PowerPoint.Application;
using PowerPointInterop = Microsoft.Office.Interop.PowerPoint;

namespace OfficeCopilot.Host.PowerPoint
{
    public sealed partial class PowerPointHostAdapter
        : ICopilotHostAdapter, IDocumentBackupHostAdapter, IDocumentRestoreHostAdapter, IPowerPointToolHost, IPowerPointDeckBuildHost, IPowerPointDeckTargetHost
    {
        private const int SelectionPreviewLimit = 500;
        private const int SelectionTextLimit = 2000;

        private readonly PowerPointApp _application;
        private readonly PowerPointComDispatcher _dispatcher;
        private readonly PowerPointBackupService _backupService;

        public PowerPointHostAdapter()
            : this(null)
        {
        }

        /// <summary>须在 PowerPoint UI 线程上构造：调度器会捕获当前的 SynchronizationContext。</summary>
        public PowerPointHostAdapter(object application)
            : this(application, null)
        {
        }

        public PowerPointHostAdapter(object application, PowerPointBackupStore backupStore)
        {
            _application = application as PowerPointApp;
            _dispatcher = new PowerPointComDispatcher();
            _backupService = new PowerPointBackupService(_application, backupStore ?? new PowerPointBackupStore());
        }

        public string HostApplicationName
        {
            get { return "PowerPoint"; }
        }

        public HostContextSnapshot GetContextSnapshot()
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = TryGetActivePresentation();
                if (presentation == null)
                {
                    return new HostContextSnapshot
                    {
                        HostApplicationName = HostApplicationName,
                        DocumentName = "No presentation",
                        Summary = _application == null
                            ? "PowerPoint application is not connected."
                            : "No active PowerPoint presentation."
                    };
                }

                var documentName = SafeString(() => presentation.Name);
                if (string.IsNullOrWhiteSpace(documentName))
                {
                    documentName = "Untitled";
                }

                var currentSlide = GetCurrentSlideIndex();
                var slideCount = SafeInt(() => presentation.Slides.Count);
                var summary = "Active presentation: " + documentName
                    + (currentSlide > 0 ? " (Slide " + currentSlide + " of " + slideCount + ")" : string.Empty);

                return new HostContextSnapshot
                {
                    HostApplicationName = HostApplicationName,
                    DocumentName = documentName,
                    SelectionPreview = Truncate(GetSelectedText(), SelectionPreviewLimit),
                    Summary = summary
                };
            });
        }

        public PowerPointPresentationInfo GetActivePresentationInfo()
        {
            return _dispatcher.Invoke(() =>
            {
                var presentation = TryGetActivePresentation();
                if (presentation == null)
                {
                    return new PowerPointPresentationInfo { IsAvailable = false };
                }

                var savedPath = PowerPointBackupService.GetSavedPath(presentation);
                return new PowerPointPresentationInfo
                {
                    Name = SafeString(() => presentation.Name),
                    FullName = savedPath,
                    IsAvailable = true,
                    IsReadOnly = SafeBool(() => ComLate.GetTri(presentation, "ReadOnly")),
                    IsSaved = savedPath.Length > 0,
                    SlideCount = SafeInt(() => presentation.Slides.Count),
                    CurrentSlideIndex = GetCurrentSlideIndex(),
                    SlideWidth = SafeFloat(() => presentation.PageSetup.SlideWidth),
                    SlideHeight = SafeFloat(() => presentation.PageSetup.SlideHeight),
                    IsBlank = SafeBool(() => IsBlankPresentation(presentation)),
                    DeckId = ComSlideBuilder.GetTag(presentation.Tags, ComSlideBuilder.DeckTag)
                };
            });
        }

        public PowerPointSelectionInfo GetSelectionInfo()
        {
            return _dispatcher.Invoke(() =>
            {
                var window = TryGetActiveWindow();
                if (window == null)
                {
                    return null;
                }

                PowerPointInterop.Selection selection;
                try
                {
                    selection = window.Selection;
                }
                catch (Exception)
                {
                    return null;
                }

                if (selection == null)
                {
                    return null;
                }

                var info = new PowerPointSelectionInfo
                {
                    Text = Truncate(GetSelectedText(), SelectionTextLimit),
                    SlideIndex = GetCurrentSlideIndex(),
                    ShapeName = string.Empty,
                    ShapeNames = new List<string>()
                };

                var type = PowerPointInterop.PpSelectionType.ppSelectionNone;
                try
                {
                    type = selection.Type;
                }
                catch (Exception)
                {
                }

                info.SelectionType = DescribeSelectionType(type);
                if (type == PowerPointInterop.PpSelectionType.ppSelectionShapes
                    || type == PowerPointInterop.PpSelectionType.ppSelectionText)
                {
                    try
                    {
                        var range = selection.ShapeRange;
                        for (var i = 1; i <= range.Count; i++)
                        {
                            info.ShapeNames.Add(range[i].Name ?? string.Empty);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (info.ShapeNames.Count > 0)
                {
                    info.ShapeName = info.ShapeNames[0];
                }

                return info;
            });
        }

        public void InsertText(string text)
        {
            if (_application == null)
            {
                return;
            }

            try
            {
                _dispatcher.Invoke(() =>
                {
                    var window = TryGetActiveWindow();
                    var textRange = window == null ? null : window.Selection.TextRange;
                    if (textRange != null)
                    {
                        textRange.Text = text ?? string.Empty;
                    }
                });
            }
            catch (Exception)
            {
                // 选区不是文本时 TextRange 会抛异常；插入失败不影响面板。
            }
        }

        public DocumentBackupResult CreateBackup(DocumentBackupRequest request)
        {
            if (_application == null)
            {
                return new DocumentBackupResult
                {
                    Success = false,
                    HostApplicationName = HostApplicationName,
                    ErrorMessage = "PowerPoint application is not connected."
                };
            }

            return _dispatcher.Invoke(() => _backupService.CreateBackup(request));
        }

        public DocumentBackupListResult ListBackups(int maxCount)
        {
            return _dispatcher.Invoke(() => _backupService.ListBackups(maxCount));
        }

        public DocumentRestoreResult RestoreBackup(DocumentRestoreRequest request)
        {
            return _dispatcher.Invoke(() => _backupService.RestoreBackup(request));
        }

        private PowerPointInterop.Presentation TryGetActivePresentation()
        {
            if (_application == null)
            {
                return null;
            }

            try
            {
                return _application.Presentations.Count == 0 ? null : _application.ActivePresentation;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        private PowerPointInterop.Presentation GetRequiredPresentation()
        {
            var presentation = TryGetActivePresentation();
            if (presentation == null)
            {
                throw new InvalidOperationException("当前没有打开的演示文稿。");
            }

            return presentation;
        }

        private PowerPointInterop.DocumentWindow TryGetActiveWindow()
        {
            if (_application == null)
            {
                return null;
            }

            try
            {
                return _application.Windows.Count == 0 ? null : _application.ActiveWindow;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        private int GetCurrentSlideIndex()
        {
            try
            {
                var window = TryGetActiveWindow();
                var slide = window == null ? null : window.View.Slide as PowerPointInterop.Slide;
                return slide == null ? 0 : slide.SlideIndex;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private string GetSelectedText()
        {
            try
            {
                var window = TryGetActiveWindow();
                if (window == null || window.Selection.Type != PowerPointInterop.PpSelectionType.ppSelectionText)
                {
                    return string.Empty;
                }

                return window.Selection.TextRange.Text ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string DescribeSelectionType(PowerPointInterop.PpSelectionType type)
        {
            switch (type)
            {
                case PowerPointInterop.PpSelectionType.ppSelectionSlides:
                    return "slides";
                case PowerPointInterop.PpSelectionType.ppSelectionShapes:
                    return "shapes";
                case PowerPointInterop.PpSelectionType.ppSelectionText:
                    return "text";
                default:
                    return "none";
            }
        }

        internal static string Truncate(string value, int limit)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= limit)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, limit) + "...";
        }

        private static string SafeString(Func<string> getter)
        {
            try
            {
                return getter() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static int SafeInt(Func<int> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static float SafeFloat(Func<float> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception)
            {
                return 0f;
            }
        }

        private static bool SafeBool(Func<bool> getter)
        {
            try
            {
                return getter();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
