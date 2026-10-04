// Simple JavaScript for auto-scrolling chat to bottom
document.addEventListener('DOMContentLoaded', function () {
    const messagesContainer = document.querySelector('.messages-container');
    if (messagesContainer) {
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }
});

// Remove JWT token on logout
document.addEventListener('click', function (e) {
    const logoutLink = e.target.closest('a[href*="/Account/Logout"]');
    if (logoutLink) {
        localStorage.removeItem('xenchat_token');
    }
});