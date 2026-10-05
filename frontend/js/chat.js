/**
 * XenChat Standalone Client - Main Application Logic
 * Supports real-time messaging, WebRTC calling, 24h stories, and responsive views.
 */
document.addEventListener('DOMContentLoaded', async () => {
    // 1. Authenticate user
    const currentUser = await AUTH.requireAuth();
    if (!currentUser) return;

    // State Variables
    let activeUserId = null;
    let usersList = [];
    let pinnedUserIds = new Set();
    let favoriteUserIds = new Set();
    let onlineUsers = new Set();
    let currentFilter = 'chats';
    let isSubmittingMessage = false;

    // WebRTC Calling State
    let peerConnection = null;
    let localStream = null;
    let isCallInitiator = false;
    let isCurrentCallVideo = false;
    let activeCallPartnerId = null;
    let pendingCallerId = null;
    let pendingCallIsVideo = false;
    let callTimerInterval = null;
    let callSeconds = 0;

    const rtcConfig = {
        iceServers: [
            { urls: 'stun:stun.l.google.com:19302' },
            { urls: 'stun:stun1.l.google.com:19302' }
        ]
    };

    // DOM Elements
    const chatList = document.getElementById('chatList');
    const chatListPanel = document.getElementById('chatListPanel');
    const conversationPanel = document.getElementById('conversationPanel');
    const conversationHeader = document.getElementById('conversationHeader');
    const messagesContainer = document.getElementById('messagesContainer');
    const messageForm = document.getElementById('messageForm');
    const messageInput = document.getElementById('messageInput');
    const chatBackBtn = document.getElementById('chatBackBtn');
    const contactSearchInput = document.getElementById('contactSearchInput');
    const searchToggleBtn = document.getElementById('searchToggleBtn');
    const searchBarContainer = document.getElementById('searchBarContainer');
    const filterPills = document.querySelectorAll('.filter-pill');
    const chatsBadge = document.getElementById('chatsBadge');
    const unreadBadge = document.getElementById('unreadBadge');
    const favBadge = document.getElementById('favBadge');
    const togglePinBtn = document.getElementById('togglePinBtn');
    const toggleFavoriteBtn = document.getElementById('toggleFavoriteBtn');

    // Rail elements
    const railUserAvatar = document.getElementById('railUserAvatar');
    const myStatusAvatar = document.getElementById('myStatusAvatar');
    const logoutBtn = document.getElementById('logoutBtn');
    const backendSettingsBtn = document.getElementById('backendSettingsBtn');
    const backendSettingsModal = document.getElementById('backendSettingsModal');
    const customApiUrlInput = document.getElementById('customApiUrlInput');
    const closeBackendSettingsBtn = document.getElementById('closeBackendSettingsBtn');
    const saveBackendSettingsBtn = document.getElementById('saveBackendSettingsBtn');

    // Status elements
    const contactStatusesList = document.getElementById('contactStatusesList');
    const addStatusBtn = document.getElementById('addStatusBtn');
    const statusFileInput = document.getElementById('statusFileInput');
    const statusUploadModal = document.getElementById('statusUploadModal');
    const statusUploadPreview = document.getElementById('statusUploadPreview');
    const statusCaptionInput = document.getElementById('statusCaptionInput');
    const submitStatusBtn = document.getElementById('submitStatusBtn');
    const closeStatusModalBtn = document.getElementById('closeStatusModalBtn');
    let selectedStatusFile = null;

    // Chat Image elements
    const attachImageBtn = document.getElementById('attachImageBtn');
    const chatImageInput = document.getElementById('chatImageInput');
    const chatImageModal = document.getElementById('chatImageModal');
    const chatImagePreview = document.getElementById('chatImagePreview');
    const chatImageCaptionInput = document.getElementById('chatImageCaptionInput');
    const sendChatImageBtn = document.getElementById('sendChatImageBtn');
    const closeChatImageModalBtn = document.getElementById('closeChatImageModalBtn');
    let selectedChatFile = null;

    // Calling elements
    const startVoiceCallBtn = document.getElementById('startVoiceCallBtn');
    const startVideoCallBtn = document.getElementById('startVideoCallBtn');
    const incomingCallModal = document.getElementById('incomingCallModal');
    const incomingCallerName = document.getElementById('incomingCallerName');
    const incomingCallAvatar = document.getElementById('incomingCallAvatar');
    const btnAcceptCall = document.getElementById('btnAcceptCall');
    const btnDeclineCall = document.getElementById('btnDeclineCall');
    const activeCallOverlay = document.getElementById('activeCallOverlay');
    const localVideo = document.getElementById('localVideo');
    const remoteVideo = document.getElementById('remoteVideo');
    const remoteAudio = document.getElementById('remoteAudio');
    const btnHangUp = document.getElementById('btnHangUp');
    const btnToggleMute = document.getElementById('btnToggleMute');
    const btnToggleCamera = document.getElementById('btnToggleCamera');
    const callDurationTimer = document.getElementById('callDurationTimer');
    const audioCallAvatarStage = document.getElementById('audioCallAvatarStage');
    const audioCallPartnerName = document.getElementById('audioCallPartnerName');
    const audioCallPartnerAvatar = document.getElementById('audioCallPartnerAvatar');

    // Emoji elements
    const emojiToggleBtn = document.getElementById('emojiToggleBtn');
    const emojiPickerPopup = document.getElementById('emojiPickerPopup');
    const closeEmojiPickerBtn = document.getElementById('closeEmojiPickerBtn');
    const emojiGrid = document.getElementById('emojiGrid');

    // Set Rail Avatars
    if (railUserAvatar) railUserAvatar.src = CONFIG.resolveAvatarUrl(currentUser.avatarPath || currentUser.avatar);
    if (myStatusAvatar) myStatusAvatar.src = CONFIG.resolveAvatarUrl(currentUser.avatarPath || currentUser.avatar);

    // ============================================================
    // SignalR Real-Time Hub Connection
    // ============================================================
    const connection = new signalR.HubConnectionBuilder()
        .withUrl(CONFIG.HUB_URL, {
            accessTokenFactory: () => AUTH.getToken() || ""
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .build();

    connection.on("ReceiveMessage", (senderId, receiverId, message, senderName, timestamp, messageId) => {
        // Prevent duplicate append
        if (messageId && document.querySelector(`.message[data-message-id="${messageId}"]`)) {
            return;
        }

        // If message is in currently open conversation
        if (activeUserId && ((senderId === currentUser.id && receiverId === activeUserId) ||
            (senderId === activeUserId && receiverId === currentUser.id))) {

            const emptyNotice = document.getElementById("emptyNotice");
            if (emptyNotice) emptyNotice.remove();

            appendMessageBubble({
                messageId: messageId || 0,
                senderId: senderId,
                receiverId: receiverId,
                content: message,
                timestamp: timestamp
            });
        }

        // Update the sidebar items
        const targetUserId = (senderId === currentUser.id) ? receiverId : senderId;
        const item = document.querySelector(`.chat-item[data-user-id="${targetUserId}"]`);
        if (item) {
            const preview = item.querySelector('.last-message-preview');
            const time = item.querySelector('.chat-time');
            if (preview) preview.textContent = message.includes('[img]') ? '📷 Photo' : message;
            if (time) time.textContent = timestamp;

            // Increment unread badge if message is for me, not in active conversation, and not a self-message
            if (receiverId === currentUser.id && senderId !== activeUserId && senderId !== currentUser.id) {
                let badge = item.querySelector('.unread-count-badge');
                if (!badge) {
                    badge = document.createElement('span');
                    badge.className = 'unread-count-badge';
                    badge.textContent = '0';
                    const bottom = item.querySelector('.chat-info-bottom');
                    if (bottom) bottom.appendChild(badge);
                }
                const count = parseInt(badge.textContent) || 0;
                badge.textContent = count + 1;
                updateBadges();
            }
        }
    });

    connection.on("MessageDeleted", (messageId, senderId, receiverId) => {
        if (activeUserId && ((senderId === currentUser.id && receiverId === activeUserId) ||
            (senderId === activeUserId && receiverId === currentUser.id))) {
            const el = document.querySelector(`.message[data-message-id="${messageId}"]`);
            if (el) el.remove();
        }
    });

    connection.on("OnlineUsersList", (onlineIds) => {
        onlineUsers = new Set(onlineIds.map(Number));
        updateOnlinePresences();
    });

    connection.on("UserStatusChanged", (userId, isOnline) => {
        if (isOnline) onlineUsers.add(userId);
        else onlineUsers.delete(userId);
        updateOnlinePresences();
    });

    // WebRTC Signaling
    connection.on("IncomingCall", (callerId, targetUserId, callerName, isVideo) => {
        if (targetUserId !== currentUser.id) return;
        pendingCallerId = callerId;
        pendingCallIsVideo = isVideo;

        if (incomingCallerName) incomingCallerName.textContent = callerName;
        if (incomingCallAvatar) incomingCallAvatar.textContent = callerName.charAt(0).toUpperCase();
        if (incomingCallModal) incomingCallModal.style.display = 'flex';
    });

    connection.on("CallAnswered", async (callerId, targetUserId, accepted, isVideo) => {
        if (callerId !== currentUser.id) return;
        if (accepted) {
            startCallTimer();
        } else {
            alert('Call declined.');
            cleanupCall();
        }
    });

    connection.on("ReceiveCallOffer", async (senderId, receiverId, sdp) => {
        if (receiverId !== currentUser.id) return;
        if (!peerConnection) initPeerConnection(pendingCallIsVideo);

        try {
            await peerConnection.setRemoteDescription(new RTCSessionDescription(JSON.parse(sdp)));
            const answer = await peerConnection.createAnswer();
            await peerConnection.setLocalDescription(answer);
            await connection.invoke("SendCallAnswer", currentUser.id, senderId, JSON.stringify(answer));
        } catch (e) {
            console.error("Error handling call offer:", e);
        }
    });

    connection.on("ReceiveCallAnswer", async (senderId, receiverId, sdp) => {
        if (receiverId !== currentUser.id || !peerConnection) return;
        try {
            await peerConnection.setRemoteDescription(new RTCSessionDescription(JSON.parse(sdp)));
        } catch (e) {
            console.error("Error setting call answer:", e);
        }
    });

    connection.on("ReceiveIceCandidate", async (senderId, receiverId, candidateJson) => {
        if (receiverId !== currentUser.id || !peerConnection) return;
        try {
            const candidate = JSON.parse(candidateJson);
            await peerConnection.addIceCandidate(new RTCIceCandidate(candidate));
        } catch (e) {
            console.error("Error adding ice candidate:", e);
        }
    });

    connection.on("CallEnded", (senderId, receiverId) => {
        if (senderId === activeCallPartnerId || receiverId === currentUser.id) {
            cleanupCall();
        }
    });

    async function startSignalR() {
        try {
            await connection.start();
            await connection.invoke("RegisterUser", currentUser.id);
            console.log("Connected to XenChat SignalR Hub.");
        } catch (err) {
            console.error("SignalR Connection Error:", err);
            setTimeout(startSignalR, 4000);
        }
    }

    startSignalR();

    // ============================================================
    // Load Overview & Render Chat List
    // ============================================================
    async function loadOverview() {
        const res = await API.get('/api/chats/overview');
        if (!res || !res.success) {
            chatList.innerHTML = `<div class="empty-state" style="padding: 40px 16px; text-align: center; color: #888;"><p>Could not connect to backend. Please check server settings.</p></div>`;
            return;
        }

        usersList = res.users || [];
        pinnedUserIds = new Set(res.pinnedUserIds || []);
        favoriteUserIds = new Set(res.favoriteUserIds || []);

        renderChatList(usersList);
        renderStatuses(res.statuses || []);
        updateBadges();

        // Check if query parameter has active userId
        const params = new URLSearchParams(window.location.search);
        const urlUserId = params.get('userId');
        if (urlUserId) {
            selectConversation(parseInt(urlUserId));
        }
    }

    function renderChatList(users) {
        if (!users || users.length === 0) {
            chatList.innerHTML = `<div class="empty-state" style="padding: 40px 16px; text-align: center; color: #888;"><p>No contacts found.</p></div>`;
            return;
        }

        chatList.innerHTML = '';
        users.forEach(u => {
            const isSelf = u.id === currentUser.id;
            const isPinned = isSelf || pinnedUserIds.has(u.id);
            const isFav = favoriteUserIds.has(u.id);

            const a = document.createElement('a');
            a.href = `javascript:void(0)`;
            a.className = `chat-item ${isPinned ? 'pinned-chat' : ''} ${u.id === activeUserId ? 'active' : ''}`;
            a.setAttribute('data-user-id', u.id);
            a.setAttribute('data-username', (u.username || '').toLowerCase());
            a.setAttribute('data-favorite', isFav ? 'true' : 'false');
            a.setAttribute('data-pinned', isPinned ? 'true' : 'false');

            const avatarSrc = CONFIG.resolveAvatarUrl(u.avatarPath || u.avatar);
            const initial = u.initial || (u.username ? u.username.charAt(0).toUpperCase() : '?');

            a.innerHTML = `
                <div class="chat-avatar-wrapper">
                    <img src="${avatarSrc}" alt="${u.username}" class="avatar-image chat-avatar" onerror="this.style.display='none'; this.nextElementSibling.style.display='flex';" />
                    <div class="avatar-circle chat-avatar-fallback" style="display: none;">${initial}</div>
                    <span class="avatar-online-badge ${onlineUsers.has(u.id) ? 'online' : ''}" data-user-id="${u.id}"></span>
                </div>
                <div class="chat-info">
                    <div class="chat-info-top">
                        <h3>
                            ${u.username}
                            ${isSelf ? '<span class="self-tag">(You)</span>' : ''}
                        </h3>
                        <div class="chat-meta-right">
                            <span class="chat-time">${u.lastMessageTime || ''}</span>
                            ${isPinned ? `
                                <span class="pinned-indicator" title="Pinned chat">
                                    <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor">
                                        <path d="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6l1 1 1-1v-6H18v-2l-2-2z"/>
                                    </svg>
                                </span>
                            ` : ''}
                        </div>
                    </div>
                    <div class="chat-info-bottom">
                        <p class="last-message-preview">${u.lastMessage && u.lastMessage.includes('[img]') ? '📷 Photo' : (u.lastMessage || (isSelf ? 'Message yourself' : 'No messages yet'))}</p>
                        ${!isSelf && u.unreadCount > 0 ? `<span class="unread-count-badge">${u.unreadCount}</span>` : ''}
                    </div>
                </div>
            `;

            a.addEventListener('click', () => selectConversation(u.id));
            chatList.appendChild(a);
        });
    }

    function renderStatuses(statuses) {
        if (!contactStatusesList) return;
        contactStatusesList.innerHTML = '';

        statuses.forEach(s => {
            const div = document.createElement('div');
            div.className = 'status-story-item';
            div.style.cssText = 'cursor: pointer; text-align: center; flex-shrink: 0;';
            div.innerHTML = `
                <div style="width: 48px; height: 48px; border-radius: 50%; padding: 2px; border: 2px solid #ee4c49; margin: 0 auto;">
                    <img src="${CONFIG.resolveAvatarUrl(s.userAvatar)}" style="width: 100%; height: 100%; border-radius: 50%; object-fit: cover;" />
                </div>
                <span style="font-size: 11px; color: #a1a1aa; margin-top: 4px; display: block; max-width: 52px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${s.username}</span>
            `;
            div.addEventListener('click', () => {
                alert(`Story by ${s.username}:\n${s.caption || 'No caption'}\nLink: ${s.mediaUrl}`);
            });
            contactStatusesList.appendChild(div);
        });
    }

    function updateOnlinePresences() {
        document.querySelectorAll('.avatar-online-badge').forEach(badge => {
            const uid = parseInt(badge.getAttribute('data-user-id'));
            if (onlineUsers.has(uid)) badge.classList.add('online');
            else badge.classList.remove('online');
        });

        if (activeUserId) {
            const headerDot = document.getElementById('chatHeaderStatusDot');
            const headerStatus = document.getElementById('chatHeaderStatus');
            const isOnline = onlineUsers.has(activeUserId);
            if (headerDot) {
                if (isOnline) headerDot.classList.add('online');
                else headerDot.classList.remove('online');
            }
            if (headerStatus) {
                headerStatus.textContent = isOnline ? 'Online' : 'Offline';
            }
        }
    }

    function updateBadges() {
        if (chatsBadge) chatsBadge.textContent = usersList.length;

        let totalUnread = 0;
        document.querySelectorAll('.unread-count-badge').forEach(b => {
            totalUnread += parseInt(b.textContent) || 0;
        });

        if (unreadBadge) {
            unreadBadge.textContent = totalUnread;
            unreadBadge.style.display = totalUnread > 0 ? 'inline-flex' : 'none';
        }

        if (favBadge) {
            favBadge.textContent = favoriteUserIds.size;
            favBadge.style.display = favoriteUserIds.size > 0 ? 'inline-flex' : 'none';
        }
    }

    // ============================================================
    // Select & Render Active Conversation
    // ============================================================
    async function selectConversation(targetUserId) {
        activeUserId = targetUserId;

        // Highlight sidebar active item
        document.querySelectorAll('.chat-item').forEach(el => {
            el.classList.toggle('active', parseInt(el.getAttribute('data-user-id')) === targetUserId);
        });

        // Clear unread count for this contact
        const activeItem = document.querySelector(`.chat-item[data-user-id="${targetUserId}"]`);
        if (activeItem) {
            const b = activeItem.querySelector('.unread-count-badge');
            if (b) b.remove();
            updateBadges();
        }

        // Mobile responsive switch
        chatListPanel.classList.remove('chat-list-view');
        chatListPanel.classList.add('chat-conversation-view');
        conversationPanel.classList.remove('chat-list-view');
        conversationPanel.classList.add('chat-conversation-view');

        // Show header & input form
        conversationHeader.style.display = 'flex';
        messageForm.style.display = 'flex';
        messageInput.focus();

        // Update URL
        if (window.history.replaceState) {
            window.history.replaceState({}, '', `?userId=${targetUserId}`);
        }

        // Fetch conversation details from API
        const res = await API.get(`/api/chats/conversation/${targetUserId}`);
        if (!res || !res.success) {
            messagesContainer.innerHTML = `<div class="empty-conversation-notice"><p>Failed to load conversation.</p></div>`;
            return;
        }

        const otherUser = res.otherUser;
        const isSelf = otherUser.isSelf;

        // Render Header
        document.getElementById('chatHeaderTitle').textContent = otherUser.username + (isSelf ? ' (You)' : '');
        const avatarImg = document.getElementById('chatHeaderAvatarImg');
        if (avatarImg) {
            avatarImg.src = CONFIG.resolveAvatarUrl(otherUser.avatarPath || otherUser.avatar);
            avatarImg.style.display = 'block';
        }

        // Update Pin Button
        const isPinned = res.isPinned;
        togglePinBtn.classList.toggle('active', isPinned);
        togglePinBtn.title = isSelf ? 'Pinned (Self-Chat)' : (isPinned ? 'Unpin chat' : 'Pin chat');
        togglePinBtn.disabled = isSelf;
        togglePinBtn.style.cursor = isSelf ? 'default' : 'pointer';
        togglePinBtn.style.opacity = isSelf ? '0.8' : '1';
        const pinSvg = togglePinBtn.querySelector('svg');
        if (pinSvg) {
            pinSvg.setAttribute('fill', isPinned ? '#f59e0b' : 'none');
            pinSvg.setAttribute('stroke', isPinned ? '#f59e0b' : 'currentColor');
        }

        // Update Favorite Button
        const isFav = res.isFavorite;
        toggleFavoriteBtn.classList.toggle('active', isFav);
        toggleFavoriteBtn.title = isFav ? 'Remove from Favorites' : 'Add to Favorites';
        const favSvg = toggleFavoriteBtn.querySelector('svg');
        if (favSvg) {
            favSvg.setAttribute('fill', isFav ? '#e11d48' : 'none');
            favSvg.setAttribute('stroke', isFav ? '#e11d48' : 'currentColor');
        }

        updateOnlinePresences();

        // Render Messages
        messagesContainer.innerHTML = '';
        const messages = res.messages || [];

        if (messages.length === 0) {
            messagesContainer.innerHTML = `
                <div class="empty-conversation-notice" id="emptyNotice" style="text-align: center; margin: auto; color: #888;">
                    <p>${isSelf ? 'This is your personal space. Send notes or messages to yourself!' : `No messages yet. Say hello to ${otherUser.username}!`}</p>
                </div>
            `;
            return;
        }

        messages.forEach(msg => appendMessageBubble(msg));
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }

    function appendMessageBubble(msg) {
        const isSent = (msg.senderId === currentUser.id);
        const div = document.createElement('div');
        div.className = `message ${isSent ? 'sent' : 'received'}`;
        if (msg.messageId) div.setAttribute('data-message-id', msg.messageId);

        const bubble = document.createElement('div');
        bubble.className = 'message-bubble';

        // Check for image content formatted as [img]url[/img]caption
        if (msg.content.startsWith('[img]') && msg.content.includes('[/img]')) {
            const endIdx = msg.content.indexOf('[/img]');
            const imgUrl = msg.content.substring(5, endIdx);
            const caption = msg.content.substring(endIdx + 6);

            const wrap = document.createElement('div');
            wrap.className = 'chat-image-wrap';

            const link = document.createElement('a');
            link.href = imgUrl;
            link.target = '_blank';
            link.rel = 'noopener noreferrer';

            const img = document.createElement('img');
            img.src = imgUrl;
            img.alt = 'Photo';
            img.className = 'chat-bubble-image';
            link.appendChild(img);
            wrap.appendChild(link);

            if (caption) {
                const capDiv = document.createElement('div');
                capDiv.className = 'chat-image-caption';
                capDiv.textContent = caption;
                wrap.appendChild(capDiv);
            }
            bubble.appendChild(wrap);
        } else {
            const p = document.createElement('p');
            p.textContent = msg.content;
            bubble.appendChild(p);
        }

        // Meta info (time + status + delete)
        const meta = document.createElement('div');
        meta.className = 'message-meta';
        meta.innerHTML = `<span class="message-time">${msg.timestamp || ''}</span>`;

        if (isSent) {
            meta.innerHTML += `
                <span class="message-status">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <polyline points="20 6 9 17 4 12"></polyline>
                    </svg>
                </span>
                <button type="button" class="message-delete-btn" title="Delete message" style="background: none; border: none; cursor: pointer; color: #888; padding: 0 4px;">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
                </button>
            `;
            const delBtn = meta.querySelector('.message-delete-btn');
            if (delBtn) {
                delBtn.addEventListener('click', async () => {
                    if (confirm('Delete this message?')) {
                        await API.post('/api/chats/delete-message', { messageId: msg.messageId });
                        await connection.invoke("DeleteMessage", msg.messageId, currentUser.id, activeUserId);
                        div.remove();
                    }
                });
            }
        }

        bubble.appendChild(meta);
        div.appendChild(bubble);
        messagesContainer.appendChild(div);
        messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }

    // ============================================================
    // Back to Chat List on Mobile
    // ============================================================
    chatBackBtn.addEventListener('click', () => {
        chatListPanel.classList.remove('chat-conversation-view');
        chatListPanel.classList.add('chat-list-view');
        conversationPanel.classList.remove('chat-conversation-view');
        conversationPanel.classList.add('chat-list-view');
        if (window.history.replaceState) {
            window.history.replaceState({}, '', window.location.pathname);
        }
    });

    // ============================================================
    // Send Message
    // ============================================================
    messageForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (isSubmittingMessage || !activeUserId) return;

        const text = messageInput.value.trim();
        if (!text) return;

        isSubmittingMessage = true;
        messageInput.value = '';

        try {
            const res = await API.post('/api/chats/send-message', {
                receiverId: activeUserId,
                message: text
            });

            if (res && res.success) {
                await connection.invoke("SendMessage", currentUser.id, activeUserId, text, currentUser.username, res.messageId || 0);
            } else {
                messageInput.value = text;
                alert(res.error || 'Failed to send message.');
            }
        } catch (err) {
            messageInput.value = text;
            console.error('Send error:', err);
        } finally {
            isSubmittingMessage = false;
            messageInput.focus();
        }
    });

    // ============================================================
    // Pin & Favorite Toggle Handlers
    // ============================================================
    togglePinBtn.addEventListener('click', async () => {
        if (!activeUserId || togglePinBtn.disabled) return;
        const res = await API.post('/api/chats/toggle-pin', { targetUserId: activeUserId });
        if (res && res.success) {
            const isPinned = res.isPinned;
            if (isPinned) pinnedUserIds.add(activeUserId);
            else pinnedUserIds.delete(activeUserId);

            togglePinBtn.classList.toggle('active', isPinned);
            togglePinBtn.title = isPinned ? 'Unpin chat' : 'Pin chat';
            const pinSvg = togglePinBtn.querySelector('svg');
            if (pinSvg) {
                pinSvg.setAttribute('fill', isPinned ? '#f59e0b' : 'none');
                pinSvg.setAttribute('stroke', isPinned ? '#f59e0b' : 'currentColor');
            }

            const item = document.querySelector(`.chat-item[data-user-id="${activeUserId}"]`);
            if (item) {
                item.setAttribute('data-pinned', isPinned ? 'true' : 'false');
                item.classList.toggle('pinned-chat', isPinned);
                const metaRight = item.querySelector('.chat-meta-right');
                if (isPinned) {
                    if (metaRight && !metaRight.querySelector('.pinned-indicator')) {
                        const pinSpan = document.createElement('span');
                        pinSpan.className = 'pinned-indicator';
                        pinSpan.title = 'Pinned chat';
                        pinSpan.innerHTML = '<svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><path d="M16 12V4h1V2H7v2h1v8l-2 2v2h5.2v6l1 1 1-1v-6H18v-2l-2-2z"/></svg>';
                        metaRight.appendChild(pinSpan);
                    }
                } else {
                    const pinSpan = item.querySelector('.pinned-indicator');
                    if (pinSpan) pinSpan.remove();
                }
            }
        }
    });

    toggleFavoriteBtn.addEventListener('click', async () => {
        if (!activeUserId) return;
        const res = await API.post('/api/chats/toggle-favorite', { targetUserId: activeUserId });
        if (res && res.success) {
            const isFav = res.isFavorite;
            if (isFav) favoriteUserIds.add(activeUserId);
            else favoriteUserIds.delete(activeUserId);

            toggleFavoriteBtn.classList.toggle('active', isFav);
            toggleFavoriteBtn.title = isFav ? 'Remove from Favorites' : 'Add to Favorites';
            const favSvg = toggleFavoriteBtn.querySelector('svg');
            if (favSvg) {
                favSvg.setAttribute('fill', isFav ? '#e11d48' : 'none');
                favSvg.setAttribute('stroke', isFav ? '#e11d48' : 'currentColor');
            }

            const item = document.querySelector(`.chat-item[data-user-id="${activeUserId}"]`);
            if (item) item.setAttribute('data-favorite', isFav ? 'true' : 'false');
            updateBadges();
        }
    });

    // ============================================================
    // Photo Sending Modal
    // ============================================================
    attachImageBtn.addEventListener('click', () => chatImageInput.click());

    chatImageInput.addEventListener('change', function () {
        if (this.files && this.files[0]) {
            selectedChatFile = this.files[0];
            const reader = new FileReader();
            reader.onload = (e) => {
                chatImagePreview.src = e.target.result;
                chatImageModal.style.display = 'flex';
                chatImageCaptionInput.value = '';
                chatImageCaptionInput.focus();
            };
            reader.readAsDataURL(selectedChatFile);
        }
    });

    closeChatImageModalBtn.addEventListener('click', () => {
        chatImageModal.style.display = 'none';
        selectedChatFile = null;
        chatImageInput.value = '';
    });

    sendChatImageBtn.addEventListener('click', async () => {
        if (!selectedChatFile || !activeUserId) return;

        sendChatImageBtn.disabled = true;
        sendChatImageBtn.textContent = 'Sending...';

        try {
            const formData = new FormData();
            formData.append('receiverId', activeUserId);
            formData.append('imageFile', selectedChatFile);
            if (chatImageCaptionInput.value.trim()) {
                formData.append('caption', chatImageCaptionInput.value.trim());
            }

            const res = await API.postForm('/api/chats/send-image', formData);
            if (res && res.success) {
                await connection.invoke("SendMessage", currentUser.id, activeUserId, res.content, currentUser.username, res.messageId || 0);
                chatImageModal.style.display = 'none';
                selectedChatFile = null;
                chatImageInput.value = '';
            } else {
                alert(res.error || 'Failed to send photo.');
            }
        } catch (err) {
            alert('Could not upload photo.');
        } finally {
            sendChatImageBtn.disabled = false;
            sendChatImageBtn.textContent = 'Send';
        }
    });

    // ============================================================
    // Status Upload Modal
    // ============================================================
    addStatusBtn.addEventListener('click', () => statusFileInput.click());

    statusFileInput.addEventListener('change', function () {
        if (this.files && this.files[0]) {
            selectedStatusFile = this.files[0];
            const reader = new FileReader();
            reader.onload = (e) => {
                statusUploadPreview.src = e.target.result;
                statusUploadModal.style.display = 'flex';
                statusCaptionInput.value = '';
                statusCaptionInput.focus();
            };
            reader.readAsDataURL(selectedStatusFile);
        }
    });

    closeStatusModalBtn.addEventListener('click', () => {
        statusUploadModal.style.display = 'none';
        selectedStatusFile = null;
        statusFileInput.value = '';
    });

    submitStatusBtn.addEventListener('click', async () => {
        if (!selectedStatusFile) return;

        submitStatusBtn.disabled = true;
        submitStatusBtn.textContent = 'Posting...';

        try {
            const formData = new FormData();
            formData.append('statusImage', selectedStatusFile);
            if (statusCaptionInput.value.trim()) {
                formData.append('caption', statusCaptionInput.value.trim());
            }

            const res = await API.postForm('/api/status/upload', formData);
            if (res && res.success) {
                statusUploadModal.style.display = 'none';
                selectedStatusFile = null;
                statusFileInput.value = '';
                await loadOverview(); // Reload stories bar
            } else {
                alert(res.error || 'Failed to post story.');
            }
        } catch (err) {
            alert('Could not upload status.');
        } finally {
            submitStatusBtn.disabled = false;
            submitStatusBtn.textContent = 'Post';
        }
    });

    // ============================================================
    // Emoji Picker Implementation
    // ============================================================
    const emojis = [
        "😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣", "🥹", "😊", "😇", "🙂", "🙃", "😉", "😌", "😍",
        "🥰", "😘", "😗", "😙", "😚", "😋", "😛", "😝", "😜", "🤪", "🤨", "🧐", "🤓", "😎", "🤩", "🥳",
        "😏", "😒", "😞", "😔", "😟", "😕", "🙁", "😣", "😖", "😫", "😩", "🥺", "😢", "😭", "😤", "😠",
        "😡", "🤬", "🤯", "😳", "🥵", "🥶", "😱", "😨", "😰", "😥", "😓", "🤗", "🤔", "🤭", "🤫", "🤥",
        "👍", "👎", "👏", "🙌", "🤝", "👊", "✊", "🤛", "🤜", "🤞", "✌️", "🤟", "🤘", "👌", "🤏", "👈",
        "👉", "👆", "👇", "☝️", "✋", "🤚", "🖐️", "🖖", "👋", "🤙", "💪", "🙏", "❤️", "🧡", "💛", "💚",
        "💙", "💜", "🖤", "🤍", "🤎", "💔", "❣️", "💕", "💞", "💓", "💗", "💖", "💘", "💝", "✨", "🔥"
    ];

    if (emojiGrid) {
        emojis.forEach(emoji => {
            const btn = document.createElement("button");
            btn.type = "button";
            btn.className = "emoji-item";
            btn.textContent = emoji;
            btn.addEventListener("click", () => {
                const start = messageInput.selectionStart || messageInput.value.length;
                const end = messageInput.selectionEnd || messageInput.value.length;
                const text = messageInput.value;
                messageInput.value = text.substring(0, start) + emoji + text.substring(end);
                messageInput.selectionStart = messageInput.selectionEnd = start + emoji.length;
                messageInput.focus();
            });
            emojiGrid.appendChild(btn);
        });
    }

    emojiToggleBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        const isOpen = emojiPickerPopup.style.display === 'flex';
        emojiPickerPopup.style.display = isOpen ? 'none' : 'flex';
    });

    closeEmojiPickerBtn.addEventListener('click', () => {
        emojiPickerPopup.style.display = 'none';
    });

    document.addEventListener('click', (e) => {
        if (emojiPickerPopup && !emojiPickerPopup.contains(e.target) && e.target !== emojiToggleBtn) {
            emojiPickerPopup.style.display = 'none';
        }
    });

    // ============================================================
    // WebRTC Voice & Video Calling Logic
    // ============================================================
    async function initPeerConnection(isVideo) {
        peerConnection = new RTCPeerConnection(rtcConfig);

        peerConnection.onicecandidate = (event) => {
            if (event.candidate && activeCallPartnerId) {
                connection.invoke("SendIceCandidate", currentUser.id, activeCallPartnerId, JSON.stringify(event.candidate));
            }
        };

        peerConnection.ontrack = (event) => {
            if (isVideo && remoteVideo) {
                remoteVideo.srcObject = event.streams[0];
            } else if (remoteAudio) {
                remoteAudio.srcObject = event.streams[0];
            }
        };

        try {
            localStream = await navigator.mediaDevices.getUserMedia({
                audio: true,
                video: isVideo
            });

            localStream.getTracks().forEach(track => peerConnection.addTrack(track, localStream));

            if (isVideo && localVideo) {
                localVideo.srcObject = localStream;
                localVideo.style.display = 'block';
                remoteVideo.style.display = 'block';
                audioCallAvatarStage.style.display = 'none';
            } else {
                if (localVideo) localVideo.style.display = 'none';
                if (remoteVideo) remoteVideo.style.display = 'none';
                audioCallAvatarStage.style.display = 'flex';
            }

            activeCallOverlay.style.display = 'flex';
        } catch (err) {
            console.error("Media devices access error:", err);
            alert("Could not access camera/microphone. Please ensure permissions are granted.");
            cleanupCall();
        }
    }

    startVoiceCallBtn.addEventListener('click', () => startCall(false));
    startVideoCallBtn.addEventListener('click', () => startCall(true));

    async function startCall(isVideo) {
        if (!activeUserId) return;
        isCallInitiator = true;
        isCurrentCallVideo = isVideo;
        activeCallPartnerId = activeUserId;

        const partnerUser = usersList.find(u => u.id === activeUserId);
        if (partnerUser) {
            audioCallPartnerName.textContent = partnerUser.username;
            audioCallPartnerAvatar.textContent = partnerUser.initial;
        }

        await initPeerConnection(isVideo);
        await connection.invoke("CallUser", currentUser.id, activeUserId, currentUser.username, isVideo);

        const offer = await peerConnection.createOffer();
        await peerConnection.setLocalDescription(offer);
        await connection.invoke("SendCallOffer", currentUser.id, activeUserId, JSON.stringify(offer));
    }

    btnAcceptCall.addEventListener('click', async () => {
        incomingCallModal.style.display = 'none';
        if (!pendingCallerId) return;

        activeCallPartnerId = pendingCallerId;
        isCurrentCallVideo = pendingCallIsVideo;

        const partnerUser = usersList.find(u => u.id === pendingCallerId);
        if (partnerUser) {
            audioCallPartnerName.textContent = partnerUser.username;
            audioCallPartnerAvatar.textContent = partnerUser.initial;
        }

        await initPeerConnection(pendingCallIsVideo);
        await connection.invoke("AnswerCall", currentUser.id, pendingCallerId, true, pendingCallIsVideo);
        startCallTimer();
    });

    btnDeclineCall.addEventListener('click', async () => {
        incomingCallModal.style.display = 'none';
        if (pendingCallerId) {
            await connection.invoke("AnswerCall", currentUser.id, pendingCallerId, false, false);
            pendingCallerId = null;
        }
    });

    btnHangUp.addEventListener('click', () => {
        if (activeCallPartnerId) {
            connection.invoke("EndCall", currentUser.id, activeCallPartnerId);
        }
        cleanupCall();
    });

    function startCallTimer() {
        callSeconds = 0;
        clearInterval(callTimerInterval);
        callTimerInterval = setInterval(() => {
            callSeconds++;
            const mins = String(Math.floor(callSeconds / 60)).padStart(2, '0');
            const secs = String(callSeconds % 60).padStart(2, '0');
            if (callDurationTimer) callDurationTimer.textContent = `${mins}:${secs}`;
        }, 1000);
    }

    function cleanupCall() {
        clearInterval(callTimerInterval);
        if (callDurationTimer) callDurationTimer.textContent = '00:00';

        if (localStream) {
            localStream.getTracks().forEach(track => track.stop());
            localStream = null;
        }
        if (peerConnection) {
            peerConnection.close();
            peerConnection = null;
        }

        activeCallOverlay.style.display = 'none';
        incomingCallModal.style.display = 'none';
        activeCallPartnerId = null;
        pendingCallerId = null;
    }

    btnToggleMute.addEventListener('click', () => {
        if (!localStream) return;
        const audioTrack = localStream.getAudioTracks()[0];
        if (audioTrack) {
            audioTrack.enabled = !audioTrack.enabled;
            btnToggleMute.textContent = audioTrack.enabled ? '🎤' : '🔇';
        }
    });

    btnToggleCamera.addEventListener('click', () => {
        if (!localStream) return;
        const videoTrack = localStream.getVideoTracks()[0];
        if (videoTrack) {
            videoTrack.enabled = !videoTrack.enabled;
            btnToggleCamera.textContent = videoTrack.enabled ? '📹' : '🚫';
        }
    });

    // ============================================================
    // Search & Filters
    // ============================================================
    searchToggleBtn.addEventListener('click', () => {
        const isOpen = searchBarContainer.style.display === 'block';
        searchBarContainer.style.display = isOpen ? 'none' : 'block';
        if (!isOpen) contactSearchInput.focus();
    });

    contactSearchInput.addEventListener('input', applyFilters);

    filterPills.forEach(pill => {
        pill.addEventListener('click', function () {
            filterPills.forEach(p => p.classList.remove('active'));
            this.classList.add('active');
            currentFilter = this.getAttribute('data-filter') || 'chats';
            applyFilters();
        });
    });

    function applyFilters() {
        const query = contactSearchInput.value.trim().toLowerCase();
        const items = document.querySelectorAll('.chat-item');
        let count = 0;

        items.forEach(item => {
            const username = item.getAttribute('data-username') || '';
            const isFav = item.getAttribute('data-favorite') === 'true';
            const unreadBadge = item.querySelector('.unread-count-badge');
            const hasUnread = unreadBadge && parseInt(unreadBadge.textContent) > 0;

            let matchesFilter = true;
            if (currentFilter === 'unread') matchesFilter = hasUnread;
            else if (currentFilter === 'favorites') matchesFilter = isFav;
            else if (currentFilter === 'groups') matchesFilter = false;

            const matchesQuery = !query || username.includes(query);

            if (matchesFilter && matchesQuery) {
                item.style.display = 'flex';
                count++;
            } else {
                item.style.display = 'none';
            }
        });
    }

    // ============================================================
    // Backend Server Settings Modal
    // ============================================================
    backendSettingsBtn.addEventListener('click', () => {
        customApiUrlInput.value = CONFIG.API_BASE_URL;
        backendSettingsModal.style.display = 'flex';
    });

    closeBackendSettingsBtn.addEventListener('click', () => {
        backendSettingsModal.style.display = 'none';
    });

    saveBackendSettingsBtn.addEventListener('click', () => {
        const val = customApiUrlInput.value.trim();
        if (val) {
            CONFIG.setApiUrl(val);
            window.location.reload();
        }
    });

    // ============================================================
    // Logout
    // ============================================================
    logoutBtn.addEventListener('click', () => AUTH.logout());

    // Initial load
    await loadOverview();
});
