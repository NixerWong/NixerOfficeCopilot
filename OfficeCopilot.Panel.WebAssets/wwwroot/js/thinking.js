import { getAssistantInfoItems, getThinkingContent, isThinkingCompleted, normalizeThinkingContent, setActiveAssistantMessageId, setAssistantInfoItems, setMessageContent, setThinkingCompleted, setThinkingContent } from './assistant-info.js';
import { ensureAssistantTextBlock, getAssistantRenderedContent, getAssistantTextBlockContent, hasPendingToolCalls, isAssistantCompleted, refreshAssistantWaitingIndicator, removeAssistantTypingIndicator, setAssistantCompleted, setAssistantTextBlockContent } from './assistant-message.js';
import { syncCodeExecutionBackups } from './code-execution-state.js';
import { ensureAssistantMessage } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { renderMarkdown } from './markdown.js';
import { ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { resetToolCallList } from './tool-calls.js';
import { t } from './i18n.js';

export function ensureThinkingPanel(element) {
    if (!element) {
        return null;
    }

    const host = element.querySelector('.assistant-thinking');
    if (!host) {
        return null;
    }

    let details = host.querySelector('.thinking-panel');
    if (!details) {
        details = document.createElement('details');
        details.className = 'thinking-panel';

        const summary = document.createElement('summary');
        summary.className = 'thinking-panel-summary';
        details.appendChild(summary);

        const content = document.createElement('div');
        content.className = 'thinking-panel-content markdown-body';
        details.appendChild(content);

        host.appendChild(details);
    }

    return {
        host: host,
        details: details,
        summary: details.querySelector('.thinking-panel-summary'),
        content: details.querySelector('.thinking-panel-content')
    };
}

export function refreshThinkingPanel(element) {
    if (!element) {
        return;
    }

    const host = element.querySelector('.assistant-thinking');
    if (!host) {
        return;
    }

    const thinkingContent = normalizeThinkingContent(getThinkingContent(element));
    if (!thinkingContent) {
        host.innerHTML = '';
        return;
    }

    const panel = ensureThinkingPanel(element);
    if (!panel) {
        return;
    }

    const summaryKey = isThinkingCompleted(element) ? 'Web.thinking.completed' : 'Web.thinking.active';
    panel.summary.dataset.l10nText = summaryKey;
    panel.summary.textContent = t(summaryKey);
    panel.content.innerHTML = renderMarkdown(thinkingContent);
}

window.addEventListener('panel-locale-changed', function () {
    document.querySelectorAll('.chat-message.assistant').forEach(refreshThinkingPanel);
});

export function resetThinkingPanel(element) {
    setThinkingContent(element, '');
    setThinkingCompleted(element, false);
    refreshThinkingPanel(element);
}

export function appendAssistantThinkingChunk(messageId, chunk) {
    const entry = ensureAssistantMessage(messageId);
    const nextContent = getThinkingContent(entry.element) + (chunk || '');

    setAssistantCompleted(entry.element, false);
    setThinkingContent(entry.element, nextContent);
    refreshThinkingPanel(entry.element);
    // 方案 1：思考中三点动画与「思考中」折叠标签并存，不因思考块到达而熄灭。
    refreshAssistantWaitingIndicator(entry.element, 0);

    ensureBottomAnchor();
    syncCodeExecutionBackups(entry.element);
    scrollToBottom();
}

export function showThinkingState(messageId, infoItems) {
    const entry = ensureAssistantMessage(messageId);
    setActiveAssistantMessageId(messageId);

    entry.content = '';
    setMessageContent(entry.element, '');
    resetThinkingPanel(entry.element);
    setAssistantInfoItems(entry.element, infoItems || getAssistantInfoItems(entry.element));
    setAssistantCompleted(entry.element, false);

    entry.element.classList.add('is-thinking');
    resetToolCallList(entry.element);
    refreshAssistantWaitingIndicator(entry.element, 0);

    ensureBottomAnchor();
    scrollToBottom();
}

export function setAssistantContent(messageId, content) {
    const entry = ensureAssistantMessage(messageId);
    setActiveAssistantMessageId(messageId);

    entry.content = content || '';
    setMessageContent(entry.element, entry.content);
    setAssistantCompleted(entry.element, true);
    entry.element.classList.remove('is-thinking');

    postMessage({ type: 'traceLog', msgType: 'setAssistantContent', requestId: messageId });
    postMessage({ type: 'traceLog', msgType: 'setAssistantContent-completed', requestId: '' + isAssistantCompleted(entry.element) + '-pendingCalls=' + hasPendingToolCalls(entry.element) });

    refreshAssistantWaitingIndicator(entry.element, -1);
    var pendingItems = entry.element.querySelectorAll('.tool-call-item.is-pending');
    postMessage({ type: 'traceLog', msgType: 'setAssistantContent-pendingCount', requestId: '' + pendingItems.length });
    postMessage({ type: 'traceLog', msgType: 'setAssistantContent-afterRefresh', requestId: messageId });

    const renderedContent = getAssistantRenderedContent(entry.element);
    if (entry.content) {
        if (!renderedContent) {
            const block = ensureAssistantTextBlock(entry.element);
            setAssistantTextBlockContent(block, entry.content);
        }
        else if (entry.content.indexOf(renderedContent) === 0) {
            const suffix = entry.content.slice(renderedContent.length);
            if (suffix) {
                const block = ensureAssistantTextBlock(entry.element);
                setAssistantTextBlockContent(
                    block,
                    getAssistantTextBlockContent(block) + suffix);
            }
        }
        else {
            const block = ensureAssistantTextBlock(entry.element);
            setAssistantTextBlockContent(block, entry.content);
        }
    }

    refreshThinkingPanel(entry.element);

    ensureBottomAnchor();
    syncCodeExecutionBackups(entry.element);
    scrollToBottom();
}

export function appendAssistantChunk(messageId, chunk) {
    const entry = ensureAssistantMessage(messageId);
    setActiveAssistantMessageId(messageId);

    if (entry.element.classList.contains('is-thinking')) {
        entry.content = '';
    }

    entry.content += chunk || '';
    setAssistantCompleted(entry.element, false);
    setMessageContent(entry.element, entry.content);
    entry.element.classList.remove('is-thinking');

    removeAssistantTypingIndicator(entry.element);

    const block = ensureAssistantTextBlock(entry.element);
    setAssistantTextBlockContent(
        block,
        getAssistantTextBlockContent(block) + (chunk || ''));

    refreshThinkingPanel(entry.element);
    refreshAssistantWaitingIndicator(entry.element, 900);

    ensureBottomAnchor();
    scrollToBottom();
}

export function completeAssistantThinking(messageId) {
    const entry = ensureAssistantMessage(messageId);
    setActiveAssistantMessageId(messageId);

    setThinkingCompleted(entry.element, true);
    setAssistantCompleted(entry.element, true);
    refreshThinkingPanel(entry.element);
    removeAssistantTypingIndicator(entry.element);

    ensureBottomAnchor();
    scrollToBottom();
}
