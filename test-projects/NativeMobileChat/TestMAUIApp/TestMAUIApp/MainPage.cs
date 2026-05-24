using System.Collections.ObjectModel;
using TestMAUIApp.Models;
using TestMAUIApp.Services;
using TestMAUIApp.Ui;

namespace TestMAUIApp;

public class MainPage : ContentPage
{
    private readonly ObservableCollection<ChatMessageRecord> _messages = [];
    private readonly CollectionView _messagesView;
    private readonly Label _statusLabel;
    private readonly Label _apiUrlLabel;
    private readonly Entry _userNameEntry;
    private readonly Entry _passwordEntry;
    private readonly Entry _messageEntry;
    private readonly Button _loginButton;
    private readonly Button _logoutButton;
    private readonly Button _refreshButton;
    private readonly Button _sendButton;

    public MainPage()
    {
        Title = "Test Chat";
        Padding = new Thickness(16);

        _apiUrlLabel = SharedUiFactory.Caption($"API: {MobileAppServices.ApiBaseAddress}");
        _statusLabel = new Label
        {
            Text = "Sign in to join the real-time chat.",
            LineBreakMode = LineBreakMode.WordWrap,
        };
        _userNameEntry = SharedUiFactory.Entry("Username", initialValue: "demo");
        _passwordEntry = SharedUiFactory.Entry("Password", isPassword: true, initialValue: "Password1!");
        _messageEntry = SharedUiFactory.Entry("Type a message...");

        _loginButton = SharedUiFactory.PrimaryButton("Sign in", OnLoginClicked);
        _logoutButton = SharedUiFactory.SecondaryButton("Sign out", OnLogoutClicked);
        _refreshButton = SharedUiFactory.SecondaryButton("Refresh", OnRefreshClicked);
        _sendButton = SharedUiFactory.PrimaryButton("Send", OnSendClicked);

        _messagesView = new CollectionView
        {
            ItemsSource = _messages,
            ItemTemplate = new DataTemplate(() =>
            {
                var author = new Label
                {
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 13,
                };
                author.SetBinding(Label.TextProperty, nameof(ChatMessageRecord.SenderName));

                var body = new Label
                {
                    FontSize = 15,
                    LineBreakMode = LineBreakMode.WordWrap,
                };
                body.SetBinding(Label.TextProperty, nameof(ChatMessageRecord.Content));

                var timestamp = new Label
                {
                    FontSize = 11,
                    TextColor = Colors.Gray,
                };
                timestamp.SetBinding(Label.TextProperty, new Binding(
                    nameof(ChatMessageRecord.SentAtUtc),
                    converter: new SentTimeValueConverter()));

                return SharedUiFactory.Card(author, body, timestamp);
            }),
            SelectionMode = SelectionMode.None,
            HeightRequest = 360,
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    SharedUiFactory.Title("TestNativeMobileBackendAPI Chat"),
                    _apiUrlLabel,
                    _statusLabel,
                    SharedUiFactory.Card(_userNameEntry, _passwordEntry, new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children = { _loginButton, _logoutButton },
                    }),
                    SharedUiFactory.Card(_refreshButton, _messagesView, _messageEntry, _sendButton),
                },
            },
        };

        MobileAppServices.RealtimeChatService.MessageReceived += OnRealtimeMessage;
        UpdateSignedInState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var cached = await MobileAppServices.AuthenticationService.GetCachedCredentialsAsync();
        if (!string.IsNullOrWhiteSpace(cached.UserName))
        {
            MainThread.BeginInvokeOnMainThread(() => _userNameEntry.Text = cached.UserName);
        }
        if (!string.IsNullOrWhiteSpace(cached.Password))
        {
            MainThread.BeginInvokeOnMainThread(() => _passwordEntry.Text = cached.Password);
        }

        if (MobileAppServices.AuthenticationService.IsAuthenticated)
        {
            await ConnectRealtimeAsync();
            await LoadMessagesAsync(refreshFromServer: true);
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await MobileAppServices.RealtimeChatService.DisconnectAsync();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            var userName = _userNameEntry.Text?.Trim() ?? string.Empty;
            var password = _passwordEntry.Text ?? string.Empty;
            var response = await MobileAppServices.AuthenticationService.LoginAsync(userName, password);

            if (response is null)
            {
                SetStatus("Sign in failed. Try demo / Password1!.");
                return;
            }

            SetStatus($"Signed in as {response.User?.DisplayName ?? response.User?.UserName}.");
            await ConnectRealtimeAsync();
            await LoadMessagesAsync(refreshFromServer: true);
        });
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            await MobileAppServices.RealtimeChatService.DisconnectAsync();
            await MobileAppServices.AuthenticationService.LogoutAsync();
            _messages.Clear();
            SetStatus("Signed out.");
        });
    }

    private async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await RunBusyAsync(() => LoadMessagesAsync(refreshFromServer: true));
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        var text = _messageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            await MobileAppServices.ChatService.SendMessageAsync(text);
            _messageEntry.Text = string.Empty;
        });
    }

    private void OnRealtimeMessage(object? sender, EventArgs e)
    {
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await LoadMessagesAsync(refreshFromServer: true);
        });
    }

    private async Task ConnectRealtimeAsync()
    {
        var token = MobileAppServices.AuthenticationService.AccessToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            await MobileAppServices.RealtimeChatService.EnsureConnectedAsync(token);
        }
    }

    private async Task LoadMessagesAsync(bool refreshFromServer)
    {
        var records = await MobileAppServices.ChatService.GetMessagesAsync(refreshFromServer);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _messages.Clear();
            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.SenderName))
                {
                    record.SenderName = record.IsOutgoing ? "You" : "Other";
                }

                _messages.Add(record);
            }
        });

        SetStatus($"{records.Count} message(s) loaded.");
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        SetInteractiveState(false);
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            UpdateSignedInState();
        }
    }

    private void UpdateSignedInState()
    {
        ExecuteOnMainThread(() =>
        {
            var signedIn = MobileAppServices.AuthenticationService.IsAuthenticated;
            _loginButton.IsEnabled = !signedIn;
            _logoutButton.IsEnabled = signedIn;
            _refreshButton.IsEnabled = signedIn;
            _messageEntry.IsEnabled = signedIn;
            _sendButton.IsEnabled = signedIn;
            _userNameEntry.IsEnabled = !signedIn;
            _passwordEntry.IsEnabled = !signedIn;
        });
    }

    private void SetInteractiveState(bool enabled)
    {
        ExecuteOnMainThread(() =>
        {
            _loginButton.IsEnabled = enabled;
            _logoutButton.IsEnabled = enabled;
            _refreshButton.IsEnabled = enabled;
            _sendButton.IsEnabled = enabled;
        });
    }

    private void SetStatus(string message)
    {
        ExecuteOnMainThread(() => _statusLabel.Text = message);
    }

    private static void ExecuteOnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        MainThread.BeginInvokeOnMainThread(action);
    }

    private sealed class SentTimeValueConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return value is DateTime sentAt
                ? sentAt.ToLocalTime().ToString("g")
                : string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}
