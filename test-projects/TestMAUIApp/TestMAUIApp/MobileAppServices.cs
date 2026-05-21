using TestMAUIApp.Services;

namespace TestMAUIApp;

public static class MobileAppServices
{
    public static string ApiBaseAddress { get; private set; } = Constants.DefaultApiBaseAddress;

    public static LocalStorageService LocalStorageService { get; }

    public static HttpService HttpService { get; }

    public static DataBridgeService DataBridgeService { get; }

    public static AuthenticationService AuthenticationService { get; }

    public static UserService UserService { get; }

    public static ChatService ChatService { get; }

    public static RealtimeChatService RealtimeChatService { get; }

    static MobileAppServices()
    {
        LocalStorageService = new LocalStorageService();
        HttpService = new HttpService();
        DataBridgeService = new DataBridgeService();

        AuthenticationService = new AuthenticationService(HttpService, LocalStorageService, DataBridgeService);
        UserService = new UserService(HttpService, LocalStorageService, DataBridgeService, AuthenticationService);
        ChatService = new ChatService(HttpService, DataBridgeService, AuthenticationService);
        RealtimeChatService = new RealtimeChatService();
    }

    public static void Configure(string apiBaseAddress)
    {
        ApiBaseAddress = apiBaseAddress;
        HttpService.SetBaseAddress(apiBaseAddress);
        AuthenticationService.ApplyStoredTokenToHttpClient();
    }
}
