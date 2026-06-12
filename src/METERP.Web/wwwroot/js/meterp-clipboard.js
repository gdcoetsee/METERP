// Clipboard helper for Blazor JS interop (testable via window.meterpClipboard in E2E).
window.meterpClipboard = {
    write: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }
        return Promise.reject(new Error("Clipboard API unavailable"));
    }
};