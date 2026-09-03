# 🖥️ Hetzner VPS Monitor Desktop App

> Desktop companion tool monitoring VPS Hetzner (Ubuntu/Linux) via SSH. On-demand only — tidak ada polling/background loop, semua data diambil saat user klik "Cek Sekarang".

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![MAUI](https://img.shields.io/badge/MAUI-Hybrid-purple.svg)](https://learn.microsoft.com/dotnet/maui/)

## 📋 Daftar Isi

- [Gambaran](#-gambaran)
- [Fitur](#-fitur)
- [Tech Stack](#-tech-stack)
- [Prerequisites](#-prerequisites)
- [Build & Run](#-build--run)
- [Konfigurasi SSH](#-konfigurasi-ssh)
- [Project Structure](#-project-structure)
- [Publish (.exe)](#-publish-exe)
- [Security & Disclaimer](#-security--disclaimer)
- [Lisensi & Credit](#-lisensi--credit)

## 🎯 Gambaran

Aplikasi desktop untuk **monitoring VPS Hetzner** tanpa harus login ke web console Hetzner atau SSH manual. On-demand monitoring — tidak ada auto-polling, jadi tidak membebani resource server. Semua data dikumpulkan via SSH command yang dikirim saat user klik tombol "Cek Sekarang".

**Kenapa perlu?**
- Tidak perlu buka Hetzner console hanya untuk cek CPU/RAM
- Lihat semua metrics dalam satu dashboard rapi
- Restart service, update package, reboot server — semua dari satu window
- History log operasi untuk audit trail

## ✨ Fitur

### Resource Monitoring
- 🧠 **CPU** usage (%) + core count — dari `top -bn1` & `nproc`
- 💾 **Memory** (%) + used/total dalam GB — dari `free -m`
- 📀 **Storage** (%) + breakdown per-disk jika multi-mount — dari `df -h`
- ⏱️ **Uptime** + Load Average (1/5/15 menit) — dari `uptime`
- Color-coded gauge: hijau < 70%, kuning 70-90%, merah ≥ 90%

### Hosted Websites
- 🌐 Auto-detect website dari nginx/apache config + Docker containers
- Badge 🔒 SSL jika port 443 listening
- Click → buka di browser default via `Launcher.OpenAsync()`

### Service Management
- ⚙️ Status 14 service umum (nginx, apache2, mysql, postgresql, docker, fail2ban, ufw, redis, php-fpm, caddy, dll)
- Visual indicator: 🟢 active, 🔴 failed, ⚪ inactive
- 🛡️ Firewall (UFW) — status + open ports dengan ALLOW/DENY badge

### Software & Docker
- 🧩 **Software Terinstall** — list dengan search real-time + 8 kategori filter (Web Server, Database, Runtime, Container, Security, Network, System, Other)
- 📦 **Docker Containers** — running containers + exposed ports

### System Updates (3-Tab Interface)
- 🧹 **Refresh Cache** — bersihkan `apt clean` + re-fetch index (untuk data stale)
- 📦 **Update** — `apt-get upgrade -y` (default, safe)
- 🔧 **Force Upgrade** — `apt full-upgrade -y` (bypass phased updates)
- 🎯 **Force Install by Name** — `apt install -y --only-upgrade <paket>` (granular)
- 🔌 **Reboot Server** — `shutdown -r +1` dengan auto-detection server kembali online

### Logs & History
- 📋 Tab Logs dengan persistent storage (`FileSystem.AppDataDirectory`)
- Setiap operasi update/reboot/check tercatat dengan timestamp + status
- Tombol 🗑️ **Hapus Semua Log** untuk clean up
- JSON file format — atomic write, FIFO max 100 entries

### First-Run Setup
- 🔐 Form konfigurasi SSH di `/settings` (host, port, username, password/key)
- 🧪 Test koneksi sebelum save
- 🔒 Kredensial disimpan di MAUI `SecureStorage` (terenkripsi di Windows Credential Manager)

### UX
- 🌙 **Dark mode default** + responsive layout
- 📱 Responsive grid (xs/sm/md/xl Bootstrap breakpoints)
- 🎨 Bootstrap 5.3.3 utility classes, custom CSS seminimal mungkin
- ⚡ Cancel previous request otomatis jika user double-click "Cek Sekarang"

## 🛠️ Tech Stack

| Layer | Technology |
|---|---|
| **Framework** | .NET 10.0 + .NET MAUI Hybrid (Blazor WebView) |
| **UI** | Razor Components + Bootstrap 5.3.3 |
| **SSH Client** | SSH.NET (Renci.SshNet) 2026.0.0 |
| **State** | Local component state, no Redux/MobX |
| **Storage** | MAUI `SecureStorage` (credentials) + JSON file (logs) |
| **Architecture** | Folder-based Clean Architecture (Application, Infrastructure, Components) |

## 📋 Prerequisites

- **.NET 10 SDK** (10.0.400 atau lebih baru)
- **MAUI Workload** — install dengan:
  ```powershell
  dotnet workload install maui-windows
  ```
- **Visual Studio 2022** v17.x+ (dengan .NET 10 preview) atau **Visual Studio Code** + C# Dev Kit
- **VPS Ubuntu/Linux** dengan SSH root access (atau user dengan sudo)

## 🚀 Build & Run

### Clone
```bash
git clone https://github.com/rizkydev/VPSMonitoring.git
cd VPSMonitoring
```

### Restore & Build (Windows target)
```powershell
dotnet restore
dotnet build -f net10.0-windows10.0.19041.0
```

### Run dari Visual Studio
1. Buka `VPS Monitoring Desktop App.slnx`
2. Pilih profile **Windows Machine** (default)
3. Tekan **F5**

### Run dari command line
```powershell
dotnet run --project "VPS Monitoring Desktop App.csproj" -f net10.0-windows10.0.19041.0
```

## 🔐 Konfigurasi SSH

Saat pertama kali buka app, otomatis redirect ke `/settings`. Isi form:

| Field | Value | Catatan |
|---|---|---|
| Host | `rizky.pro` (atau IP VPS) | IP numerik juga OK |
| Port | `22` | Default SSH |
| Username | `root` | Hardcoded untuk VPS monitoring |
| Auth Method | `Password` atau `Private Key` | Private key support raw text atau path ke `.pem` file |

Klik **Test Koneksi** untuk verifikasi. Kalau sukses, klik **Simpan** → kredensial tersimpan encrypted di `SecureStorage` → redirect ke dashboard.

## 📁 Project Structure

```
VPS Monitoring Desktop App/
├── Application/          # Kontrak & DTO (zero external dependency)
│   ├── DTOs/             # SshConnectionConfig, SystemSnapshot, UpdateLogEntry, dll
│   ├── Interfaces/       # ISshService, ICredentialStore, IVpsMonitorService, IUpdateService, dll
│   └── Services/         # PackageCategorizer (heuristic)
├── Infrastructure/       # Implementasi
│   ├── Ssh/              # SshNetService, SshMonitorService, SshUpdateService, SshServerControlService
│   └── Storage/          # SecureCredentialStore, JsonUpdateLogService
├── Components/           # Razor UI (presentation)
│   ├── Layout/           # NavMenu (top-center), MainLayout
│   ├── Pages/            # Home (dashboard), Settings, Logs
│   └── Shared/           # Gauge, SoftwareList, WebsiteList, ServiceList, DockerList, FirewallCard, UpdateCard
├── Platforms/            # MAUI platform code
├── Resources/            # Icons, fonts, splash
├── wwwroot/              # Blazor web assets (index.html, app.css, lib/bootstrap)
├── App.xaml(.cs)
├── MainPage.xaml(.cs)
├── MauiProgram.cs        # DI registrations
├── AGENTS.md             # Project memory untuk AI agent sessions
└── VPS Monitor Desktop App.csproj
```

**Layer dependency:**
- `Components → Application + Infrastructure` (UI boleh tahu keduanya)
- `Infrastructure → Application` (implementasi depend interface)
- `Application → nothing` (pure contracts & DTOs)

## 📦 Publish (.exe)

Lihat detail di [AGENTS.md](./AGENTS.md) → section "Build & Run". Ringkas:

```cmd
cd /d "C:\Users\Rocky\Documents\Workplace\Portofolio\VPS Monitor Desktop App"
dotnet publish -f net10.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:WindowsOnly=true
```

Output di `bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\`.

**Catatan penting:** Untuk .NET 10 + MAUI 10.0.20, `PublishSingleFile=true` + `WindowsAppSDKSelfContained=true` punya issue dengan WinAppSDK 2.x conflict. **Framework-dependent** (di atas) lebih reliable — tapi butuh .NET 10 runtime di mesin target. Untuk portofolio, install manual: https://dotnet.microsoft.com/download/dotnet/10.0.

## 🛡️ Security & Disclaimer

### Keamanan yang Diterapkan
- ✅ Kredensial disimpan di MAUI `SecureStorage` (terenkripsi di Windows Credential Manager)
- ✅ Support SSH key-based auth (lebih aman dari password)
- ✅ Tidak pernah print/log kredensial ke console
- ✅ SSH connection di-disconnect setelah command selesai
- ✅ Update action butuh confirmation modal eksplisit
- ✅ Reboot pakai `shutdown -r +1` (delay 1 menit, cancelable via `sudo shutdown -c`)

### Yang Harus Anda Ketahui

> ⚠️ **Aplikasi ini menjalankan command langsung di server Anda via SSH dengan privilege setara user yang login.** Untuk user `root`, itu berarti **akses penuh tanpa konfirmasi lagi di server**.

**Tanggung jawab Anda sebagai user:**
- 🔍 **Baca setiap command** sebelum klik OK (app nampilkan command yang akan dijalankan, tapi tidak setiap output detail)
- 🧪 **Test di VPS non-production dulu** — pakai Hetzner test instance atau VPS lain
- 💾 **Backup data** sebelum run "Force Upgrade" atau "Reboot"
- 📜 **Audit Logs** secara berkala di tab `📋 Logs` — semua operasi tercatat dengan timestamp
- 🔐 **Jangan share kredensial** — kredensial hanya tersimpan di mesin lokal Anda

**Limitasi yang tidak bisa di-fix dari app:**
- ❌ Tidak bisa undo reboot setelah `shutdown -r +1` terkirim (sampai 1 menit, lalu tidak ada jalan kembali)
- ❌ Tidak bisa interrupt `apt-get upgrade` yang sedang berjalan (SSH.NET tidak support mid-command cancel)
- ❌ Tidak bisa OS upgrade (`do-release-upgrade`) — terlalu berisiko untuk automasi

## 🐛 Troubleshooting

### `.exe` tidak bisa dibuka
1. Install **Windows App Runtime 1.7+**: https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
2. Install **WebView2 Runtime** (biasanya sudah ada di Windows 10/11 modern): https://developer.microsoft.com/microsoft-edge/webview2/
3. Pastikan **.NET 10 Runtime** terinstall (untuk framework-dependent publish)

### SSH connection gagal
- Test manual dari terminal: `ssh root@your-server.com`
- Pastikan port 22 terbuka di firewall VPS
- Cek private key permission di Linux (`chmod 600 key.pem`)

### Package stuck setelah update
Lihat tab 🎯 **By Name** di System Updates card, atau baca [AGENTS.md](./AGENTS.md) untuk detail phased updates.

## 📄 Lisensi & Credit

**License:** [MIT](./LICENSE) — lihat file LICENSE untuk detail lengkap.

**⚠️ ATTRIBUTION REQUIRED:** Jika Anda fork, copy, modifikasi, atau distribute software ini (apapun bentuknya), Anda **WAJIB**:
- Preserve copyright notice "Copyright (c) 2026 Rizky (rizkydev)"
- Cantumkan credit ke original author di README/documentation/deployment
- Jangan claim sebagai karya asli Anda

**⚠️ NO LIABILITY:** Penulis TIDAK bertanggung jawab atas:
- Downtime, data loss, atau corruption di server Anda
- Kerugian finansial dari cloud resource usage
- Security breach atau unauthorized access
- System instability atau package conflict
- Efek samping lainnya dari penggunaan software ini

Gunakan dengan risiko Anda sendiri. **Test di environment non-production dulu.**

## 🙏 Credit

**Original Author:** [Rizky (@rizkydev)](https://github.com/rizkydev)

**Built with:**
- [SSH.NET](https://github.com/sshnet/SSH.NET) — MIT License
- [Bootstrap 5.3.3](https://getbootstrap.com/) — MIT License
- [.NET MAUI](https://learn.microsoft.com/dotnet/maui/) — MIT License

---

⭐ **Star repo ini** jika bermanfaat untuk Anda!
