import { resetCodeBlockBackupState, setCodeBlockBackupCompleted } from './code-blocks.js';
import { ensureCodeBlockBackupElements } from './markdown.js';
import { state } from './state.js';

export function getToolCallStateMap(element) {
    if (!element) {
        return null;
    }

    if (!element.__toolCallStateMap) {
        element.__toolCallStateMap = new Map();
    }

    return element.__toolCallStateMap;
}

export function getCodeExecutionStates(element) {
    if (!element) {
        return [];
    }

    if (!element.__codeExecutionStates) {
        element.__codeExecutionStates = [];
    }

    return element.__codeExecutionStates;
}

export function normalizeCodeExecutionState(state) {
    if (!state) {
        return null;
    }

    const codeBlockIndex = parseInt(state.codeBlockIndex, 10);
    const backupPath = String(state.backupPath || '').trim();
    const language = String(state.language || '').trim().toLowerCase();

    if (!isFinite(codeBlockIndex) || codeBlockIndex < 0 || !backupPath) {
        return null;
    }

    return {
        codeBlockIndex: codeBlockIndex,
        language: language,
        backupPath: backupPath
    };
}

export function setCodeExecutionStates(element, states) {
    if (!element) {
        return;
    }

    const normalizedStates = [];
    const source = Array.isArray(states) ? states : [];

    for (let i = 0; i < source.length; i++) {
        const normalized = normalizeCodeExecutionState(source[i]);
        if (!normalized) {
            continue;
        }

        const existingIndex = normalizedStates.findIndex(function (item) {
            return item.codeBlockIndex === normalized.codeBlockIndex;
        });

        if (existingIndex >= 0) {
            normalizedStates[existingIndex] = normalized;
        }
        else {
            normalizedStates.push(normalized);
        }
    }

    element.__codeExecutionStates = normalizedStates;
    syncCodeExecutionBackups(element);
}

export function upsertCodeExecutionState(element, state) {
    if (!element) {
        return;
    }

    const normalized = normalizeCodeExecutionState(state);
    if (!normalized) {
        return;
    }

    const states = getCodeExecutionStates(element);
    let updated = false;

    for (let i = 0; i < states.length; i++) {
        if (states[i].codeBlockIndex === normalized.codeBlockIndex) {
            states[i] = normalized;
            updated = true;
            break;
        }
    }

    if (!updated) {
        states.push(normalized);
    }

    syncCodeExecutionBackups(element);
}

export function assignCodeBlockIndexes(element) {
    if (!element) {
        return [];
    }

    const codeBlocks = element.querySelectorAll('.code-block');
    for (let i = 0; i < codeBlocks.length; i++) {
        codeBlocks[i].dataset.codeBlockIndex = String(i);
        ensureCodeBlockBackupElements(codeBlocks[i]);
    }

    return codeBlocks;
}

export function syncCodeExecutionBackups(element) {
    if (!element) {
        return;
    }

    const states = getCodeExecutionStates(element);
    const codeBlocks = assignCodeBlockIndexes(element);
    const stateMap = new Map();

    for (let i = 0; i < states.length; i++) {
        stateMap.set(states[i].codeBlockIndex, states[i]);
    }

    for (let i = 0; i < codeBlocks.length; i++) {
        const codeBlock = codeBlocks[i];
        if (codeBlock.__backupPending) {
            continue;
        }

        const codeBlockIndex = parseInt(codeBlock.dataset.codeBlockIndex || '-1', 10);
        const state = stateMap.get(codeBlockIndex);

        resetCodeBlockBackupState(codeBlock);

        if (state && state.backupPath) {
            setCodeBlockBackupCompleted(codeBlock, state.backupPath);
        }
    }
}

export function getAssistantPendingBackupCount(element) {
    if (!element) {
        return 0;
    }

    return element.__assistantPendingBackupCount || 0;
}

export function setAssistantPendingBackupCount(element, count) {
    if (!element) {
        return;
    }

    element.__assistantPendingBackupCount = count > 0 ? count : 0;
}

export function getAssistantBackupPaths(element) {
    if (!element) {
        return [];
    }

    if (!element.__assistantBackupPaths) {
        element.__assistantBackupPaths = [];
    }

    return element.__assistantBackupPaths;
}

export function refreshAssistantBackupStatus(element) {
    if (!element) {
        return;
    }

    const host = element.querySelector('.assistant-backup-status');
    if (!host) {
        return;
    }

    const pendingEl = host.querySelector('.assistant-backup-pending');
    const viewBtn = host.querySelector('.view-backups-btn');
    const restoreBtn = host.querySelector('.restore-backup-btn');
    const pendingCount = getAssistantPendingBackupCount(element);
    const backupPaths = getAssistantBackupPaths(element);
    const hasPending = pendingCount > 0;
    const hasBackups = backupPaths.length > 0;

    host.hidden = !hasPending && !hasBackups;

    if (pendingEl) {
        pendingEl.hidden = !hasPending;
    }

    if (restoreBtn) {
        restoreBtn.hidden = !hasBackups;
    }

    if (viewBtn) {
        viewBtn.hidden = !hasBackups;

        const title = backupPaths.length > 1
            ? '查看备份（' + backupPaths.length + '）'
            : '查看备份';

        viewBtn.title = title;
        viewBtn.setAttribute('aria-label', title);
    }
}

export function clearAssistantBackupState(element) {
    if (!element) {
        return;
    }

    setAssistantPendingBackupCount(element, 0);
    element.__assistantBackupPaths = [];
    refreshAssistantBackupStatus(element);
}

export function appendAssistantBackupPath(element, backupPath) {
    if (!element) {
        return;
    }

    const normalized = String(backupPath || '').trim();
    if (!normalized) {
        refreshAssistantBackupStatus(element);
        return;
    }

    const paths = getAssistantBackupPaths(element);
    if (paths.indexOf(normalized) >= 0) {
        refreshAssistantBackupStatus(element);
        return;
    }

    paths.push(normalized);
    refreshAssistantBackupStatus(element);
}

export function incrementAssistantPendingBackup(element) {
    if (!element) {
        return;
    }

    setAssistantPendingBackupCount(element, getAssistantPendingBackupCount(element) + 1);
    refreshAssistantBackupStatus(element);
}

export function completeAssistantPendingBackup(element, backupPath) {
    if (!element) {
        return;
    }

    setAssistantPendingBackupCount(element, getAssistantPendingBackupCount(element) - 1);

    if (backupPath) {
        appendAssistantBackupPath(element, backupPath);
        return;
    }

    refreshAssistantBackupStatus(element);
}

export function hydrateAssistantBackupState(element, toolCalls) {
    if (!element) {
        return;
    }

    setAssistantPendingBackupCount(element, 0);
    element.__assistantBackupPaths = [];

    if (toolCalls && toolCalls.length) {
        for (let i = 0; i < toolCalls.length; i++) {
            const toolCall = toolCalls[i];
            if (!toolCall) {
                continue;
            }

            if (toolCall.requiresBackup && String(toolCall.status || '').toLowerCase() === 'running') {
                setAssistantPendingBackupCount(element, getAssistantPendingBackupCount(element) + 1);
            }

            if (toolCall.backupPath) {
                appendAssistantBackupPath(element, toolCall.backupPath);
            }
        }
    }

    refreshAssistantBackupStatus(element);
}
