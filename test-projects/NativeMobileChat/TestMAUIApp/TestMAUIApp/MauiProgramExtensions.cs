using Microsoft.Extensions.Logging;
using TestMAUIApp.Services;

namespace TestMAUIApp
{
    public static class MauiProgramExtensions
    {
        public static MauiAppBuilder UseSharedMauiApp(
            this MauiAppBuilder builder,
            string? apiBaseAddress = null)
        {
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .AddAppServices(apiBaseAddress);

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder;
        }

        public static MauiApp BuildSharedMauiApp(this MauiAppBuilder builder)
        {
            var app = builder.Build();
            ServiceRegistration.ConfigureHttpClient(app.Services);
            MobileAppServices.Initialize(app.Services);
            return app;
        }
    }
}
