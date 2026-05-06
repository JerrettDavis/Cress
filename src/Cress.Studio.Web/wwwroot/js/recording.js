// Recording interop helpers for Cress Studio Web

window.cressRecording = {
    /**
     * Scroll a container element to its bottom, making the newest event visible.
     * Called by RecordingLivePanel.razor after each render.
     * @param {Element} el - The scrollable container reference.
     */
    scrollToBottom: function (el) {
        if (el && el.scrollHeight !== undefined) {
            el.scrollTop = el.scrollHeight;
        }
    }
};
