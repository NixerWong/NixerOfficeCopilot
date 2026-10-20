import { appendToAssistantStream } from './conversation.js';
import { escapeHtml } from './markdown.js';
import { ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { chatList } from './state.js';

export function showTodoUpdate(data) {
    if (!chatList) {
        return;
    }

    var todos;
    try {
        todos = typeof data.todos === 'string'
            ? JSON.parse(data.todos)
            : (data.todos || []);
    }
    catch (e) {
        todos = [];
    }

    // Remove any existing todo list
    var existingTodos = document.querySelectorAll('.todo-list-inline');
    for (var i = 0; i < existingTodos.length; i++) {
        if (existingTodos[i].parentNode) {
            existingTodos[i].parentNode.removeChild(existingTodos[i]);
        }
    }

    if (!todos || !todos.length) {
        return;
    }

    var statusIcons = {
        pending: '\u26aa',
        in_progress: '\ud83d\udd35',
        completed: '\u2705'
    };

    var itemsHtml = '';
    for (var ti = 0; ti < todos.length; ti++) {
        var todo = todos[ti];
        var status = todo.status || 'pending';
        var icon = statusIcons[status] || '\u26aa';
        itemsHtml += ''
            + '<li class="todo-item status-' + status + '">'
            + '  <span class="todo-status-icon status-' + status + '">' + icon + '</span>'
            + '  <span class="todo-item-text">' + escapeHtml(todo.content || '') + '</span>'
            + '</li>';
    }

    var container = document.createElement('div');
    container.className = 'todo-list-inline';
    container.innerHTML = ''
        + '<div class="todo-list-header">'
        + '  <span>\ud83d\udccb \u4efb\u52a1\u5217\u8868</span>'
        + '</div>'
        + '<ul class="todo-list-body">' + itemsHtml + '</ul>';

    appendToAssistantStream(container);
    ensureBottomAnchor();
    scrollToBottom();
}

// ================================================================
//  Phase 7C: Plan Approval — inline approval component
// ================================================================
