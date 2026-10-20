import { isRunnableVbaLanguage, parseFenceInfo } from './code-blocks.js';
import { chatList, hostCapabilities } from './state.js';

export function escapeHtml(value) {
    return String(value || '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

export function escapeAttribute(value) {
    return escapeHtml(value).replace(/`/g, '&#96;');
}

export function renderInlineMarkdown(text) {
    const inlineTokens = [];
    let safeText = String(text || '');

    safeText = safeText.replace(/<br\s*\/?>/gi, '@@INLINE_BR@@');

    safeText = safeText.replace(/`([^`]+)`/g, function (_, code) {
        const token = '@@INLINE_CODE_' + inlineTokens.length + '@@';
        inlineTokens.push('<code>' + escapeHtml(code) + '</code>');
        return token;
    });

    safeText = escapeHtml(safeText);
    safeText = safeText.replace(/@@INLINE_BR@@/g, '<br />');

    safeText = safeText.replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, function (_, label, url) {
        return '<a href="' + escapeAttribute(url) + '" target="_blank" rel="noreferrer noopener">' + escapeHtml(label) + '</a>';
    });

    safeText = safeText.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    safeText = safeText.replace(/(^|[^\*])\*([^\*]+)\*/g, '$1<em>$2</em>');
    safeText = safeText.replace(/~~([^~]+)~~/g, '<del>$1</del>');

    for (let i = 0; i < inlineTokens.length; i++) {
        safeText = safeText.replace('@@INLINE_CODE_' + i + '@@', inlineTokens[i]);
    }

    return safeText;
}

export function isTableSeparator(line) {
    return /^\s*\|?(\s*:?-{3,}:?\s*\|)+\s*:?-{3,}:?\s*\|?\s*$/.test(line || '');
}

export function parseTableCells(line) {
    return String(line || '')
        .trim()
        .replace(/^\|/, '')
        .replace(/\|$/, '')
        .split('|')
        .map(function (cell) {
            return cell.trim();
        });
}

export function findInlineTableSeparator(line) {
    const source = String(line || '');

    for (let i = 0; i < source.length; i++) {
        if (source.charAt(i) !== '|') {
            continue;
        }

        const matched = source.slice(i).match(/^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?/);
        if (!matched) {
            continue;
        }

        if (i > 0 && source.slice(0, i).indexOf('|') < 0) {
            continue;
        }

        return {
            index: i,
            length: matched[0].length
        };
    }

    return null;
}

export function normalizeMarkdownTables(text) {
    const source = String(text || '');
    if (!source || source.indexOf('|') < 0) {
        return source;
    }

    const lines = source.split('\n');
    const normalizedLines = [];
    let inFence = false;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];

        if (/^\s*```/.test(line)) {
            normalizedLines.push(line);
            inFence = !inFence;
            continue;
        }

        if (inFence || !line) {
            normalizedLines.push(line);
            continue;
        }

        let remaining = line;
        let splitCount = 0;

        while (remaining && splitCount < 8) {
            const separator = findInlineTableSeparator(remaining);
            if (!separator) {
                break;
            }

            const before = remaining.slice(0, separator.index).trimEnd();
            const separatorLine = remaining.slice(separator.index, separator.index + separator.length).trim();
            const after = remaining.slice(separator.index + separator.length).trimStart();

            if (before) {
                normalizedLines.push(before);
            }

            normalizedLines.push(separatorLine);
            remaining = after;
            splitCount++;
        }

        if (remaining) {
            normalizedLines.push(remaining);
        }
    }

    return normalizedLines.join('\n');
}

export function renderCodeBlock(info, code) {
    const fence = parseFenceInfo(info);
    const canRun = hostCapabilities.canExecuteVba && isRunnableVbaLanguage(fence.languageKey);
    const canOpenInVbaEditor = hostCapabilities.canOpenInVbaEditor && isRunnableVbaLanguage(fence.languageKey);

    return ''
        + '<div class="code-block" data-language="' + escapeAttribute(fence.languageKey) + '">'
        + '  <div class="code-block-header">'
        + '    <span class="code-block-language">' + escapeHtml(fence.displayLabel) + '</span>'
        + '    <div class="code-block-actions">'
        + '      <span class="code-block-backup-pending" hidden>备份中...</span>'
        + '      <button type="button" class="code-block-action-btn view-code-backup-btn" title="查看备份" aria-label="查看备份" data-original-title="查看备份" hidden></button>'
        + '      <button type="button" class="code-block-action-btn copy-code-btn" title="复制代码" aria-label="复制代码" data-original-title="复制代码"></button>'
        + (canOpenInVbaEditor
            ? '      <button type="button" class="code-block-action-btn open-vba-editor-btn" title="在 VBA 编辑器中打开" aria-label="在 VBA 编辑器中打开" data-original-title="在 VBA 编辑器中打开"></button>'
            : '')
        + (canRun
            ? '      <button type="button" class="code-block-action-btn run-code-btn" title="直接运行 VBA" aria-label="直接运行 VBA" data-original-title="直接运行 VBA"></button>'
            : '')
        + '    </div>'
        + '  </div>'
        + '  <pre><code>' + escapeHtml(code || '') + '</code></pre>'
        + '</div>';
}

export function createRunCodeButton() {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'code-block-action-btn run-code-btn';
    button.title = '直接运行 VBA';
    button.setAttribute('aria-label', '直接运行 VBA');
    button.dataset.originalTitle = '直接运行 VBA';
    return button;
}

export function createOpenVbaEditorButton() {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'code-block-action-btn open-vba-editor-btn';
    button.title = '在 VBA 编辑器中打开';
    button.setAttribute('aria-label', '在 VBA 编辑器中打开');
    button.dataset.originalTitle = '在 VBA 编辑器中打开';
    return button;
}

export function ensureCodeBlockBackupElements(codeBlock) {
    if (!codeBlock) {
        return null;
    }

    const actions = codeBlock.querySelector('.code-block-actions');
    if (!actions) {
        return null;
    }

    let pendingEl = actions.querySelector('.code-block-backup-pending');
    if (!pendingEl) {
        pendingEl = document.createElement('span');
        pendingEl.className = 'code-block-backup-pending';
        pendingEl.textContent = '备份中...';
        pendingEl.hidden = true;
        actions.insertBefore(pendingEl, actions.firstChild);
    }

    let viewBtn = actions.querySelector('.view-code-backup-btn');
    if (!viewBtn) {
        viewBtn = document.createElement('button');
        viewBtn.type = 'button';
        viewBtn.className = 'code-block-action-btn view-code-backup-btn';
        viewBtn.title = '查看备份';
        viewBtn.setAttribute('aria-label', '查看备份');
        viewBtn.dataset.originalTitle = '查看备份';
        viewBtn.hidden = true;
        actions.insertBefore(viewBtn, pendingEl.nextSibling);
    }

    return {
        pendingEl: pendingEl,
        viewBtn: viewBtn
    };
}

export function refreshCodeBlockActions() {
    if (!chatList) {
        return;
    }

    const codeBlocks = chatList.querySelectorAll('.code-block');
    for (let i = 0; i < codeBlocks.length; i++) {
        const codeBlock = codeBlocks[i];
        const actions = codeBlock.querySelector('.code-block-actions');
        if (!actions) {
            continue;
        }

        ensureCodeBlockBackupElements(codeBlock);

        const language = codeBlock.getAttribute('data-language') || '';
        const shouldShowRunButton = hostCapabilities.canExecuteVba && isRunnableVbaLanguage(language);
        const shouldShowOpenVbaEditorButton = hostCapabilities.canOpenInVbaEditor && isRunnableVbaLanguage(language);
        const existingRunButton = actions.querySelector('.run-code-btn');
        const existingOpenVbaEditorButton = actions.querySelector('.open-vba-editor-btn');

        if (shouldShowOpenVbaEditorButton && !existingOpenVbaEditorButton) {
            const runButton = actions.querySelector('.run-code-btn');
            const button = createOpenVbaEditorButton();

            if (runButton) {
                actions.insertBefore(button, runButton);
            }
            else {
                actions.appendChild(button);
            }
        }
        else if (!shouldShowOpenVbaEditorButton && existingOpenVbaEditorButton && existingOpenVbaEditorButton.parentNode) {
            existingOpenVbaEditorButton.parentNode.removeChild(existingOpenVbaEditorButton);
        }

        if (shouldShowRunButton && !existingRunButton) {
            actions.appendChild(createRunCodeButton());
        }
        else if (!shouldShowRunButton && existingRunButton && existingRunButton.parentNode) {
            existingRunButton.parentNode.removeChild(existingRunButton);
        }
    }
}

export function renderMarkdown(markdown) {
    const text = normalizeMarkdownTables(normalizeMarkdownCodeFences(String(markdown || '').replace(/\r\n?/g, '\n')));
    const lines = text.split('\n');
    const html = [];
    let index = 0;
    let paragraphLines = [];

    function flushParagraph() {
        if (!paragraphLines.length) {
            return;
        }

        html.push('<p>' + paragraphLines.map(function (line) {
            return renderInlineMarkdown(line);
        }).join('<br />') + '</p>');

        paragraphLines = [];
    }

    function renderList(startIndex, ordered) {
        const items = [];
        let currentIndex = startIndex;
        const pattern = ordered ? /^\s*\d+\.\s+(.*)$/ : /^\s*[-*+]\s+(.*)$/;

        while (currentIndex < lines.length) {
            const match = lines[currentIndex].match(pattern);
            if (!match) {
                break;
            }

            items.push('<li>' + renderInlineMarkdown(match[1]) + '</li>');
            currentIndex++;
        }

        html.push((ordered ? '<ol>' : '<ul>') + items.join('') + (ordered ? '</ol>' : '</ul>'));
        return currentIndex;
    }

    function renderBlockquote(startIndex) {
        const quoteLines = [];
        let currentIndex = startIndex;

        while (currentIndex < lines.length && /^\s*>/.test(lines[currentIndex])) {
            quoteLines.push(lines[currentIndex].replace(/^\s*>\s?/, ''));
            currentIndex++;
        }

        html.push('<blockquote>' + quoteLines.map(function (line) {
            return renderInlineMarkdown(line);
        }).join('<br />') + '</blockquote>');

        return currentIndex;
    }

    function renderTable(startIndex) {
        const headerCells = parseTableCells(lines[startIndex]);
        let currentIndex = startIndex + 2;
        const bodyRows = [];

        while (currentIndex < lines.length && /\|/.test(lines[currentIndex]) && lines[currentIndex].trim() !== '') {
            const cells = parseTableCells(lines[currentIndex]);
            bodyRows.push('<tr>' + cells.map(function (cell) {
                return '<td>' + renderInlineMarkdown(cell) + '</td>';
            }).join('') + '</tr>');
            currentIndex++;
        }

        html.push(
            '<table><thead><tr>'
            + headerCells.map(function (cell) {
                return '<th>' + renderInlineMarkdown(cell) + '</th>';
            }).join('')
            + '</tr></thead><tbody>'
            + bodyRows.join('')
            + '</tbody></table>');

        return currentIndex;
    }

    while (index < lines.length) {
        const line = lines[index];
        const trimmed = line.trim();

        if (!trimmed) {
            flushParagraph();
            index++;
            continue;
        }

        if (/^```/.test(trimmed)) {
            flushParagraph();

            const language = trimmed.replace(/^```/, '').trim();
            const codeLines = [];
            index++;

            while (index < lines.length && !/^```/.test(lines[index].trim())) {
                codeLines.push(lines[index]);
                index++;
            }

            html.push(renderCodeBlock(language, codeLines.join('\n')));

            if (index < lines.length) {
                index++;
            }

            continue;
        }

        const headingMatch = trimmed.match(/^(#{1,6})(?:\s+|(?=\S))(.*)$/);
        if (headingMatch) {
            flushParagraph();

            const level = headingMatch[1].length;
            const headingText = (headingMatch[2] || '').trim();
            html.push('<h' + level + '>' + renderInlineMarkdown(headingText) + '</h' + level + '>');
            index++;
            continue;
        }

        if (/^(-{3,}|\*{3,}|_{3,})$/.test(trimmed)) {
            flushParagraph();
            html.push('<hr />');
            index++;
            continue;
        }

        if (/\|/.test(line) && index + 1 < lines.length && isTableSeparator(lines[index + 1])) {
            flushParagraph();
            index = renderTable(index);
            continue;
        }

        if (/^\s*[-*+]\s+/.test(line)) {
            flushParagraph();
            index = renderList(index, false);
            continue;
        }

        if (/^\s*\d+\.\s+/.test(line)) {
            flushParagraph();
            index = renderList(index, true);
            continue;
        }

        if (/^\s*>/.test(line)) {
            flushParagraph();
            index = renderBlockquote(index);
            continue;
        }

        paragraphLines.push(line);
        index++;
    }

    flushParagraph();
    return html.join('');
}

export function normalizeMarkdownCodeFences(text) {
    const source = String(text || '');
    if (!source || source.indexOf('```') < 0) {
        return source;
    }

    const lines = source.split('\n');
    const normalizedLines = [];
    let inFence = false;

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];

        if (!inFence) {
            const fenceIndex = line.indexOf('```');
            if (fenceIndex > 0 && line.slice(0, fenceIndex).trim()) {
                const before = line.slice(0, fenceIndex);
                const after = line.slice(fenceIndex);

                normalizedLines.push(before);
                normalizedLines.push(after);
                inFence = true;
                continue;
            }

            normalizedLines.push(line);
            if (/^\s*```/.test(line)) {
                inFence = true;
            }

            continue;
        }

        const closingFenceIndex = line.indexOf('```');
        if (closingFenceIndex >= 0) {
            const beforeFence = line.slice(0, closingFenceIndex + 3);
            const afterFence = line.slice(closingFenceIndex + 3);

            normalizedLines.push(beforeFence);
            inFence = false;

            if (afterFence.trim()) {
                normalizedLines.push(afterFence);
            }

            continue;
        }

        normalizedLines.push(line);
    }

    return normalizedLines.join('\n');
}
