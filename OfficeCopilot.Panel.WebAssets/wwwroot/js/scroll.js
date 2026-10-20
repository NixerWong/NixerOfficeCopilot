import { bottomAnchor, chatList, messageMap, pendingCodeExecutionRequests, pendingOpenInVbaEditorRequests, state } from './state.js';

export function createAssistantRole() {
    return ''
        + '<div class="chat-role assistant-role-row">'
        + '  <div class="assistant-role-anchor" tabindex="0">'
        + '    <span class="copilot-badge">'
        + '      <img class="copilot-avatar" src="' + state.assistantAvatarUrl + '" alt="bot" onerror="this.onerror=null;this.style.display=\'none\'" />'
        + '      <span>Nixer Copilot</span>'
        + '    </span>'
        + '    <div class="assistant-hover-panel"></div>'
        + '  </div>'
        + '  <div class="assistant-backup-status" hidden>'
        + '      <span class="assistant-backup-pending" hidden>备份中...</span>'
        + '      <button type="button" class="message-icon-btn restore-backup-btn" title="撤销本次修改（从备份生成还原副本，不覆盖原文件）" aria-label="撤销本次修改" data-original-title="撤销本次修改" hidden></button>'
        + '      <button type="button" class="message-icon-btn view-backups-btn" title="查看备份" aria-label="查看备份" data-original-title="查看备份" hidden></button>'
        + '  </div>'
        + '</div>';
}

export function ensureBottomAnchor() {
    if (!chatList) {
        return;
    }

    bottomAnchor.className = 'chat-bottom-anchor';
    if (bottomAnchor.parentNode !== chatList || chatList.lastChild !== bottomAnchor) {
        chatList.appendChild(bottomAnchor);
    }
}

export function createScrollToBottomButton() {
    if (state.scrollToBottomButton) {
        return;
    }

    state.scrollToBottomButton = document.createElement('button');
    state.scrollToBottomButton.type = 'button';
    state.scrollToBottomButton.className = 'scroll-to-bottom-btn';
    state.scrollToBottomButton.title = '追底';
    state.scrollToBottomButton.setAttribute('aria-label', '滚动到底部并恢复自动跟随');
    state.scrollToBottomButton.innerHTML = ''
        + '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">'
        + '  <path d="M8 3.5V12.5M8 12.5L5 9.5M8 12.5L11 9.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>'
        + '</svg>';
    state.scrollToBottomButton.addEventListener('click', function () {
        scrollToBottom(true);
    });

    document.body.appendChild(state.scrollToBottomButton);
}

export function updateScrollToBottomButton() {
    if (!state.scrollToBottomButton) {
        createScrollToBottomButton();
    }

    if (state.shouldAutoFollow) {
        state.scrollToBottomButton.classList.remove('is-visible');
    } else {
        state.scrollToBottomButton.classList.add('is-visible');
    }
}

export function clearConversation() {
    if (!chatList) {
        return;
    }

    chatList.innerHTML = '';
    messageMap.clear();
    pendingCodeExecutionRequests.clear();
    pendingOpenInVbaEditorRequests.clear();
    ensureBottomAnchor();
}

export function isNearBottom() {
    if (!chatList) {
        return true;
    }

    return chatList.scrollHeight - chatList.scrollTop - chatList.clientHeight <= 64;
}

export function performScrollToBottom() {
    if (!chatList) {
        return;
    }

    ensureBottomAnchor();

    bottomAnchor.scrollIntoView({
        block: 'end',
        inline: 'nearest'
    });

    chatList.scrollTop = chatList.scrollHeight;
}

export function scrollToBottom(force) {
    if (!chatList) {
        return;
    }

    if (force) {
        state.shouldAutoFollow = true;
        updateScrollToBottomButton();
    }

    if (!state.shouldAutoFollow) {
        return;
    }

    ensureBottomAnchor();

    if (state.scrollFrameToken) {
        cancelAnimationFrame(state.scrollFrameToken);
    }

    state.scrollFrameToken = requestAnimationFrame(function () {
        performScrollToBottom();

        requestAnimationFrame(function () {
            performScrollToBottom();

            requestAnimationFrame(function () {
                performScrollToBottom();
                state.scrollFrameToken = 0;
            });
        });
    });
}
