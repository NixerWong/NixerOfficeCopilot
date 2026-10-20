import { appendToAssistantStream } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { escapeHtml } from './markdown.js';
import { ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { chatList } from './state.js';

export var _pendingPlanApprovals = {};

export function showPlanApproval(data) {
    var requestId = data.requestId || '';
    if (!requestId || !chatList) {
        return;
    }

    dismissPlanApproval(requestId);

    var planText = data.plan || '';

    var container = document.createElement('div');
    container.className = 'plan-approval-panel';
    container.dataset.requestId = requestId;

    container.innerHTML = ''
        + '<div class="plan-approval-header">'
        + '  <span>\ud83d\udccb</span>'
        + '  <span>Agent \u5df2\u51c6\u5907\u597d\u5b9e\u65bd\u8ba1\u5212\uff0c\u8bf7\u5ba1\u67e5</span>'
        + '</div>'
        + '<div class="plan-approval-body">'
        + '  <textarea class="plan-approval-text" rows="12">' + escapeHtml(planText) + '</textarea>'
        + '</div>'
        + '<div class="plan-approval-footer">'
        + '  <button type="button" class="plan-reject-btn">\u2717 \u62d2\u7edd</button>'
        + '  <button type="button" class="plan-approve-btn">\u2713 \u6279\u51c6</button>'
        + '</div>';

    _pendingPlanApprovals[requestId] = { container: container };

    appendToAssistantStream(container);
    ensureBottomAnchor();
    scrollToBottom();

    var approveBtn = container.querySelector('.plan-approve-btn');
    var rejectBtn = container.querySelector('.plan-reject-btn');
    var textArea = container.querySelector('.plan-approval-text');

    if (approveBtn) {
        approveBtn.addEventListener('click', function () {
            if (approveBtn.disabled) return;
            approveBtn.disabled = true;
            if (rejectBtn) rejectBtn.disabled = true;
            var editedPlan = textArea ? textArea.value : planText;
            postMessage({
                type: 'submitUserInteraction',
                requestId: requestId,
                answers: JSON.stringify({ approved: true, plan: editedPlan }),
                annotations: '{}',
                cancelled: false
            });
        });
    }

    if (rejectBtn) {
        rejectBtn.addEventListener('click', function () {
            if (rejectBtn.disabled) return;
            approveBtn.disabled = true;
            rejectBtn.disabled = true;
            postMessage({
                type: 'submitUserInteraction',
                requestId: requestId,
                answers: JSON.stringify({ approved: false }),
                annotations: '{}',
                cancelled: true
            });
        });
    }
}

export function dismissPlanApproval(requestId) {
    var containers = document.querySelectorAll('.plan-approval-panel[data-request-id="' + requestId + '"]');
    for (var i = 0; i < containers.length; i++) {
        if (containers[i].parentNode) {
            containers[i].parentNode.removeChild(containers[i]);
        }
    }
    delete _pendingPlanApprovals[requestId];
}

// ================================================================
//  Phase 7C: Tool Confirmation — inline confirmation component
// ================================================================
