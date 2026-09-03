# AGENTS.md — Hetzner VPS Monitor Desktop App

> Project memory untuk sesi vibe coding selanjutnya. Baca dulu sebelum kerja di project ini.

## Project Overview

- **Tujuan**: Desktop companion tool monitoring VPS Hetzner (Ubuntu/Linux) via SSH. On-demand only — tidak ada polling/background loop, semua data diambil saat user klik "Cek Sekarang".
- **Stack**: .NET MAUI Hybrid (Blazor WebView) di .NET 10
- **Target**: Windows desktop + MacCatalyst (csproj multi-target)
- **GitHub**: https://github.com/rizkydev/VPSMonitoring.git (main branch, public)
- **Brief lengkap**: lihat `brief-hetzner-vps-monitor.md` (tersimpan terpisah).

## Build & Run

```powershell
# Restore + build (Windows target utama)
dotnet build "C:\Users\Rocky\Documents\Workplace\Portofolio\VPS Monitor Desktop App\VPS Monitor Desktop App.csproj" -f net10.0-windows10.0.19041.0

# Atau buka di Visual Studio / Rider, langsung F5
# Active debug profile: Windows Machine
```

Build output: `bin/Debug/net10.0-windows10.0.19041.0/win-x64/`

## Installer (Windows .exe)

Pakai **Inno Setup 6.7+** (install via `choco install innosetup`).

```powershell
# Step 1: Publish app (framework-dependent, ~150MB)
dotnet publish -f net10.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:WindowsOnly=true

# Step 2: Compile installer (~45 detik)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss

# Output: installer-output\HetznerVPSMonitor-Setup-1.0.0.exe (~42MB compressed)
```

