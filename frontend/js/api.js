/**
 * XenChat API Client
 * Wraps fetch calls with automatic JWT Bearer token authentication and error handling.
 */
(function (global) {
    const API = {
        async request(path, options = {}) {
            const baseUrl = global.CONFIG ? global.CONFIG.API_BASE_URL : '';
            const url = path.startsWith('http://') || path.startsWith('https://')
                ? path
                : `${baseUrl}${path.startsWith('/') ? '' : '/'}${path}`;

            const headers = options.headers || {};
            const token = localStorage.getItem('xenchat_token');
            if (token) {
                headers['Authorization'] = `Bearer ${token}`;
            }

            if (!options.isFormData && !headers['Content-Type']) {
                headers['Content-Type'] = 'application/json';
            }

            const fetchOptions = {
                ...options,
                headers
            };

            try {
                const response = await fetch(url, fetchOptions);

                if (response.status === 401) {
                    localStorage.removeItem('xenchat_token');
                    // Only redirect if not already on login or signup pages
                    const currentPage = window.location.pathname.split('/').pop();
                    if (currentPage !== 'login.html' && currentPage !== 'signup.html' && currentPage !== 'verify-otp.html') {
                        window.location.href = 'login.html';
                    }
                    return { success: false, status: 401, error: 'Session expired. Please log in again.' };
                }

                const contentType = response.headers.get('content-type') || '';
                if (contentType.includes('application/json')) {
                    const data = await response.json();
                    return data;
                } else {
                    const text = await response.text();
                    return { success: response.ok, data: text };
                }
            } catch (err) {
                console.error(`API request error for ${url}:`, err);
                return {
                    success: false,
                    error: 'Unable to connect to the server. Please check your backend connection.'
                };
            }
        },

        get(path) {
            return this.request(path, { method: 'GET' });
        },

        post(path, data) {
            return this.request(path, {
                method: 'POST',
                body: JSON.stringify(data || {})
            });
        },

        postForm(path, formData) {
            return this.request(path, {
                method: 'POST',
                body: formData,
                isFormData: true
            });
        },

        delete(path, data) {
            return this.request(path, {
                method: 'DELETE',
                body: JSON.stringify(data || {})
            });
        }
    };

    global.API = API;
})(window);
