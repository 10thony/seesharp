using TestMAUIApp.Configuration;
using TestMAUIApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TestMAUIApp;

public static class MobileAppServices
{
    private static IServiceProvider? _services;

    public static string ApiBaseAddress =>
        _services?.GetService(typeof(AppOptions)) is AppOptions options
            ? options.ApiBaseAddress
            : Constants.DefaultApiBaseAddress;

    public static LocalStorageService LocalStorageService => GetRequired<LocalStorageService>();

    public static HttpService HttpService => GetRequired<HttpService>();

    public static DataBridgeService DataBridgeService => GetRequired<DataBridgeService>();

    public static AuthenticationService AuthenticationService => GetRequired<AuthenticationService>();

    public static UserService UserService => GetRequired<UserService>();

    public static ChatService ChatService => GetRequired<ChatService>();

    public static RealtimeChatService RealtimeChatService => GetRequired<RealtimeChatService>();

    public static INavigationService NavigationService => GetRequired<INavigationService>();

    public static void Initialize(IServiceProvider services)
    {
        _services = services;
    }

    private static T GetRequired<T>() where T : notnull
    {
        if (_services is null)
        {
            throw new InvalidOperationException("MobileAppServices has not been initialized.");
        }

        return (T)_services.GetRequiredService(typeof(T));
    }
}
