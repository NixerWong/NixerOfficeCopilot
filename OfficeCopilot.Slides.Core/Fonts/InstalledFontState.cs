using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace OfficeCopilot.Slides.Fonts
{
    /// <summary>
    /// 本机字体状态：从 Windows Fonts 注册表的文件记录和 OpenType 名称表构造字体字典，
    /// 并按文件内容识别已安装字面。
    /// </summary>
    public static class InstalledFontState
    {
        private const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";
        private const int LegacyFamilyNameId = 1;
        private const int LegacySubfamilyNameId = 2;
        private const int TypographicFamilyNameId = 16;
        private const int TypographicSubfamilyNameId = 17;
        private const int EnglishUnitedStates = 0x0409;
        private static readonly Regex OpticalSizeToken = new Regex(
            @"\s+\d+(?:\.\d+)?pt(?=\s|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ISet<string> FamilyNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in RegisteredFontFiles())
            {
                foreach (var face in TrueTypeInfo.ReadNameRecords(path).GroupBy(record => record.FaceIndex))
                {
                    names.UnionWith(FamilyNamesFromRecords(face));
                }
            }

            return names;
        }

        public static ISet<string> RegisteredFontNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in RegisteredFontFiles())
            {
                foreach (var face in TrueTypeInfo.ReadNameRecords(path).GroupBy(record => record.FaceIndex))
                {
                    names.UnionWith(FontNamesFromRecords(face));
                }
            }

            return names;
        }

        internal static ISet<string> FamilyNamesFromRecords(IEnumerable<TrueTypeNameRecord> records)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var typographicFamily = NormalizeFontName(ReadName(records, TypographicFamilyNameId));
            var family = string.IsNullOrWhiteSpace(typographicFamily)
                ? NormalizeFontName(ReadName(records, LegacyFamilyNameId))
                : typographicFamily;
            if (!string.IsNullOrWhiteSpace(family))
            {
                names.Add(family);
            }

            return names;
        }

        internal static ISet<string> FontNamesFromRecords(IEnumerable<TrueTypeNameRecord> records)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var legacyFamily = NormalizeFontName(ReadName(records, LegacyFamilyNameId));
            var legacySubfamily = NormalizeFontName(ReadName(records, LegacySubfamilyNameId));
            var typographicFamily = NormalizeFontName(ReadName(records, TypographicFamilyNameId));
            var typographicSubfamily = NormalizeFontName(ReadName(records, TypographicSubfamilyNameId));
            var family = string.IsNullOrWhiteSpace(typographicFamily) ? legacyFamily : typographicFamily;

            AddName(names, family);
            AddName(names, legacyFamily);
            AddName(names, typographicFamily);
            AddFaceName(names, family, legacySubfamily);
            AddFaceName(names, family, typographicSubfamily);
            AddFaceName(names, typographicFamily, typographicSubfamily);

            return names;
        }

        /// <summary>仅对大小与 <paramref name="sizes"/> 之一相同的已注册文件计算哈希，避免读取全部系统字体。</summary>
        public static ISet<string> FileHashes(ISet<long> sizes)
        {
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sizes == null || sizes.Count == 0)
            {
                return hashes;
            }

            foreach (var path in RegisteredFontFiles())
            {
                try
                {
                    if (sizes.Contains(new FileInfo(path).Length))
                    {
                        hashes.Add(Sha256(path));
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return hashes;
        }

        public static ISet<string> ManifestFileHashes(ISet<long> sizes, string manifestPath)
        {
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sizes == null || sizes.Count == 0) return hashes;

            var manifest = InstalledFontManifest.Load(manifestPath);
            foreach (var entry in manifest.Fonts.Where(font => font.Registered && !string.IsNullOrWhiteSpace(font.Path)))
            {
                var file = new FileInfo(entry.Path);
                if (!file.Exists || !sizes.Contains(file.Length)) continue;

                var actualHash = Sha256(entry.Path);
                if (string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    hashes.Add(actualHash);
                }
            }

            return hashes;
        }

        public static IDictionary<string, ISet<string>> SystemFontHashesByFamily()
        {
            return SystemFontHashesByFamily(null);
        }

        public static IDictionary<string, ISet<string>> SystemFontHashesByFamily(ISet<string> requestedFamilyNames)
        {
            var hashesByFamily = new Dictionary<string, ISet<string>>(StringComparer.OrdinalIgnoreCase);
            if (requestedFamilyNames != null && requestedFamilyNames.Count == 0)
            {
                return hashesByFamily;
            }

            foreach (var path in RegisteredFontFiles())
            {
                var registeredFamilyNames = TrueTypeInfo.ReadNameRecords(path)
                    .Where(record => record.NameId == 1 || record.NameId == 16)
                    .Select(record => NormalizeFontName(record.Value))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                if (requestedFamilyNames != null && !registeredFamilyNames.Any(requestedFamilyNames.Contains))
                {
                    continue;
                }

                var hash = Sha256(path);
                foreach (var familyName in registeredFamilyNames)
                {
                    ISet<string> familyHashes;
                    if (!hashesByFamily.TryGetValue(familyName, out familyHashes))
                    {
                        familyHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        hashesByFamily.Add(familyName, familyHashes);
                    }

                    familyHashes.Add(hash);
                }
            }

            return hashesByFamily;
        }

        public static IList<string> RegisteredFontFiles()
        {
            var result = new List<string>();
            result.AddRange(RegisteredFiles(Registry.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.Fonts)));
            result.AddRange(RegisteredFiles(Registry.CurrentUser, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts")));
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static IList<string> RegisteredFiles(RegistryKey hive, string directory)
        {
            var result = new List<string>();
            using (var key = hive.OpenSubKey(FontsKey))
            {
                if (key == null)
                {
                    return result;
                }

                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name) as string;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var path = Path.IsPathRooted(value) ? value : Path.Combine(directory, value);
                    if (File.Exists(path))
                    {
                        result.Add(path);
                    }
                }
            }

            return result;
        }

        public static string NormalizeFontName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return Regex.Replace(OpticalSizeToken.Replace(name, string.Empty), @"\s{2,}", " ").Trim();
        }

        private static string ReadName(IEnumerable<TrueTypeNameRecord> records, int nameId)
        {
            if (records == null)
            {
                return null;
            }

            var matches = records.Where(record => record.NameId == nameId).ToList();
            var english = matches.FirstOrDefault(record => record.LanguageId == EnglishUnitedStates);
            return (english ?? matches.FirstOrDefault())?.Value;
        }

        private static void AddName(ISet<string> names, string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        private static void AddFaceName(ISet<string> names, string family, string subfamily)
        {
            if (!string.IsNullOrWhiteSpace(family) && !string.IsNullOrWhiteSpace(subfamily))
            {
                names.Add(family + " " + subfamily);
            }
        }
    }
}
