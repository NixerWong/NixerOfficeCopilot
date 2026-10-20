import { getMessageContent, resolveAssistantInfoTargetMessageId, setAssistantInfoItems, setMessageContent, setThinkingCompleted, setThinkingContent } from './assistant-info.js';
import { onCopyButtonClick } from './clipboard.js';
import { openBackupArtifact } from './code-blocks.js';
import { getAssistantBackupPaths, getToolCallStateMap, refreshAssistantBackupStatus, setAssistantPendingBackupCount, setCodeExecutionStates } from './code-execution-state.js';
import { postMessage } from './host-bridge.js';
import { renderMarkdown } from './markdown.js';
import { createAssistantRole } from './scroll.js';
import { state } from './state.js';
import { getToolCallListElement } from './tool-calls.js';

export function createTypingIndicator() {
    return ''
        + '<div class="typing-indicator" aria-label="Nixer Copilot 正在应答" title="Nixer Copilot 正在应答">'
        + '  <span></span>'
        + '  <span></span>'
        + '  <span></span>'
        + '</div>';
}

export function setAssistantActiveTextBlock(element, block) {
    if (!element) {
        return;
    }

    element.__assistantActiveTextBlock = block || null;
}

export function setAssistantCompleted(element, completed) {
    if (!element) {
        return;
    }

    element.__assistantCompleted = !!completed;
}

export function isAssistantCompleted(element) {
    if (!element) {
        return true;
    }

    return !!element.__assistantCompleted;
}

export function getAssistantActiveTextBlock(element) {
    if (!element) {
        return null;
    }

    return element.__assistantActiveTextBlock || null;
}

export function setAssistantTextBlockContent(block, content) {
    if (!block) {
        return;
    }

    block.__assistantTextContent = content || '';
    block.innerHTML = block.__assistantTextContent
        ? renderMarkdown(block.__assistantTextContent)
        : '';
}

export function getAssistantTextBlockContent(block) {
    if (!block) {
        return '';
    }

    return block.__assistantTextContent || '';
}

export function getAssistantRenderedContent(element) {
    const stream = getToolCallListElement(element);
    if (!stream) {
        return '';
    }

    const textBlocks = stream.querySelectorAll('.assistant-text-block');
    let content = '';

    for (let i = 0; i < textBlocks.length; i++) {
        content += getAssistantTextBlockContent(textBlocks[i]);
    }

    return content;
}

export function createAssistantTextBlock() {
    const block = document.createElement('div');
    block.className = 'chat-text markdown-body assistant-text-block';
    setAssistantTextBlockContent(block, '');
    return block;
}

export function ensureAssistantTextBlock(element) {
    if (!element) {
        return null;
    }

    const stream = getToolCallListElement(element);
    if (!stream) {
        return null;
    }

    let block = getAssistantActiveTextBlock(element);
    if (block && block.parentNode === stream) {
        return block;
    }

    block = createAssistantTextBlock();
    stream.appendChild(block);
    setAssistantActiveTextBlock(element, block);
    return block;
}

export function removeAssistantTypingIndicator(element) {
    const stream = getToolCallListElement(element);
    if (!stream) {
        return;
    }

    const typing = stream.querySelector('.assistant-typing');
    if (typing && typing.parentNode) {
        typing.parentNode.removeChild(typing);
    }
}

export function showAssistantTypingIndicator(element) {
    const stream = getToolCallListElement(element);
    if (!stream) {
        return;
    }

    let typing = stream.querySelector('.assistant-typing');
    if (!typing) {
        typing = document.createElement('div');
        typing.className = 'assistant-typing';
        typing.innerHTML = createTypingIndicator();
        stream.appendChild(typing);
        return;
    }

    // 已在显示：只有不在最底部时才移到底部，避免三点动画悬在内容/工具项中间；
    // 已在最底部则不动，避免高频重排/重建导致闪烁。
    if (stream.lastElementChild !== typing) {
        stream.appendChild(typing);
    }
}

export function clearAssistantWaitingIndicatorTimer(element) {
    if (!element || !element.__assistantWaitingIndicatorTimer) {
        return;
    }

    window.clearTimeout(element.__assistantWaitingIndicatorTimer);
    element.__assistantWaitingIndicatorTimer = 0;
}

export function hasPendingToolCalls(element) {
    const stream = getToolCallListElement(element);
    if (!stream) {
        return false;
    }

    return !!stream.querySelector('.tool-call-item.is-pending');
}

