// 面板共享状态与常量。
// 可变绑定集中在 state 对象上：ES Module 的 import 绑定是只读的，跨模块赋值必须走对象属性。

export const chatList = document.getElementById('chat-list');
export const messageMap = new Map();
export const bottomAnchor = document.createElement('div');
export const SCROLL_AWAY_THRESHOLD = 64;
export const ASSET_BASE_URL = 'https://appassets.local/';

export const hostCapabilities = {
    canExecuteVba: false,
    canExecuteVbaReason: '',
    canOpenInVbaEditor: false,
    canOpenInVbaEditorReason: ''
};

export const pendingCodeExecutionRequests = new Map();
export const pendingOpenInVbaEditorRequests = new Map();
export const state = {
    scrollToBottomButton: null,
    busy: false,
    scrollFrameToken: 0,
    shouldAutoFollow: true,
    activeAssistantMessageId: '',
    assistantAvatarUrl: ASSET_BASE_URL + 'images/bot.png'
};
