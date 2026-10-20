import { ensureAssistantMessage } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { renderMarkdown } from './markdown.js';
import { ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { state } from './state.js';
import { ensureToolCallItem } from './tool-calls.js';

export function hideToolConfirmationPanel(item) {
    if (!item) {
        return;
    }

    const panel = item.querySelector('.tool-confirmation-panel');
    if (!panel) {
        return;
    }

    item.dataset.confirmationRequestId = '';
    panel.hidden = true;

    const select = panel.querySelector('.tool-confirmation-select');
    const button = panel.querySelector('.tool-confirmation-confirm-btn');

    if (select) {
        select.disabled = false;
        select.value = 'allow-once';
    }

    if (button) {
        button.disabled = false;
    }
}

export function buildToolConfirmationMarkdown(data) {
    const lines = [];

    lines.push('需要用户确认后才能继续执行。');
    if (data.operationSummary) {
        lines.push('');
        lines.push('**本次操作**');
        lines.push('');
        lines.push(String(data.operationSummary));
    }
    lines.push('');
    lines.push('- 工具：`' + String(data.toolDisplayName || data.toolName || '工具调用') + '`');
    lines.push('- 风险级别：`' + String(data.riskLevel || 'high') + '`');

    if (data.documentName) {
        lines.push('- 文档：`' + String(data.documentName) + '`');
    }

    if (data.backupPath) {
        lines.push('- 备份：`' + String(data.backupPath) + '`');
    }

    return lines.join('\n');
}

export function showToolConfirmationRequest(data) {
    const assistantMessageId = data && data.messageId ? String(data.messageId) : '';
    const toolCallId = data && data.toolCallId ? String(data.toolCallId) : '';
    const toolName = data && data.toolName ? String(data.toolName) : '工具调用';
    const requestId = data && data.requestId ? String(data.requestId) : '';

    if (!assistantMessageId || !toolCallId || !requestId) {
        return;
    }

    const entry = ensureAssistantMessage(assistantMessageId);
    const item = ensureToolCallItem(entry.element, toolCallId, toolName);
    if (!item) {
        return;
    }

    item.open = true;
    item.dataset.confirmationRequestId = requestId;

    const stateEl = item.querySelector('.tool-call-state');
    if (stateEl) {
        stateEl.textContent = '等待确认';
    }

    const panel = item.querySelector('.tool-confirmation-panel');
    if (!panel) {
        return;
    }

    const textEl = panel.querySelector('.tool-confirmation-text');
    const argumentsDetails = panel.querySelector('.tool-confirm-arguments');
    const argumentsSummaryEl = argumentsDetails ? argumentsDetails.querySelector('summary') : null;
    const argumentsPre = argumentsDetails ? argumentsDetails.querySelector('pre') : null;
    const selectEl = panel.querySelector('.tool-confirmation-select');
    const button = panel.querySelector('.tool-confirmation-confirm-btn');

    if (textEl) {
        textEl.innerHTML = renderMarkdown(buildToolConfirmationMarkdown(data));
    }

    const argumentsJson = data.argumentsJson || data.argumentsSummary || '';
    if (argumentsDetails) {
        argumentsDetails.hidden = !argumentsJson;
    }
    if (argumentsSummaryEl) {
        argumentsSummaryEl.textContent = data.argumentsJson
            ? '查看完整参数 JSON'
            : '查看参数摘要';
    }
    if (argumentsPre) {
        argumentsPre.textContent = String(argumentsJson);
    }

    if (selectEl) {
        selectEl.value = data.defaultDecision || 'allow-once';
        selectEl.disabled = false;
    }

    if (button) {
        button.disabled = false;
    }

    panel.hidden = false;

    ensureBottomAnchor();
    scrollToBottom();
}

export function onToolConfirmationConfirmButtonClick(button) {
    if (!button || button.disabled) {
        return;
    }

    const item = button.closest('.tool-call-item');
    if (!item) {
        return;
    }

    const requestId = item.dataset.confirmationRequestId || '';
    if (!requestId) {
        return;
    }

    const panel = item.querySelector('.tool-confirmation-panel');
    const selectEl = panel ? panel.querySelector('.tool-confirmation-select') : null;
    const stateEl = item.querySelector('.tool-call-state');
    const decision = selectEl ? (selectEl.value || 'deny') : 'deny';

    if (selectEl) {
        selectEl.disabled = true;
    }

    button.disabled = true;

    if (stateEl) {
        stateEl.textContent = decision === 'deny'
            ? '已拒绝'
            : '已确认，继续执行';
    }

    panel.hidden = true;

    postMessage({
        type: 'submitToolConfirmation',
        requestId: requestId,
        decision: decision
    });
}
