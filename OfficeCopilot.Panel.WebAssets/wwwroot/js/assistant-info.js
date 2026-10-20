import { ensureAssistantMessage } from './conversation.js';
import { chatList, state } from './state.js';

export function setMessageContent(element, content) {
    if (!element) {
        return;
    }

    element.__messageContent = content || '';
}

export function getMessageContent(element) {
    if (!element) {
        return '';
    }

    return element.__messageContent || '';
}

export function setThinkingContent(element, content) {
    if (!element) {
        return;
    }

    element.__thinkingContent = content || '';
}

export function getThinkingContent(element) {
    if (!element) {
        return '';
    }

    return element.__thinkingContent || '';
}

export function normalizeThinkingContent(content) {
    return String(content || '')
        .replace(/<\s*think\s*>/gi, '')
        .replace(/<\s*\/\s*think\s*>/gi, '')
        .replace(/^\s*(?:thinking\s*process|thought\s*process|reasoning)\s*[：:]\s*/i, '')
        .trim();
}

export function setThinkingCompleted(element, completed) {
    if (!element) {
        return;
    }

    element.__thinkingCompleted = !!completed;
}

export function clearThinkingState(element) {
    if (!element) {
        return;
    }

    setThinkingContent(element, '');
    setThinkingCompleted(element, false);

    const host = element.querySelector('.assistant-thinking');
    if (host) {
        host.innerHTML = '';
    }
}

export function isThinkingCompleted(element) {
    if (!element) {
        return false;
    }

    return !!element.__thinkingCompleted;
}

export function getAssistantInfoItems(element) {
    if (!element) {
        return [];
    }

    if (!element.__assistantInfoItems) {
        element.__assistantInfoItems = [];
    }

    return element.__assistantInfoItems;
}

export function normalizeAssistantInfoItem(text) {
    return String(text || '')
        .replace(/\r/g, ' ')
        .replace(/\n/g, ' ')
        .trim();
}

export function refreshAssistantInfoPanel(element) {
    if (!element) {
        return;
    }

    const panel = element.querySelector('.assistant-hover-panel');
    if (!panel) {
        return;
    }

    const items = getAssistantInfoItems(element);
    panel.innerHTML = '';
    panel.classList.toggle('has-items', !!items.length);

    for (let i = 0; i < items.length; i++) {
        const item = document.createElement('div');
        item.className = 'assistant-hover-item';
        item.textContent = items[i];
        panel.appendChild(item);
    }
}

export function appendAssistantInfoItems(element, items) {
    if (!element || !items || !items.length) {
        return;
    }

    const currentItems = getAssistantInfoItems(element);
    let changed = false;

    for (let i = 0; i < items.length; i++) {
        const normalized = normalizeAssistantInfoItem(items[i]);
        if (!normalized) {
            continue;
        }

        if (currentItems.indexOf(normalized) >= 0) {
            continue;
        }

        currentItems.push(normalized);
        changed = true;
    }

    if (changed) {
        refreshAssistantInfoPanel(element);
    }
}

export function setAssistantInfoItems(element, items) {
    if (!element) {
        return;
    }

    element.__assistantInfoItems = [];
    appendAssistantInfoItems(element, items || []);
    refreshAssistantInfoPanel(element);
}

export function appendAssistantInfo(messageId, items) {
    const targetMessageId = resolveAssistantInfoTargetMessageId(messageId || '');
    if (!targetMessageId) {
        return;
    }

    const entry = ensureAssistantMessage(targetMessageId);
    appendAssistantInfoItems(entry.element, items || []);
}

export function setActiveAssistantMessageId(messageId = '') {
    state.activeAssistantMessageId = messageId;

    if (!state.activeAssistantMessageId || !chatList) {
        return;
    }

    const assistantMessages = chatList.querySelectorAll('.chat-message.assistant[data-message-id]');
    for (let i = 0; i < assistantMessages.length; i++) {
        const element = assistantMessages[i];
        if ((element.dataset.messageId || '') === state.activeAssistantMessageId) {
            continue;
        }

        clearThinkingState(element);
    }
}

export function getLatestAssistantMessageId() {
    if (!chatList) {
        return '';
    }

    const messages = chatList.querySelectorAll('.chat-message.assistant[data-message-id]');
    for (let i = messages.length - 1; i >= 0; i--) {
        const messageId = messages[i].dataset.messageId || '';
        if (messageId) {
            return messageId;
        }
    }

    return '';
}

export function resolveAssistantInfoTargetMessageId(preferredMessageId) {
    return preferredMessageId || state.activeAssistantMessageId || getLatestAssistantMessageId();
}

export function splitAssistantInfoText(text) {
    const normalized = normalizeAssistantInfoItem(text)
        .replace(/；/g, '\n')
        .replace(/;/g, '\n')
        .replace(/。/g, '\n');

    return normalized
        .split('\n')
        .map(function (item) {
            return normalizeAssistantInfoItem(item);
        })
        .filter(function (item) {
            return !!item;
        });
}

export function mapStatusToAssistantInfoItems(text) {
    const value = normalizeAssistantInfoItem(text);
    if (!value) {
        return [];
    }

    if (value.indexOf('已加载宿主上下文') === 0) {
        return splitAssistantInfoText(value.replace(/^已加载宿主上下文[：: ]*/, ''));
    }

    if (value.indexOf('当前未获取到有效宿主上下文') === 0) {
        return ['当前未获取到有效宿主上下文'];
    }

    if (value.indexOf('已注册 ') === 0 && value.indexOf('个工具') > 0) {
        const matched = value.match(/^已注册\s*(\d+)\s*个工具/);
        return [
            matched ? ('工具：' + matched[1] + ' 个') : '工具：已注册',
            '/tools 查看已注册工具',
            '/call <toolName> {json} 直接触发'
        ];
    }

    return [];
}
