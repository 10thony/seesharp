namespace TestMAUIApp.Droid
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseSharedMauiApp(Constants.AndroidEmulatorApiBaseAddress);

            return builder.Build();
        }
    }
}
