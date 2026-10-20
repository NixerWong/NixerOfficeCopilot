using System;
using System.Collections.Generic;
using System.IO;

namespace OfficeCopilot.Slides.Deck
{
    /// <summary>
    /// 渲染与预览使用的虚拟地址。渲染 WebView2 只允许单一来源 <c>https://nixer-deck.local/</c>
    /// （同源，字体无需 CORS）：<c>/lib/</c> → 内置素材，<c>/user/</c> → 用户素材，<c>/decks/</c> → 工作区根。
    /// 面板用 <c>https://nixer-decks.local/</c> 只读映射工作区根，用于显示候选预览图。
    /// </summary>
    public static class DeckUrls
    {
        public const string RenderHost = "nixer-deck.local";
        public const string PanelHost = "nixer-decks.local";
        public const string RenderOrigin = "https://" + RenderHost + "/";
        public const string PanelOrigin = "https://" + PanelHost + "/";

        public const string LibSegment = "lib";
        public const string UserSegment = "user";
        public const string DecksSegment = "decks";

        public static string Lib(string relative)
        {
            return RenderOrigin + LibSegment + "/" + (relative ?? string.Empty).TrimStart('/');
        }

        public static string User(string relative)
        {
            return RenderOrigin + UserSegment + "/" + (relative ?? string.Empty).TrimStart('/');
        }

        public static string DeckBase(string deckId)
        {
            return RenderOrigin + DecksSegment + "/" + deckId + "/";
        }

        public static string PanelImage(string deckId, string relative)
        {
            return PanelOrigin + deckId + "/" + (relative ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }
    }

    /// <summary>把渲染请求 URL 映射为本地文件；越界、未知来源一律拒绝。</summary>
    public sealed class DeckUrlResolver
    {
        private static readonly Dictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".htm", "text/html; charset=utf-8" },
            { ".css", "text/css; charset=utf-8" },
            { ".js", "text/javascript; charset=utf-8" },
            { ".json", "application/json; charset=utf-8" },
            { ".svg", "image/svg+xml" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif", "image/gif" },
            { ".webp", "image/webp" },
            { ".ttf", "font/ttf" },
            { ".otf", "font/otf" },
            { ".woff", "font/woff" },
            { ".woff2", "font/woff2" },
            { ".txt", "text/plain; charset=utf-8" },
            { ".md", "text/plain; charset=utf-8" }
        };

        private readonly string _builtInRoot;
        private readonly string _userRoot;
        private readonly string _decksRoot;

        public DeckUrlResolver(string builtInRoot, string userRoot, string decksRoot)
        {
            _builtInRoot = Normalize(builtInRoot);
            _userRoot = Normalize(userRoot);
            _decksRoot = Normalize(decksRoot);
        }

        public bool TryResolve(string url, out string filePath)
        {
            filePath = null;
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, DeckUrls.RenderHost, StringComparison.OrdinalIgnoreCase)
                || !uri.IsDefaultPort)
            {
                return false;
            }

            string path;
            try
            {
                path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0)
            {
                return false;
            }

            var slash = path.IndexOf('/');
            if (slash <= 0)
            {
                return false;
            }

            var segment = path.Substring(0, slash);
            var rest = path.Substring(slash + 1);
            string root;
            if (string.Equals(segment, DeckUrls.LibSegment, StringComparison.OrdinalIgnoreCase)) root = _builtInRoot;
            else if (string.Equals(segment, DeckUrls.UserSegment, StringComparison.OrdinalIgnoreCase)) root = _userRoot;
            else if (string.Equals(segment, DeckUrls.DecksSegment, StringComparison.OrdinalIgnoreCase))
            {
                root = _decksRoot;
                var idEnd = rest.IndexOf('/');
                if (idEnd <= 0 || !DeckIds.IsValidDeckId(rest.Substring(0, idEnd))) return false;
            }
            else return false;

            if (string.IsNullOrEmpty(root) || rest.Length == 0)
            {
                return false;
            }

            foreach (var part in rest.Split('/'))
            {
                if (part == ".." || part == ".")
                {
                    return false;
                }
            }

            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(root, rest.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }

            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !ContentTypes.ContainsKey(Path.GetExtension(full) ?? string.Empty))
            {
                return false;
            }

            filePath = full;
            return true;
        }

        public static string GetContentType(string path)
        {
            string type;
            return ContentTypes.TryGetValue(Path.GetExtension(path) ?? string.Empty, out type) ? type : "application/octet-stream";
        }

        private static string Normalize(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
