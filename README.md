# Remote Mumu MVP

MVP dashboard for room-based emulator collaboration over ASP.NET Core SignalR and a Windows agent.

## Projects

- `Server`: ASP.NET Core API and realtime SignalR hub
- `Client`: React/Vite web dashboard
- `Agent`: Windows desktop connector for authorized adb controls

## Development

```powershell
npm.cmd --prefix Client install
npm.cmd --prefix Client run dev
dotnet run --project Server
```

The desktop agent is intentionally permission-gated and sends only explicit input events approved by the room host.

## Cloud deployment variables

Frontend build:

```text
VITE_API_URL=https://api.your-domain.com
```

Backend environment:

```text
CORS_ORIGINS=https://your-domain.com,http://localhost:5173
```

Desktop Connector environment:

```text
REMOTEMUMU_API_URL=https://api.your-domain.com
REMOTEMUMU_WEB_URL=https://your-domain.com
```

Google OAuth Web Client must include both:

```text
http://localhost:5173
https://your-domain.com
```

Production requires persistent PostgreSQL and reverse proxy HTTPS; JSON state is suitable only for local/single-instance deployment.

## Triển khai miễn phí

### 1. Frontend: GitHub Pages

1. Đẩy repository này lên GitHub nhánh `main`.
2. GitHub → Repository → Settings → Pages → Source: **GitHub Actions**.
3. GitHub → Settings → Secrets and variables → Actions → New repository secret:
   - Name: `VITE_API_URL`
   - Value: URL Render backend, ví dụ `https://remote-mumu-api.onrender.com`.
4. Push nhánh `main`; workflow sẽ build và deploy frontend.

### 2. Backend: Render Free

1. Mở Render Blueprint/New → Web Service.
2. Chọn GitHub repository.
3. Render đọc `render.yaml`.
4. Environment variable `CORS_ORIGINS` đặt URL GitHub Pages sau khi được tạo.
5. Render Free cần backend wake sau idle; SignalR có thể reconnect nhưng latency ban đầu cao.

### 3. Google OAuth

Thêm Authorized JavaScript origin:

```text
https://<username>.github.io
```

Nếu frontend được serve ở `/repo`, thêm origin domain gốc như trên, không thêm đường `/repo`.

Lưu ý: render single instance JSON state sẽ mất khi restart. Production nên dùng PostgreSQL trước khi mở public lớn.
