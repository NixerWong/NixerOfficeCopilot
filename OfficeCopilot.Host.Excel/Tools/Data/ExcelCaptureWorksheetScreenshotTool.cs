using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OfficeCopilot.Agent.Abstractions;
using OfficeCopilot.Host.Abstractions;

namespace OfficeCopilot.Host.Excel.Tools
{
    public sealed class ExcelCaptureWorksheetScreenshotTool : ExcelToolBase
    {
        private static readonly ToolDefinition _definition = new ToolDefinition
        {
            Name = "excel_capture_worksheet_screenshot",
            DisplayName = "捕获工作表截图",
            Description = "将指定区域、当前选区或工作表已用区域渲染为 PNG 截图，支持传入图片文件名并落盘，便于 AI 模型理解表格结构。",
            SourceKind = ToolSourceKind.BuiltIn,
            SourceName = "Excel",
            IsReadOnly = true,
            RequiresConfirmation = false,
            InputSchemaJson = "{"
                + "\"type\":\"object\","
                + "\"properties\":{"
                + "\"worksheetName\":{\"type\":\"string\"},"
                + "\"address\":{\"type\":\"string\"},"
                + "\"imageFileName\":{\"type\":\"string\"},"
                + "\"useSelectionWhenAddressEmpty\":{\"type\":\"boolean\"},"
                + "\"useUsedRangeWhenAddressEmpty\":{\"type\":\"boolean\",\"default\":true},"
                + "\"maxImageWidth\":{\"type\":\"integer\"},"
                + "\"maxImageHeight\":{\"type\":\"integer\"}"
                + "},"
                + "\"additionalProperties\":false"
                + "}"
        };

        public override ToolDefinition Definition
        {
            get { return _definition; }
        }

        public override Task<ToolExecutionResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request)
        {
            try
            {
                var host = GetExcelToolHost(context);
                var argumentsJson = request == null ? null : request.ArgumentsJson;
                var arguments = ExcelToolJsonSerializer.Deserialize<ExcelWorksheetScreenshotRequest>(argumentsJson);
                arguments = ExcelToolRequestNormalizer.NormalizeWorksheetScreenshotRequest(argumentsJson, arguments);

                var result = host.CaptureWorksheetScreenshot(arguments);
                SaveImageToFile(arguments, result);
                var content = BuildContent(result);
                var structuredContentJson = ExcelToolJsonSerializer.Serialize(result);

                return Task.FromResult(CreateSuccess(request, content, structuredContentJson));
            }
            catch (Exception ex)
            {
                return Task.FromResult(CreateFailure(
                    request,
                    ex,
                    "excel.capture_worksheet_screenshot_failed"));
            }
        }

        private static string BuildContent(ExcelWorksheetScreenshotResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("已捕获 Excel 截图。");
            builder.AppendLine("工作簿：" + (result.WorkbookName ?? string.Empty));
            builder.AppendLine("工作表：" + (result.WorksheetName ?? string.Empty));
            builder.AppendLine("区域：" + (result.Address ?? string.Empty));
            builder.AppendLine("区域大小：" + result.RowCount + " 行 x " + result.ColumnCount + " 列");
            builder.AppendLine("截图来源：" + GetCaptureSourceDisplayName(result.CaptureSource));
            builder.AppendLine("图片格式：" + (result.MimeType ?? "image/png"));
            builder.AppendLine("图片尺寸：" + result.ImageWidth + " x " + result.ImageHeight);
            builder.AppendLine("图片字节数：" + result.ByteCount);

            if (!string.IsNullOrWhiteSpace(result.SavedImagePath))
            {
                builder.AppendLine("落盘文件：" + result.SavedImagePath);
            }

            builder.Append("结构化结果中包含 imageBase64");
            if (!string.IsNullOrWhiteSpace(result.SavedImagePath))
            {
                builder.Append(" 和 savedImagePath");
            }

            builder.Append("，可直接供多模态模型使用。");
            return builder.ToString();
        }

        private static void SaveImageToFile(
            ExcelWorksheetScreenshotRequest arguments,
            ExcelWorksheetScreenshotResult result)
        {
            if (arguments == null || result == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(arguments.ImageFileName))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(result.ImageBase64))
            {
                throw new InvalidOperationException("截图结果中缺少图片数据，无法写入文件。");
            }

            var imageBytes = Convert.FromBase64String(result.ImageBase64);
            var imagePath = BuildImagePath(arguments.ImageFileName);
            var directory = Path.GetDirectoryName(imagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(imagePath, imageBytes);
            result.SavedImagePath = imagePath;
        }

        private static string BuildImagePath(string imageFileName)
        {
            var input = (imageFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new InvalidOperationException("图片文件名无效，无法写入文件。");
            }

            if (Path.IsPathRooted(input))
            {
                var directory = Path.GetDirectoryName(input);
                var fileName = SanitizeFileName(Path.GetFileName(input));
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new InvalidOperationException("图片文件名无效，无法写入文件。");
                }

                var extension = Path.GetExtension(fileName);
                if (string.IsNullOrWhiteSpace(extension))
                {
                    fileName += ".png";
                }
                else if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = Path.GetFileNameWithoutExtension(fileName) + ".png";
                }

                if (string.IsNullOrWhiteSpace(directory))
                {
                    throw new InvalidOperationException("图片保存路径无效，无法写入文件。");
                }

                return Path.Combine(directory, fileName);
            }

            var normalizedFileName = SanitizeFileName(Path.GetFileName(input));
            if (string.IsNullOrWhiteSpace(normalizedFileName))
            {
                throw new InvalidOperationException("图片文件名无效，无法写入文件。");
            }

            var normalizedExtension = Path.GetExtension(normalizedFileName);
            if (string.IsNullOrWhiteSpace(normalizedExtension))
            {
                normalizedFileName += ".png";
            }
            else if (!string.Equals(normalizedExtension, ".png", StringComparison.OrdinalIgnoreCase))
            {
                normalizedFileName = Path.GetFileNameWithoutExtension(normalizedFileName) + ".png";
            }

            var rootDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NixerOfficeCopilot",
                "Artifacts",
                "ExcelScreenshots");

            return Path.Combine(rootDirectory, normalizedFileName);
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                var isInvalid = false;
                for (var j = 0; j < invalidChars.Length; j++)
                {
                    if (invalidChars[j] == current)
                    {
                        isInvalid = true;
                        break;
                    }
                }

                builder.Append(isInvalid ? '_' : current);
            }

            return builder.ToString().Trim('_');
        }

        private static string GetCaptureSourceDisplayName(string captureSource)
        {
            if (string.Equals(captureSource, "selection", StringComparison.OrdinalIgnoreCase))
            {
                return "当前选区";
            }

            if (string.Equals(captureSource, "usedRange", StringComparison.OrdinalIgnoreCase))
            {
                return "工作表已用区域";
            }

            return "指定区域";
        }
    }
}