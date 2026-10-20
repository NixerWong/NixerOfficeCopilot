using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OfficeCopilot.Slides.Fonts
{
    /// <summary>字面在本机的供给方式。</summary>
    public enum FontFaceProvision
    {
        /// <summary>本机已有同名族：HTML 预览与 PPT 都使用系统字体，不加载随包文件。</summary>
        SystemFamily,

        /// <summary>本机已注册与随包文件字节一致的字面（按 SHA-256 识别）。</summary>
        Installed,

        /// <summary>本机缺失，构建前需要安装随包文件。</summary>
        Pending
    }

    public sealed class FontResolutionRow
    {
        public string Family { get; set; }

        public string Folder { get; set; }

        public string File { get; set; }

        public int Weight { get; set; }

        public string Style { get; set; }

        /// <summary>PowerPoint 中应写入的字体名（随包文件的旧式族名）。</summary>
        public string PptName { get; set; }

        public FontFaceProvision Provision { get; set; }
    }

    /// <summary>
    /// 构建前为每个随包字面确定 HTML 与 PPT 使用的字体来源，避免预览与 PPTX 使用不同字形。
    /// </summary>
    public sealed class FontResolutionTable
    {
        private readonly IList<FontResolutionRow> _rows;

        private FontResolutionTable(IList<FontResolutionRow> rows, ISet<string> systemFamilies)
        {
            _rows = rows;
            SystemFamilies = systemFamilies;
        }

        public IReadOnlyList<FontResolutionRow> Rows
        {
            get { return (IReadOnlyList<FontResolutionRow>)_rows; }
        }

        /// <summary>本机可见的全部系统字体族名。</summary>
        public ISet<string> SystemFamilies { get; private set; }

        public static ISet<long> BundledSizes(FontCatalog catalog)
        {
            var sizes = new HashSet<long>();
            foreach (var family in catalog.Families)
            {
                foreach (var face in family.Faces)
                {
                    var path = Path.Combine(family.Directory, face.File);
                    if (File.Exists(path))
                    {
                        sizes.Add(new FileInfo(path).Length);
                    }
                }
            }

            return sizes;
        }        public static FontResolutionTable Build(FontCatalog catalog, ISet<string> systemFamilies, ISet<string> installedHashes)
        {
            systemFamilies = systemFamilies ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            installedHashes = installedHashes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<FontResolutionRow>();
            foreach (var family in catalog.Families)
            {
                var systemHas = systemFamilies.Contains(family.Family);
                foreach (var face in family.Faces)
                {
                    var path = Path.Combine(family.Directory, face.File);
                    var pptName = TrueTypeInfo.ReadLegacyFamilyName(path);
                    if (string.IsNullOrWhiteSpace(pptName))
                    {
                        pptName = family.Family;
                    }

                    FontFaceProvision provision;
                    if (systemHas)
                    {
                        provision = FontFaceProvision.SystemFamily;
                    }
                    else if (installedHashes.Contains(InstalledFontState.Sha256(path)))
                    {
                        provision = FontFaceProvision.Installed;
                    }
                    else
                    {
                        provision = FontFaceProvision.Pending;
                    }

                    rows.Add(new FontResolutionRow
                    {
                        Family = family.Family,
                        Folder = family.Folder,
                        File = face.File,
                        Weight = face.Weight,
                        Style = face.Style,
                        PptName = pptName,
                        Provision = provision
                    });
                }
            }

            return new FontResolutionTable(rows, systemFamilies);
        }

        /// <summary>族的每个字面都已由系统或已安装文件提供，无需再安装随包文件。</summary>
        public bool IsProvided(string family)
        {
            var faces = _rows.Where(r => string.Equals(r.Family, family, StringComparison.OrdinalIgnoreCase)).ToList();
            return faces.Count > 0 && faces.All(r => r.Provision != FontFaceProvision.Pending);
        }
    }
}
