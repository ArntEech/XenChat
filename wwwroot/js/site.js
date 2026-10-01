// Simple JavaScript for auto-scrolling chat to bottom
document.addEventListener('DOMContentLoaded', function () {
    const messagesContainer = document.querySelector('.messages-container');
    if (messagesContainer) {
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }
});