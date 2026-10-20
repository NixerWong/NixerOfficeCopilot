using System;
using System.Collections.Generic;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Analysis
{
    internal sealed class ExcelWorksheetMapCache
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, ExcelWorksheetListResult> _results =
            new Dictionary<string, ExcelWorksheetListResult>(StringComparer.OrdinalIgnoreCase);

        public bool TryGet(string workbookKey, out ExcelWorksheetListResult result)
        {
            result = null;
            if (string.IsNullOrWhiteSpace(workbookKey))
            {
                return false;
            }

            lock (_sync)
            {
                ExcelWorksheetListResult cached;
                if (!_results.TryGetValue(workbookKey, out cached))
                {
                    return false;
                }

                result = Clone(cached);
                return true;
            }
        }

        public void Store(string workbookKey, ExcelWorksheetListResult result)
        {
            if (string.IsNullOrWhiteSpace(workbookKey)
                || result == null
                || HasInspectionErrors(result))
            {
                return;
            }

            lock (_sync)
            {
                _results[workbookKey] = Clone(result);
            }
        }

        public void Invalidate(string workbookKey)
        {
            if (string.IsNullOrWhiteSpace(workbookKey))
            {
                return;
            }

            lock (_sync)
            {
                _results.Remove(workbookKey);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _results.Clear();
            }
        }

        private static bool HasInspectionErrors(ExcelWorksheetListResult result)
        {
            if (result.Worksheets == null)
            {
                return false;
            }

            foreach (var worksheet in result.Worksheets)
            {
                if (worksheet != null && !string.IsNullOrWhiteSpace(worksheet.InspectionError))
                {
                    return true;
                }
            }

            return false;
        }

        private static ExcelWorksheetListResult Clone(ExcelWorksheetListResult result)
        {
            var clone = new ExcelWorksheetListResult
            {
                WorkbookName = result.WorkbookName ?? string.Empty,
                TotalWorksheetCount = result.TotalWorksheetCount,
                IsTruncated = result.IsTruncated
            };

            if (result.WorksheetGroups != null)
            {
                foreach (var group in result.WorksheetGroups)
                {
                    clone.WorksheetGroups.Add(group == null
                        ? null
                        : new ExcelWorksheetHeaderGroup
                        {
                            GroupId = group.GroupId ?? string.Empty,
                            MinimumHeaderSimilarity = group.MinimumHeaderSimilarity,
                            WorksheetNames = group.WorksheetNames == null
                                ? new List<string>()
                                : new List<string>(group.WorksheetNames)
                        });
                }
            }

            if (result.SharedKeyCandidates != null)
            {
                foreach (var candidate in result.SharedKeyCandidates)
                {
                    clone.SharedKeyCandidates.Add(candidate == null
                        ? null
                        : new ExcelSharedKeyCandidate
                        {
                            ColumnName = candidate.ColumnName ?? string.Empty,
                            UniquenessVerified = candidate.UniquenessVerified,
                            WorksheetNames = candidate.WorksheetNames == null
                                ? new List<string>()
                                : new List<string>(candidate.WorksheetNames)
                        });
                }
            }

            if (result.Worksheets == null)
            {
                clone.Worksheets = null;
                return clone;
            }

            foreach (var worksheet in result.Worksheets)
            {
                if (worksheet == null)
                {
                    clone.Worksheets.Add(null);
                    continue;
                }

                clone.Worksheets.Add(new ExcelWorksheetInfo
                {
                    Name = worksheet.Name ?? string.Empty,
                    CodeName = worksheet.CodeName ?? string.Empty,
                    Index = worksheet.Index,
                    IsVisible = worksheet.IsVisible,
                    UsedRangeAddress = worksheet.UsedRangeAddress ?? string.Empty,
                    UsedRangeRows = worksheet.UsedRangeRows,
                    UsedRangeColumns = worksheet.UsedRangeColumns,
                    HeaderSignature = worksheet.HeaderSignature ?? string.Empty,
                    HeaderColumns = worksheet.HeaderColumns == null
                        ? new List<string>()
                        : new List<string>(worksheet.HeaderColumns),
                    HasFormulas = worksheet.HasFormulas,
                    IsProtected = worksheet.IsProtected,
                    RoleGuess = worksheet.RoleGuess ?? "other",
                    InspectionError = worksheet.InspectionError ?? string.Empty
                });
            }

            return clone;
        }
    }
}
