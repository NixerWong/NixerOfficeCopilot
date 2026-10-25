import { appendToAssistantStream } from './conversation.js';
import { postMessage } from './host-bridge.js';
import { escapeHtml } from './markdown.js';
import { ensureBottomAnchor, scrollToBottom } from './scroll.js';
import { chatList, state } from './state.js';
import { t } from './i18n.js';

export var _pendingAskUserQuestions = {};

var PREVIEW_IMAGE_PATTERN = /^(https:\/\/nixer-decks\.local\/\d{8}-\d{6}-[a-z0-9]{6}\/previews\/[a-z][a-z0-9-]{0,23}\.png)(?:\?v=([^#]*))?$/;
var PREVIEW_IMAGE_ID_PATTERN = /^(\d{8}-\d{6}-[a-z0-9]{6})\/([a-z][a-z0-9-]{0,23})(?:\/([a-zA-Z0-9._~-]{0,64}))?$/;

// 只接受演示工作区映射下的图片地址，其余一律忽略。
export function safePreviewImage(url) {
    return resolvePreviewImageOption({ previewImage: url }).url;
}

function resolvePreviewImageOption(option) {
    if (option && typeof option.previewImageId === 'string' && option.previewImageId.length > 0) {
        var idMatch = PREVIEW_IMAGE_ID_PATTERN.exec(option.previewImageId);
        if (!idMatch) {
            return { url: '', cacheVersionRemoved: false };
        }
        var validIdVersion = idMatch[3] !== undefined && idMatch[3].length === 12;
        return {
            url: 'https://nixer-decks.local/' + idMatch[1] + '/previews/' + idMatch[2] + '.png'
                + (validIdVersion ? '?v=' + idMatch[3] : ''),
            cacheVersionRemoved: idMatch[3] !== undefined && !validIdVersion
        };
    }

    var url = option && typeof option.previewImage === 'string' ? option.previewImage : '';
    if (typeof url !== 'string' || url.length > 512) {
        return { url: '', cacheVersionRemoved: false };
    }
    var match = PREVIEW_IMAGE_PATTERN.exec(url);
    if (!match) {
        return { url: '', cacheVersionRemoved: false };
    }
    var hasCacheVersion = url.indexOf('?v=') >= 0;
    var validCacheVersion = match[2] !== undefined && /^[a-f0-9]{12}$/.test(match[2]);
    return {
        url: match[1] + (validCacheVersion ? '?v=' + match[2] : ''),
        cacheVersionRemoved: hasCacheVersion && !validCacheVersion
    };
}

function showPreviewLightbox(src) {
    var existing = document.querySelector('.auq-lightbox');
    if (existing) {
        existing.remove();
    }
    var overlay = document.createElement('div');
    overlay.className = 'auq-lightbox';
    var img = document.createElement('img');
    img.alt = '';
    img.src = src;
    overlay.appendChild(img);
    overlay.addEventListener('click', function () {
        overlay.remove();
    });
    document.body.appendChild(overlay);
}

function renderPreviewText(previewCol, text) {
    previewCol.textContent = '';
    var block = document.createElement('div');
    if (text) {
        block.className = 'question-preview-text';
        block.textContent = text;
    }
    else {
        block.className = 'question-preview-placeholder';
        block.dataset.l10nText = 'Web.ask.previewPrompt';
        block.textContent = t('Web.ask.previewPrompt');
    }
    previewCol.appendChild(block);
}

export function showAskUserQuestion(data) {
    var requestId = data.requestId || '';
    if (!requestId || !chatList) {
        return;
    }

    // Remove any existing AskUserQuestion UI for this request
    dismissAskUserQuestion(requestId);

    var questions;
    try {
        questions = typeof data.questions === 'string'
            ? JSON.parse(data.questions)
            : (data.questions || []);
    }
    catch (e) {
        postMessage({ type: 'traceLog', msgType: 'askUserQuestion-parse-fail', requestId: requestId });
        questions = [];
    }

    if (!questions || !questions.length) {
        postMessage({ type: 'traceLog', msgType: 'askUserQuestion-empty', requestId: requestId });
        return;
    }

    postMessage({ type: 'traceLog', msgType: 'askUserQuestion-render', requestId: requestId });

    var container = document.createElement('div');
    container.className = 'ask-user-question';
    container.dataset.requestId = requestId;

    // Header
    container.innerHTML = ''
        + '<div class="ask-user-question-header">'
        + '  <span class="auq-icon">\u2753</span>'
        + '  <span data-l10n-text="Web.ask.header">Nixer Copilot needs your response</span>'
        + '</div>'
        + '<div class="ask-user-question-body"></div>'
        + '<div class="ask-user-question-footer">'
        + '  <button type="button" class="auq-cancel-btn" data-l10n-text="Web.ask.cancel"></button>'
        + '  <button type="button" class="auq-submit-btn" data-l10n-text="Web.ask.submit"></button>'
        + '</div>';

    var body = container.querySelector('.ask-user-question-body');

    for (var qi = 0; qi < questions.length; qi++) {
        var q = questions[qi];
        var qDiv = document.createElement('div');
        qDiv.className = 'question-item';
        qDiv.dataset.questionIndex = String(qi);

        // Check if any option has preview
        var hasPreview = false;
        if (!q.multiSelect && q.options) {
            for (var oi = 0; oi < q.options.length; oi++) {
                if (q.options[oi].preview) {
                    hasPreview = true;
                    break;
                }
            }
        }

        if (hasPreview) {
            qDiv.classList.add('has-preview');
        }

        var optionImages = (q.options || []).map(function (option) {
            var resolved = resolvePreviewImageOption(option);
            if (resolved.cacheVersionRemoved) {
                postMessage({ type: 'traceLog', msgType: 'askUserQuestion-preview-cache-version-removed', requestId: requestId });
            }
            else if (!resolved.url && option && (option.previewImageId || option.previewImage)) {
                postMessage({ type: 'traceLog', msgType: 'askUserQuestion-preview-image-rejected', requestId: requestId });
            }
            return resolved.url;
        });

        if (optionImages.some(function (image) { return !!image; })) {
            qDiv.classList.add('has-images');
        }

        var optionsHtml = '';
        var inputType = q.multiSelect ? 'checkbox' : 'radio';
        var inputName = 'auq_q_' + requestId + '_' + qi;

        if (q.options) {
            for (var oi2 = 0; oi2 < q.options.length; oi2++) {
                var opt = q.options[oi2];
                var image = optionImages[oi2] || '';
                optionsHtml += ''
                    + '<li class="option-item' + (image ? ' has-image' : '') + '">'
                    + '  <label class="option-label-wrap" data-option-index="' + oi2 + '">'
                    + '    <input type="' + inputType + '" name="' + escapeHtml(inputName) + '" value="' + escapeHtml(opt.label || '') + '">'
                    + '    <span class="option-text-block">'
                    + '      <span class="option-label-text">' + escapeHtml(opt.label || '') + '</span>'
                    + '      <span class="option-description">' + escapeHtml(opt.description || '') + '</span>'
                    + '    </span>'
                    + (image ? '    <img class="option-preview-image" alt="" loading="lazy" data-l10n-title="Web.ask.previewEnlarge" src="' + escapeHtml(image) + '">' : '')
                    + '  </label>'
                    + '</li>';
            }
        }

        // "Other" option
        optionsHtml += ''
            + '<li class="option-item option-other">'
            + '  <div class="option-other-input">'
            + '    <span class="option-other-label" data-l10n-text="Web.ask.other"></span>'
            + '    <input type="text" name="' + escapeHtml(inputName) + '_other" data-l10n-placeholder="Web.ask.otherPlaceholder">'
            + '  </div>'
            + '</li>';

        qDiv.innerHTML = ''
            + (q.header ? '<span class="question-header-chip">' + escapeHtml(q.header) + '</span>' : '')
            + '<p class="question-text">' + escapeHtml(q.question || '') + '</p>'
            + '<div class="question-options-col">'
            + '  <ul class="question-options">' + optionsHtml + '</ul>'
            + '</div>'
            + (hasPreview ? '<div class="question-preview-col"><div class="question-preview-placeholder">\u9009\u62e9\u4e00\u4e2a\u9009\u9879\u67e5\u770b\u9884\u89c8</div></div>' : '');

        body.appendChild(qDiv);
    }

    // Store pending request
    _pendingAskUserQuestions[requestId] = {
        container: container,
        questions: questions
    };

    appendToAssistantStream(container);
    ensureBottomAnchor();
    scrollToBottom();

    // Wire up events
    bindAskUserQuestionEvents(container, requestId, questions);
}

export function bindAskUserQuestionEvents(container, requestId, questions) {
    if (!container) {
        return;
    }

    // Radio/checkbox change \u2192 show preview if applicable
    container.addEventListener('change', function (event) {
        var target = event.target;
        if (!target || (target.type !== 'radio' && target.type !== 'checkbox')) {
            return;
        }

        var questionItem = target.closest('.question-item');
        if (!questionItem) {
            return;
        }

        if (target.type === 'radio') {
            // Update selected state on label wraps
            var allLabels = questionItem.querySelectorAll('.option-label-wrap');
            for (var i = 0; i < allLabels.length; i++) {
                allLabels[i].classList.remove('is-selected');
            }
            var label = target.closest('.option-label-wrap');
            if (label) {
                label.classList.add('is-selected');
            }

            // Show preview if available
            var previewCol = questionItem.querySelector('.question-preview-col');
            if (previewCol && questions) {
                var qi = parseInt(questionItem.dataset.questionIndex, 10);
                var q = questions[qi];
                if (q && q.options) {
                    var oi = target.value ? findOptionIndex(q.options, target.value) : -1;
                    renderPreviewText(previewCol, oi >= 0 ? q.options[oi].preview : '');
                }
            }
        }
    });

    // 点预览图：选中该项（label 默认行为）并放大显示
    container.addEventListener('click', function (event) {
        var target = event.target;
        if (target && target.classList && target.classList.contains('option-preview-image')) {
            var src = safePreviewImage(target.getAttribute('src'));
            if (src) {
                showPreviewLightbox(src);
            }
        }
    });

    // Submit button
    var submitBtn = container.querySelector('.auq-submit-btn');
    if (submitBtn) {
        submitBtn.addEventListener('click', function () {
            submitAskUserQuestion(requestId, questions);
        });
    }

    // Cancel button
    var cancelBtn = container.querySelector('.auq-cancel-btn');
    if (cancelBtn) {
        cancelBtn.addEventListener('click', function () {
            cancelAskUserQuestion(requestId);
        });
    }
}

export function findOptionIndex(options, label) {
    for (var i = 0; i < options.length; i++) {
        if (options[i].label === label) {
            return i;
        }
    }
    return -1;
}

export function submitAskUserQuestion(requestId, questions) {
    console.log('[chat.js] submitAskUserQuestion called: requestId=' + requestId);

    var container = _pendingAskUserQuestions[requestId]
        ? _pendingAskUserQuestions[requestId].container
        : document.querySelector('.ask-user-question[data-request-id="' + requestId + '"]');

    if (!container) {
        console.log('[chat.js] submitAskUserQuestion: container NOT FOUND for ' + requestId);
        return;
    }

    var answers = {};
    var annotations = {};

    var questionItems = container.querySelectorAll('.question-item');
    for (var qi = 0; qi < questionItems.length; qi++) {
        var qItem = questionItems[qi];
        var q = questions && questions[qi];
        var questionText = q ? q.question : ('Question ' + qi);

        // Get selected radio/checkbox values
        var selectedInputs = qItem.querySelectorAll('input[type="radio"]:checked, input[type="checkbox"]:checked');
        var selectedLabels = [];
        for (var si = 0; si < selectedInputs.length; si++) {
            selectedLabels.push(selectedInputs[si].value);
        }

        // Get "Other" text
        var otherInput = qItem.querySelector('input[type="text"]');
        var otherText = otherInput ? otherInput.value.trim() : '';

        // Combine
        var combined = [];
        if (selectedLabels.length > 0) {
            combined = combined.concat(selectedLabels);
        }
        if (otherText) {
            combined.push(otherText);
        }

        if (combined.length > 0) {
            answers[questionText] = combined.join(', ');
        }

        // Annotations (preview + notes)
        if (q && q.options && selectedLabels.length === 1) {
            var oi = findOptionIndex(q.options, selectedLabels[0]);
            if (oi >= 0 && q.options[oi].preview) {
                if (!annotations[questionText]) {
                    annotations[questionText] = {};
                }
                annotations[questionText].preview = q.options[oi].preview;
            }
        }
    }

    // Disable buttons to prevent double-submit
    var submitBtn = container.querySelector('.auq-submit-btn');
    var cancelBtn = container.querySelector('.auq-cancel-btn');
    if (submitBtn) { submitBtn.disabled = true; }
    if (cancelBtn) { cancelBtn.disabled = true; }

    // Post to C#
    console.log('[chat.js] submitAskUserQuestion posting to C#: requestId=' + requestId + ', answers=' + JSON.stringify(answers));
    postMessage({
        type: 'submitUserInteraction',
        requestId: requestId,
        answers: JSON.stringify(answers),
        annotations: JSON.stringify(annotations),
        cancelled: false
    });
    console.log('[chat.js] submitAskUserQuestion postMessage sent for ' + requestId);
}

export function cancelAskUserQuestion(requestId) {
    var container = _pendingAskUserQuestions[requestId]
        ? _pendingAskUserQuestions[requestId].container
        : document.querySelector('.ask-user-question[data-request-id="' + requestId + '"]');

    if (container) {
        var submitBtn = container.querySelector('.auq-submit-btn');
        var cancelBtn = container.querySelector('.auq-cancel-btn');
        if (submitBtn) { submitBtn.disabled = true; }
        if (cancelBtn) { cancelBtn.disabled = true; }
    }

    postMessage({
        type: 'submitUserInteraction',
        requestId: requestId,
        answers: '{}',
        annotations: '{}',
        cancelled: true
    });
}

export function dismissAskUserQuestion(requestId) {
    var containers = document.querySelectorAll('.ask-user-question[data-request-id="' + requestId + '"]');
    for (var i = 0; i < containers.length; i++) {
        if (containers[i].parentNode) {
            containers[i].parentNode.removeChild(containers[i]);
        }
    }

    delete _pendingAskUserQuestions[requestId];
}

// ================================================================
//  Phase 7C: TodoUpdate — inline todo list rendering
// ================================================================
