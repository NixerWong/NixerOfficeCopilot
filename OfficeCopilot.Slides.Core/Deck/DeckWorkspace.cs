using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using OfficeCopilot.Slides.Fonts;
using OfficeCopilot.Slides.Html;

namespace OfficeCopilot.Slides.Deck
{
    /// <summary>
    /// 一个 Deck 工作区（蓝图 §5.1）：manifest.json + slides/sNN.html + theme.css + build/ + previews/ + assets/。
    /// 页 id 稳定，页序由 manifest 决定。非线程安全，调用方按工具调用串行使用。
    /// </summary>
    public sealed class DeckWorkspace
    {
        public const string ManifestFileName = "manifest.json";
        public const string ThemeCssFileName = "theme.css";

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        internal DeckWorkspace(string directory, DeckManifest manifest)
        {
            Directory = directory;
            Manifest = manifest;
        }

        public string Id { get { return Manifest.Id; } }
        public string Directory { get; }
        public DeckManifest Manifest { get; }

        public string SlidesDir { get { return Path.Combine(Directory, "slides"); } }
        public string BuildDir { get { return Path.Combine(Directory, "build"); } }
        public string PreviewsDir { get { return Path.Combine(Directory, "previews"); } }
        public string AssetsDir { get { return Path.Combine(Directory, "assets"); } }

        public IReadOnlyList<DeckSlideInfo> Slides { get { return Manifest.Slides; } }

        internal static DeckWorkspace CreateNew(string directory, DeckManifest manifest)
        {
            var workspace = new DeckWorkspace(directory, manifest);
            System.IO.Directory.CreateDirectory(workspace.SlidesDir);
            System.IO.Directory.CreateDirectory(workspace.BuildDir);
            System.IO.Directory.CreateDirectory(workspace.PreviewsDir);
            System.IO.Directory.CreateDirectory(workspace.AssetsDir);
            workspace.Save();
            return workspace;
        }

        internal static DeckWorkspace Load(string directory)
        {
            var path = Path.Combine(directory, ManifestFileName);
            var manifest = JsonConvert.DeserializeObject<DeckManifest>(File.ReadAllText(path, Utf8)) ?? new DeckManifest();
            manifest.Slides = (manifest.Slides ?? new List<DeckSlideInfo>())
                .Where(s => s != null && DeckIds.IsValidSlideId(s.Id))
                .ToList();
            return new DeckWorkspace(directory, manifest);
        }

        public void Configure(string style, string density, string language, string title)
        {
            if (style != null) Manifest.Style = style.Trim();
            if (density != null) Manifest.Density = DeckDensities.Normalize(density);
            if (!string.IsNullOrWhiteSpace(language)) Manifest.Language = language.Trim();
            if (title != null) Manifest.Title = title.Trim();
            Save();
        }

        public int IndexOf(string slideId)
        {
            return Manifest.Slides.FindIndex(s => string.Equals(s.Id, slideId, StringComparison.Ordinal));
        }

        public string GetSlideHtml(string slideId)
        {
            RequireSlide(slideId);
            return File.ReadAllText(SlidePath(slideId), Utf8);
        }

        /// <summary>按页序返回全部页 HTML。</summary>
        public IList<string> GetAllSlideHtml()
        {
            return Manifest.Slides.Select(s => File.ReadAllText(SlidePath(s.Id), Utf8)).ToList();
        }

