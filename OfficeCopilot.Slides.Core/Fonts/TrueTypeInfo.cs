using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace OfficeCopilot.Slides.Fonts
{
    /// <summary>name 表中的一条 Windows（平台 3）名称记录。</summary>
    public sealed class TrueTypeNameRecord
    {
        public int NameId { get; set; }
        public int LanguageId { get; set; }
        public string Value { get; set; }
        public int FaceIndex { get; set; }
    }

    public sealed class UnicodeCoverageRange
    {
        [JsonProperty("start")]
        public int Start { get; set; }

        [JsonProperty("end")]
        public int End { get; set; }
    }

    /// <summary>
    /// 读取 TrueType 文件的最少信息：轮廓类型、OS/2.fsType 嵌入许可位与 name 表中的旧式族名。
    /// </summary>
    public static class TrueTypeInfo
    {
        private const int LegacyFamilyNameId = 1;
        private const int EnglishUnitedStates = 0x0409;

        private static readonly ConcurrentDictionary<string, string> LegacyNameCache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, IReadOnlyList<UnicodeCoverageRange>> UnicodeCoverageCache =
            new ConcurrentDictionary<string, IReadOnlyList<UnicodeCoverageRange>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 读取 name ID 1（旧式族名）。PowerPoint/GDI 用它识别非 RIBBI 字重，例如 "Albert Sans Medium"。
        /// 读取失败返回 null。
        /// </summary>
        public static string ReadLegacyFamilyName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            return LegacyNameCache.GetOrAdd(path, ReadLegacyFamilyNameUncached);
        }

        private static string ReadLegacyFamilyNameUncached(string path)
        {
            try
            {
                return ReadNameRecord(File.ReadAllBytes(path), LegacyFamilyNameId);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return null;
        }

        /// <summary>读取文件 name 表中所有 Windows Unicode 名称记录（平台 3），用于判断名称是否已被其他字体占用。</summary>
        public static IList<TrueTypeNameRecord> ReadNameRecords(string path)
        {
            var result = new List<TrueTypeNameRecord>();
            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    var signature = ReadUInt32(br);
                    if (signature == 0x74746366)
                    {
                        ReadUInt32(br);
                        var fontCount = ReadUInt32(br);
                        if (fontCount > int.MaxValue || fontCount > (fs.Length - fs.Position) / 4)
                        {
                            return result;
                        }

                        var faceOffsets = new List<uint>((int)fontCount);
                        for (var faceIndex = 0; faceIndex < (int)fontCount; faceIndex++)
                        {
                            faceOffsets.Add(ReadUInt32(br));
                        }

                        for (var faceIndex = 0; faceIndex < faceOffsets.Count; faceIndex++)
                        {
                            ReadNameRecordsForFace(br, faceOffsets[faceIndex], faceIndex, result);
                        }
                    }
                    else
                    {
                        ReadNameRecordsForFace(br, 0, 0, result, signature);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (IndexOutOfRangeException)
            {
            }

            return result;
        }

        private static void ReadNameRecordsForFace(
            BinaryReader br,
            long faceOffset,
            int faceIndex,
            IList<TrueTypeNameRecord> result,
            uint? knownSignature = null)
        {
            var stream = br.BaseStream;
            stream.Position = faceOffset;
            if (!knownSignature.HasValue)
            {
                ReadUInt32(br);
            }
            else
            {
                stream.Position += 4;
            }

            var numTables = ReadUInt16(br);
            br.ReadBytes(6);
            long nameOffset = -1;
            long nameLength = 0;
            for (var index = 0; index < numTables; index++)
            {
                var tag = new string(br.ReadChars(4));
                ReadUInt32(br);
                var offset = ReadUInt32(br);
                var length = ReadUInt32(br);
                if (tag == "name")
                {
                    nameOffset = offset;
                    nameLength = length;
                }
            }

            if (nameOffset < 0 || nameOffset > stream.Length || nameLength > stream.Length - nameOffset)
            {
                return;
            }

            stream.Position = nameOffset;
            var table = br.ReadBytes((int)Math.Min(nameLength, 1 << 20));
            if (table.Length < 6)
            {
                return;
            }

            var count = U16(table, 2);
            var storage = U16(table, 4);
            for (var index = 0; index < count; index++)
            {
                var entry = 6 + 12 * index;
                if (entry + 12 > table.Length || U16(table, entry) != 3)
                {
                    continue;
                }

                var length = U16(table, entry + 8);
                var valueOffset = storage + U16(table, entry + 10);
                if (valueOffset + length > table.Length)
                {
                    continue;
                }

                result.Add(new TrueTypeNameRecord
                {
                    NameId = U16(table, entry + 6),
                    LanguageId = U16(table, entry + 4),
                    Value = Encoding.BigEndianUnicode.GetString(table, valueOffset, length),
                    FaceIndex = faceIndex
                });
            }
        }

        /// <summary>读取 Unicode cmap format 4/12 中实际映射到非零字形的码点区间。</summary>
        public static IReadOnlyList<UnicodeCoverageRange> ReadUnicodeCoverage(string path)
        {
            return ReadUnicodeCoverage(path, 0);
        }

        /// <summary>读取 TrueType Collection 指定字面的 Unicode cmap 覆盖。</summary>
        public static IReadOnlyList<UnicodeCoverageRange> ReadUnicodeCoverage(string path, int faceIndex)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("字体路径不能为空。", nameof(path));
            if (faceIndex < 0) throw new ArgumentOutOfRangeException(nameof(faceIndex));
            var fullPath = Path.GetFullPath(path);
            var cacheKey = fullPath + "|" + faceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return UnicodeCoverageCache.GetOrAdd(cacheKey, ignored => ReadUnicodeCoverageUncached(fullPath, faceIndex));
        }

        private static IReadOnlyList<UnicodeCoverageRange> ReadUnicodeCoverageUncached(string path, int faceIndex)
        {
            byte[] cmap = null;
            using (var stream = File.OpenRead(path))
            {
                long faceOffset = 0;
                var signature = ReadAt(stream, 0, 4);
                if (Encoding.ASCII.GetString(signature) == "ttcf")
                {
                    var collectionHeader = ReadAt(stream, 0, 12);
                    var faceCount = U32(collectionHeader, 8);
                    if (faceCount > int.MaxValue || (uint)faceIndex >= faceCount)
                    {
                        throw new InvalidDataException("字体集合字面索引超出范围：" + path);
                    }

                    faceOffset = U32(ReadAt(stream, 12 + 4L * faceIndex, 4), 0);
                }
                else if (faceIndex != 0)
                {
                    throw new InvalidDataException("独立字体文件只包含字面 0：" + path);
                }

                var header = ReadAt(stream, faceOffset, 12);
                var tableCount = U16(header, 4);
                for (var index = 0; index < tableCount; index++)
                {
                    var record = ReadAt(stream, faceOffset + 12 + 16L * index, 16);
                    if (Encoding.ASCII.GetString(record, 0, 4) != "cmap") continue;

                    var offset = U32(record, 8);
                    var length = U32(record, 12);
                    if (length > int.MaxValue || (ulong)offset + length > (ulong)stream.Length)
                    {
                        throw new InvalidDataException("字体 cmap 表超出文件范围：" + path);
                    }

                    cmap = ReadAt(stream, offset, (int)length);
                    break;
                }
            }

            if (cmap == null || cmap.Length < 4 || U16(cmap, 0) != 0)
            {
                throw new InvalidDataException("字体缺少有效的 Unicode cmap 表：" + path);
            }

            var encodingCount = U16(cmap, 2);
            var subtables = new List<Tuple<int, int>>();
            for (var index = 0; index < encodingCount; index++)
            {
                var recordOffset = 4 + 8 * index;
                RequireRange(cmap, recordOffset, 8);
                var platform = U16(cmap, recordOffset);
                var encoding = U16(cmap, recordOffset + 2);
                if (platform != 0 && !(platform == 3 && (encoding == 1 || encoding == 10))) continue;

                var offset = U32(cmap, recordOffset + 4);
                if (offset > int.MaxValue) continue;
                var subtableOffset = (int)offset;
                RequireRange(cmap, subtableOffset, 2);
                var format = U16(cmap, subtableOffset);
                if (format == 4 || format == 12)
                {
                    subtables.Add(Tuple.Create(format, subtableOffset));
                }
            }

            if (subtables.Count == 0)
            {
                throw new InvalidDataException("字体没有 format 4 或 format 12 的 Unicode cmap：" + path);
            }

            var ranges = new List<UnicodeCoverageRange>();
            foreach (var subtable in subtables.OrderByDescending(item => item.Item1))
            {
                if (subtable.Item1 == 12)
                {
                    ReadFormat12Coverage(cmap, subtable.Item2, ranges);
                }
                else
                {
                    ReadFormat4Coverage(cmap, subtable.Item2, ranges);
                }
            }

            var merged = MergeCoverageRanges(ranges);
            if (merged.Count == 0)
            {
                throw new InvalidDataException("字体 Unicode cmap 没有可用字符映射：" + path);
            }

            return new ReadOnlyCollection<UnicodeCoverageRange>(merged);
        }

        private static void ReadFormat12Coverage(byte[] cmap, int offset, IList<UnicodeCoverageRange> ranges)
        {
            RequireRange(cmap, offset, 16);
            var length = U32(cmap, offset + 4);
            var groupCount = U32(cmap, offset + 12);
            if (length < 16 || offset + (long)length > cmap.Length || groupCount > (length - 16) / 12)
            {
                throw new InvalidDataException("Unicode cmap format 12 结构无效。");
            }

            for (var index = 0; index < groupCount; index++)
            {
                var groupOffset = offset + 16 + 12 * index;
                var start = U32(cmap, groupOffset);
                var end = U32(cmap, groupOffset + 4);
                var startGlyph = U32(cmap, groupOffset + 8);
                if (start > end || end > 0x10FFFF || startGlyph > int.MaxValue)
                {
                    throw new InvalidDataException("Unicode cmap format 12 字符区间无效。");
                }

                if (startGlyph == 0) start++;
                AddCoverageRange(ranges, (int)start, (int)end);
            }
        }

        private static void ReadFormat4Coverage(byte[] cmap, int offset, IList<UnicodeCoverageRange> ranges)
        {
            RequireRange(cmap, offset, 14);
            var length = U16(cmap, offset + 2);
            var segmentCount = U16(cmap, offset + 6) / 2;
            if (length < 16 || (long)offset + length > cmap.Length || segmentCount == 0)
            {
                throw new InvalidDataException("Unicode cmap format 4 结构无效。");
            }

            var endCodes = offset + 14;
            var startCodes = endCodes + 2 * segmentCount + 2;
            var deltas = startCodes + 2 * segmentCount;
            var rangeOffsets = deltas + 2 * segmentCount;
            RequireRange(cmap, rangeOffsets, 2 * segmentCount);
            if (rangeOffsets + 2 * segmentCount > offset + length)
            {
                throw new InvalidDataException("Unicode cmap format 4 段区间越界。");
            }

            for (var segment = 0; segment < segmentCount; segment++)
            {
                var start = U16(cmap, startCodes + 2 * segment);
                var end = U16(cmap, endCodes + 2 * segment);
                var delta = unchecked((short)U16(cmap, deltas + 2 * segment));
                var rangeWord = rangeOffsets + 2 * segment;
                var rangeOffset = U16(cmap, rangeWord);
                if (start > end) throw new InvalidDataException("Unicode cmap format 4 字符段无效。");

                var runStart = -1;
                for (var codePoint = start; codePoint <= end && codePoint < 0xFFFF; codePoint++)
                {
                    int glyph;
                    if (rangeOffset == 0)
                    {
                        glyph = (codePoint + delta) & 0xFFFF;
                    }
                    else
                    {
                        var glyphAddress = rangeWord + rangeOffset + 2 * (codePoint - start);
                        if (glyphAddress < offset || glyphAddress + 2 > offset + length)
                        {
                            throw new InvalidDataException("Unicode cmap format 4 glyph 区间越界。");
                        }

                        glyph = U16(cmap, glyphAddress);
                        if (glyph != 0) glyph = (glyph + delta) & 0xFFFF;
                    }

                    if (glyph != 0)
                    {
                        if (runStart < 0) runStart = codePoint;
                    }
                    else if (runStart >= 0)
                    {
                        AddCoverageRange(ranges, runStart, codePoint - 1);
                        runStart = -1;
                    }
                }

                if (runStart >= 0)
                {
                    AddCoverageRange(ranges, runStart, Math.Min(end, 0xFFFE));
                }
            }
        }

        private static void AddCoverageRange(IList<UnicodeCoverageRange> ranges, int start, int end)
        {
            if (start > end || start > 0x10FFFF || end < 0) return;
            start = Math.Max(0, start);
            end = Math.Min(0x10FFFF, end);
            if (start <= 0xD7FF)
            {
                ranges.Add(new UnicodeCoverageRange { Start = start, End = Math.Min(end, 0xD7FF) });
            }
            if (end >= 0xE000)
            {
                ranges.Add(new UnicodeCoverageRange { Start = Math.Max(start, 0xE000), End = end });
            }
        }

        private static List<UnicodeCoverageRange> MergeCoverageRanges(IEnumerable<UnicodeCoverageRange> ranges)
        {
            var sorted = ranges
                .Where(range => range.Start <= range.End)
                .OrderBy(range => range.Start)
                .ThenBy(range => range.End)
                .ToList();
            var merged = new List<UnicodeCoverageRange>();
            foreach (var range in sorted)
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

        private static byte[] ReadAt(FileStream stream, long offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > stream.Length)
            {
                throw new InvalidDataException("字体表数据超出文件范围。");
            }

            var bytes = new byte[count];
            stream.Position = offset;
            var read = 0;
            while (read < count)
            {
                var current = stream.Read(bytes, read, count - read);
                if (current == 0) throw new EndOfStreamException("字体表数据被截断。");
                read += current;
            }

            return bytes;
        }

        private static void RequireRange(byte[] data, int offset, int count)
        {
            if (offset < 0 || count < 0 || (long)offset + count > data.Length)
            {
                throw new InvalidDataException("字体 cmap 子表数据越界。");
            }
        }

        /// <summary>读取指定 name ID 的值：优先 en-US，其次任一语言；没有则返回 null。</summary>
        public static string ReadName(string path, int nameId)
        {
            var records = ReadNameRecords(path).Where(r => r.NameId == nameId).ToList();
            var english = records.FirstOrDefault(r => r.LanguageId == EnglishUnitedStates);
            return (english ?? records.FirstOrDefault())?.Value;
        }

        private static string ReadNameRecord(byte[] data, int nameId)
        {
            try
            {
                var numTables = U16(data, 4);
                for (var i = 0; i < numTables; i++)
                {
                    var record = 12 + 16 * i;
                    if (Encoding.ASCII.GetString(data, record, 4) != "name")
                    {
                        continue;
                    }

                    var table = (int)U32(data, record + 8);
                    var count = U16(data, table + 2);
                    var storage = table + U16(data, table + 4);
                    string fallback = null;
                    for (var r = 0; r < count; r++)
                    {
                        var entry = table + 6 + 12 * r;
                        if (U16(data, entry + 6) != nameId || U16(data, entry) != 3)
                        {
                            continue;
                        }

                        var value = Encoding.BigEndianUnicode.GetString(
                            data, storage + U16(data, entry + 10), U16(data, entry + 8));
                        if (U16(data, entry + 4) == EnglishUnitedStates)
                        {
                            return value;
                        }

                        fallback = fallback ?? value;
                    }

                    return fallback;
                }
            }
            catch (ArgumentException)
            {
            }
            catch (IndexOutOfRangeException)
            {
            }

            return null;
        }

        private static int U16(byte[] data, int offset)
        {
            return (data[offset] << 8) | data[offset + 1];
        }

        private static uint U32(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
        }

        /// <summary>
        /// 嵌入许可描述："installable" / "editable" / "preview-print" / "restricted" / "unknown"。
        /// </summary>
        public static string ReadEmbedding(string path, out bool isTrueTypeOutline)
        {
            isTrueTypeOutline = false;
            try
            {
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    var sfnt = ReadUInt32(br);
                    isTrueTypeOutline = sfnt == 0x00010000 || sfnt == 0x74727565; // 1.0 或 'true'；'OTTO' 为 CFF。
                    var numTables = ReadUInt16(br);
                    br.ReadBytes(6);
                    for (var i = 0; i < numTables; i++)
                    {
                        var tag = new string(br.ReadChars(4));
                        ReadUInt32(br);
                        var offset = ReadUInt32(br);
                        ReadUInt32(br);
                        if (tag != "OS/2")
                        {
                            continue;
                        }

                        fs.Position = offset + 8;
                        var fsType = ReadUInt16(br);
                        return DescribeFsType(fsType);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return "unknown";
        }

        public static string DescribeFsType(int fsType)
        {
            if ((fsType & 0x0008) != 0) return "editable";
            if ((fsType & 0x0004) != 0) return "preview-print";
            if ((fsType & 0x0002) != 0) return "restricted";
            return "installable";
        }

        public static bool IsEmbeddable(string embedding)
        {
            return embedding == "installable" || embedding == "editable" || embedding == "preview-print";
        }

        private static uint ReadUInt32(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
        }

        private static int ReadUInt16(BinaryReader br)
        {
            var b = br.ReadBytes(2);
            return b[0] << 8 | b[1];
        }
    }
}
