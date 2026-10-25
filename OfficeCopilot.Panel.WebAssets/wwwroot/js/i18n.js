let messages = Object.create(null);
let locale = 'en';
let localizationObserver = null;

export function t(key, ...args) {
    const template = messages[key] || key;
    return template.replace(/\{(\d+)\}/g, function (match, index) {
        return index < args.length ? String(args[index]) : match;
    });
}

export function setLocale(data) {
    messages = data && data.messages ? data.messages : Object.create(null);
    const requestedLocale = data && String(data.locale || '').toLowerCase();
    locale = requestedLocale && requestedLocale.indexOf('zh') === 0
        ? 'zh-Hans'
        : requestedLocale && requestedLocale.indexOf('ja') === 0
            ? 'ja'
            : requestedLocale && requestedLocale.indexOf('de') === 0
                ? 'de'
                : requestedLocale && requestedLocale.indexOf('es') === 0
                    ? 'es'
                    : 'en';
    document.documentElement.lang = locale === 'zh-Hans'
        ? 'zh-CN'
        : locale === 'ja'
            ? 'ja-JP'
            : locale === 'de'
                ? 'de-DE'
                : locale === 'es'
                    ? 'es-ES'
                    : 'en';
    document.title = t('Web.panel.documentTitle');
    applyLocalizedAttributes(document);
    if (!localizationObserver && document.body) {
        localizationObserver = new MutationObserver(function (records) {
            records.forEach(function (record) {
                record.addedNodes.forEach(function (node) {
                    if (node.nodeType === Node.ELEMENT_NODE) {
                        applyLocalizedAttributes(node);
                    }
                });
            });
        });
        localizationObserver.observe(document.body, { childList: true, subtree: true });
    }
    window.dispatchEvent(new CustomEvent('panel-locale-changed', { detail: { locale: locale } }));
}

export function applyLocalizedAttributes(root) {
    const scope = root || document;
    const select = function (selector) {
        const matches = [];
        if (scope.matches && scope.matches(selector)) {
            matches.push(scope);
        }
        return matches.concat(Array.from(scope.querySelectorAll(selector)));
    };
    select('[data-l10n-text]').forEach(function (element) {
        element.textContent = t(element.dataset.l10nText);
    });
    select('[data-l10n-title]').forEach(function (element) {
        element.title = t(element.dataset.l10nTitle);
    });
    select('[data-l10n-aria]').forEach(function (element) {
        element.setAttribute('aria-label', t(element.dataset.l10nAria));
    });
    select('[data-l10n-placeholder]').forEach(function (element) {
        element.setAttribute('placeholder', t(element.dataset.l10nPlaceholder));
    });
    select('[data-l10n-original-title]').forEach(function (element) {
        element.dataset.originalTitle = element.dataset.l10nCount
            ? t(element.dataset.l10nOriginalTitle, element.dataset.l10nCount)
            : t(element.dataset.l10nOriginalTitle);
    });
    select('[data-l10n-count]').forEach(function (element) {
        const key = element.dataset.l10nTitle;
        if (key) {
            const title = t(key, element.dataset.l10nCount);
            element.title = title;
            element.setAttribute('aria-label', title);
        }
    });
}

export function getLocale() {
    return locale;
}
