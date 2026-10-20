import { getLatestAssistantMessageId, setActiveAssistantMessageId, setAssistantInfoItems } from './assistant-info.js';
import { createMessageElement, ensureAssistantTextBlock, setAssistantTextBlockContent } from './assistant-message.js';
import { syncBusyState } from './clipboard.js';
import { setCodeExecutionStates, syncCodeExecutionBackups } from './code-execution-state.js';
import { clearConversation, ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { chatList, messageMap, state } from './state.js';
import { setAssistantContent } from './thinking.js';
import { getToolCallListElement, hydrateAssistantToolCalls, renderAssistantContentParts } from './tool-calls.js';

export function showWelcome() {
    clearConversation();
    scrollToBottom(true);
}

export function renderConversation(messages) {
    clearConversation();
    setActiveAssistantMessageId('');

    if (!messages || !messages.length) {
        scrollToBottom(true);
        return;
    }

    messages.forEach(function (message) {
        const element = createMessageElement(message);
        chatList.appendChild(element);

        if (message.id) {
            messageMap.set(message.id, {
                element: element,
                role: message.role,
                content: message.content || ''
            });
        }

        if (message.role === 'assistant') {
            if (message.id) {
                setActiveAssistantMessageId(message.id);
            }

            setAssistantInfoItems(element, message.infoItems || []);

            // 优先使用 ContentParts 按正确顺序交错渲染文本和工具调用
            if (message.contentParts && message.contentParts.length) {
                renderAssistantContentParts(element, message);
            }
            else {
                // 回退：旧数据无 ContentParts
                if (message.id) {
                    setAssistantContent(message.id, message.content || '');
                }
                else if (message.content) {
                    const block = ensureAssistantTextBlock(element);
                    setAssistantTextBlockContent(block, message.content || '');
                    syncCodeExecutionBackups(element);
                }

                hydrateAssistantToolCalls(element, message.toolCalls || [], false);
            }
        }
    });

    ensureBottomAnchor();
    syncBusyState();
    scrollToBottom(true);
}

export function appendMessage(message) {
    const element = createMessageElement(message);
    chatList.appendChild(element);

    if (message.role === 'assistant' && message.id) {
        setActiveAssistantMessageId(message.id);
    }

    if (message.id) {
        messageMap.set(message.id, {
            element: element,
            role: message.role,
            content: message.content || ''
        });
    }

    if (message.role === 'assistant' && message.content) {
        if (message.id) {
            setAssistantContent(message.id, message.content || '');
        }
        else {
            const block = ensureAssistantTextBlock(element);
            setAssistantTextBlockContent(block, message.content || '');
            syncCodeExecutionBackups(element);
        }
    }

    if (message.role === 'assistant') {
        setAssistantInfoItems(element, message.infoItems || []);
        setCodeExecutionStates(element, message.codeExecutions || []);
    }

    ensureBottomAnchor();
    syncBusyState();
    scrollToBottom();
}

export function ensureAssistantMessage(messageId) {
    let entry = messageMap.get(messageId);
    if (entry) {
        return entry;
    }

    appendMessage({
        id: messageId,
        role: 'assistant',
        content: ''
    });

    return messageMap.get(messageId);
}

// Phase 7C: append interaction component to current assistant message stream
export function appendToAssistantStream(container) {
    var msgId = state.activeAssistantMessageId || getLatestAssistantMessageId();
    if (msgId) {
        var entry = ensureAssistantMessage(msgId);
        var stream = getToolCallListElement(entry.element);
        if (stream) {
            stream.appendChild(container);
            return;
        }
    }
    appendToAssistantStream(container);
}
