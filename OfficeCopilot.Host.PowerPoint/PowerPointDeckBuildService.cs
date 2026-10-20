using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;
using OfficeCopilot.Slides.Rendering;

namespace OfficeCopilot.Host.PowerPoint
{
    public interface IPowerPointDeckBuildHost
    {
        IList<int> AppendSlides(IList<PowerPointDeckBuildSlide> slides);

        PptVerificationReport VerifySlides(IList<int> slideIndexes, IList<PowerPointDeckBuildSlide> slides);
    }

    /// <summary>支持替换现有页 / 新建演示文稿的构建宿主（蓝图 P4）。</summary>
    public interface IPowerPointDeckTargetHost
    {
        IList<int> PlaceSlides(IList<PowerPointDeckBuildSlide> slides, PowerPointDeckBuildTarget target);
    }

    public sealed class PowerPointDeckBuildTarget
    {
        public const string AppendKind = "append";
        public const string ReplaceKind = "replace";
        public const string NewKind = "new";

        /// <summary>当前文稿是空白文稿：删掉空白页后直接在当前文稿中构建。</summary>
        public const string ReuseBlankKind = "reuse_blank";

        public string Kind { get; set; } = AppendKind;

        public int ReplaceFrom { get; set; }

        public int ReplaceTo { get; set; }

        public bool IsReplace => string.Equals(Kind, ReplaceKind, StringComparison.OrdinalIgnoreCase);

        public bool IsNew => string.Equals(Kind, NewKind, StringComparison.OrdinalIgnoreCase);

        public bool IsReuseBlank => string.Equals(Kind, ReuseBlankKind, StringComparison.OrdinalIgnoreCase);

        public static PowerPointDeckBuildTarget Append() => new PowerPointDeckBuildTarget();
    }

    public interface IPowerPointDeckBuildService
    {
        Task<PowerPointDeckBuildResult> BuildAsync(DeckWorkspace workspace, string deckUrl, string mode, CancellationToken cancellationToken, PowerPointDeckBuildTarget target = null);
    }

    public sealed class PowerPointDeckBuildSlide
    {
        public SlideBuildPlan Plan { get; set; }

        public string CaptureImagePath { get; set; }

        public IList<string> ImagePaths { get; set; } = new List<string>();

        /// <summary>工作区身份，写入 NIXER.DECK / NIXER.SLIDE / NIXER.HASH 标签（蓝图 §5.1）。</summary>
        public string DeckId { get; set; }

        public string SlideKey { get; set; }

        public string Hash { get; set; }
    }

    public sealed class PowerPointDeckBuildResult
    {
        public IList<int> SlideIndexes { get; set; } = new List<int>();

        public IList<string> CaptureFiles { get; set; } = new List<string>();

        public IList<PptVerificationIssue> BuildIssues { get; set; } = new List<PptVerificationIssue>();
    }

    public sealed class PowerPointDeckBuildService : IPowerPointDeckBuildService
    {
        private readonly IPowerPointDeckBuildHost _host;
        private readonly IDeckRenderService _renderer;
        private readonly DeckUrlResolver _resolver;
        private readonly SlideAssetPaths _paths;

        public int RepairIterationCap { get; set; } = 3;

        /// <summary>为空时不安装字体。生产环境由组合根注入 <see cref="PowerPointFontInstaller"/>。</summary>
        public IPowerPointFontInstaller FontInstaller { get; set; }

        public PowerPointDeckBuildService(IPowerPointDeckBuildHost host, IDeckRenderService renderer, SlideAssetPaths paths, string decksRoot)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            _resolver = new DeckUrlResolver(paths.BuiltInRoot, paths.UserRoot, decksRoot);
            _paths = paths;
        }

