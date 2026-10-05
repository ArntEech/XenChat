/**
 * XenChat Frontend Configuration
 * Manages API endpoints and SignalR Hub connections for separated deployment.
 */
(function (global) {
    const defaultApiUrl = (function () {
        // 1. Explicit window override
        if (global.XENCHAT_API_URL) return global.XENCHAT_API_URL;

        // 2. Saved local storage configuration
        const saved = localStorage.getItem('xenchat_api_url');
        if (saved) return saved.replace(/\/+$/, '');

        // 3. If running locally, default to ASP.NET Core backend port
        const hostname = window.location.hostname;
        if (hostname === 'localhost' || hostname === '127.0.0.1') {
            // Default ASP.NET Core ports
            return 'http://localhost:5000';
        }

        // 4. Default production backend on Render (Replace with your backend URL)
        return 'https://xenchat-backend.onrender.com';
    })();

    const CONFIG = {
        API_BASE_URL: defaultApiUrl,

        get HUB_URL() {
            return `${this.API_BASE_URL}/chatHub`;
        },

        setApiUrl(url) {
            if (!url) return;
            const cleaned = url.trim().replace(/\/+$/, '');
            this.API_BASE_URL = cleaned;
            localStorage.setItem('xenchat_api_url', cleaned);
        },

        resolveAvatarUrl(path) {
            if (!path) return 'images/avatars/user.png';
            if (path.startsWith('http://') || path.startsWith('https://') || path.startsWith('//')) {
                return path;
            }
            if (path.startsWith('/')) {
                // If it's a relative path to avatars, try local frontend assets first or fallback to backend
                return `${this.API_BASE_URL}${path}`;
            }
            return path;
        }
    };

    global.CONFIG = CONFIG;
})(window);
