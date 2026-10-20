using OfficeCopilot.Host.Abstractions;
using System;
using System.Windows.Forms;

namespace OfficeCopilot.Panel.WebView.Services
{
    public sealed class ChatToolConfirmationHandler : IToolConfirmationHandler
    {
        private readonly CopilotPanelControl _owner;

        public ChatToolConfirmationHandler(CopilotPanelControl owner)
        {
            _owner = owner;
        }

        public bool Confirm(ToolConfirmationRequest request)
        {
            request = request ?? new ToolConfirmationRequest();

            if (_owner == null)
            {
                return false;
            }

            if (_owner.IsToolApprovedPermanently(request.ToolName))
            {
                return true;
            }

            if (_owner.IsToolApprovedForCurrentSession(request.ToolName))
            {
                return true;
            }

            if (!_owner.CanUseInlineToolConfirmation() || !_owner.InvokeRequired)
            {
                return new WindowsFormsToolConfirmationHandler(_owner).Confirm(request);
            }

            var decision = _owner.RequestToolConfirmationFromWeb(request);

            if (string.Equals(decision, "allow-always", StringComparison.OrdinalIgnoreCase))
            {
                _owner.ApproveToolPermanently(request.ToolName);
                _owner.ApproveToolForCurrentSession(request.ToolName);
                return true;
            }

            if (string.Equals(decision, "allow-session", StringComparison.OrdinalIgnoreCase))
            {
                _owner.ApproveToolForCurrentSession(request.ToolName);
                return true;
            }

            if (string.Equals(decision, "allow-once", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}