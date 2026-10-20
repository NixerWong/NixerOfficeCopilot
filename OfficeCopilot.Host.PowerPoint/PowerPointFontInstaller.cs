using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using OfficeCopilot.Slides.Assets;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Host.PowerPoint
{
    /// <summary>在生成 PowerPoint 页面前，把本次用到但本机未安装的内置字体安装到当前用户目录。</summary>
    public interface IPowerPointFontInstaller
    {
        IList<PptVerificationIssue> EnsureFonts(IEnumerable<SlideBuildPlan> plans, ISet<string> installedFonts);
    }

    /// <summary>向当前用户注册字体文件。抽象出来便于测试。</summary>
    public interface IUserFontRegistrar
    {
        bool IsSupported { get; }

        /// <summary>注册字体：写入当前用户注册表，并以非 private 方式加载到本会话，使正在运行的宿主（PowerPoint）无需重启即可渲染。</summary>
        void Register(string fontFilePath, string displayName);

        bool IsRegistered(string fontFilePath);

        void Unregister(string fontFilePath);

        void NotifyFontsChanged();
    }

    /// <summary>
    /// Windows 10 1809+ 的用户字体：文件放在 %LOCALAPPDATA%\Microsoft\Windows\Fonts，
    /// 注册到 HKCU，全程不需要管理员权限。
    /// </summary>
    public sealed class WindowsUserFontRegistrar : IUserFontRegistrar
    {
        private const int MinimumUserFontBuild = 17763;
        private const string CurrentVersionRegistryPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        private const string UserFontsRegistryPath = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
        private const uint WmFontChange = 0x001D;

        private static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);

        public bool IsSupported
        {
            get
            {
                using (var key = Registry.LocalMachine.OpenSubKey(CurrentVersionRegistryPath))
                {
                    int build;
                    var text = key == null ? null : key.GetValue("CurrentBuildNumber") as string;
                    return int.TryParse(text, out build) && build >= MinimumUserFontBuild;
                }
            }
        }

        public void Register(string fontFilePath, string displayName)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(UserFontsRegistryPath))
            {
                key.SetValue(displayName + " (TrueType)", fontFilePath, RegistryValueKind.String);
            }

            // 必须是非 private 加载：PowerPoint 的文本渲染只使用会话字体表中的字体，FR_PRIVATE 仅对枚举可见，渲染时会回退。
            if (AddFontResourceEx(fontFilePath, 0, IntPtr.Zero) == 0)
            {
                throw new IOException("无法加载字体文件：" + fontFilePath);
            }
        }

        public bool IsRegistered(string fontFilePath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UserFontsRegistryPath))
            {
                return key != null && key.GetValueNames()
                    .Any(name => MatchesPath(key.GetValue(name) as string, fontFilePath));
            }
        }

        public void Unregister(string fontFilePath)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(UserFontsRegistryPath, true))
            {
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        if (MatchesPath(key.GetValue(name) as string, fontFilePath))
                        {
                            key.DeleteValue(name, false);
                        }
                    }
                }
            }

            RemoveFontResourceEx(fontFilePath, 0, IntPtr.Zero);
        }

        public void NotifyFontsChanged()
        {
            SendNotifyMessage(HwndBroadcast, WmFontChange, IntPtr.Zero, IntPtr.Zero);
        }

        private static bool MatchesPath(string registeredPath, string expectedPath)
        {
            if (string.IsNullOrWhiteSpace(registeredPath) || string.IsNullOrWhiteSpace(expectedPath)) return false;
            var fontsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");
            var resolvedPath = Path.IsPathRooted(registeredPath)
                ? registeredPath
                : Path.Combine(fontsDirectory, registeredPath);
            return string.Equals(Path.GetFullPath(resolvedPath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase);
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveFontResourceEx(string fileName, uint flags, IntPtr reserved);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

    public sealed class PowerPointFontInstaller : IPowerPointFontInstaller
    {
        private readonly SlideAssetPaths _paths;
        private readonly string _destinationDir;
        private readonly IUserFontRegistrar _registrar;
        private readonly Func<ISet<long>, ISet<string>> _installedFileHashes;
        private readonly string _manifestPath;

        public PowerPointFontInstaller(SlideAssetPaths paths, string destinationDir, IUserFontRegistrar registrar)
            : this(
                paths,
                destinationDir,
                registrar,
                sizes => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                Path.Combine(destinationDir ?? Path.GetTempPath(), "installed.json"))
        {
        }

        /// <param name="installedFileHashes">按随包文件大小返回本机已注册字体文件的 SHA-256 集合；便于测试注入。</param>
        public PowerPointFontInstaller(SlideAssetPaths paths, string destinationDir, IUserFontRegistrar registrar, Func<ISet<long>, ISet<string>> installedFileHashes)
            : this(
                paths,
                destinationDir,
                registrar,
                installedFileHashes,
                Path.Combine(destinationDir ?? Path.GetTempPath(), "installed.json"))
        {
        }

        public PowerPointFontInstaller(
            SlideAssetPaths paths,
            string destinationDir,
            IUserFontRegistrar registrar,
            Func<ISet<long>, ISet<string>> installedFileHashes,
            string manifestPath)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _destinationDir = destinationDir ?? throw new ArgumentNullException(nameof(destinationDir));
            _registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
            _installedFileHashes = installedFileHashes ?? throw new ArgumentNullException(nameof(installedFileHashes));
            _manifestPath = manifestPath ?? throw new ArgumentNullException(nameof(manifestPath));
        }

        public static PowerPointFontInstaller CreateDefault(SlideAssetPaths paths)
        {
            var userFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");
            var manifestPath = FontInstallConstants.DefaultManifestPath;
            return new PowerPointFontInstaller(
                paths,
                userFonts,
                new WindowsUserFontRegistrar(),
                sizes => InstalledFontState.ManifestFileHashes(sizes, manifestPath),
                manifestPath);
        }

        public IList<PptVerificationIssue> EnsureFonts(IEnumerable<SlideBuildPlan> plans, ISet<string> installedFonts)
        {
            var issues = new List<PptVerificationIssue>();
            var catalog = FontCatalog.Load(_paths);
            var names = CollectFamilyNames(plans);
            var manifest = InstalledFontManifest.Load(_manifestPath);
            var manifestChanged = CleanupPendingDeletions(manifest, issues);
            if (_registrar.IsSupported)
            {
                manifestChanged |= MigrateLegacyFonts(catalog, names, manifest, issues);
            }
            if (manifestChanged) manifest.Save(_manifestPath);

            var systemShadows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                systemShadows.UnionWith(FontShadowDiagnostics.FindHashMismatches(
                    catalog,
                    InstalledFontState.SystemFontHashesByFamily()));
            }
            catch (IOException exception)
            {
                issues.Add(Issue(
                    "ppt.font_shadow_check_failed",
                    "无法读取系统字体文件以检查同名冲突：" + exception.Message,
                    string.Empty));
            }
            catch (UnauthorizedAccessException exception)
            {
                issues.Add(Issue(
                    "ppt.font_shadow_check_failed",
                    "没有权限读取系统字体文件以检查同名冲突：" + exception.Message,
                    string.Empty));
            }
            foreach (var family in catalog.Families.Where(family => systemShadows.Contains(family.Family)
                && names.Any(name => ReferenceEquals(ResolveFamily(name, catalog), family))))
            {
                issues.Add(Issue(
                    "ppt.font_system_shadow",
                    "系统中存在与随包版本哈希不同的同名字体族“" + family.Family + "”；PowerPoint 可能优先使用系统字形。请在目标 Office 2019 环境确认渲染结果。",
                    family.Family));
            }

            var table = FontResolutionTable.Build(catalog, installedFonts, _installedFileHashes(FontResolutionTable.BundledSizes(catalog)));
            var unresolved = FindUnresolvedFamilies(names, catalog, installedFonts);
            if (unresolved.Count > 0)
            {
                var missing = string.Join("、", unresolved);
                issues.Add(Issue(
                    "ppt.font_missing",
                    "以下字体既不在本机安装，也不在随附字体库中，PowerPoint 将以替代字体显示：" + missing + "。请安装这些字体，或改用随附字体。",
                    missing));
            }

            var families = ResolveMissingFamilies(names, catalog, installedFonts, table);
            if (families.Count == 0)
            {
                return issues;
            }

            var familyNames = string.Join("、", families.Select(f => f.Family));
            if (!_registrar.IsSupported)
            {
                issues.Add(Issue(
                    "ppt.font_install_skipped",
                    "本机 Windows 版本低于 10 1809，无法为当前用户安装字体（" + familyNames + "）；PowerPoint 将以替代字体显示。",
                    familyNames));
                return issues;
            }

            var installed = new List<string>();
            var fontsChanged = false;
            foreach (var family in families)
            {
                try
                {
                    InstallFamily(family, manifest, ref fontsChanged);
                    installed.Add(family.Family);
                }
                catch (Exception ex)
                {
                    issues.Add(Issue(
                        "ppt.font_install_failed",
                        "字体 " + family.Family + " 未能安装到当前用户目录（" + ex.Message + "）；PowerPoint 将以替代字体显示。可手动安装该字体。",
                        family.Family));
                }
            }

            if (fontsChanged)
            {
                _registrar.NotifyFontsChanged();
            }

            if (installed.Count > 0)
            {
                var visible = PptVerifier.GetInstalledFonts();
                var pending = installed.Where(f => !visible.Contains(f)).ToList();
                var message = "已为当前用户安装字体：" + string.Join("、", installed) + "。";
                message += pending.Count == 0
                    ? "当前 PowerPoint 进程已识别这些字体，无需重启。"
                    : "以下字体当前进程仍未识别：" + string.Join("、", pending) + "。若生成结果仍是替代字体，请重启 PowerPoint 后再生成。";
                issues.Add(Issue("ppt.font_installed", message, string.Join(",", installed)));
            }

            return issues;
        }

        private bool MigrateLegacyFonts(
            FontCatalog catalog,
            ISet<string> names,
            InstalledFontManifest manifest,
            IList<PptVerificationIssue> issues)
        {
            if (!Directory.Exists(_destinationDir)) return false;
            var knownLegacyPaths = new HashSet<string>(
                catalog.Families.SelectMany(family =>
                    family.Faces.Select(face =>
                        Path.GetFullPath(Path.Combine(_destinationDir, FontInstallConstants.LegacyFileNamePrefix + face.File)))),
                StringComparer.OrdinalIgnoreCase);
            var faces = catalog.Families
                .Where(family => names.Any(name => ReferenceEquals(ResolveFamily(name, catalog), family)))
                .SelectMany(family => family.Faces.Select(face => new LegacyFontFace
                {
                    Family = family.Family,
                    Face = face,
                    SourcePath = Path.Combine(family.Directory, face.File),
                    LegacyPath = Path.Combine(_destinationDir, FontInstallConstants.LegacyFileNamePrefix + face.File),
                    CurrentPath = Path.Combine(_destinationDir, FontInstallConstants.FileNamePrefix + face.File)
                }))
                .ToList();
            var changed = false;

            foreach (var legacyPath in Directory.GetFiles(_destinationDir, FontInstallConstants.LegacyFileNamePrefix + "*"))
            {
                var matchingFaces = faces.Where(face => string.Equals(face.LegacyPath, legacyPath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matchingFaces.Count == 0)
                {
                    if (knownLegacyPaths.Contains(Path.GetFullPath(legacyPath))) continue;
                    issues.Add(Issue(
                        "ppt.font_legacy_conflict",
                        "发现无法与随包字体对应的旧安装文件，已保留不动：" + Path.GetFileName(legacyPath),
                        Path.GetFileName(legacyPath)));
                    continue;
                }

                var legacyHash = InstalledFontState.Sha256(legacyPath);
                var face = matchingFaces.FirstOrDefault(candidate =>
                    string.Equals(InstalledFontState.Sha256(candidate.SourcePath), legacyHash, StringComparison.OrdinalIgnoreCase));
                if (face == null)
                {
                    issues.Add(Issue(
                        "ppt.font_legacy_conflict",
                        "旧安装文件与当前随包字体哈希不同，已保留不动：" + Path.GetFileName(legacyPath),
                        Path.GetFileName(legacyPath)));
                    continue;
                }

                var currentPath = face.CurrentPath;
                if (File.Exists(currentPath)
                    && !string.Equals(InstalledFontState.Sha256(currentPath), legacyHash, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(Issue(
                        "ppt.font_legacy_conflict",
                        "新版命名的安装文件与当前随包字体哈希不同，旧文件已保留且未覆盖：" + Path.GetFileName(currentPath),
                        Path.GetFileName(currentPath)));
                    continue;
                }

                try
                {
                    if (!File.Exists(currentPath))
                    {
                        File.Copy(face.SourcePath, currentPath, false);
                    }

                    if (!_registrar.IsRegistered(currentPath))
                    {
                        _registrar.Register(currentPath, Path.GetFileNameWithoutExtension(currentPath));
                        changed = true;
                    }

                    manifest.Upsert(CreateManifestEntry(face.Family, face.Face, currentPath));
                    manifest.Save(_manifestPath);
                    _registrar.Unregister(legacyPath);
                    manifest.Remove(legacyPath);
                    if (!TryDeleteLegacyFile(legacyPath))
                    {
                        AddPendingDeletion(manifest, legacyPath);
                    }

                    manifest.Save(_manifestPath);
                    changed = true;
                }
                catch (Exception exception)
                {
                    issues.Add(Issue(
                        "ppt.font_migration_failed",
                        "旧字体迁移失败，旧文件已尽量保留以避免 PowerPoint 回退：" + Path.GetFileName(legacyPath) + "（" + exception.Message + "）。",
                        Path.GetFileName(legacyPath)));
                }
            }

            if (changed) _registrar.NotifyFontsChanged();
            return changed;
        }

        private bool CleanupPendingDeletions(InstalledFontManifest manifest, IList<PptVerificationIssue> issues)
        {
            var changed = false;
            foreach (var pendingPath in manifest.PendingDeletionPaths.ToList())
            {
                if (!IsLegacyFontPath(pendingPath))
                {
                    issues.Add(Issue(
                        "ppt.font_cleanup_pending",
                        "字体清单中的待清理路径不在允许的旧字体目录或命名范围内，未删除：" + pendingPath,
                        pendingPath));
                    continue;
                }

                if (TryDeleteLegacyFile(pendingPath))
                {
                    manifest.PendingDeletionPaths.Remove(pendingPath);
                    changed = true;
                }
                else
                {
                    issues.Add(Issue(
                        "ppt.font_cleanup_pending",
                        "旧字体已解除注册，但文件仍被占用；将在后续构建时重试删除：" + Path.GetFileName(pendingPath),
                        Path.GetFileName(pendingPath)));
                }
            }

            return changed;
        }

        private bool IsLegacyFontPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetFullPath(_destinationDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(fullPath).StartsWith(FontInstallConstants.LegacyFileNamePrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryDeleteLegacyFile(string path)
        {
            if (!File.Exists(path)) return true;
            try
            {
                File.Delete(path);
                return !File.Exists(path);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void AddPendingDeletion(InstalledFontManifest manifest, string path)
        {
            if (!manifest.PendingDeletionPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                manifest.PendingDeletionPaths.Add(path);
            }
        }

        private static InstalledFontManifestEntry CreateManifestEntry(string family, FontFaceEntry face, string path)
        {
            return new InstalledFontManifestEntry
            {
                Family = family,
                FileName = Path.GetFileName(path),
                Path = path,
                Sha256 = InstalledFontState.Sha256(path),
                FontVersion = TrueTypeInfo.ReadName(path, 5),
                Registered = true
            };
        }

        internal static ISet<string> CollectFamilyNames(IEnumerable<SlideBuildPlan> plans)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plan in plans ?? Enumerable.Empty<SlideBuildPlan>())
            {
                if (plan == null) continue;

                foreach (var text in plan.TextBlocks)
                {
                    names.Add(text.FontName);
                    names.Add(text.FontNameFarEast);
                }

                foreach (var table in plan.Tables)
                {
                    foreach (var cell in table.Rows.SelectMany(row => row))
                    {
                        names.Add(cell.FontName);
                        names.Add(cell.FontNameFarEast);
                    }
                }
            }

            names.Remove(null);
            names.Remove(string.Empty);
            return names;
        }

        private static IList<string> FindUnresolvedFamilies(ISet<string> names, FontCatalog catalog, ISet<string> installedFonts)
        {
            var result = new List<string>();
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var entry = ResolveFamily(name, catalog);
                if (entry != null && entry.Directory != null) continue;
                if (installedFonts != null && installedFonts.Contains(name)) continue;
                result.Add(name);
            }

            return result;
        }

        private static IList<FontFamilyEntry> ResolveMissingFamilies(ISet<string> names, FontCatalog catalog, ISet<string> installedFonts, FontResolutionTable table)
        {
            var result = new List<FontFamilyEntry>();
            foreach (var name in names)
            {
                var entry = ResolveFamily(name, catalog);
                if (entry == null || entry.Directory == null) continue;
                if (installedFonts != null && installedFonts.Contains(entry.Family)) continue;
                if (table != null && table.IsProvided(entry.Family)) continue;
                if (!result.Contains(entry)) result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// 先精确匹配族名；再匹配以族名开头的字重变体（如 "Albert Sans Medium" 属于 "Albert Sans" 目录）。
        /// </summary>
        private static FontFamilyEntry ResolveFamily(string name, FontCatalog catalog)
        {
            FontFamilyEntry entry;
            if (catalog.TryResolve(name, out entry)) return entry;

            return catalog.Families
                .Where(f => name.StartsWith(f.Family + " ", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Family.Length)
                .FirstOrDefault();
        }

        private void InstallFamily(FontFamilyEntry family, InstalledFontManifest manifest, ref bool fontsChanged)
        {
            Directory.CreateDirectory(_destinationDir);
            foreach (var face in family.Faces)
            {
                var source = Path.Combine(family.Directory, face.File);
                var targetName = FontInstallConstants.FileNamePrefix + face.File;
                var target = Path.Combine(_destinationDir, targetName);
                // Windows Font Cache Service 会长期占用已注册的字体文件，覆盖写会失败；内容一致时无需复制。
                var contentMatches = IsSameContent(source, target);
                if (!contentMatches)
                {
                    File.Copy(source, target, true);
                }

                if (!_registrar.IsRegistered(target) || !contentMatches)
                {
                    _registrar.Register(target, Path.GetFileNameWithoutExtension(targetName));
                    fontsChanged = true;
                }

                manifest.Upsert(CreateManifestEntry(family.Family, face, target));
                manifest.Save(_manifestPath);
            }
        }

        private static bool IsSameContent(string source, string target)
        {
            if (!File.Exists(target)) return false;
            if (new FileInfo(source).Length != new FileInfo(target).Length) return false;

            return File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(target));
        }

        private static PptVerificationIssue Issue(string code, string message, string expected)
        {
            return new PptVerificationIssue
            {
                Code = code,
                Message = message,
                Expected = expected
            };
        }

        private sealed class LegacyFontFace
        {
            public string Family { get; set; }
            public FontFaceEntry Face { get; set; }
            public string SourcePath { get; set; }
            public string LegacyPath { get; set; }
            public string CurrentPath { get; set; }
        }
    }
}
