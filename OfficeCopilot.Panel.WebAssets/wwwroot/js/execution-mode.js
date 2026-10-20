import { ensureAssistantMessage } from './conversation.js';

export function showExecutionMode(data) {
    const messageId = String(data && data.messageId || '');
    if (!messageId) {
        return;
    }

    const entry = ensureAssistantMessage(messageId);
    if (!entry || !entry.element) {
        return;
    }

    const body = entry.element.querySelector('.assistant-body');
    if (!body) {
        return;
    }

    let banner = body.querySelector('.execution-mode-banner');
    if (!banner) {
        banner = document.createElement('div');
        banner.className = 'execution-mode-banner';
        body.insertBefore(banner, body.firstChild);
    }

    banner.classList.toggle('is-warning', !!(data && data.warning));
    banner.textContent = String(data && data.text || '');
}
