using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeCopilot.Slides.Deck;
using OfficeCopilot.Slides.Fonts;

namespace OfficeCopilot.Host.PowerPoint
{
    internal static class PowerPointFontIdentityResolver
    {
        internal static IList<string> FindMismatches(
            FontCatalog catalog,
            IEnumerable<SlideBuildPlan> plans,
            IDictionary<string, ISet<string>> registeredHashesByFamily = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (plans == null) throw new ArgumentNullException(nameof(plans));

            var plannedNames = new HashSet<string>(
                PowerPointFontInstaller.CollectFamilyNames(plans)
                    .Select(InstalledFontState.NormalizeFontName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
                StringComparer.OrdinalIgnoreCase);
            var usedFamilies = catalog.Families
                .Where(family => GetFontNames(family).Any(plannedNames.Contains))
                .ToList();
            if (usedFamilies.Count == 0)
            {
                return new List<string>();
            }

            if (registeredHashesByFamily == null)
            {
                var registryLookupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var family in usedFamilies)
                {
                    registryLookupNames.UnionWith(GetFontNames(family));
                }

                registeredHashesByFamily = InstalledFontState.SystemFontHashesByFamily(registryLookupNames);
            }

            var mismatches = new List<string>();
            foreach (var family in usedFamilies)
            {
                var expectedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var missingFiles = new List<string>();
                foreach (var face in family.Faces)
                {
                    var path = Path.Combine(family.Directory ?? string.Empty, face.File ?? string.Empty);
                    if (!File.Exists(path))
                    {
                        missingFiles.Add(face.File ?? "(unknown face)");
                        continue;
                    }

                    expectedHashes.Add(InstalledFontState.Sha256(path));
                }

                var registeredHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in GetFontNames(family))
                {
                    ISet<string> hashes;
                    if (registeredHashesByFamily.TryGetValue(name, out hashes) && hashes != null)
                    {
                        registeredHashes.UnionWith(hashes);
                    }
                }

                if (missingFiles.Count > 0
                    || expectedHashes.Count == 0
                    || !expectedHashes.IsSubsetOf(registeredHashes))
                {
                    mismatches.Add(
                        family.Family
                        + "（打包字面 " + expectedHashes.Count
                        + " 个 hash，注册字典 " + registeredHashes.Count
                        + " 个 hash"
                        + (missingFiles.Count == 0 ? string.Empty : "，缺少文件 " + string.Join("、", missingFiles))
                        + "）");
                }
            }

            return mismatches.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static ISet<string> GetFontNames(FontFamilyEntry family)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddName(names, family.Family);
            foreach (var face in family.Faces)
            {
                AddName(names, face.PptName);
                AddName(names, face.PptFarEastName);
                if (family.Directory == null || string.IsNullOrWhiteSpace(face.File))
                {
                    continue;
                }

                var path = Path.Combine(family.Directory, face.File);
                if (!File.Exists(path))
                {
                    continue;
                }

                var legacyFamily = TrueTypeInfo.ReadName(path, 1);
                var legacySubfamily = TrueTypeInfo.ReadName(path, 2);
                var typographicFamily = TrueTypeInfo.ReadName(path, 16);
                var typographicSubfamily = TrueTypeInfo.ReadName(path, 17);
                AddName(names, legacyFamily);
                AddName(names, typographicFamily);
                AddFaceName(names, legacyFamily, legacySubfamily);
                AddFaceName(names, typographicFamily, typographicSubfamily);
            }

            return names;
        }

        private static void AddName(ISet<string> names, string name)
        {
            var normalized = InstalledFontState.NormalizeFontName(name);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                names.Add(normalized);
            }
        }

        private static void AddFaceName(ISet<string> names, string family, string subfamily)
        {
            if (!string.IsNullOrWhiteSpace(family) && !string.IsNullOrWhiteSpace(subfamily))
            {
                AddName(names, family + " " + subfamily);
            }
        }
    }
}