        /// <summary>写入若干页。先全部清洗校验，全部通过才落盘。</summary>
        public IList<DeckSlidePutResult> PutSlides(IList<DeckSlideInput> inputs, FontCatalog fonts = null)
        {
            if (inputs == null || inputs.Count == 0)
            {
                throw new DeckException("ppt.deck_invalid_argument", "slides 为空。");
            }

            var prepared = new List<KeyValuePair<DeckSlideInput, SanitizedSlide>>();
            var newCount = 0;
            for (var i = 0; i < inputs.Count; i++)
            {
                var input = inputs[i] ?? new DeckSlideInput();
                if (!string.IsNullOrWhiteSpace(input.Id))
                {
                    input.Id = input.Id.Trim().ToLowerInvariant();
                    if (IndexOf(input.Id) < 0)
                    {
                        throw new DeckException("ppt.deck_slide_not_found", "第 " + (i + 1) + " 项：工作区没有页 " + input.Id + "；新增页请省略 id。当前页：" + DescribeIds());
                    }
                }
                else
                {
                    newCount++;
                }

                SanitizedSlide sanitized;
                if (!string.IsNullOrWhiteSpace(input.Id) && IsLockedCover(input.Id))
                {
                    prepared.Add(new KeyValuePair<DeckSlideInput, SanitizedSlide>(input, null));
                    continue;
                }

                try
                {
                    sanitized = SlideHtmlSanitizer.SanitizeSlide(input.Html, fonts);
                }
                catch (ArgumentException ex)
                {
                    throw new DeckException("ppt.deck_invalid_html", "第 " + (i + 1) + " 项：" + ex.Message);
                }

                prepared.Add(new KeyValuePair<DeckSlideInput, SanitizedSlide>(input, sanitized));
            }

            if (Manifest.Slides.Count + newCount > DeckIds.MaxSlides)
            {
                throw new DeckException("ppt.deck_too_many_slides", "一个工作区最多 " + DeckIds.MaxSlides + " 页。");
            }

            System.IO.Directory.CreateDirectory(SlidesDir);
            var results = new List<DeckSlidePutResult>();
            foreach (var pair in prepared)
            {
                var input = pair.Key;
                var sanitized = pair.Value;
                var created = string.IsNullOrWhiteSpace(input.Id);
                if (!created && IsLockedCover(input.Id))
                {
                    var locked = Manifest.Slides[IndexOf(input.Id)];
                    results.Add(new DeckSlidePutResult
                    {
                        Id = locked.Id,
                        Hash = locked.Hash,
                        Title = locked.Title,
                        Locked = true,
                        Removed = new List<string> { "该页是用户选定的封面（候选 " + Manifest.Origin.Preview + "），已锁定，本次覆盖被忽略；封面字体/字号/配色校验问题仅供参考，不要修改。" }
                    });
                    continue;
                }

                DeckSlideInfo info;
                if (created)
                {
                    info = new DeckSlideInfo { Id = AllocateSlideId() };
                    var position = input.Position.HasValue
                        ? Math.Max(1, Math.Min(input.Position.Value, Manifest.Slides.Count + 1))
                        : Manifest.Slides.Count + 1;
                    Manifest.Slides.Insert(position - 1, info);
                }
                else
                {
                    info = Manifest.Slides[IndexOf(input.Id)];
                }

                info.Hash = ComputeHash(sanitized.Html);
                info.Title = sanitized.Title;
                info.Layout = sanitized.Layout;
                File.WriteAllText(SlidePath(info.Id), sanitized.Html, Utf8);

                results.Add(new DeckSlidePutResult
                {
                    Id = info.Id,
                    Created = created,
                    Hash = info.Hash,
                    Title = info.Title,
                    Removed = sanitized.Removed
                });
            }

            foreach (var r in results)
            {
                r.Position = IndexOf(r.Id) + 1;
            }

            Save();
            return results;
        }

        public void RemoveSlides(IEnumerable<string> slideIds)
        {
            var ids = (slideIds ?? Enumerable.Empty<string>()).Select(p => (p ?? string.Empty).Trim().ToLowerInvariant()).Distinct().ToList();
            if (ids.Count == 0)
            {
                throw new DeckException("ppt.deck_invalid_argument", "ids 为空。");
            }

            foreach (var id in ids)
            {
                RequireSlide(id);
            }

            foreach (var id in ids)
            {
                Manifest.Slides.RemoveAt(IndexOf(id));
                var path = SlidePath(id);
                if (File.Exists(path)) File.Delete(path);
            }

            Save();
        }

        /// <summary>按给定顺序重排；必须恰好列出全部页。</summary>
        public void Reorder(IList<string> order)
        {
            var ids = (order ?? new List<string>()).Select(p => (p ?? string.Empty).Trim().ToLowerInvariant()).ToList();
            var current = Manifest.Slides.Select(s => s.Id).ToList();
            if (ids.Count != current.Count || ids.Distinct().Count() != ids.Count || ids.Except(current).Any())
            {
                throw new DeckException("ppt.deck_invalid_order", "order 必须恰好列出全部 " + current.Count + " 页且不重复。当前页：" + DescribeIds());
            }

            Manifest.Slides = ids.Select(id => Manifest.Slides[IndexOf(id)]).ToList();
            Save();
        }

        public string ReadThemeCss()
        {
            var path = Path.Combine(Directory, ThemeCssFileName);
            return File.Exists(path) ? File.ReadAllText(path, Utf8) : string.Empty;
        }

        /// <summary>写入主题 CSS（经清洗），返回被删除的内容摘要。</summary>
        public IList<string> WriteThemeCss(string css, FontCatalog fonts = null)
        {
            var removed = new List<string>();
            var cleaned = CssSanitizer.Sanitize(css ?? string.Empty, removed, fonts);
            File.WriteAllText(Path.Combine(Directory, ThemeCssFileName), cleaned, Utf8);
            Save();
            return removed;
        }

