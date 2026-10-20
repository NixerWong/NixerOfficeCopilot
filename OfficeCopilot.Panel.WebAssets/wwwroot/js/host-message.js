import { dismissAskUserQuestion, showAskUserQuestion } from './ask-user.js';
import { appendAssistantInfo, mapStatusToAssistantInfoItems } from './assistant-info.js';
import { syncBusyState } from './clipboard.js';
import { handleCodeExecutionBackupState, handleCodeExecutionResult, handleOpenInVbaEditorResult } from './code-blocks.js';
import { appendMessage, renderConversation, showWelcome } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { refreshCodeBlockActions } from './markdown.js';
import { dismissPlanApproval, showPlanApproval } from './plan-approval.js';
import { showExecutionMode } from './execution-mode.js';
import { scrollToBottom } from './scroll.js';
import { hostCapabilities, state, ASSET_BASE_URL } from './state.js';
import { appendAssistantChunk, appendAssistantThinkingChunk, completeAssistantThinking, setAssistantContent, showThinkingState } from './thinking.js';
import { showTodoUpdate } from './todo.js';
import { showToolCallCompleted, showToolCallStarted } from './tool-calls.js';
import { dismissToolConfirmation, showToolConfirmation } from './tool-confirmation.js';
import { showToolConfirmationRequest } from './tool-confirmation-ui.js';

export function handleHostMessage(data) {
    if (!data || !data.type) {
        return;
    }

    // Phase 7C: echo back to C# for file logging
    if (data.type === 'askUserQuestion' || data.type === 'toolConfirmation' || data.type === 'planApproval' || data.type === 'todoUpdate') {
        postMessage({ type: 'traceLog', msgType: data.type, requestId: data.requestId || '' });
    }

    switch (data.type) {
        case 'showWelcome':
            showWelcome();
            break;

        case 'hydrateConversation':
            renderConversation(data.messages || []);
            break;

        case 'appendMessage':
            if (data.message) {
                appendMessage(data.message);
            }
            break;

        case 'resumeAutoFollow':
            scrollToBottom(true);
            break;

        case 'beginAssistantMessage':
            showThinkingState(data.messageId, data.infoItems || []);
            break;

        case 'appendAssistantThinkingChunk':
            appendAssistantThinkingChunk(data.messageId || '', data.chunk || '');
            break;

        case 'completeAssistantThinking':
            completeAssistantThinking(data.messageId || '');
            break;

        case 'appendAssistantChunk':
            appendAssistantChunk(data.messageId, data.chunk || '');
            break;

        case 'completeAssistantMessage':
            setAssistantContent(data.messageId, data.content || '');
            break;

        case 'appendAssistantInfo':
            appendAssistantInfo(data.messageId || '', data.items || []);
            break;

        case 'agentToolCallStarted':
            showToolCallStarted(
                data.assistantMessageId || '',
                data.toolCallId || '',
                data.toolName || '',
                !!data.requiresBackup,
                data.backupPath || '');
            break;

        case 'agentToolCallCompleted':
            showToolCallCompleted(
                data.assistantMessageId || '',
                data.toolCallId || '',
                data.toolName || '',
                !!data.success,
                data.content || '',
                data.errorMessage || '',
                data.backupPath || '');
            break;

        case 'status':
            const infoItems = mapStatusToAssistantInfoItems(data.text || '');
            if (infoItems.length) {
                appendAssistantInfo(data.messageId || '', infoItems);
            }
            else {
                appendMessage({
                    id: 'status_' + Date.now(),
                    role: 'assistant',
                    content: data.text || ''
                });
            }
            break;

        case 'executionMode':
            showExecutionMode(data);
            break;

        case 'brief.message':
            appendAssistantInfo(data.messageId || state.activeAssistantMessageId || '', [data.message || '']);
            break;

        case 'setBusy':
            state.busy = !!data.busy;
            postMessage({ type: 'traceLog', msgType: 'setBusy-' + state.busy, requestId: '' });
            syncBusyState();
            break;

        case 'setCapabilities':
            hostCapabilities.canExecuteVba = !!(data.capabilities && data.capabilities.canExecuteVba);
            hostCapabilities.canExecuteVbaReason = data.capabilities && data.capabilities.canExecuteVbaReason
                ? String(data.capabilities.canExecuteVbaReason)
                : '';
            hostCapabilities.canOpenInVbaEditor = !!(data.capabilities && data.capabilities.canOpenInVbaEditor);
            hostCapabilities.canOpenInVbaEditorReason = data.capabilities && data.capabilities.canOpenInVbaEditorReason
                ? String(data.capabilities.canOpenInVbaEditorReason)
                : '';
            refreshCodeBlockActions();
            break;

        case 'setAssistantAvatar':
            state.assistantAvatarUrl = ASSET_BASE_URL + String(data.avatarPath || 'images/bot.png');
            document.querySelectorAll('.copilot-avatar').forEach(img => {
                img.src = state.assistantAvatarUrl;
                img.style.display = '';
            });
            break;

        case 'openInVbaEditorResult':
            handleOpenInVbaEditorResult(data);
            break;

        case 'codeExecutionBackupState':
            handleCodeExecutionBackupState(data);
            break;

        case 'codeExecutionResult':
            handleCodeExecutionResult(data);
            break;

        case 'submitToolConfirmation':
            handleToolConfirmationSubmit(data);
            break;

        case 'requestToolConfirmation':
            showToolConfirmationRequest(data);
            break;

        case 'askUserQuestion':
            showAskUserQuestion(data);
            break;

        case 'planApproval':
            showPlanApproval(data);
            break;

        case 'toolConfirmation':
            showToolConfirmation(data);
            break;

        case 'todoUpdate':
            showTodoUpdate(data);
            break;

        case 'userInteractionCompleted':
            dismissAskUserQuestion(data.requestId || '');
            dismissPlanApproval(data.requestId || '');
            dismissToolConfirmation(data.requestId || '');
            break;
    }
}

// ================================================================
//  Phase 7C: AskUserQuestion — inline interactive question rendering
// ================================================================
