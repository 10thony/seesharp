using Microsoft.Extensions.Logging;

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
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            MobileAppServices.Configure(apiBaseAddress ?? Constants.DefaultApiBaseAddress);

            return builder;
        }
    }
}
