import { copyTextToClipboard, flashButtonState } from './clipboard.js';
import { assignCodeBlockIndexes, upsertCodeExecutionState } from './code-execution-state.js';
import { ensureAssistantMessage } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { ensureCodeBlockBackupElements } from './markdown.js';
import { pendingCodeExecutionRequests, pendingOpenInVbaEditorRequests, state } from './state.js';
import { appendCodeExecutionFailureMessage } from './tool-confirmation.js';
import { t } from './i18n.js';

export function parseFenceInfo(info) {
    const value = String(info || '').trim();
    if (!value) {
        return {
            languageKey: 'text',
            displayLabel: 'text'
        };
    }

    const firstToken = value.split(/\s+/, 1)[0] || value;
    return {
        languageKey: firstToken.trim().toLowerCase(),
        displayLabel: value.toLowerCase()
    };
}

export function isRunnableVbaLanguage(language) {
    const value = String(language || '').trim().toLowerCase();
    return value === 'vba'
        || value === 'vb'
        || value === 'visual-basic'
        || value === 'visualbasic';
}

export function setCodeExecutionButtonBusy(button, isBusy) {
    if (!button) {
        return;
    }

    button.disabled = !!isBusy;
    button.classList.toggle('is-busy', !!isBusy);

    if (isBusy) {
        button.title = t('Web.code.running');
        button.setAttribute('aria-label', button.title);
        button.dataset.l10nTitle = 'Web.code.running';
        button.dataset.l10nAria = 'Web.code.running';
    }
    else {
        const originalTitle = t(button.dataset.l10nOriginalTitle || 'Web.action.runVba');
        button.title = originalTitle;
        button.setAttribute('aria-label', originalTitle);
        button.dataset.l10nTitle = button.dataset.l10nOriginalTitle || 'Web.action.runVba';
        button.dataset.l10nAria = button.dataset.l10nTitle;
    }
}

export function resetCodeBlockBackupState(codeBlock) {
    const ui = ensureCodeBlockBackupElements(codeBlock);
    if (!ui) {
        return;
    }

    codeBlock.__backupPath = '';
    codeBlock.__backupPending = false;
    ui.pendingEl.hidden = true;
    ui.viewBtn.hidden = true;
}

export function setCodeBlockBackupPending(codeBlock, isPending) {
    const ui = ensureCodeBlockBackupElements(codeBlock);
    if (!ui) {
        return;
    }

    ui.pendingEl.hidden = !isPending;
    codeBlock.__backupPending = !!isPending;

    if (isPending) {
        codeBlock.__backupPath = '';
        ui.viewBtn.hidden = true;
    }
}

export function setCodeBlockBackupCompleted(codeBlock, backupPath) {
    const ui = ensureCodeBlockBackupElements(codeBlock);
    if (!ui) {
        return;
    }

    codeBlock.__backupPath = String(backupPath || '').trim();
    codeBlock.__backupPending = false;
    ui.pendingEl.hidden = true;
    ui.viewBtn.hidden = !codeBlock.__backupPath;
}

export function getCodeBlockBackupPath(codeBlock) {
    if (!codeBlock) {
        return '';
    }

    return codeBlock.__backupPath || '';
}

export function openBackupArtifact(backupPath, backupPaths) {
    const normalizedPaths = Array.isArray(backupPaths)
        ? backupPaths.filter(function (item) {
            return !!String(item || '').trim();
        })
        : [];

    if (!String(backupPath || '').trim() && !normalizedPaths.length) {
        return;
    }

    postMessage({
        type: 'openBackupArtifact',
        backupPath: String(backupPath || ''),
        backupPaths: normalizedPaths
    });
}

export function onRunCodeButtonClick(button) {
    if (!button || button.disabled) {
        return;
    }

    const codeBlock = button.closest('.code-block');
    if (!codeBlock) {
        return;
    }

    const language = codeBlock.getAttribute('data-language') || '';
    if (!isRunnableVbaLanguage(language)) {
        return;
    }

    const codeElement = codeBlock.querySelector('pre > code');
    const code = codeElement ? (codeElement.textContent || '') : '';
    if (!code) {
        return;
    }

    const messageElement = codeBlock.closest('.chat-message.assistant[data-message-id]');
    const messageId = messageElement ? (messageElement.dataset.messageId || '') : '';
    assignCodeBlockIndexes(messageElement);
    const codeBlockIndex = parseInt(codeBlock.dataset.codeBlockIndex || '-1', 10);
    const requestId = 'run_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 8);

    resetCodeBlockBackupState(codeBlock);
    setCodeBlockBackupPending(codeBlock, true);

    pendingCodeExecutionRequests.set(requestId, {
        button: button,
        codeBlock: codeBlock,
        messageId: messageId
    });

    setCodeExecutionButtonBusy(button, true);

    postMessage({
        type: 'executeCodeBlock',
        requestId: requestId,
        messageId: messageId,
        codeBlockIndex: isFinite(codeBlockIndex) ? codeBlockIndex : -1,
        language: language,
        code: code
    });
}