Script: `installer.iss` di root. Install destination default `C:\Program Files\Hetzner VPS Monitor\`. Start Menu + Desktop shortcut, Uninstaller included. **NOTE**: `installer-output/` di-gitignore (build artifact, jangan commit).

## Arsitektur (Folder-Based Clean Architecture)

Single .csproj dengan 3 folder logis. **Tidak** multi-project — portofolio vibe coding lebih cepat iterasi.

```
VPS Monitor Desktop App/
├── Application/          # Kontrak & DTO (depend: zero external)
│   ├── DTOs/             # SshConnectionConfig, SystemSnapshot, CpuMetrics, dll
│   ├── Interfaces/       # ISshService, ICredentialStore, IVpsMonitorService
│   └── Services/         # PackageCategorizer (pure logic, no IO)
├── Infrastructure/       # Implementasi (depend: SSH.NET, MAUI SecureStorage)
│   ├── Ssh/              # SshNetService (test connection), SshMonitorService (snapshot)
│   └── Storage/          # SecureCredentialStore (MAUI SecureStorage)
├── Components/           # Razor UI (presentation layer)
│   ├── Layout/           # MainLayout, NavMenu
│   ├── Pages/            # Home (dashboard), Settings
│   └── Shared/           # Reusable: Gauge, SoftwareList, WebsiteList, ServiceList, DockerList, FirewallCard
├── Platforms/            # MAUI platform code (Windows, Android, iOS, MacCatalyst)
├── Resources/            # Icons, fonts, splash (template)
├── wwwroot/              # Blazor web assets (index.html, app.css, lib/bootstrap)
├── App.xaml(.cs)         # MAUI app entry
├── MainPage.xaml(.cs)    # Hosts BlazorWebView
└── MauiProgram.cs        # DI registrations
```

**Layer dependency**: `Components → Application + Infrastructure` (UI boleh tahu keduanya), `Infrastructure → Application` (implementasi depend interface), `Application → nothing` (pure).

## Konvensi

- **Namespace**: `VPS_Monitor_Desktop_App.{Layer}.{Sub}` (mis. `VPS_Monitor_Desktop_App.Infrastructure.Ssh`)
- **File naming**: `PascalCase` untuk semua file, suffix per-jenis (`Service` untuk interface impl, `Card`/`List` untuk komponen Razor)
- **UI**: Bootstrap 5.3.3 utility dulu, custom CSS seminimal mungkin. Dark mode default (`<body data-bs-theme="dark">`)
- **DTO design**: 
  - **Record** untuk immutable value (mis. `ConnectionTestResult` yang sekali buat)
  - **Mutable class** untuk form binding (mis. `SshConnectionConfig` — `InputText` butuh setter)
- **Service registration**: `AddSingleton` (semua service stateless)
- **CancellationToken** disupport di semua async public method

## Konflik Nama yang Sudah Di-Resolve

⚠️ **`VPS_Monitor_Desktop_App.Application` bentrok dengan `Microsoft.Maui.Controls.Application`**.

Gejala: `App.xaml.cs` error CS0118 ("'Application' is a namespace but is used like a type").

**Fix**: `App.xaml.cs:3` pakai fully qualified `Microsoft.Maui.Controls.Application`. Jangan rename folder `Application/` — sudah jadi pattern.

## Keputusan Teknis Penting

1. **On-demand only** (sesuai brief): tidak ada background timer, tidak ada SignalR, tidak ada push. Trigger eksplisit dari UI.
2. **Single shell script per "Cek Sekarang"**: 1 sesi SSH, 1 command panjang dengan output delimited `=== section ===`. Lebih cepat dari N command terpisah (1 round-trip), lebih mudah di-parse.
3. **SSH commands sequential** (bukan parallel): SSH.NET `SshClient` tidak thread-safe. 15 command × ~150ms = ~2s total masih acceptable.
4. **Gauge**: CSS `conic-gradient` zero-dependency, tidak pakai library chart. Color threshold: hijau <70, kuning 70-90, merah ≥90.
5. **Launcher untuk buka website**: `Launcher.Default.OpenAsync(uri)` dari `Microsoft.Maui.ApplicationModel` (built-in MAUI, tidak perlu package).
6. **Kategori package**: heuristik prefix-match di `Application/Services/PackageCategorizer.cs`. Pragmatis, bukan comprehensive. Paket tidak match → kategori `System` (fallback).
7. **OS family detection**: parse `/etc/os-release`, mapping ke enum `OsFamily` (Debian/Rhel/Arch). Belum dipakai di command selection — tersimpan untuk iterasi opsional (auto-detect package manager).

## SSH Commands yang Dijalankan (1 sesi, sequential)

Lihat `Infrastructure/Ssh/SshMonitorService.cs:30-66` untuk script lengkap. Sections:
- `top`, `nproc` → CPU + core count
- `free` → Memory
- `df` → Storage (skip tmpfs/devtmpfs/overlay)
- `uptime` → Uptime + load average
- `os-release` → OS family detection
- `packages` → `dpkg-query` (Debian) atau `rpm -qa` (RHEL fallback)
- `services` → loop 14 service umum (nginx, apache2, mysql, dll) via `systemctl is-active`
- `nginx-config` + `nginx-status` → websites (grep sites-enabled untuk server_name)
- `apache-config` + `apache-status` → websites (apache2ctl -S)
- `docker-ps` → running containers + exposed ports
- `ufw` → firewall status
- `ss` → listening ports (untuk SSL detection di websites)

## State Management

Tidak ada state management library (Redux, dll). Pakai:
- `@inject` di Razor untuk service
- Local `private` fields per page (`_snapshot`, `_isLoading`, `_errorBanner`)
- `_cts` (CancellationTokenSource) di `Home.razor` agar klik berulang cancel request sebelumnya
- `IDisposable` di Home untuk cleanup

## Current State (sudah jalan)

✅ First-run SSH setup (`/settings` page, form + test + save via SecureStorage)
✅ Dashboard dengan "Cek Sekarang" button
✅ Resource Usage cards (CPU/RAM/Storage/Uptime) dengan gauge conic-gradient
✅ Multi-disk breakdown (jika > 1 disk)
✅ Hosted Websites (clickable → Launcher.OpenAsync) + SSL badge
✅ Service Status (14 service umum)
✅ Firewall (UFW) + open ports
✅ Docker Containers (jika docker terinstall & ada container jalan)
✅ Software Installed (search + filter kategori + grouped)
✅ OS family detection
✅ Dark mode default
✅ Error handling (banner + skeleton + last-known state)
✅ Mobile/responsive layout (Bootstrap grid breakpoints)

## Pending / Future Work

Dari brief, 10 fitur opsional. Status:

| # | Fitur | Status |
|---|---|---|
| 1 | Service Status Checker | ✅ done |
| 2 | SSL Certificate Expiry Checker | ❌ — perlu parse `openssl s_client`, tambah command. |
| 3 | Firewall (UFW) | ✅ done |
| 4 | Uptime & Load Average | ✅ done (di metric card) |
| 5 | Docker Container List | ✅ done |
| 6 | Multi-Server Profile | ❌ — major refactor. `ICredentialStore` perlu support multiple profiles + dropdown UI. |
| 7 | Export/Share Report (PDF) | ❌ — perlu library PDF (QuestPDF?). |
| 8 | Riwayat Pengecekan Lokal | ❌ — perlu storage (SQLite atau JSON file). |
| 9 | Quick Action (restart service) | ❌ — perlu konfirmasi dialog + `ISshService.ExecuteCommandAsync` (sudah ada). |
| 10 | Auto-detect OS Family | ✅ enum-nya done, command selection belum di-switch per family. |

**Belum test runtime**: kode sudah compile tapi belum dijalankan dengan VPS Hetzner sungguhan. Untuk verifikasi pertama, butuh:
- VPS Ubuntu dengan SSH aktif
- Akses password ATAU private key
- Setujui bahwa beberapa command mungkin butuh sudo (UFW)

## Tips Iterasi Selanjutnya

- **Mau tambah section baru di dashboard**: buat komponen di `Components/Shared/`, terima `IReadOnlyList<X>` atau `X?` sebagai `[Parameter, EditorRequired]`. Tambah di `Home.razor` + section card.
- **Mau tambah SSH command**: tambah marker di `SshMonitorService.cs:68-71`, tambah parser, tambah field di `SystemSnapshot` DTO.
- **Mau tambah DTO**: edit `Application/DTOs/SystemSnapshot.cs`. Ingat: mutable class untuk form binding, record untuk value immutable.
- **Mau ubah warna gauge**: edit `Components/Shared/Gauge.razor:31-36` (color threshold) atau CSS `Gauge.razor.css`.
- **Build errors namespace**: cek `Components/_Imports.razor` — pastikan namespace `Components.Shared` ada di sana.

## Environment

- .NET SDK: 10.0.400 (default, tidak ada `global.json`)
- Workload MAUI: `maui-windows` terinstall (untuk Windows build). Android/iOS/MacCatalyst workload juga ada tapi belum diuji.
- Windows: pakai `WindowsPackageType=None` (unpackaged Win32, bukan MSIX) — lihat csproj:42
- XAML Source Generation aktif (`<MauiXamlInflator>SourceGen</MauiXamlInflator>`)
- Bootstrap: 5.3.3 di `wwwroot/lib/bootstrap/`
