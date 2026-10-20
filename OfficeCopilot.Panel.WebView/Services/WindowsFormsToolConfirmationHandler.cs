using OfficeCopilot.Host.Abstractions;
using System;
using System.Text;
using System.Windows.Forms;

namespace OfficeCopilot.Panel.WebView.Services
{
    public sealed class WindowsFormsToolConfirmationHandler : IToolConfirmationHandler
    {
        private readonly Control _owner;

        public WindowsFormsToolConfirmationHandler(Control owner)
        {
            _owner = owner;
        }

        public bool Confirm(ToolConfirmationRequest request)
        {
            request = request ?? new ToolConfirmationRequest();
            var title = string.IsNullOrWhiteSpace(request.ToolDisplayName)
                ? "高风险工具确认"
                : request.ToolDisplayName + " - 执行确认";
            var message = BuildMessage(request);

            DialogResult result = DialogResult.No;
            if (_owner != null && _owner.IsHandleCreated)
            {
                if (_owner.InvokeRequired)
                {
                    _owner.Invoke(new Action(() =>
                    {
                        result = MessageBox.Show(_owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    }));
                }
                else
                {
                    result = MessageBox.Show(_owner, message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                }
            }
            else
            {
                result = MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            }

            return result == DialogResult.Yes;
        }

        private static string BuildMessage(ToolConfirmationRequest request)
        {
            var builder = new StringBuilder();
            builder.AppendLine("该操作可能修改当前 Office 文档。请确认后继续。");
            builder.AppendLine("工具：" + Safe(request.ToolDisplayName, request.ToolName));
            builder.AppendLine("风险级别：" + Safe(request.RiskLevel, "high"));

            if (!string.IsNullOrWhiteSpace(request.DocumentName))
            {
                builder.AppendLine("文档：" + request.DocumentName);
            }

            if (!string.IsNullOrWhiteSpace(request.OperationSummary))
            {
                builder.AppendLine("操作：" + request.OperationSummary);
            }

            if (!string.IsNullOrWhiteSpace(request.BackupPath))
            {
                builder.AppendLine("执行前备份：" + request.BackupPath);
            }

            if (!string.IsNullOrWhiteSpace(request.ArgumentsSummary))
            {
                builder.AppendLine();
                builder.AppendLine("参数摘要：");
                builder.AppendLine(request.ArgumentsSummary);
            }

            builder.AppendLine();
            builder.Append("选择“是”继续执行，选择“否”取消。");
            return builder.ToString().Trim();
        }

        private static string Safe(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
