import { ensureAssistantMessage } from './conversation.js';
import { chatList, state } from './state.js';
import { t } from './i18n.js';

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
        item.textContent = localizeAssistantInfoItem(items[i]);
        panel.appendChild(item);
    }
}

function localizeAssistantInfoItem(item) {
    const value = String(item || '');
    if (value === '当前未获取到有效宿主上下文' || value === 'No valid host context is available') {
        return t('Web.info.noHostContext');
    }
    if (value === '已加载宿主上下文' || value === 'Host context loaded') {
        return t('Web.info.hostContextLoaded');
    }
    if (value === '/tools 查看已注册工具' || value === '/tools to list registered tools') {
        return t('Web.info.toolsHelp');
    }
    if (value === '/call <toolName> {json} 直接触发' || value === '/call <toolName> {json} to invoke directly') {
        return t('Web.info.callHelp');
    }

    const labels = [
        [/^当前活动(?:工作簿|文档)：/, 'Web.info.activeDocument'],
        [/^Active document:\s*/, 'Web.info.activeDocument'],
        [/^当前工作表：/, 'Web.info.worksheet'],
        [/^Current worksheet:\s*/, 'Web.info.worksheet'],
        [/^当前选区：/, 'Web.info.selection'],
        [/^Current selection:\s*/, 'Web.info.selection']
    ];
    for (let i = 0; i < labels.length; i++) {
        const match = value.match(labels[i][0]);
        if (match) {
            return t(labels[i][1], value.slice(match[0].length));
        }
    }

    const count = value.match(/^工具：\s*(.+?)\s*个$/) || value.match(/^Tools:\s*(.+)$/);
    if (count) {
        return t('Web.info.toolsCount', count[1]);
    }
    if (value === '工具：已注册' || value === 'Tools: registered') {
        return t('Web.info.toolsRegistered');
    }
    return value;
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

    window.addEventListener('panel-locale-changed', function () {
        document.querySelectorAll('.chat-message.assistant').forEach(refreshAssistantInfoPanel);
    });

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
        return [t('Web.info.noHostContext')];
    }

    if (value.indexOf('已注册 ') === 0 && value.indexOf('个工具') > 0) {
        const matched = value.match(/^已注册\s*(\d+)\s*个工具/);
        return [
            matched ? t('Web.info.toolsCount', matched[1]) : t('Web.info.toolsRegistered'),
            t('Web.info.toolsHelp'),
            t('Web.info.callHelp')
        ];
    }

    return [];
}