export function onOpenVbaEditorButtonClick(button) {
    if (!button || button.disabled) {
        return;
    }

    const codeBlock = button.closest('.code-block');
    if (!codeBlock) {
        return;
    }

    const language = codeBlock.getAttribute('data-language') || '';
    if (!isRunnableVbaLanguage(language)) {
        return;
    }

    const codeElement = codeBlock.querySelector('pre > code');
    const code = codeElement ? (codeElement.textContent || '') : '';
    if (!code) {
        return;
    }

    const messageElement = codeBlock.closest('.chat-message.assistant[data-message-id]');
    const messageId = messageElement ? (messageElement.dataset.messageId || '') : '';
    assignCodeBlockIndexes(messageElement);
    const codeBlockIndex = parseInt(codeBlock.dataset.codeBlockIndex || '-1', 10);
    const requestId = 'open_vbe_' + Date.now().toString(36) + '_' + Math.random().toString(36).slice(2, 8);

    pendingOpenInVbaEditorRequests.set(requestId, {
        button: button,
        codeBlock: codeBlock,
        messageId: messageId
    });

    setCodeExecutionButtonBusy(button, true);

    postMessage({
        type: 'openCodeInVbaEditor',
        requestId: requestId,
        messageId: messageId,
        codeBlockIndex: isFinite(codeBlockIndex) ? codeBlockIndex : -1,
        language: language,
        code: code
    });
}

export function onCodeBlockCopyButtonClick(button) {
    if (!button || button.disabled) {
        return;
    }

    const codeBlock = button.closest('.code-block');
    if (!codeBlock) {
        return;
    }

    const codeElement = codeBlock.querySelector('pre > code');
    const code = codeElement ? (codeElement.textContent || '') : '';
    if (!code) {
        return;
    }

    copyTextToClipboard(code)
        .then(function () {
            flashButtonState(button, 'is-success', t('Web.clipboard.copied'));
        })
        .catch(function () {
            flashButtonState(button, 'is-error', t('Web.clipboard.copyFailed'));
        });
}

export function handleCodeExecutionResult(data) {
    const requestId = data && data.requestId ? data.requestId : '';
    const context = requestId ? pendingCodeExecutionRequests.get(requestId) : null;
    const button = context ? context.button : null;
    const codeBlock = context ? context.codeBlock : null;
    const messageId = context ? context.messageId : '';
    const success = !!(data && data.success);
    const message = data && data.message ? String(data.message) : '';
    const backupPath = data && data.backupPath ? String(data.backupPath) : '';

    if (requestId) {
        pendingCodeExecutionRequests.delete(requestId);
    }

    if (codeBlock) {
        setCodeBlockBackupPending(codeBlock, false);

        if (backupPath) {
            setCodeBlockBackupCompleted(codeBlock, backupPath);

            const messageElement = messageId
                ? ensureAssistantMessage(messageId).element
                : codeBlock.closest('.chat-message.assistant[data-message-id]');

            upsertCodeExecutionState(messageElement, {
                codeBlockIndex: codeBlock.dataset.codeBlockIndex,
                language: codeBlock.getAttribute('data-language') || '',
                backupPath: backupPath
            });
        }
    }

    if (button) {
        setCodeExecutionButtonBusy(button, false);

        flashButtonState(
            button,
            success ? 'is-success' : 'is-error',
            success ? t('Web.code.ran') : (message || t('Web.code.runFailed')));
    }

    if (!success) {
        appendCodeExecutionFailureMessage(message || t('Web.code.runFailed'));
    }
}

export function handleOpenInVbaEditorResult(data) {
    const requestId = data && data.requestId ? String(data.requestId) : '';
    const context = requestId ? pendingOpenInVbaEditorRequests.get(requestId) : null;
    const button = context ? context.button : null;
    const success = !!(data && data.success);
    const message = data && data.message ? String(data.message) : '';

    if (requestId) {
        pendingOpenInVbaEditorRequests.delete(requestId);
    }

    if (button) {
        setCodeExecutionButtonBusy(button, false);
        flashButtonState(
            button,
            success ? 'is-success' : 'is-error',
            success ? t('Web.code.opened') : (message || t('Web.code.openFailed')));
    }
}

export function handleCodeExecutionBackupState(data) {
    const requestId = data && data.requestId ? String(data.requestId) : '';
    const state = data && data.state ? String(data.state).toLowerCase() : '';
    const backupPath = data && data.backupPath ? String(data.backupPath) : '';
    const context = requestId ? pendingCodeExecutionRequests.get(requestId) : null;
    const codeBlock = context ? context.codeBlock : null;

    if (!codeBlock) {
        return;
    }

    if (state === 'pending') {
        setCodeBlockBackupPending(codeBlock, true);
        return;
    }

    if (state === 'completed') {
        setCodeBlockBackupCompleted(codeBlock, backupPath);

        const messageElement = context && context.messageId
            ? ensureAssistantMessage(context.messageId).element
            : codeBlock.closest('.chat-message.assistant[data-message-id]');

        upsertCodeExecutionState(messageElement, {
            codeBlockIndex: codeBlock.dataset.codeBlockIndex,
            language: codeBlock.getAttribute('data-language') || '',
            backupPath: backupPath
        });
        return;
    }

    if (state === 'failed') {
        setCodeBlockBackupPending(codeBlock, false);
    }
}

export function onViewCodeBackupButtonClick(button) {
    if (!button || button.disabled) {
        return;
    }

    const codeBlock = button.closest('.code-block');
    const backupPath = getCodeBlockBackupPath(codeBlock);
    if (!backupPath) {
        return;
    }

    openBackupArtifact(backupPath, []);
}
