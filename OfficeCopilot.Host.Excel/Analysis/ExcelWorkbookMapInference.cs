using System;
using System.Collections.Generic;
using System.Linq;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Analysis
{
    internal static class ExcelWorkbookMapInference
    {
        private const decimal MinimumHeaderSimilarity = 0.8m;

        public static void Populate(ExcelWorksheetListResult workbookMap)
        {
            if (workbookMap == null)
            {
                throw new ArgumentNullException(nameof(workbookMap));
            }

            workbookMap.WorksheetGroups = new List<ExcelWorksheetHeaderGroup>();
            workbookMap.SharedKeyCandidates = new List<ExcelSharedKeyCandidate>();

            var worksheets = (workbookMap.Worksheets ?? new List<ExcelWorksheetInfo>())
                .Where(worksheet =>
                    worksheet != null
                    && string.IsNullOrWhiteSpace(worksheet.InspectionError)
                    && !string.IsNullOrWhiteSpace(worksheet.Name))
                .Select(worksheet => new WorksheetHeaders(worksheet, GetHeaders(worksheet)))
                .Where(item => item.Headers.Count > 0)
                .OrderBy(item => item.Worksheet.Index)
                .ToList();

            PopulateSimilarGroups(workbookMap, worksheets);
            PopulateSharedKeyCandidates(workbookMap, worksheets);
        }

        private static void PopulateSimilarGroups(
            ExcelWorksheetListResult workbookMap,
            IList<WorksheetHeaders> worksheets)
        {
            var grouped = new HashSet<ExcelWorksheetInfo>();
            var groupNumber = 0;
            foreach (var seed in worksheets)
            {
                if (grouped.Contains(seed.Worksheet))
                {
                    continue;
                }

                var members = new List<WorksheetHeaders> { seed };
                foreach (var candidate in worksheets)
                {
                    if (ReferenceEquals(candidate.Worksheet, seed.Worksheet)
                        || grouped.Contains(candidate.Worksheet))
                    {
                        continue;
                    }

                    if (members.All(member =>
                        GetSimilarity(member.Headers, candidate.Headers) >= MinimumHeaderSimilarity))
                    {
                        members.Add(candidate);
                    }
                }

                if (members.Count < 2)
                {
                    continue;
                }

                foreach (var member in members)
                {
                    grouped.Add(member.Worksheet);
                }

                var minimumSimilarity = 1m;
                for (var left = 0; left < members.Count; left++)
                {
                    for (var right = left + 1; right < members.Count; right++)
                    {
                        minimumSimilarity = Math.Min(
                            minimumSimilarity,
                            GetSimilarity(members[left].Headers, members[right].Headers));
                    }
                }

                groupNumber++;
                workbookMap.WorksheetGroups.Add(new ExcelWorksheetHeaderGroup
                {
                    GroupId = "header-group-" + groupNumber,
                    MinimumHeaderSimilarity = minimumSimilarity,
                    WorksheetNames = members
                        .Select(member => member.Worksheet.Name)
                        .ToList()
                });
            }
        }

        private static void PopulateSharedKeyCandidates(
            ExcelWorksheetListResult workbookMap,
            IList<WorksheetHeaders> worksheets)
        {
            var worksheetsByHeader = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var item in worksheets)
            {
                foreach (var header in item.Headers)
                {
                    List<string> worksheetNames;
                    if (!worksheetsByHeader.TryGetValue(header, out worksheetNames))
                    {
                        worksheetNames = new List<string>();
                        worksheetsByHeader.Add(header, worksheetNames);
                    }

                    if (!worksheetNames.Contains(item.Worksheet.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        worksheetNames.Add(item.Worksheet.Name);
                    }
                }
            }

            workbookMap.SharedKeyCandidates = worksheetsByHeader
                .Where(item => item.Value.Count > 1)
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new ExcelSharedKeyCandidate
                {
                    ColumnName = item.Key,
                    WorksheetNames = item.Value.OrderBy(
                        name => name,
                        StringComparer.OrdinalIgnoreCase).ToList(),
                    UniquenessVerified = false
                })
                .ToList();
        }

        private static HashSet<string> GetHeaders(ExcelWorksheetInfo worksheet)
        {
            var columns = worksheet.HeaderColumns;
            if (columns == null || columns.Count == 0)
            {
                columns = (worksheet.HeaderSignature ?? string.Empty)
                    .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(header => header.Trim())
                    .ToList();
            }

            return new HashSet<string>(
                columns
                    .Where(header => !string.IsNullOrWhiteSpace(header))
                    .Select(header => header.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        private static decimal GetSimilarity(
            ISet<string> leftHeaders,
            ISet<string> rightHeaders)
        {
            var intersectionCount = leftHeaders.Count(rightHeaders.Contains);
            var unionCount = leftHeaders.Count + rightHeaders.Count - intersectionCount;
            return unionCount == 0
                ? 0m
                : (decimal)intersectionCount / unionCount;
        }

        private sealed class WorksheetHeaders
        {
            public WorksheetHeaders(ExcelWorksheetInfo worksheet, HashSet<string> headers)
            {
                Worksheet = worksheet;
                Headers = headers;
            }

            public ExcelWorksheetInfo Worksheet { get; private set; }

            public HashSet<string> Headers { get; private set; }
        }
    }
}
