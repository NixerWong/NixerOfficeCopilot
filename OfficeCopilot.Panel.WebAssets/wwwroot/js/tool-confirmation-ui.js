import { ensureAssistantMessage } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { renderMarkdown } from './markdown.js';
import { ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { state } from './state.js';
import { ensureToolCallItem } from './tool-calls.js';
import { t } from './i18n.js';

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

    lines.push(t('Web.confirmation.heading'));
    if (data.operationSummary) {
        lines.push('');
        lines.push('**' + t('Web.confirmation.operation') + '**');
        lines.push('');
        lines.push(String(data.operationSummary));
    }
    lines.push('');
    lines.push('- ' + t('Web.confirmation.tool') + ': `' + String(data.toolDisplayName || data.toolName || t('Web.message.toolCall')) + '`');
    lines.push('- ' + t('Web.confirmation.risk') + ': `' + String(data.riskLevel || 'high') + '`');

    if (data.documentName) {
        lines.push('- ' + t('Web.confirmation.document') + ': `' + String(data.documentName) + '`');
    }

    if (data.backupPath) {
        lines.push('- ' + t('Web.confirmation.backup') + ': `' + String(data.backupPath) + '`');
    }

    return lines.join('\n');
}

export function showToolConfirmationRequest(data) {
    const assistantMessageId = data && data.messageId ? String(data.messageId) : '';
    const toolCallId = data && data.toolCallId ? String(data.toolCallId) : '';
    const toolName = data && data.toolName ? String(data.toolName) : t('Web.message.toolCall');
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
        stateEl.dataset.l10nText = 'Web.confirmation.waiting';
        stateEl.textContent = t('Web.confirmation.waiting');
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
        textEl.__toolConfirmationData = data;
        textEl.innerHTML = renderMarkdown(buildToolConfirmationMarkdown(data));
    }

    const argumentsJson = data.argumentsJson || data.argumentsSummary || '';
    if (argumentsDetails) {
        argumentsDetails.hidden = !argumentsJson;
    }
    if (argumentsSummaryEl) {
        argumentsSummaryEl.dataset.l10nText = data.argumentsJson
            ? 'Web.confirmation.details'
            : 'Web.confirmation.summary';
        argumentsSummaryEl.textContent = t(argumentsSummaryEl.dataset.l10nText);
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
        const key = decision === 'deny' ? 'Web.confirmation.denied' : 'Web.confirmation.accepted';
        stateEl.dataset.l10nText = key;
        stateEl.textContent = t(key);
    }

    panel.hidden = true;

    postMessage({
        type: 'submitToolConfirmation',
        requestId: requestId,
        decision: decision
    });
}

window.addEventListener('panel-locale-changed', function () {
    document.querySelectorAll('.tool-confirmation-text').forEach(function (element) {
        if (element.__toolConfirmationData) {
            element.innerHTML = renderMarkdown(buildToolConfirmationMarkdown(element.__toolConfirmationData));
        }
    });
});
