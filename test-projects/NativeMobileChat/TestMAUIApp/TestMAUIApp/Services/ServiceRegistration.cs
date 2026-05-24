using TestMAUIApp.Configuration;
using TestMAUIApp.Pages;

namespace TestMAUIApp.Services;

public static class ServiceRegistration
{
    public static MauiAppBuilder AddAppServices(this MauiAppBuilder builder, string? apiBaseAddress = null)
    {
        var options = new AppOptions
        {
            ApiBaseAddress = apiBaseAddress ?? Constants.DefaultApiBaseAddress,
        };

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<LocalStorageService>();
        builder.Services.AddSingleton<HttpService>();
        builder.Services.AddSingleton<DataBridgeService>();
        builder.Services.AddSingleton<AuthenticationService>();
        builder.Services.AddSingleton<UserService>();
        builder.Services.AddSingleton<ChatService>();
        builder.Services.AddSingleton<RealtimeChatService>();
        builder.Services.AddSingleton<ThreadService>();
        builder.Services.AddSingleton<ConversationService>();
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<ChatPageFactory>();

        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<ThreadFlyoutPage>();
        builder.Services.AddTransient<NewDirectChatPage>();
        builder.Services.AddTransient<NewGroupChatPage>();
        builder.Services.AddTransient<MainFlyoutShell>();

        return builder;
    }

    public static void ConfigureHttpClient(IServiceProvider services)
    {
        var options = services.GetRequiredService<AppOptions>();
        var http = services.GetRequiredService<HttpService>();
        http.SetBaseAddress(options.ApiBaseAddress);
        services.GetRequiredService<AuthenticationService>().ApplyStoredTokenToHttpClient();
    }
}
