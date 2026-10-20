using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OfficeCopilot.Slides.Fonts
{
    /// <summary>诊断本机字体与随包 @font-face 的同名冲突；不改写或过滤样式表。</summary>
    public static class FontShadowDiagnostics
    {
        private static readonly Regex FontFaceBlock = new Regex(@"@font-face\s*\{[^}]*\}", RegexOptions.Compiled);
        private static readonly Regex FamilyDeclaration = new Regex(@"font-family:\s*'([^']*)'", RegexOptions.Compiled);
        private static readonly Regex SourceFolder = new Regex(@"url\('([^/']+)/", RegexOptions.Compiled);

        public static IList<string> FindSystemShadows(string css, ISet<string> systemFamilies)
        {
            var shadows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(css) || systemFamilies == null || systemFamilies.Count == 0)
            {
                return shadows.ToList();
            }

            foreach (Match block in FontFaceBlock.Matches(css))
            {
                var family = FamilyDeclaration.Match(block.Value);
                var folder = SourceFolder.Match(block.Value);
                if (!family.Success) continue;
                var familyName = family.Groups[1].Value;
                var folderFamily = folder.Success
                    ? FontFaceNaming.FolderToFamily(folder.Groups[1].Value)
                    : string.Empty;
                if (systemFamilies.Contains(familyName) || systemFamilies.Contains(folderFamily))
                {
                    shadows.Add(familyName);
                }
            }

            return shadows.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static IList<string> FindHashMismatches(FontCatalog catalog, IDictionary<string, ISet<string>> systemFontHashes)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (systemFontHashes == null || systemFontHashes.Count == 0) return new List<string>();

            var mismatches = new List<string>();
            foreach (var family in catalog.Families)
            {
                if (family.Directory == null) continue;
                var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { family.Family };
                var expectedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var face in family.Faces)
                {
                    var path = System.IO.Path.Combine(family.Directory, face.File);
                    expectedHashes.Add(InstalledFontState.Sha256(path));
                    var legacyName = TrueTypeInfo.ReadLegacyFamilyName(path);
                    var typographicName = TrueTypeInfo.ReadName(path, 16);
                    if (!string.IsNullOrWhiteSpace(legacyName)) candidateNames.Add(legacyName);
                    if (!string.IsNullOrWhiteSpace(typographicName)) candidateNames.Add(typographicName);
                }

                var systemHashes = candidateNames
                    .Where(systemFontHashes.ContainsKey)
                    .SelectMany(name => systemFontHashes[name])
                    .Aggregate(
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        (hashes, hash) =>
                        {
                            hashes.Add(hash);
                            return hashes;
                        });
                if (systemHashes.Count > 0 && !expectedHashes.SetEquals(systemHashes))
                {
                    mismatches.Add(family.Family);
                }
            }

            return mismatches.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
