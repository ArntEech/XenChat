# XenChat — Separated Frontend & Backend Deployment Guide 🚀

This repository now provides a fully decoupled architecture:
1. **Frontend**: Standalone static HTML/CSS/JavaScript SPA in `frontend/` (ready for **Vercel**, **Netlify**, or **Cloudflare Pages**).
2. **Backend**: ASP.NET Core 8 Web API & SignalR server in the root project (ready for **Render**, **Railway**, or **Fly.io**).

---

## 🏗 Architecture Overview

```text
┌─────────────────────────────────┐                 ┌─────────────────────────────────┐
│        Frontend (Vercel)        │                 │         Backend (Render)        │
│  - Static HTML5, CSS3, JS       │  REST API (JWT) │  - ASP.NET Core 8 Web API       │
│  - SignalR JavaScript Client    │ ───────────────>│  - SignalR Hub (/chatHub)       │
│  - WebRTC PeerConnection        │ <───────────────│  - PostgreSQL (EF Core)         │
│  - Hosted on Vercel / Netlify   │   WebSocket/WSS │  - Cloudinary Media CDN         │
└─────────────────────────────────┘                 └─────────────────────────────────┘
```

---

## 🌐 1. Deploying the Backend (Render / Railway / Fly.io)

### Option A: Render (Web Service via Docker)
1. Push your repository to GitHub.
2. In [Render Dashboard](https://dashboard.render.com), click **New +** -> **Web Service**.
3. Connect your repository.
4. Set the following options:
   - **Environment:** Docker
   - **Dockerfile Path:** `./Dockerfile`
   - **Docker Context:** `.`
5. Configure Environment Variables in Render:
   - `DATABASE_URL`: Your PostgreSQL connection string (from Neon, Supabase, or Render Postgres).
   - `ALLOWED_ORIGINS`: Your frontend URL, e.g. `https://your-app.vercel.app` (or `*` during initial testing).
   - `CLOUDINARY_URL`: `cloudinary://<api_key>:<api_secret>@<cloud_name>`
   - `SMTP_HOST`: `smtp.gmail.com`
   - `SMTP_PORT`: `587`
   - `SMTP_USER`: Your Gmail / SMTP email
   - `SMTP_PASS`: Your App Password
   - `JWT_SECRET`: A secure 32+ character secret string.
6. Click **Deploy Web Service**.
7. Note down your backend URL (e.g., `https://xenchat-backend.onrender.com`).

---

## 🎨 2. Deploying the Frontend (Vercel / Netlify)

### Option A: Vercel
1. In the [Vercel Dashboard](https://vercel.com), click **Add New** -> **Project**.
2. Import your GitHub repository.
3. In **Root Directory**, click Edit and select **`frontend`**.
4. Leave Framework Preset as **Other** (it will automatically use `vercel.json`).
5. In **Environment Variables**, you can optionally set:
   - Key: `XENCHAT_API_URL`
   - Value: `https://xenchat-backend.onrender.com`
6. Click **Deploy**!

> **Note:** You can also configure the backend URL directly in the UI! On the login page, click **⚙️ Backend Server Settings** and enter your backend URL. It is saved in `localStorage` across visits.

### Option B: Netlify
1. In [Netlify Dashboard](https://app.netlify.com), click **Add new site** -> **Import an existing project**.
2. Connect your repository.
3. Configure build settings:
   - **Base directory:** `frontend`
   - **Publish directory:** `.`
4. Click **Deploy Site**.

---

## 💻 3. Local Development

### Running the Backend:
```bash
dotnet run
# Listens on http://localhost:5000 (or http://localhost:8080)
```

### Running the Frontend Locally:
You can serve the `frontend/` folder with any static web server:
```bash
# Using npx serve:
npx serve frontend -p 3000

# Or using Python:
cd frontend && python -m http.server 3000

# Or using VS Code Live Server extension on frontend/index.html
```

Open `http://localhost:3000` in your browser. It will automatically connect to `http://localhost:5000`!
