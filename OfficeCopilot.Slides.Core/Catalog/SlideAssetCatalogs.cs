using System;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Slides.Catalog
{
    /// <summary>进程内共享的素材目录（首次使用时加载，可显式重载以拾取用户目录变更）。</summary>
    public sealed class SlideAssetCatalogs
    {
        private static readonly object SyncRoot = new object();
        private static SlideAssetCatalogs _default;
        private readonly Lazy<SystemFontCatalog> _systemFonts;

        public SlideAssetCatalogs(SlideAssetPaths paths)
        {
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
            Fonts = FontCatalog.Load(paths);
            Styles = StyleCatalog.Load(paths, Fonts);
            _systemFonts = new Lazy<SystemFontCatalog>(() => SystemFontCatalog.Default);
        }

        public SlideAssetPaths Paths { get; }
        public FontCatalog Fonts { get; }
        public StyleCatalog Styles { get; }
        public SystemFontCatalog SystemFonts { get { return _systemFonts.Value; } }

        public static SlideAssetCatalogs Default
        {
            get
            {
                lock (SyncRoot)
                {
                    return _default ?? (_default = new SlideAssetCatalogs(SlideAssetPaths.CreateDefault()));
                }
            }
        }

        public static SlideAssetCatalogs Reload()
        {
            lock (SyncRoot)
            {
                _default = new SlideAssetCatalogs(SlideAssetPaths.CreateDefault());
                return _default;
            }
        }
    }
}
