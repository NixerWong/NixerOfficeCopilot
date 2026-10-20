import { appendMessage, appendToAssistantStream } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { escapeHtml } from './markdown.js';
import { ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { chatList } from './state.js';

export var _pendingToolConfirmations = {};

export function showToolConfirmation(data) {
    var requestId = data.requestId || '';
    if (!requestId || !chatList) {
        postMessage({ type: 'traceLog', msgType: 'toolConfirmation-skip', requestId: requestId });
        return;
    }

    dismissToolConfirmation(requestId);

    postMessage({ type: 'traceLog', msgType: 'toolConfirmation-render', requestId: requestId });

    var toolName = data.toolName || '\u672a\u77e5\u5de5\u5177';
    var toolDescription = data.toolDescription || '';
    var operationSummary = data.operationSummary || '';
    var riskLevel = data.riskLevel || 'high';
    var argumentsSummary = data.argumentsSummary || '';
    var argumentsJson = data.argumentsJson || '';

    var riskClass = 'risk-' + riskLevel;
    var riskLabel = { low: '\u4f4e\u98ce\u9669', medium: '\u4e2d\u7b49\u98ce\u9669', high: '\u9ad8\u98ce\u9669' }[riskLevel] || riskLevel;

    var container = document.createElement('div');
    container.className = 'tool-confirm-panel';
    container.dataset.requestId = requestId;

    var fieldsHtml = '';
    if (operationSummary) {
        fieldsHtml += '<div class="tool-confirm-operation-summary">'
            + '<span class="tool-confirm-label">\u672c\u6b21\u64cd\u4f5c</span>'
            + '<div class="tool-confirm-value">' + escapeHtml(operationSummary) + '</div>'
            + '</div>';
    }
    if (toolName) {
        fieldsHtml += '<div class="tool-confirm-field"><span class="tool-confirm-label">\u5de5\u5177</span><span class="tool-confirm-value">' + escapeHtml(toolName) + '</span></div>';
    }
    if (toolDescription) {
        fieldsHtml += '<div class="tool-confirm-field"><span class="tool-confirm-label">\u63cf\u8ff0</span><span class="tool-confirm-value">' + escapeHtml(toolDescription) + '</span></div>';
    }
    fieldsHtml += '<div class="tool-confirm-field"><span class="tool-confirm-label">\u98ce\u9669\u7b49\u7ea7</span><span class="tool-confirm-risk ' + riskClass + '">' + riskLabel + '</span></div>';
    if (argumentsJson || argumentsSummary) {
        var argumentText = argumentsJson || argumentsSummary;
        var argumentLabel = argumentsJson ? '\u67e5\u770b\u5b8c\u6574\u53c2\u6570 JSON' : '\u67e5\u770b\u53c2\u6570\u6458\u8981';
        fieldsHtml += '<details class="tool-confirm-arguments">'
            + '<summary>' + argumentLabel + '</summary>'
            + '<pre class="tool-confirm-arguments-json">' + escapeHtml(argumentText) + '</pre>'
            + '</details>';
    }

    container.innerHTML = ''
        + '<div class="tool-confirm-header">'
        + '  <span>\u26a0\ufe0f</span>'
        + '  <span>\u5de5\u5177\u6267\u884c\u786e\u8ba4</span>'
        + '</div>'
        + '<div class="tool-confirm-body">' + fieldsHtml + '</div>'
        + '<div class="tool-confirm-footer">'
        + '  <select class="tool-confirmation-select">'
        + '    <option value="deny">\u62d2\u7edd\u6388\u6743</option>'
        + '    <option value="allow-once" selected>\u5355\u6b21\u6388\u6743</option>'
        + '    <option value="allow-session">\u672c\u4f1a\u8bdd\u81ea\u52a8\u6388\u6743</option>'
        + '    <option value="allow-always">\u6c38\u4e45\u81ea\u52a8\u6388\u6743</option>'
        + '  </select>'
        + '  <button type="button" class="tool-confirmation-confirm-btn">\u786e\u8ba4</button>'
        + '</div>';

    _pendingToolConfirmations[requestId] = { container: container };

    appendToAssistantStream(container);
    ensureBottomAnchor();
    scrollToBottom();

    var confirmBtn = container.querySelector('.tool-confirmation-confirm-btn');
    var selectEl = container.querySelector('.tool-confirmation-select');

    if (confirmBtn) {
        confirmBtn.addEventListener('click', function () {
            if (confirmBtn.disabled) return;
            confirmBtn.disabled = true;
            if (selectEl) selectEl.disabled = true;
            var decision = selectEl ? selectEl.value : 'deny';
            postMessage({
                type: 'submitUserInteraction',
                requestId: requestId,
                answers: JSON.stringify({ approved: true, decision: decision }),
                annotations: '{}',
                cancelled: decision === 'deny'
            });
        });
    }
}

export function dismissToolConfirmation(requestId) {
    var containers = document.querySelectorAll('.tool-confirm-panel[data-request-id="' + requestId + '"]');
    for (var i = 0; i < containers.length; i++) {
        if (containers[i].parentNode) {
            containers[i].parentNode.removeChild(containers[i]);
        }
    }
    delete _pendingToolConfirmations[requestId];
}

export function appendCodeExecutionFailureMessage(message) {
    const text = String(message || '').trim();
    if (!text) {
        return;
    }

    appendMessage({
        id: 'code_execution_error_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 8),
        role: 'assistant',
        content: '### VBA 执行失败\n\n' + text
    });
}
