using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace OfficeCopilot.Slides.Fonts
{
    public sealed class SystemFontFamilyInfo
    {
        [JsonProperty("family")] public string Family { get; set; }
        [JsonProperty("supportsChinese")] public bool SupportsChinese { get; set; }
        [JsonProperty("coverage", NullValueHandling = NullValueHandling.Ignore)] public IList<UnicodeCoverageRange> Coverage { get; set; }
    }

    /// <summary>本机已注册字体的中文字符覆盖目录；首次加载完整扫描，之后复用按字体文件清单指纹缓存的结果。</summary>
    public sealed class SystemFontCatalog
    {
        private const int CacheVersion = 1;
        private static readonly object DefaultSync = new object();
        private static readonly int[] ChineseProbeCharacters =
        {
            0x4E2D, 0x6587, 0x56FD, 0x570B, 0x8BF7, 0x8ACB, 0x7684, 0x8BED, 0x8A9E, 0x53D1,
            0x767C, 0x4E0E, 0x8207, 0x89C4, 0x898F, 0x8303, 0x7CBE, 0x8BB2, 0x8B1B, 0x6C42
        };

        private static SystemFontCatalog _default;
        private readonly IDictionary<string, SystemFontFamilyInfo> _byName;

        [JsonIgnore]
        public IReadOnlyList<SystemFontFamilyInfo> Families { get; private set; }

        [JsonIgnore]
        public IList<string> ChineseFamilies { get; private set; }

        [JsonIgnore]
        public bool CacheHit { get; private set; }

        [JsonIgnore]
        public int FontFilesAnalyzed { get; private set; }

        [JsonIgnore]
        public int FacesSkipped { get; private set; }

        [JsonIgnore]
        public IList<string> Warnings { get; private set; }

        private SystemFontCatalog(
            IEnumerable<SystemFontFamilyInfo> families,
            bool cacheHit,
            int fontFilesAnalyzed,
            int facesSkipped,
            IEnumerable<string> warnings)
        {
            var entries = (families ?? Enumerable.Empty<SystemFontFamilyInfo>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Family))
                .GroupBy(entry => entry.Family.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => Merge(group))
                .OrderBy(entry => entry.Family, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _byName = entries.ToDictionary(entry => entry.Family, StringComparer.OrdinalIgnoreCase);
            Families = entries.AsReadOnly();
            ChineseFamilies = entries.Where(entry => entry.SupportsChinese)
                .Select(entry => entry.Family)
                .ToList()
                .AsReadOnly();
            CacheHit = cacheHit;
            FontFilesAnalyzed = fontFilesAnalyzed;
            FacesSkipped = facesSkipped;
            Warnings = (warnings ?? Enumerable.Empty<string>()).ToList().AsReadOnly();
        }

        public static SystemFontCatalog Default
        {
            get
            {
                lock (DefaultSync)
                {
                    if (_default == null)
                    {
                        var cachePath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "NixerOfficeCopilot",
                            "fonts",
                            "system-font-catalog.json");
                        _default = Load(cachePath, InstalledFontState.RegisteredFontFiles());
                    }

                    return _default;
                }
            }
        }

        public string Summary
        {
            get
            {
                var source = CacheHit ? "命中缓存" : "首次扫描";
                return source + "；分析字体文件 " + FontFilesAnalyzed.ToString(CultureInfo.InvariantCulture)
                    + " 个，识别字体族 " + Families.Count.ToString(CultureInfo.InvariantCulture)
                    + " 个，其中支持中文 " + ChineseFamilies.Count.ToString(CultureInfo.InvariantCulture)
                    + " 个；跳过无法分析的字面 " + FacesSkipped.ToString(CultureInfo.InvariantCulture) + " 个。";
            }
        }

        public bool IsChineseFamily(string family)
        {
            SystemFontFamilyInfo entry;
            return !string.IsNullOrWhiteSpace(family)
                && _byName.TryGetValue(family.Trim(), out entry)
                && entry.SupportsChinese;
        }

        public bool Contains(string family)
        {
            return !string.IsNullOrWhiteSpace(family) && _byName.ContainsKey(family.Trim());
        }

        public static SystemFontCatalog Load(string cachePath, IEnumerable<string> fontPaths)
        {
            if (string.IsNullOrWhiteSpace(cachePath)) throw new ArgumentException("字体缓存路径不能为空。", nameof(cachePath));
            if (fontPaths == null) throw new ArgumentNullException(nameof(fontPaths));

            var warnings = new List<string>();
            var files = DescribeFiles(fontPaths, warnings);
            var fingerprint = Fingerprint(files);
            var cached = TryReadCache(cachePath, fingerprint, warnings);
            if (cached != null)
            {
                return new SystemFontCatalog(
                    cached.Families,
                    true,
                    cached.FontFilesAnalyzed,
                    cached.FacesSkipped,
                    warnings);
            }

            var scan = Scan(files);
            warnings.AddRange(scan.Warnings);
            var catalog = new SystemFontCatalog(
                scan.Families,
                false,
                files.Count,
                scan.FacesSkipped,
                warnings);
            WriteCache(cachePath, fingerprint, catalog, warnings);
            return new SystemFontCatalog(
                catalog.Families,
                false,
                catalog.FontFilesAnalyzed,
                catalog.FacesSkipped,
                warnings);
        }

        private static List<FontFileDescriptor> DescribeFiles(IEnumerable<string> fontPaths, IList<string> warnings)
        {
            var files = new List<FontFileDescriptor>();
            foreach (var path in fontPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    var file = new FileInfo(fullPath);
                    if (!file.Exists) continue;
                    files.Add(new FontFileDescriptor
                    {
                        Path = fullPath,
                        Length = file.Length,
                        LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks
                    });
                }
                catch (IOException)
                {
                    warnings.Add("有字体文件无法读取元数据，未纳入系统字体分析。");
                }
                catch (UnauthorizedAccessException)
                {
                    warnings.Add("有字体文件无权读取元数据，未纳入系统字体分析。");
                }
                catch (ArgumentException)
                {
                    warnings.Add("有字体注册路径无效，未纳入系统字体分析。");
                }
            }

            return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string Fingerprint(IEnumerable<FontFileDescriptor> files)
        {
            var material = string.Join(
                "\n",
                files.Select(file => file.Path.ToUpperInvariant()
                    + "\0" + file.Length.ToString(CultureInfo.InvariantCulture)
                    + "\0" + file.LastWriteUtcTicks.ToString(CultureInfo.InvariantCulture)));
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(material))).Replace("-", string.Empty);
            }
        }

        private static CachedCatalog TryReadCache(string cachePath, string fingerprint, IList<string> warnings)
        {
            if (!File.Exists(cachePath)) return null;
            try
            {
                var cached = JsonConvert.DeserializeObject<CachedCatalog>(File.ReadAllText(cachePath, Encoding.UTF8));
                if (cached != null && cached.Version == CacheVersion
                    && string.Equals(cached.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)
                    && cached.Families != null)
                {
                    return cached;
                }
            }
            catch (JsonException)
            {
                warnings.Add("系统字体缓存格式无效，已重新扫描。");
            }
            catch (IOException)
            {
                warnings.Add("系统字体缓存无法读取，已重新扫描。");
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add("系统字体缓存无权读取，已重新扫描。");
            }

            return null;
        }

        private static ScanResult Scan(IList<FontFileDescriptor> files)
        {
            var families = new Dictionary<string, SystemFontFamilyInfo>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            var facesSkipped = 0;
            foreach (var file in files)
            {
                IList<TrueTypeNameRecord> nameRecords;
                try
                {
                    nameRecords = TrueTypeInfo.ReadNameRecords(file.Path);
                }
                catch (IOException)
                {
                    facesSkipped++;
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    facesSkipped++;
                    continue;
                }
                catch (InvalidDataException)
                {
                    facesSkipped++;
                    continue;
                }
                catch (ArgumentException)
                {
                    facesSkipped++;
                    continue;
                }
                catch (IndexOutOfRangeException)
                {
                    facesSkipped++;
                    continue;
                }

                var faces = nameRecords.GroupBy(record => record.FaceIndex).ToList();
                if (faces.Count == 0)
                {
                    facesSkipped++;
                    continue;
                }

                foreach (var face in faces)
                {
                    var names = face
                        .Where(record => record.NameId == 1 || record.NameId == 16)
                        .Select(record => InstalledFontState.NormalizeFontName(record.Value))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (names.Count == 0)
                    {
                        facesSkipped++;
                        continue;
                    }

                    IReadOnlyList<UnicodeCoverageRange> coverage;
                    try
                    {
                        coverage = TrueTypeInfo.ReadUnicodeCoverage(file.Path, face.Key);
                    }
                    catch (IOException)
                    {
                        facesSkipped++;
                        continue;
                    }
                    catch (InvalidDataException)
                    {
                        facesSkipped++;
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        facesSkipped++;
                        continue;
                    }
                    catch (ArgumentException)
                    {
                        facesSkipped++;
                        continue;
                    }
                    catch (IndexOutOfRangeException)
                    {
                        facesSkipped++;
                        continue;
                    }

                    var supportsChinese = ChineseProbeCharacters.Count(codePoint => Covers(coverage, codePoint)) >= 6;
                    foreach (var name in names)
                    {
                        SystemFontFamilyInfo entry;
                        if (!families.TryGetValue(name, out entry))
                        {
                            entry = new SystemFontFamilyInfo
                            {
                                Family = name,
                                Coverage = new List<UnicodeCoverageRange>()
                            };
                            families.Add(name, entry);
                        }

                        entry.SupportsChinese |= supportsChinese;
                        if (supportsChinese)
                        {
                            entry.Coverage = MergeCoverage(entry.Coverage.Concat(coverage));
                        }
                    }
                }
            }

            if (facesSkipped > 0)
            {
                warnings.Add("有 " + facesSkipped.ToString(CultureInfo.InvariantCulture)
                    + " 个系统字体文件或字面无法读取名称/cmap，未用于字体分类。");
            }

            return new ScanResult
            {
                Families = families.Values.OrderBy(entry => entry.Family, StringComparer.OrdinalIgnoreCase).ToList(),
                FacesSkipped = facesSkipped,
                Warnings = warnings
            };
        }

        private static bool Covers(IEnumerable<UnicodeCoverageRange> ranges, int codePoint)
        {
            return ranges.Any(range => range.Start <= codePoint && codePoint <= range.End);
        }

        private static IList<UnicodeCoverageRange> MergeCoverage(IEnumerable<UnicodeCoverageRange> ranges)
        {
            var merged = new List<UnicodeCoverageRange>();
            foreach (var range in ranges.OrderBy(range => range.Start).ThenBy(range => range.End))
            {
                if (merged.Count == 0 || (long)range.Start > (long)merged[merged.Count - 1].End + 1)
                {
                    merged.Add(new UnicodeCoverageRange { Start = range.Start, End = range.End });
                }
                else
                {
                    merged[merged.Count - 1].End = Math.Max(merged[merged.Count - 1].End, range.End);
                }
            }

            return merged;
        }

        private static SystemFontFamilyInfo Merge(IEnumerable<SystemFontFamilyInfo> entries)
        {
            var list = entries.ToList();
            var supportsChinese = list.Any(entry => entry.SupportsChinese);
            return new SystemFontFamilyInfo
            {
                Family = list[0].Family,
                SupportsChinese = supportsChinese,
                Coverage = supportsChinese
                    ? MergeCoverage(list.Where(entry => entry.SupportsChinese)
                        .SelectMany(entry => entry.Coverage ?? new List<UnicodeCoverageRange>()))
                    : null
            };
        }

        private static void WriteCache(string cachePath, string fingerprint, SystemFontCatalog catalog, IList<string> warnings)
        {
            var directory = Path.GetDirectoryName(cachePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                warnings.Add("系统字体缓存目录无效，分析结果仅保存在当前进程。");
                return;
            }

            var temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                var cached = new CachedCatalog
                {
                    Version = CacheVersion,
                    Fingerprint = fingerprint,
                    FontFilesAnalyzed = catalog.FontFilesAnalyzed,
                    FacesSkipped = catalog.FacesSkipped,
                    Families = catalog.Families.ToList()
                };
                File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(cached), new UTF8Encoding(false));
                if (File.Exists(cachePath))
                {
                    File.Replace(temporaryPath, cachePath, null);
                }
                else
                {
                    File.Move(temporaryPath, cachePath);
                }
            }
            catch (IOException)
            {
                warnings.Add("系统字体分析缓存无法写入，下次启动将重新扫描。");
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add("无权写入系统字体分析缓存，下次启动将重新扫描。");
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                        warnings.Add("无法清理临时系统字体缓存文件。");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        warnings.Add("无权清理临时系统字体缓存文件。");
                    }
                }
            }
        }

        private sealed class FontFileDescriptor
        {
            public string Path { get; set; }
            public long Length { get; set; }
            public long LastWriteUtcTicks { get; set; }
        }

        private sealed class CachedCatalog
        {
            [JsonProperty("version")] public int Version { get; set; }
            [JsonProperty("fingerprint")] public string Fingerprint { get; set; }
            [JsonProperty("fontFilesAnalyzed")] public int FontFilesAnalyzed { get; set; }
            [JsonProperty("facesSkipped")] public int FacesSkipped { get; set; }
            [JsonProperty("families")] public IList<SystemFontFamilyInfo> Families { get; set; }
        }

        private sealed class ScanResult
        {
            public IList<SystemFontFamilyInfo> Families { get; set; }
            public int FacesSkipped { get; set; }
            public IList<string> Warnings { get; set; }
        }
    }
}
