using TestMAUIApp.Configuration;
using TestMAUIApp.Services;
using TestMAUIApp.Ui;

namespace TestMAUIApp.Pages;

public class LoginPage : ContentPage
{
    private readonly INavigationService _navigationService;
    private readonly AuthenticationService _authenticationService;
    private readonly ChatService _chatService;
    private readonly RealtimeChatService _realtimeChatService;
    private readonly AppOptions _appOptions;

    private readonly Label _statusLabel;
    private readonly Entry _userNameEntry;
    private readonly Entry _passwordEntry;
    private readonly Button _signInButton;

    public LoginPage(
        INavigationService navigationService,
        AuthenticationService authenticationService,
        ChatService chatService,
        RealtimeChatService realtimeChatService,
        AppOptions appOptions)
    {
        _navigationService = navigationService;
        _authenticationService = authenticationService;
        _chatService = chatService;
        _realtimeChatService = realtimeChatService;
        _appOptions = appOptions;

        Title = "Sign in";
        Padding = new Thickness(16);
        SharedUiFactory.ApplyPageChrome(this);

        _statusLabel = SharedUiFactory.BodyLabel("Sign in to join the real-time chat.");

        _userNameEntry = SharedUiFactory.Entry("Username", initialValue: "demo");
        _passwordEntry = SharedUiFactory.Entry("Password", isPassword: true, initialValue: "Password1!");
        _signInButton = SharedUiFactory.PrimaryButton("Sign in", OnSignInClicked);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    SharedUiFactory.Title("TestNativeMobileBackendAPI"),
                    SharedUiFactory.Caption($"API: {_appOptions.ApiBaseAddress}"),
                    _statusLabel,
                    SharedUiFactory.Card(_userNameEntry, _passwordEntry, _signInButton),
                },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var cached = await _authenticationService.GetCachedCredentialsAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(cached.UserName))
        {
            _userNameEntry.Text = cached.UserName;
        }

        if (!string.IsNullOrWhiteSpace(cached.Password))
        {
            _passwordEntry.Text = cached.Password;
        }

        if (!_authenticationService.IsAuthenticated)
        {
            return;
        }

        try
        {
            await CompleteSignInAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await _realtimeChatService.DisconnectAsync().ConfigureAwait(false);
            await _authenticationService.LogoutAsync().ConfigureAwait(false);
            SetStatus($"Session expired. Sign in again. ({ex.Message})");
        }
    }

    private async void OnSignInClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var userName = _userNameEntry.Text?.Trim() ?? string.Empty;
            var password = _passwordEntry.Text ?? string.Empty;
            var response = await _authenticationService.LoginAsync(userName, password).ConfigureAwait(false);

            if (response is null)
            {
                SetStatus("Sign in failed. Try demo / Password1!.");
                return;
            }

            SetStatus($"Signed in as {response.User?.DisplayName ?? response.User?.UserName}.");
            await CompleteSignInAsync().ConfigureAwait(false);
        });
    }

    private async Task CompleteSignInAsync()
    {
        var token = await _authenticationService.GetValidAccessTokenAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            await _realtimeChatService.EnsureConnectedAsync(token).ConfigureAwait(false);
        }

        await _chatService.SyncGlobalMessagesAsync().ConfigureAwait(false);
        await _navigationService.NavigateToMainShellAsync().ConfigureAwait(false);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        _signInButton.IsEnabled = false;
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            _signInButton.IsEnabled = true;
        }
    }

    private void SetStatus(string message) =>
        MainThread.BeginInvokeOnMainThread(() => _statusLabel.Text = message);
}
