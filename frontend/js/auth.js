/**
 * XenChat Auth Service
 * Manages authentication lifecycle, guards, and local storage state.
 */
(function (global) {
    const AUTH = {
        getToken() {
            return localStorage.getItem('xenchat_token');
        },

        setToken(token) {
            if (token) localStorage.setItem('xenchat_token', token);
            else localStorage.removeItem('xenchat_token');
        },

        getCurrentUser() {
            try {
                const stored = localStorage.getItem('xenchat_user');
                return stored ? JSON.parse(stored) : null;
            } catch {
                return null;
            }
        },

        setCurrentUser(user) {
            if (user) localStorage.setItem('xenchat_user', JSON.stringify(user));
            else localStorage.removeItem('xenchat_user');
        },

        isAuthenticated() {
            return !!this.getToken();
        },

        async requireAuth() {
            const token = this.getToken();
            if (!token) {
                window.location.href = 'login.html';
                return null;
            }

            const res = await global.API.get('/api/auth/me');
            if (res && res.success && res.user) {
                this.setCurrentUser(res.user);
                return res.user;
            } else {
                this.logout();
                return null;
            }
        },

        async redirectIfAuthenticated() {
            const token = this.getToken();
            if (!token) return;

            const res = await global.API.get('/api/auth/me');
            if (res && res.success) {
                window.location.href = 'index.html';
            } else {
                this.setToken(null);
                this.setCurrentUser(null);
            }
        },

        async login(email, password) {
            const res = await global.API.post('/api/auth/login', { email, password });
            if (res && res.success && res.token) {
                this.setToken(res.token);
                this.setCurrentUser(res.user);
            }
            return res;
        },

        async signup(username, email, password, confirmPassword) {
            return await global.API.post('/api/auth/signup', {
                username,
                email,
                password,
                confirmPassword
            });
        },

        async verifyOtp(email, code) {
            const res = await global.API.post('/api/auth/verify-otp', { email, code });
            if (res && res.success && res.token) {
                this.setToken(res.token);
                this.setCurrentUser(res.user);
            }
            return res;
        },

        async resendOtp(email) {
            return await global.API.post('/api/auth/resend-otp', { email });
        },

        logout() {
            this.setToken(null);
            this.setCurrentUser(null);
            try {
                global.API.post('/api/auth/logout', {});
            } catch (e) {
                // ignore
            }
            window.location.href = 'login.html';
        }
    };

    global.AUTH = AUTH;
})(window);