        public DeckPreviewSource WritePreviewSource(string name, string style, string html, string css)
        {
            if (!DeckIds.IsValidPreviewName(name))
            {
                throw new DeckException("ppt.deck_invalid_argument", "候选名不合法：" + name);
            }

            if (string.IsNullOrWhiteSpace(style) || string.IsNullOrWhiteSpace(html))
            {
                throw new DeckException("ppt.deck_invalid_argument", "候选来源必须包含 style 和 html。");
            }

            var normalizedStyle = style.Trim();
            var sourceCss = css ?? string.Empty;
            var hash = ComputePreviewSourceHash(normalizedStyle, html, sourceCss);
            var generatedUtc = DateTime.UtcNow;
            File.WriteAllText(PreviewPath(name, ".slide.html"), html, Utf8);
            File.WriteAllText(PreviewPath(name, ".css"), sourceCss, Utf8);
            File.WriteAllText(
                PreviewPath(name, ".json"),
                JsonConvert.SerializeObject(new DeckPreviewSourceInfo
                {
                    Name = name,
                    Style = normalizedStyle,
                    Hash = hash,
                    GeneratedUtc = generatedUtc
                }, Formatting.Indented),
                Utf8);

            return new DeckPreviewSource
            {
                Name = name,
                Style = normalizedStyle,
                Hash = hash,
                GeneratedUtc = generatedUtc,
                Html = html,
                Css = sourceCss
            };
        }