        public async Task<PowerPointDeckBuildResult> BuildAsync(DeckWorkspace workspace, string deckUrl, string mode, CancellationToken cancellationToken, PowerPointDeckBuildTarget target = null)
        {
            target = target ?? PowerPointDeckBuildTarget.Append();
            var targetHost = _host as IPowerPointDeckTargetHost;
            var placed = target.IsReplace || target.IsNew || target.IsReuseBlank;
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            if (workspace.Slides.Count == 0) throw new DeckException("ppt.deck_empty", "工作区还没有页面。");
            if (!SlideBuildModes.IsValid(mode))
            {
                throw new DeckException("ppt.deck_invalid_argument", "mode 必须是 fidelity、editable 或 image。");
            }

            var imageMode = SlideBuildModes.IsImage(mode);
            var captureMode = imageMode ? SlideBuildModes.Fidelity : mode;

            if (target.IsReplace && workspace.Slides.Count != target.ReplaceTo - target.ReplaceFrom + 1)
            {
                throw new DeckException(
                    "ppt.deck_replace_count_mismatch",
                    "工作区包含 " + workspace.Slides.Count + " 页，但替换范围包含 "
                    + (target.ReplaceTo - target.ReplaceFrom + 1) + " 页；为避免重复页面，构建页数必须与替换页数一致。");
            }

            if (placed && targetHost == null)
            {
                throw new DeckException("ppt.deck_invalid_argument", "当前宿主不支持 target=" + target.Kind + "。");
            }

            var captureDirectory = Path.Combine(workspace.BuildDir, "capture");
            Directory.CreateDirectory(captureDirectory);
            var fonts = FontCatalog.Load(_paths);
            var inputs = new List<PowerPointDeckBuildSlide>(workspace.Slides.Count);
            var captureFiles = new List<string>(workspace.Slides.Count * 2);
            for (var index = 1; index <= workspace.Slides.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var capture = await _renderer.CaptureSlideAsync(deckUrl, index, captureMode, cancellationToken).ConfigureAwait(false);
                var slidePath = Path.Combine(captureDirectory, "s" + index.ToString("D2") + ".png");
                var backgroundPath = Path.Combine(captureDirectory, "bg" + index.ToString("D2") + ".png");
                File.WriteAllBytes(slidePath, capture.SlidePng);
                captureFiles.Add(slidePath);
                if (!imageMode)
                {
                    File.WriteAllBytes(backgroundPath, capture.BackgroundPng);
                    captureFiles.Add(backgroundPath);
                }

                capture.Data.BackgroundImagePath = imageMode ? slidePath : backgroundPath;
                var plan = BuildPlanner.Create(capture.Data, mode, fonts);
                plan.RepairIterationCap = Math.Max(0, Math.Min(10, RepairIterationCap));
                var imagePaths = plan.Images.Select((image, imageIndex) => ResolveImagePath(image.Source, captureDirectory, index, imageIndex)).ToList();
                var info = workspace.Slides[index - 1];
                inputs.Add(new PowerPointDeckBuildSlide
                {
                    Plan = plan,
                    CaptureImagePath = slidePath,
                    ImagePaths = imagePaths,
                    DeckId = workspace.Id,
                    SlideKey = info.Id,
                    Hash = info.Hash
                });
            }

            var fontIssues = InstallMissingFonts(inputs.Select(input => input.Plan));
            var unresolvedFontNames = PowerPointFontNameResolver.NormalizePlans(
                inputs.Select(input => input.Plan),
                InstalledFontState.RegisteredFontNames());
            if (unresolvedFontNames.Count > 0)
            {
                throw new DeckException(
                    "ppt.font_name_unresolved",
                    "以下字体名未能在 Windows 注册字体字典中匹配；构建已阻止，避免 PowerPoint 静默替代字体："
                    + string.Join("、", unresolvedFontNames));
            }

            IList<string> fontIdentityMismatches;
            try
            {
                fontIdentityMismatches = PowerPointFontIdentityResolver.FindMismatches(
                    fonts,
                    inputs.Select(input => input.Plan));
            }
            catch (IOException exception)
            {
                throw new DeckException(
                    "ppt.font_identity_unverified",
                    "无法读取注册字体文件以确认它们与随包字体一致；构建已阻止：" + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new DeckException(
                    "ppt.font_identity_unverified",
                    "没有权限读取注册字体文件以确认它们与随包字体一致；构建已阻止：" + exception.Message);
            }

            if (fontIdentityMismatches.Count > 0)
            {
                throw new DeckException(
                    "ppt.font_identity_mismatch",
                    "以下随包字体字面未能全部在 Windows 已注册字体文件中确认；构建已阻止，避免 HTML 与 PowerPoint 使用不同字形："
                    + string.Join("、", fontIdentityMismatches));
            }

            var slideIndexes = targetHost != null && placed
                ? targetHost.PlaceSlides(inputs, target)
                : _host.AppendSlides(inputs);

            return new PowerPointDeckBuildResult
            {
                SlideIndexes = slideIndexes,
                CaptureFiles = captureFiles,
                BuildIssues = fontIssues
            };
        }

        private IList<PptVerificationIssue> InstallMissingFonts(IEnumerable<SlideBuildPlan> plans)
        {
            if (FontInstaller == null)
            {
                return new List<PptVerificationIssue>();
            }

            try
            {
                return FontInstaller.EnsureFonts(plans, PptVerifier.GetInstalledFonts());
            }
            catch (Exception ex)
            {
                return new List<PptVerificationIssue>
                {
                    new PptVerificationIssue
                    {
                        Code = "ppt.font_install_failed",
                        Message = "字体安装步骤异常，PowerPoint 将以替代字体显示：" + ex.Message
                    }
                };
            }
        }

        private string ResolveImagePath(string source, string captureDirectory, int slide, int imageIndex)
        {
            if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("采集到的图片没有来源。");
            if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = source.IndexOf(',');
                if (comma < 0 || source.IndexOf(";base64", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidOperationException("仅支持 base64 编码的内嵌图片。");
                }

                var mime = source.Substring(11, source.IndexOf(';') - 11).ToLowerInvariant();
                var extension = mime == "png" ? ".png" : mime == "jpeg" || mime == "jpg" ? ".jpg" : mime == "gif" ? ".gif" : null;
                if (extension == null) throw new InvalidOperationException("PowerPoint 原生构建暂不支持内嵌图片格式：" + mime);
                var path = Path.Combine(captureDirectory, "image-s" + slide.ToString("D2") + "-" + imageIndex.ToString("D2") + extension);
                File.WriteAllBytes(path, Convert.FromBase64String(source.Substring(comma + 1)));
                return path;
            }

            if (!_resolver.TryResolve(source, out var resolved))
            {
                throw new InvalidOperationException("图片不在本地工作区或素材目录中：" + source);
            }
            if (!File.Exists(resolved)) throw new FileNotFoundException("图片素材文件不存在。", resolved);
            return resolved;
        }
    }
}