export function isAssistantWaitingIndicatorTarget(element) {
    if (!element) {
        return false;
    }

    const messageId = element.dataset.messageId || '';
    if (!messageId) {
        return false;
    }

    return messageId === resolveAssistantInfoTargetMessageId('');
}

export function shouldShowAssistantWaitingIndicator(element) {
    if (!element || isAssistantCompleted(element)) {
        return false;
    }

    return element.classList.contains('is-thinking')
        || hasPendingToolCalls(element)
        || (state.busy && isAssistantWaitingIndicatorTarget(element));
}

export function refreshAssistantWaitingIndicator(element, delayMs) {
    if (!element) {
        return;
    }

    clearAssistantWaitingIndicatorTimer(element);

    if (!shouldShowAssistantWaitingIndicator(element)) {
        removeAssistantTypingIndicator(element);
        return;
    }

    if (delayMs > 0) {
        element.__assistantWaitingIndicatorTimer = window.setTimeout(function () {
            element.__assistantWaitingIndicatorTimer = 0;

            if (shouldShowAssistantWaitingIndicator(element)) {
                showAssistantTypingIndicator(element);
            }
            else {
                removeAssistantTypingIndicator(element);
            }
        }, delayMs);

        return;
    }

    showAssistantTypingIndicator(element);
}

export function createMessageElement(message) {
    const wrapper = document.createElement('div');
    wrapper.className = 'chat-message ' + message.role;
    wrapper.dataset.messageId = message.id || '';
    setMessageContent(wrapper, message.content || '');
    setThinkingContent(wrapper, '');
    setThinkingCompleted(wrapper, false);
    setAssistantCompleted(wrapper, false);
    setAssistantInfoItems(wrapper, message.infoItems || []);
    getToolCallStateMap(wrapper);
    setAssistantActiveTextBlock(wrapper, null);
    setAssistantPendingBackupCount(wrapper, 0);
    wrapper.__assistantBackupPaths = [];
    setCodeExecutionStates(wrapper, message.codeExecutions || []);

    if (message.role === 'assistant') {
        wrapper.innerHTML = ''
            + createAssistantRole()
            + '<div class="assistant-body">'
            + '  <div class="assistant-thinking"></div>'
            + '  <div class="assistant-stream"></div>'
            + '</div>'
            + '<div class="message-toolbar">'
            + '  <button type="button" class="message-icon-btn copy-message-btn" title="复制" aria-label="复制"></button>'
            + '</div>';

        const copyBtn = wrapper.querySelector('.copy-message-btn');
        copyBtn.dataset.originalTitle = '复制';
        copyBtn.addEventListener('click', function () {
            onCopyButtonClick(copyBtn, getMessageContent(wrapper));
        });

        const viewBackupsBtn = wrapper.querySelector('.view-backups-btn');
        viewBackupsBtn.addEventListener('click', function () {
            const backupPaths = getAssistantBackupPaths(wrapper);
            if (!backupPaths.length) {
                return;
            }

            openBackupArtifact('', backupPaths);
        });

        const restoreBackupBtn = wrapper.querySelector('.restore-backup-btn');
        restoreBackupBtn.addEventListener('click', function () {
            const backupPaths = getAssistantBackupPaths(wrapper);
            if (!backupPaths.length) {
                return;
            }

            postMessage({
                type: 'restoreBackupArtifact',
                backupPath: backupPaths[0]
            });
        });

        refreshAssistantBackupStatus(wrapper);
    }
    else {
        wrapper.innerHTML = ''
            + '<div class="chat-role">你</div>'
            + '<div class="chat-text"></div>'
            + '<div class="message-toolbar">'
            + '  <button type="button" class="message-icon-btn copy-message-btn" title="复制" aria-label="复制"></button>'
            + '  <button type="button" class="message-icon-btn retry-message-btn" title="重试" aria-label="重试"></button>'
            + '</div>';

        wrapper.querySelector('.chat-text').textContent = message.content || '';

        const copyBtn = wrapper.querySelector('.copy-message-btn');
        copyBtn.dataset.originalTitle = '复制';
        copyBtn.addEventListener('click', function () {
            onCopyButtonClick(copyBtn, getMessageContent(wrapper));
        });

        const retryBtn = wrapper.querySelector('.retry-message-btn');
        retryBtn.dataset.originalTitle = '重试';
        retryBtn.addEventListener('click', function () {
            if (!state.busy) {
                postMessage({
                    type: 'requestRetryMessage',
                    messageId: message.id || ''
                });
            }
        });

        retryBtn.disabled = state.busy;
    }

    return wrapper;
}