        public DeckPreviewSource ReadPreviewSource(string name)
        {
            if (!DeckIds.IsValidPreviewName(name))
            {
                throw new DeckException("ppt.deck_invalid_argument", "候选名不合法：" + name);
            }

            var htmlPath = PreviewPath(name, ".slide.html");
            var cssPath = PreviewPath(name, ".css");
            var metadataPath = PreviewPath(name, ".json");
            if (!File.Exists(htmlPath) || !File.Exists(cssPath) || !File.Exists(metadataPath))
            {
                throw new DeckException("ppt.deck_preview_not_found", "工作区中找不到候选 " + name + " 的可复用来源，请重新运行 ppt_style_preview。");
            }

            DeckPreviewSourceInfo metadata;
            try
            {
                metadata = JsonConvert.DeserializeObject<DeckPreviewSourceInfo>(File.ReadAllText(metadataPath, Utf8));
            }
            catch (JsonException ex)
            {
                throw new DeckException("ppt.deck_preview_invalid", "候选 " + name + " 的来源元数据无法读取：" + ex.Message);
            }

            var html = File.ReadAllText(htmlPath, Utf8);
            var css = File.ReadAllText(cssPath, Utf8);
            if (metadata == null
                || !string.Equals(metadata.Name, name, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(metadata.Style)
                || !string.Equals(
                    metadata.Hash,
                    ComputePreviewSourceHash(metadata.Style, html, css),
                    StringComparison.Ordinal))
            {
                throw new DeckException("ppt.deck_preview_invalid", "候选 " + name + " 的来源文件与元数据不匹配，请重新运行 ppt_style_preview。");
            }

            return new DeckPreviewSource
            {
                Name = metadata.Name,
                Style = metadata.Style,
                Hash = metadata.Hash,
                GeneratedUtc = metadata.GeneratedUtc,
                Html = html,
                Css = css
            };
        }

        public bool IsLockedCover(string slideId)
        {
            var cover = Manifest.Origin == null ? null : Manifest.Origin.Slide;
            return !string.IsNullOrEmpty(cover)
                && string.Equals(cover, (slideId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public void RecordPreviewOrigin(DeckPreviewSource source, string coverSlideId = null)
        {
            if (source == null || !DeckIds.IsValidPreviewName(source.Name) || string.IsNullOrWhiteSpace(source.Hash))
            {
                throw new ArgumentException("有效的候选来源是必需的。", nameof(source));
            }

            Manifest.Origin = new DeckManifestOrigin
            {
                Preview = source.Name,
                Hash = source.Hash,
                Slide = string.IsNullOrWhiteSpace(coverSlideId) ? null : coverSlideId.Trim().ToLowerInvariant()
            };
            Save();
        }

        public string WriteBuildFile(string fileName, string content)
        {
            System.IO.Directory.CreateDirectory(BuildDir);
            var path = Path.Combine(BuildDir, fileName);
            File.WriteAllText(path, content, Utf8);
            return path;
        }

        public string PreviewPath(string name, string extension)
        {
            if (!DeckIds.IsValidPreviewName(name))
            {
                throw new DeckException("ppt.deck_invalid_argument", "候选名只能是小写字母开头的 a-z0-9-（≤24 字符）：" + name);
            }

            System.IO.Directory.CreateDirectory(PreviewsDir);
            return Path.Combine(PreviewsDir, name + extension);
        }

        public void Save()
        {
            Manifest.UpdatedUtc = DateTime.UtcNow;
            var json = JsonConvert.SerializeObject(Manifest, Formatting.Indented);
            var path = Path.Combine(Directory, ManifestFileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, json, Utf8);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        public static string ComputeHash(string html)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Utf8.GetBytes(html ?? string.Empty));
                var sb = new StringBuilder();
                for (var i = 0; i < 6; i++) sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string ComputePreviewSourceHash(string style, string html, string css)
        {
            var payload = (style ?? string.Empty) + "\0" + (html ?? string.Empty) + "\0" + (css ?? string.Empty);
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Utf8.GetBytes(payload));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var value in bytes)
                {
                    sb.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        private string SlidePath(string slideId)
        {
            return Path.Combine(SlidesDir, slideId + ".html");
        }

        private void RequireSlide(string slideId)
        {
            if (!DeckIds.IsValidSlideId(slideId) || IndexOf(slideId) < 0)
            {
                throw new DeckException("ppt.deck_slide_not_found", "工作区没有页 " + slideId + "。当前页：" + DescribeIds());
            }
        }

        private string AllocateSlideId()
        {
            var used = new HashSet<int>(Manifest.Slides.Select(s => DeckIds.ParseSlideNumber(s.Id)));
            var next = used.Count == 0 ? 1 : used.Max() + 1;
            if (next > DeckIds.MaxSlides)
            {
                next = Enumerable.Range(1, DeckIds.MaxSlides).First(n => !used.Contains(n));
            }

            return DeckIds.FormatSlideId(next);
        }

        private string DescribeIds()
        {
            return Manifest.Slides.Count == 0 ? "（无）" : string.Join(", ", Manifest.Slides.Select(s => s.Id));
        }
    }

    /// <summary>工作区根目录：<c>%LOCALAPPDATA%\NixerOfficeCopilot\Decks</c>。</summary>
    public sealed class DeckWorkspaceStore
    {
        private const string IdAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

        public DeckWorkspaceStore()
            : this(GetDefaultRoot())
        {
        }

        public DeckWorkspaceStore(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static string GetDefaultRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "Decks");
        }

        public DeckWorkspace Create(string style, string density, string language, string title)
        {
            System.IO.Directory.CreateDirectory(Root);
            var now = DateTime.Now;
            string id;
            string dir;
            do
            {
                id = now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + RandomSuffix();
                dir = Path.Combine(Root, id);
            }
            while (System.IO.Directory.Exists(dir));

            System.IO.Directory.CreateDirectory(dir);
            var manifest = new DeckManifest
            {
                Id = id,
                Style = string.IsNullOrWhiteSpace(style) ? null : style.Trim(),
                Density = DeckDensities.Normalize(density),
                Language = string.IsNullOrWhiteSpace(language) ? "zh-CN" : language.Trim(),
                Title = title,
                CreatedUtc = DateTime.UtcNow
            };
            return DeckWorkspace.CreateNew(dir, manifest);
        }

        public DeckWorkspace Open(string deckId)
        {
            var id = (deckId ?? string.Empty).Trim();
            if (!DeckIds.IsValidDeckId(id))
            {
                throw new DeckException(
                    "ppt.deck_invalid_argument",
                    "deckId 格式不正确：" + id + "。请原样使用 ppt_deck_create 返回的完整工作区 ID（格式 YYYYMMDD-HHmmss-xxxxxx），不要自行构造。");
            }

            var dir = Path.Combine(Root, id);
            if (!File.Exists(Path.Combine(dir, DeckWorkspace.ManifestFileName)))
            {
                throw new DeckException("ppt.deck_not_found", "找不到工作区 " + id + "（可能已过期清理），请用 ppt_deck_create 新建。");
            }

            return DeckWorkspace.Load(dir);
        }

        /// <summary>删除最后修改早于 maxAge 的工作区，返回删除数量。</summary>
        public int Purge(TimeSpan maxAge)
        {
            if (!System.IO.Directory.Exists(Root))
            {
                return 0;
            }

            var cutoff = DateTime.UtcNow - maxAge;
            var count = 0;
            foreach (var dir in System.IO.Directory.GetDirectories(Root))
            {
                if (!DeckIds.IsValidDeckId(Path.GetFileName(dir)))
                {
                    continue;
                }

                var manifest = Path.Combine(dir, DeckWorkspace.ManifestFileName);
                var stamp = File.Exists(manifest) ? File.GetLastWriteTimeUtc(manifest) : System.IO.Directory.GetLastWriteTimeUtc(dir);
                if (stamp >= cutoff)
                {
                    continue;
                }

                try
                {
                    System.IO.Directory.Delete(dir, true);
                    count++;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return count;
        }

        private static string RandomSuffix()
        {
            var bytes = new byte[6];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var chars = new char[6];
            for (var i = 0; i < 6; i++) chars[i] = IdAlphabet[bytes[i] % IdAlphabet.Length];
            return new string(chars);
        }
    }
}
