using Microsoft.Extensions.Logging;
using VPS_Monitor_Desktop_App.Application.Interfaces;
using VPS_Monitor_Desktop_App.Infrastructure.Ssh;
using VPS_Monitor_Desktop_App.Infrastructure.Storage;

namespace VPS_Monitor_Desktop_App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
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

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
