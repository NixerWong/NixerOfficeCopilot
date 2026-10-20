using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace OfficeCopilot.Host.Excel.Analysis
{
    internal sealed class ExcelVbaScopeSnapshot : IDisposable
    {
        private const long MaximumValueHashCellCount = 200000;

        private readonly Dictionary<string, ExcelVbaWorksheetSnapshot> _worksheets;
        private readonly List<string> _notes;
        private readonly List<IntPtr> _identityReferences;

        internal ExcelVbaScopeSnapshot(
            Dictionary<string, ExcelVbaWorksheetSnapshot> worksheets,
            List<string> notes)
            : this(worksheets, notes, new List<IntPtr>())
        {
        }

        private ExcelVbaScopeSnapshot(
            Dictionary<string, ExcelVbaWorksheetSnapshot> worksheets,
            List<string> notes,
            List<IntPtr> identityReferences)
        {
            _worksheets = worksheets;
            _notes = notes;
            _identityReferences = identityReferences;
        }

        public IList<string> Notes
        {
            get { return _notes; }
        }

        public static ExcelVbaScopeSnapshot Capture(
            ExcelInterop.Workbook workbook,
            IEnumerable<string> declaredTargetWorksheetNames,
            bool hashUntargetedValues)
        {
            if (workbook == null)
            {
                throw new ArgumentNullException(nameof(workbook));
            }

            var targets = new HashSet<string>(
                declaredTargetWorksheetNames ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var worksheets = new Dictionary<string, ExcelVbaWorksheetSnapshot>(
                StringComparer.OrdinalIgnoreCase);
            var notes = new List<string>();
            var identityReferences = new List<IntPtr>();
            object worksheetsObject = null;
            try
            {
                worksheetsObject = ((dynamic)workbook).Worksheets;
                var worksheetCount = Convert.ToInt32(((dynamic)worksheetsObject).Count);
                for (var index = 1; index <= worksheetCount; index++)
                {
                    object worksheetObject = null;
                    object usedRangeObject = null;
                    object listObjectsObject = null;
                    object rowsObject = null;
                    object columnsObject = null;
                    try
                    {
                        worksheetObject = ((dynamic)worksheetsObject).Item(index);
                        var worksheetName = Convert.ToString(((dynamic)worksheetObject).Name) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(worksheetName))
                        {
                            throw new InvalidOperationException("发现名称为空的工作表，无法建立可靠的范围基线。");
                        }

                        usedRangeObject = ((dynamic)worksheetObject).UsedRange;
                        var usedRange = (ExcelInterop.Range)usedRangeObject;
                        var usedRangeAddress = Convert.ToString(((dynamic)usedRange).Address) ?? string.Empty;
                        rowsObject = ((dynamic)usedRange).Rows;
                        columnsObject = ((dynamic)usedRange).Columns;
                        var rowCount = Convert.ToInt64(((dynamic)rowsObject).Count);
                        var columnCount = Convert.ToInt64(((dynamic)columnsObject).Count);
                        var snapshot = new ExcelVbaWorksheetSnapshot
                        {
                            Name = worksheetName,
                            CodeName = Convert.ToString(((dynamic)worksheetObject).CodeName) ?? string.Empty,
                            ComIdentity = GetComIdentity(worksheetObject, identityReferences),
                            UsedRangeAddress = usedRangeAddress,
                            ListObjectSignatures = ReadListObjectSignatures(worksheetObject, out listObjectsObject)
                        };

                        if (string.IsNullOrWhiteSpace(snapshot.CodeName))
                        {
                            notes.Add(
                                "工作表 \"" + worksheetName
                                + "\" 的 CodeName 基线为空；将使用 COM 对象身份区分首次赋值与工作表替换。");
                        }

                        var cellCount = rowCount * columnCount;
                        if (hashUntargetedValues && !targets.Contains(worksheetName))
                        {
                            if (cellCount <= MaximumValueHashCellCount)
                            {
                                snapshot.ValueAndFormulaHash = HashRange(usedRange);
                            }
                            else
                            {
                                notes.Add(
                                    "工作表 \"" + worksheetName + "\" 的 UsedRange 有 "
                                    + cellCount.ToString(CultureInfo.InvariantCulture)
                                    + " 个单元格，超过 200,000 上限；该表仅执行结构核对，未计算值/公式哈希。");
                            }
                        }

                        if (worksheets.ContainsKey(worksheetName))
                        {
                            throw new InvalidOperationException(
                                "工作簿中存在重复工作表名 \"" + worksheetName + "\"，无法建立可靠的范围基线。");
                        }

                        worksheets.Add(worksheetName, snapshot);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            "无法读取 VBA 范围核对所需的工作表元数据"
                            + (worksheetObject == null
                                ? "（序号 " + index.ToString(CultureInfo.InvariantCulture) + "）"
                                : " \"" + (Convert.ToString(((dynamic)worksheetObject).Name) ?? string.Empty) + "\"")
                            + "。", ex);
                    }
                    finally
                    {
                        ReleaseComObject(listObjectsObject);
                        ReleaseComObject(columnsObject);
                        ReleaseComObject(rowsObject);
                        ReleaseComObject(usedRangeObject);
                        ReleaseComObject(worksheetObject);
                    }
                }
            }
            catch
            {
                ReleaseIdentityReferences(identityReferences);
                throw;
            }
            finally
            {
                ReleaseComObject(worksheetsObject);
            }

            return new ExcelVbaScopeSnapshot(worksheets, notes, identityReferences);
        }

        public IList<string> FindOutOfScopeChanges(
            ExcelVbaScopeSnapshot after,
            IEnumerable<string> declaredTargetWorksheetNames,
            IEnumerable<string> declaredNewWorksheetNames)
        {
            if (after == null)
            {
                throw new ArgumentNullException(nameof(after));
            }

            var targets = new HashSet<string>(
                declaredTargetWorksheetNames ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var newTargets = new HashSet<string>(
                declaredNewWorksheetNames ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var changes = new List<string>();

            foreach (var beforeSheet in _worksheets.Values)
            {
                ExcelVbaWorksheetSnapshot afterSheet;
                if (!after._worksheets.TryGetValue(beforeSheet.Name, out afterSheet))
                {
                    changes.Add("工作表 \"" + beforeSheet.Name + "\" 已被删除或改名。");
                    continue;
                }

                var comIdentityChanged = beforeSheet.ComIdentity != 0
                    && afterSheet.ComIdentity != 0
                    && beforeSheet.ComIdentity != afterSheet.ComIdentity;
                var codeNameChanged = !string.Equals(
                    beforeSheet.CodeName,
                    afterSheet.CodeName,
                    StringComparison.OrdinalIgnoreCase);
                var isCodeNameInitializationForSameObject =
                    string.IsNullOrWhiteSpace(beforeSheet.CodeName)
                    && !string.IsNullOrWhiteSpace(afterSheet.CodeName)
                    && beforeSheet.ComIdentity != 0
                    && beforeSheet.ComIdentity == afterSheet.ComIdentity;
                if (comIdentityChanged
                    || (codeNameChanged
                        && !isCodeNameInitializationForSameObject))
                {
                    changes.Add(
                        "工作表 \"" + beforeSheet.Name + "\" 已被替换（CodeName 从 \""
                        + beforeSheet.CodeName + "\" 变为 \"" + afterSheet.CodeName + "\"）。");
                    continue;
                }

                if (targets.Contains(beforeSheet.Name))
                {
                    continue;
                }

                if (!string.Equals(
                    beforeSheet.UsedRangeAddress,
                    afterSheet.UsedRangeAddress,
                    StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(
                        "未声明目标工作表 \"" + beforeSheet.Name + "\" 的 UsedRange 从 "
                        + beforeSheet.UsedRangeAddress + " 变为 " + afterSheet.UsedRangeAddress + "。");
                }

                if (!beforeSheet.ListObjectSignatures.SequenceEqual(
                    afterSheet.ListObjectSignatures,
                    StringComparer.OrdinalIgnoreCase))
                {
                    changes.Add(
                        "未声明目标工作表 \"" + beforeSheet.Name
                        + "\" 的 Excel 表格名称或范围发生变化。");
                }

                if (beforeSheet.ValueAndFormulaHash != null
                    && afterSheet.ValueAndFormulaHash != null
                    && !string.Equals(
                        beforeSheet.ValueAndFormulaHash,
                        afterSheet.ValueAndFormulaHash,
                        StringComparison.Ordinal))
                {
                    changes.Add(
                        "未声明目标工作表 \"" + beforeSheet.Name + "\" 的单元格值或公式发生变化。");
                }
            }

            foreach (var afterSheet in after._worksheets.Values)
            {
                if (_worksheets.ContainsKey(afterSheet.Name))
                {
                    continue;
                }

                if (!targets.Contains(afterSheet.Name) || !newTargets.Contains(afterSheet.Name))
                {
                    changes.Add(
                        "出现未声明的新工作表 \"" + afterSheet.Name + "\"。");
                }
            }

            return changes;
        }

        public void Dispose()
        {
            ReleaseIdentityReferences(_identityReferences);
        }

        private static List<string> ReadListObjectSignatures(
            object worksheetObject,
            out object listObjectsObject)
        {
            listObjectsObject = null;
            var signatures = new List<string>();
            listObjectsObject = ((dynamic)worksheetObject).ListObjects;
            var count = Convert.ToInt32(((dynamic)listObjectsObject).Count);
            for (var index = 1; index <= count; index++)
            {
                object listObject = null;
                object rangeObject = null;
                try
                {
                    listObject = ((dynamic)listObjectsObject).Item(index);
                    rangeObject = ((dynamic)listObject).Range;
                    var name = Convert.ToString(((dynamic)listObject).Name) ?? string.Empty;
                    var address = Convert.ToString(((dynamic)rangeObject).Address) ?? string.Empty;
                    signatures.Add(name + "|" + address);
                }
                finally
                {
                    ReleaseComObject(rangeObject);
                    ReleaseComObject(listObject);
                }
            }

            signatures.Sort(StringComparer.OrdinalIgnoreCase);
            return signatures;
        }

        private static string HashRange(ExcelInterop.Range range)
        {
            using (var hash = SHA256.Create())
            {
                AppendHashText(hash, "values");
                AppendHashObject(hash, ((dynamic)range).Value2);
                AppendHashText(hash, "formulas");
                AppendHashObject(hash, ((dynamic)range).Formula);
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(hash.Hash).Replace("-", string.Empty);
            }
        }

        private static void AppendHashObject(HashAlgorithm hash, object value)
        {
            var array = value as Array;
            if (array == null)
            {
                AppendHashCell(hash, value);
                return;
            }

            AppendHashText(hash, "array:" + array.Rank.ToString(CultureInfo.InvariantCulture));
            if (array.Rank == 2)
            {
                for (var row = array.GetLowerBound(0); row <= array.GetUpperBound(0); row++)
                {
                    for (var column = array.GetLowerBound(1); column <= array.GetUpperBound(1); column++)
                    {
                        AppendHashCell(hash, array.GetValue(row, column));
                    }
                }

                return;
            }

            foreach (var item in array)
            {
                AppendHashCell(hash, item);
            }
        }

        private static void AppendHashCell(HashAlgorithm hash, object value)
        {
            if (value == null)
            {
                AppendHashText(hash, "<null>");
                return;
            }

            var formattable = value as IFormattable;
            var text = formattable == null
                ? value.ToString()
                : formattable.ToString(null, CultureInfo.InvariantCulture);
            AppendHashText(hash, value.GetType().FullName ?? value.GetType().Name);
            AppendHashText(hash, text ?? string.Empty);
        }

        private static void AppendHashText(HashAlgorithm hash, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            var length = BitConverter.GetBytes(bytes.Length);
            hash.TransformBlock(length, 0, length.Length, length, 0);
            if (bytes.Length > 0)
            {
                hash.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
            }
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.ReleaseComObject(value);
            }
        }

        private static long GetComIdentity(object value, ICollection<IntPtr> identityReferences)
        {
            var identity = Marshal.GetIUnknownForObject(value);
            try
            {
                identityReferences.Add(identity);
                return identity.ToInt64();
            }
            catch
            {
                Marshal.Release(identity);
                throw;
            }
        }

        private static void ReleaseIdentityReferences(IList<IntPtr> identityReferences)
        {
            if (identityReferences == null)
            {
                return;
            }

            foreach (var identity in identityReferences)
            {
                if (identity != IntPtr.Zero)
                {
                    Marshal.Release(identity);
                }
            }

            identityReferences.Clear();
        }
    }

    internal sealed class ExcelVbaWorksheetSnapshot
    {
        public string Name { get; set; }

        public string CodeName { get; set; }

        public long ComIdentity { get; set; }

        public string UsedRangeAddress { get; set; }

        public List<string> ListObjectSignatures { get; set; }

        public string ValueAndFormulaHash { get; set; }
    }
}
