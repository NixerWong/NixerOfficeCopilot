using System;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    internal static class ExcelToolRequestNormalizer
    {
        public static ExcelRangeReadRequest NormalizeRangeReadRequest(
            string argumentsJson,
            ExcelRangeReadRequest request)
        {
            request = request ?? new ExcelRangeReadRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);

            if (request.MaxRows <= 0)
            {
                request.MaxRows = 20;
            }

            if (request.MaxColumns <= 0)
            {
                request.MaxColumns = 10;
            }

            if (!HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = string.IsNullOrWhiteSpace(request.Address);
            }

            return request;
        }

        public static ExcelRangeRequest NormalizeRangeRequest(
            string argumentsJson,
            ExcelRangeRequest request)
        {
            request = request ?? new ExcelRangeRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);

            if (request.MaxRows <= 0)
            {
                request.MaxRows = 50;
            }

            if (request.MaxColumns <= 0)
            {
                request.MaxColumns = 20;
            }

            if (!HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = string.IsNullOrWhiteSpace(request.Address);
            }

            return request;
        }

        public static ExcelRangeWriteRequest NormalizeRangeWriteRequest(
            string argumentsJson,
            ExcelRangeWriteRequest request)
        {
            request = request ?? new ExcelRangeWriteRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);
            request.Value = request.Value ?? string.Empty;

            if (request.Values == null)
            {
                request.Values = new System.Collections.Generic.List<string[]>();
            }

            if (!HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = string.IsNullOrWhiteSpace(request.Address);
            }

            return request;
        }

        public static ExcelRangeFindRequest NormalizeRangeFindRequest(
            string argumentsJson,
            ExcelRangeFindRequest request)
        {
            request = request ?? new ExcelRangeFindRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);
            request.SearchValue = NormalizeText(request.SearchValue);

            if (request.MaxResults <= 0)
            {
                request.MaxResults = 100;
            }

            if (!HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = string.IsNullOrWhiteSpace(request.Address);
            }

            return request;
        }

        public static ExcelRangeFormulaValidationRequest NormalizeRangeFormulaValidationRequest(
            string argumentsJson,
            ExcelRangeFormulaValidationRequest request)
        {
            request = request ?? new ExcelRangeFormulaValidationRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);

            if (request.MaxRows <= 0)
            {
                request.MaxRows = 100;
            }

            if (request.MaxColumns <= 0)
            {
                request.MaxColumns = 30;
            }

            if (!HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = string.IsNullOrWhiteSpace(request.Address);
            }

            return request;
        }

        public static ExcelAnalyzeRangeRequest NormalizeAnalyzeRangeRequest(
            string argumentsJson,
            ExcelAnalyzeRangeRequest request)
        {
            request = request ?? new ExcelAnalyzeRangeRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);
            request.Operation = NormalizeText(request.Operation);
            request.Function = NormalizeText(request.Function);
            request.ReturnAddress = NormalizeText(request.ReturnAddress);

            if (string.IsNullOrWhiteSpace(request.Operation))
            {
                request.Operation = "aggregate";
            }

            if (string.IsNullOrWhiteSpace(request.Function))
            {
                request.Function = "max";
            }

            if (!HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = string.IsNullOrWhiteSpace(request.Address);
            }

            return request;
        }

        public static ExcelWorksheetDataRequest NormalizeWorksheetDataRequest(
            string argumentsJson,
            ExcelWorksheetDataRequest request)
        {
            request = request ?? new ExcelWorksheetDataRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);

            if (request.MaxRows <= 0)
            {
                request.MaxRows = 50;
            }

            if (request.MaxColumns <= 0)
            {
                request.MaxColumns = 20;
            }

            if (!HasJsonProperty(argumentsJson, "useUsedRangeWhenAddressEmpty"))
            {
                request.UseUsedRangeWhenAddressEmpty = true;
            }

            return request;
        }

        public static ExcelWorksheetScreenshotRequest NormalizeWorksheetScreenshotRequest(
            string argumentsJson,
            ExcelWorksheetScreenshotRequest request)
        {
            request = request ?? new ExcelWorksheetScreenshotRequest();
            request.WorksheetName = NormalizeText(request.WorksheetName);
            request.Address = NormalizeText(request.Address);
            request.ImageFileName = NormalizeText(request.ImageFileName);

            if (request.MaxImageWidth <= 0)
            {
                request.MaxImageWidth = 1600;
            }

            if (request.MaxImageHeight <= 0)
            {
                request.MaxImageHeight = 1200;
            }

            if (!HasJsonProperty(argumentsJson, "useSelectionWhenAddressEmpty"))
            {
                request.UseSelectionWhenAddressEmpty = false;
            }

            if (!HasJsonProperty(argumentsJson, "useUsedRangeWhenAddressEmpty"))
            {
                request.UseUsedRangeWhenAddressEmpty = true;
            }

            return request;
        }

        private static string NormalizeText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static bool HasJsonProperty(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            return json.IndexOf(
                "\"" + propertyName + "\"",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}