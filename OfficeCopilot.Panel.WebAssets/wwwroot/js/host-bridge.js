export function postMessage(message) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(message);
    }
}
