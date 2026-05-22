namespace TestMAUIApp.WinUI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseSharedMauiApp(Constants.WindowsApiBaseAddress);

            return builder.BuildSharedMauiApp();
        }
    }
}
