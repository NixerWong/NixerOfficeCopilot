import { setMessageContent } from './assistant-info.js';
import { createAssistantTextBlock, hasPendingToolCalls, refreshAssistantWaitingIndicator, setAssistantActiveTextBlock, setAssistantCompleted, setAssistantTextBlockContent } from './assistant-message.js';
import { appendAssistantBackupPath, clearAssistantBackupState, completeAssistantPendingBackup, getToolCallStateMap, hydrateAssistantBackupState, incrementAssistantPendingBackup, refreshAssistantBackupStatus, syncCodeExecutionBackups } from './code-execution-state.js';
import { ensureAssistantMessage } from './conversation.js';
import { escapeHtml, renderMarkdown } from './markdown.js';
import { ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { state } from './state.js';
import { refreshThinkingPanel } from './thinking.js';
import { hideToolConfirmationPanel } from './tool-confirmation-ui.js';
import { t } from './i18n.js';

function setLocalizedState(element, key) {
    if (!element) {
        return;
    }
    element.dataset.l10nText = key;
    element.textContent = t(key);
}

export function getToolCallListElement(element) {
    return element ? element.querySelector('.assistant-stream') : null;
}

export function resetToolCallList(element) {
    const list = getToolCallListElement(element);
    if (!list) {
        return;
    }

    list.innerHTML = '';
    element.__toolCallStateMap = new Map();
    setAssistantActiveTextBlock(element, null);
    clearAssistantBackupState(element);
    syncCodeExecutionBackups(element);
}

export function removeToolCallItemsOnly(element) {
    const list = getToolCallListElement(element);
    if (!list) {
        return;
    }

    const items = list.querySelectorAll('.tool-call-item');
    for (let i = 0; i < items.length; i++) {
        if (items[i].parentNode) {
            items[i].parentNode.removeChild(items[i]);
        }
    }

    element.__toolCallStateMap = new Map();
}

export function createToolCallItem(toolCallId, toolName) {
    const item = document.createElement('details');
    item.className = 'tool-call-item is-pending';
    item.dataset.toolCallId = toolCallId || '';
    item.dataset.requiresBackup = 'false';
    item.dataset.backupPending = 'false';
    item.dataset.backupPath = '';
    item.dataset.confirmationRequestId = '';

    item.innerHTML = ''
        + '<summary class="tool-call-summary">'
        + '  <span class="tool-call-summary-main">'
        + '    <span class="tool-call-chevron" aria-hidden="true"></span>'
        + '    <span class="tool-call-title">' + escapeHtml(toolName || t('Web.message.toolCall')) + '</span>'
        + '  </span>'
        + '</summary>'
        + '<div class="tool-call-content">'
        + '  <div class="tool-call-state" data-l10n-text="Web.message.executing"></div>'
        + '  <div class="tool-confirmation-panel" hidden>'
        + '    <div class="tool-confirmation-text markdown-body"></div>'
        + '    <details class="tool-confirm-arguments" hidden>'
        + '      <summary data-l10n-text="Web.confirmation.details"></summary>'
        + '      <pre class="tool-confirm-arguments-json"></pre>'
        + '    </details>'
        + '    <div class="tool-confirmation-actions">'
        + '      <select class="tool-confirmation-select">'
        + '        <option value="deny" data-l10n-text="Web.confirmation.deny"></option>'
        + '        <option value="allow-once" selected data-l10n-text="Web.confirmation.allowOnce"></option>'
        + '        <option value="allow-session" data-l10n-text="Web.confirmation.allowSession"></option>'
        + '        <option value="allow-always" data-l10n-text="Web.confirmation.allowAlways"></option>'
        + '      </select>'
        + '      <button type="button" class="tool-confirmation-confirm-btn" data-l10n-text="Web.confirmation.confirm"></button>'
        + '    </div>'
        + '  </div>'
        + '  <div class="tool-call-result markdown-body"></div>'
        + '</div>';

    return item;
}

export function ensureToolCallItem(element, toolCallId, toolName) {
    if (!element) {
        return null;
    }

    const list = getToolCallListElement(element);
    if (!list) {
        return null;
    }

    const stateMap = getToolCallStateMap(element);
    let item = stateMap.get(toolCallId || '');
    if (item) {
        return item;
    }

    item = createToolCallItem(toolCallId, toolName);
    list.appendChild(item);
    stateMap.set(toolCallId || '', item);
    return item;
}

export function showToolCallStarted(assistantMessageId, toolCallId, toolName, toolDisplayName, requiresBackup, backupPath) {
    const displayName = toolDisplayName || toolName;
    const entry = ensureAssistantMessage(assistantMessageId);
    setAssistantCompleted(entry.element, false);

    const item = ensureToolCallItem(entry.element, toolCallId, displayName);
    if (!item) {
        return;
    }

    item.classList.remove('is-success', 'is-error');
    item.classList.add('is-pending');

    const needsBackup = !!requiresBackup;
    const normalizedBackupPath = String(backupPath || '').trim();
    item.dataset.requiresBackup = needsBackup ? 'true' : 'false';
    item.dataset.backupPath = normalizedBackupPath;

    if (normalizedBackupPath) {
        item.dataset.backupPending = 'false';
        appendAssistantBackupPath(entry.element, normalizedBackupPath);
    }
    else if (needsBackup) {
        if (item.dataset.backupPending !== 'true') {
            item.dataset.backupPending = 'true';
            incrementAssistantPendingBackup(entry.element);
        }
    }
    else {
        item.dataset.backupPending = 'false';
    }

    const titleEl = item.querySelector('.tool-call-title');
    if (titleEl) {
        titleEl.textContent = displayName || t('Web.message.toolCall');
    }

    const stateEl = item.querySelector('.tool-call-state');
    if (stateEl) {
        setLocalizedState(stateEl, 'Web.message.executing');
    }

    const resultEl = item.querySelector('.tool-call-result');
    if (resultEl) {
        resultEl.innerHTML = '';
    }

    setAssistantActiveTextBlock(entry.element, null);
    refreshAssistantWaitingIndicator(entry.element, 0);

    ensureBottomAnchor();
    syncCodeExecutionBackups(entry.element);
    scrollToBottom();
}

export function showToolCallCompleted(assistantMessageId, toolCallId, toolName, toolDisplayName, success, content, errorMessage, backupPath) {
    const displayName = toolDisplayName || toolName;
    const entry = ensureAssistantMessage(assistantMessageId);
    const item = ensureToolCallItem(entry.element, toolCallId, displayName);
    if (!item) {
        return;
    }

    hideToolConfirmationPanel(item);

    item.classList.remove('is-pending', 'is-success', 'is-error');
    item.classList.add(success ? 'is-success' : 'is-error');

    const normalizedBackupPath = String(backupPath || '').trim();
    item.dataset.backupPath = normalizedBackupPath;

    if (item.dataset.backupPending === 'true') {
        item.dataset.backupPending = 'false';
        completeAssistantPendingBackup(entry.element, normalizedBackupPath);
    }
    else if (normalizedBackupPath) {
        appendAssistantBackupPath(entry.element, normalizedBackupPath);
    }

    const titleEl = item.querySelector('.tool-call-title');
    if (titleEl) {
        titleEl.textContent = displayName || t('Web.message.toolCall');
    }

    const stateEl = item.querySelector('.tool-call-state');
    if (stateEl) {
        setLocalizedState(stateEl, success ? 'Web.message.completed' : 'Web.message.failed');
    }

    const resultEl = item.querySelector('.tool-call-result');
    if (resultEl) {
        const text = success ? (content || '') : (errorMessage || content || '');
        resultEl.innerHTML = text ? renderMarkdown(text) : '';
    }

    setAssistantCompleted(entry.element, false);
    refreshAssistantWaitingIndicator(entry.element, 0);

    ensureBottomAnchor();
    syncCodeExecutionBackups(entry.element);
    scrollToBottom();
}

export function renderAssistantContentParts(element, message) {
    // 按 ContentParts 顺序交错渲染文本和工具调用
    var toolCalls = message.toolCalls || [];
    var toolCallMap = {};
    for (var i = 0; i < toolCalls.length; i++) {
        var tc = toolCalls[i];
        if (tc && tc.toolCallId) {
            toolCallMap[tc.toolCallId] = tc;
        }
    }

    // 清除已有的工具调用项（文本块由后续创建）
    removeToolCallItemsOnly(element);
    setAssistantActiveTextBlock(element, null);

    var parts = message.contentParts || [];
    var allToolCallIds = [];
    for (var p = 0; p < parts.length; p++) {
        var part = parts[p];
        if (!part) { continue; }

        if (part.type === 'text' && part.text) {
            var block = createAssistantTextBlock();
            setAssistantTextBlockContent(block, part.text);
            var stream = getToolCallListElement(element);
            if (stream) { stream.appendChild(block); }
            setAssistantActiveTextBlock(element, block);
        }
        else if (part.type === 'tool_call' && part.toolCallId) {
            var tcData = toolCallMap[part.toolCallId];
            if (tcData) {
                renderHistoryToolCallItem(element, tcData);
                allToolCallIds.push(part.toolCallId);
            }
        }
    }

    // 填充 ToolCalls 用于 backup path 提取
    var usedToolCalls = [];
    for (var j = 0; j < allToolCallIds.length; j++) {
        var tc = toolCallMap[allToolCallIds[j]];
        if (tc) { usedToolCalls.push(tc); }
    }

    setMessageContent(element, message.content || '');
    setAssistantActiveTextBlock(element, null);
    setAssistantCompleted(element, true);
    element.classList.remove('is-thinking');
    hydrateAssistantBackupState(element, usedToolCalls);
    syncCodeExecutionBackups(element);
    refreshAssistantWaitingIndicator(element, 0);
    refreshThinkingPanel(element);
    ensureBottomAnchor();
}

export function renderHistoryToolCallItem(element, toolCall) {
    var toolCallId = toolCall.toolCallId || '';
    var toolName = toolCall.toolName || t('Web.message.toolCall');
    var status = String(toolCall.status || '').toLowerCase();
    var success = !!toolCall.success;

    var item = ensureToolCallItem(element, toolCallId, toolName);
    if (!item) { return; }

    item.classList.remove('is-pending', 'is-success', 'is-error');
    item.dataset.requiresBackup = toolCall.requiresBackup ? 'true' : 'false';
    item.dataset.backupPending = toolCall.requiresBackup && status === 'running' ? 'true' : 'false';
    item.dataset.backupPath = toolCall.backupPath || '';

    if (status === 'running') {
        item.classList.add('is-pending');
    } else if (success || status === 'completed') {
        item.classList.add('is-success');
    } else {
        item.classList.add('is-error');
    }

    var titleEl = item.querySelector('.tool-call-title');
    if (titleEl) { titleEl.textContent = toolName; }

    var stateEl = item.querySelector('.tool-call-state');
    if (stateEl) {
        if (status === 'running') {
            setLocalizedState(stateEl, 'Web.message.executing');
        } else if (success || status === 'completed') {
            setLocalizedState(stateEl, 'Web.message.completed');
        } else {
            setLocalizedState(stateEl, 'Web.message.failed');
        }
    }

    var resultEl = item.querySelector('.tool-call-result');
    if (resultEl) {
        var text = (success || status === 'completed')
            ? (toolCall.content || '')
            : (toolCall.errorMessage || toolCall.content || '');
        resultEl.innerHTML = text ? renderMarkdown(text) : '';
    }
}

export function hydrateAssistantToolCalls(element, toolCalls, clearFirst) {
    if (!element) {
        return;
    }

    if (clearFirst !== false) {
        resetToolCallList(element);
    }
    else {
        removeToolCallItemsOnly(element);
    }

    if (!toolCalls || !toolCalls.length) {
        refreshAssistantBackupStatus(element);
        return;
    }

    for (let i = 0; i < toolCalls.length; i++) {
        const toolCall = toolCalls[i];
        if (!toolCall) {
            continue;
        }

        const toolCallId = toolCall.toolCallId || '';
        const toolName = toolCall.toolName || t('Web.message.toolCall');
        const status = String(toolCall.status || '').toLowerCase();
        const success = !!toolCall.success;

        const item = ensureToolCallItem(element, toolCallId, toolName);
        if (!item) {
            continue;
        }

        item.classList.remove('is-pending', 'is-success', 'is-error');
        item.dataset.requiresBackup = toolCall.requiresBackup ? 'true' : 'false';
        item.dataset.backupPending = toolCall.requiresBackup && status === 'running' ? 'true' : 'false';
        item.dataset.backupPath = toolCall.backupPath || '';

        if (status === 'running') {
            item.classList.add('is-pending');
        }
        else if (success || status === 'completed') {
            item.classList.add('is-success');
        }
        else {
            item.classList.add('is-error');
        }

        const titleEl = item.querySelector('.tool-call-title');
        if (titleEl) {
            titleEl.textContent = toolName;
        }

        const stateEl = item.querySelector('.tool-call-state');
        if (stateEl) {
            if (status === 'running') {
                setLocalizedState(stateEl, 'Web.message.executing');
            }
            else if (success || status === 'completed') {
                setLocalizedState(stateEl, 'Web.message.completed');
            }
            else {
                setLocalizedState(stateEl, 'Web.message.failed');
            }
        }

        const resultEl = item.querySelector('.tool-call-result');
        if (resultEl) {
            const text = (success || status === 'completed')
                ? (toolCall.content || '')
                : (toolCall.errorMessage || toolCall.content || '');

            resultEl.innerHTML = text ? renderMarkdown(text) : '';
        }
    }

    hydrateAssistantBackupState(element, toolCalls);
    setAssistantActiveTextBlock(element, null);
    syncCodeExecutionBackups(element);
    setAssistantCompleted(element, !hasPendingToolCalls(element));
    refreshAssistantWaitingIndicator(element, 0);
}
