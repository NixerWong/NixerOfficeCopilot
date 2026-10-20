import { onCodeBlockCopyButtonClick, onOpenVbaEditorButtonClick, onRunCodeButtonClick, onViewCodeBackupButtonClick } from './code-blocks.js';
import { showWelcome } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { handleHostMessage } from './host-message.js';
import { ensureBottomAnchor, isNearBottom, updateScrollToBottomButton } from './scroll.js';
import { chatList, state } from './state.js';
import { onToolConfirmationConfirmButtonClick } from './tool-confirmation-ui.js';

if (chatList) {
    ensureBottomAnchor();

    chatList.addEventListener('scroll', function () {
        state.shouldAutoFollow = isNearBottom();
        updateScrollToBottomButton();
    });

    chatList.addEventListener('click', function (event) {
        const target = event.target;

        const viewCodeBackupButton = target && target.closest ? target.closest('.view-code-backup-btn') : null;
        if (viewCodeBackupButton) {
            onViewCodeBackupButtonClick(viewCodeBackupButton);
            return;
        }

        const copyCodeButton = target && target.closest ? target.closest('.copy-code-btn') : null;
        if (copyCodeButton) {
            onCodeBlockCopyButtonClick(copyCodeButton);
            return;
        }

        const runButton = target && target.closest ? target.closest('.run-code-btn') : null;
        if (runButton) {
            onRunCodeButtonClick(runButton);
            return;
        }

        const openVbaEditorButton = target && target.closest ? target.closest('.open-vba-editor-btn') : null;
        if (openVbaEditorButton) {
            onOpenVbaEditorButtonClick(openVbaEditorButton);
            return;
        }

        const toolConfirmationButton = target && target.closest ? target.closest('.tool-confirmation-confirm-btn') : null;
        if (toolConfirmationButton) {
            onToolConfirmationConfirmButtonClick(toolConfirmationButton);
            return;
        }
    });
}

if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', function (event) {
        handleHostMessage(event.data);
    });

    postMessage({ type: 'ready' });
}
else {
    showWelcome();
}
