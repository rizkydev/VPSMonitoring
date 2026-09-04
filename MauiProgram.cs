using Microsoft.Extensions.Logging;
using VPS_Monitor_Desktop_App.Application.Interfaces;
using VPS_Monitor_Desktop_App.Infrastructure.Services;
using VPS_Monitor_Desktop_App.Infrastructure.Ssh;
using VPS_Monitor_Desktop_App.Infrastructure.Storage;

namespace VPS_Monitor_Desktop_App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // ------------------------------------------------------------------
            // WINDOWS ONLY: paksa WebView2 pakai user data folder di %LOCALAPPDATA%.
            //
            // Default UDF untuk Win32 (unpackaged) = <exe-dir>\<exe-name>.WebView2\
            // Saat app di-install ke C:\Program Files\VPSMonitoringDesktop\,
            // folder itu ada tapi user cuma punya (RX) read+execute — WebView2
            // gak bisa write, init silent-fail, BlazorWebView hitam.
            //
            // WebView2 baca env var WEBVIEW2_USER_DATA_FOLDER SEBELUM init dan
            // override default. Folder %LOCALAPPDATA%\VPSMonitoringDesktop\WebView2Data
            // selalu writable (user-owned).
            //
            // Reference: https://learn.microsoft.com/microsoft-edge/webview2/concepts/user-data-folder
            // ------------------------------------------------------------------
#if WINDOWS
            var webView2UserDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VPSMonitoringDesktop",
                "WebView2Data");
            Directory.CreateDirectory(webView2UserDataFolder);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webView2UserDataFolder);
#endif

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // Application services — singletons karena stateless & aman dipakai lintas page.
            builder.Services.AddSingleton<ISshService, SshNetService>();
            builder.Services.AddSingleton<ICredentialStore, SecureCredentialStore>();
            builder.Services.AddSingleton<IVpsMonitorService, SshMonitorService>();
            builder.Services.AddSingleton<IUpdateService, SshUpdateService>();
            builder.Services.AddSingleton<IServerControlService, SshServerControlService>();
            builder.Services.AddSingleton<IUpdateLogService, JsonUpdateLogService>();

            // Singleton STATE services — hold CancellationToken di level app (bukan component)
            // supaya long-running process (Cek Sekarang, Update Packages) TIDAK ke-cancel
            // saat user navigate ke page lain. Component subscribe event untuk re-render.
            builder.Services.AddSingleton<ISnapshotCheckState, SnapshotCheckState>();
            builder.Services.AddSingleton<IUpdateState, UpdateState>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
