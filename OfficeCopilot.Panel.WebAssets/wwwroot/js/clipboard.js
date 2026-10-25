import { refreshAssistantWaitingIndicator } from './assistant-message.js';
import { chatList, state } from './state.js';
import { t } from './i18n.js';

export function copyTextToClipboard(text) {
    const value = String(text || '');
    if (!value) {
        return Promise.reject(new Error('empty'));
    }

    if (window.isSecureContext
        && navigator.clipboard
        && typeof navigator.clipboard.writeText === 'function') {
        return navigator.clipboard.writeText(value);
    }

    return Promise.reject(new Error('clipboard api unavailable'));
}

export function flashButtonState(button, state, title) {
    if (!button) {
        return;
    }

    button.classList.remove('is-success', 'is-error');

    if (state) {
        button.classList.add(state);
    }

    if (title) {
        button.title = title;
        button.setAttribute('aria-label', title);
    }

    window.setTimeout(function () {
        button.classList.remove('is-success', 'is-error');

        const originalTitle = button.dataset.l10nOriginalTitle
            ? t(button.dataset.l10nOriginalTitle)
            : (button.dataset.originalTitle || '');
        if (originalTitle) {
            button.title = originalTitle;
            button.setAttribute('aria-label', originalTitle);
        }
    }, 1200);
}

export function onCopyButtonClick(button, text) {
    copyTextToClipboard(text)
        .then(function () {
            flashButtonState(button, 'is-success', t('Web.clipboard.copied'));
        })
        .catch(function () {
            flashButtonState(button, 'is-error', t('Web.clipboard.copyFailed'));
        });
}

export function syncBusyState() {
    if (!chatList) {
        return;
    }

    const retryButtons = chatList.querySelectorAll('.retry-message-btn');
    for (let i = 0; i < retryButtons.length; i++) {
        retryButtons[i].disabled = state.busy;
    }

    const assistantMessages = chatList.querySelectorAll('.chat-message.assistant[data-message-id]');
    for (let i = 0; i < assistantMessages.length; i++) {
        refreshAssistantWaitingIndicator(assistantMessages[i], state.busy ? 0 : -1);
    }
}
