using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using TestMAUIApp.Models;
using TestMAUIApp.Services;
using TestMAUIApp.Ui;

namespace TestMAUIApp.Pages;

public class ThreadFlyoutPage : ContentPage
{
    private readonly INavigationService _navigationService;
    private readonly ThreadService _threadService;
    private readonly ChatService _chatService;
    private readonly RealtimeChatService _realtimeChatService;
    private readonly AuthenticationService _authenticationService;
    private readonly IServiceProvider _services;

    private readonly ObservableCollection<ChatThreadSummary> _threads = [];
    private readonly CollectionView _threadsView;
    private readonly Label _statusLabel;
    private readonly Button _newChatButton;
    private readonly Button _newGroupButton;
    private readonly Button _refreshButton;
    private readonly Button _signOutButton;

    public ThreadFlyoutPage(
        INavigationService navigationService,
        ThreadService threadService,
        ChatService chatService,
        RealtimeChatService realtimeChatService,
        AuthenticationService authenticationService,
        IServiceProvider services)
    {
        _navigationService = navigationService;
        _threadService = threadService;
        _chatService = chatService;
        _realtimeChatService = realtimeChatService;
        _authenticationService = authenticationService;
        _services = services;

        Title = "Chats";
        Padding = new Thickness(12);
        SharedUiFactory.ApplyPageChrome(this, AppPalette.FlyoutBackground);

        _statusLabel = SharedUiFactory.Caption("Select a conversation.");
        _newChatButton = SharedUiFactory.PrimaryButton("New direct chat", OnNewChatClicked);
        _newGroupButton = SharedUiFactory.SecondaryButton("New group", OnNewGroupClicked);
        _refreshButton = SharedUiFactory.SecondaryButton("Refresh threads", OnRefreshClicked);
        _signOutButton = SharedUiFactory.SecondaryButton("Sign out", OnSignOutClicked);

        _threadsView = new CollectionView
        {
            ItemsSource = _threads,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var title = SharedUiFactory.EmphasisLabel();
                title.SetBinding(Label.TextProperty, nameof(ChatThreadSummary.RecipientName));

                var preview = SharedUiFactory.BodyLabel();
                preview.FontSize = 13;
                preview.LineBreakMode = LineBreakMode.TailTruncation;
                preview.MaxLines = 2;
                preview.TextColor = AppPalette.CaptionText;
                preview.SetBinding(Label.TextProperty, nameof(ChatThreadSummary.LastMessagePreview));

                var timestamp = SharedUiFactory.MutedLabel();
                timestamp.HorizontalOptions = LayoutOptions.End;
                timestamp.SetBinding(Label.TextProperty, nameof(ChatThreadSummary.LastMessageAtDisplay));

                var meta = SharedUiFactory.MutedLabel();
                meta.SetBinding(Label.TextProperty, new Binding(
                    nameof(ChatThreadSummary.MessageCount),
                    stringFormat: "{0} message(s)"));

                var kind = SharedUiFactory.MutedLabel();
                kind.SetBinding(Label.TextProperty, nameof(ChatThreadSummary.ThreadTypeLabel));

                return SharedUiFactory.Card(title, preview, timestamp, meta, kind);
            }),
        };

        _threadsView.SelectionChanged += OnThreadSelected;

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            RowSpacing = 12,
        };

        var title = SharedUiFactory.Title("Threads");
        Grid.SetRow(title, 0);
        layout.Children.Add(title);

        Grid.SetRow(_statusLabel, 1);
        layout.Children.Add(_statusLabel);

        Grid.SetRow(_newChatButton, 2);
        layout.Children.Add(_newChatButton);

        Grid.SetRow(_newGroupButton, 3);
        layout.Children.Add(_newGroupButton);

        Grid.SetRow(_refreshButton, 4);
        layout.Children.Add(_refreshButton);

        _threadsView.VerticalOptions = LayoutOptions.Fill;
        Grid.SetRow(_threadsView, 5);
        layout.Children.Add(_threadsView);

        Grid.SetRow(_signOutButton, 6);
        layout.Children.Add(_signOutButton);

        Content = layout;

        _realtimeChatService.MessageReceived += OnRealtimeMessage;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadThreadsAsync(refreshFromServer: true).ConfigureAwait(false);
    }

    private async void OnNewChatClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<NewDirectChatPage>()).ConfigureAwait(false);

    private async void OnNewGroupClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(_services.GetRequiredService<NewGroupChatPage>()).ConfigureAwait(false);

    private async void OnRefreshClicked(object? sender, EventArgs e) =>
        await LoadThreadsAsync(refreshFromServer: true).ConfigureAwait(false);

    private async void OnSignOutClicked(object? sender, EventArgs e)
    {
        await _realtimeChatService.DisconnectAsync().ConfigureAwait(false);
        await _authenticationService.LogoutAsync().ConfigureAwait(false);
        await _navigationService.NavigateToLoginAsync().ConfigureAwait(false);
    }

    private async void OnThreadSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ChatThreadSummary thread)
        {
            return;
        }

        try
        {
            _threadsView.SelectedItem = null;
            await _navigationService.NavigateToChatAsync(thread).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Navigation error: {ex.Message}";
        }
    }

    private void OnRealtimeMessage(object? sender, EventArgs e) =>
        _ = MainThread.InvokeOnMainThreadAsync(() => LoadThreadsAsync(refreshFromServer: true));

    private async Task LoadThreadsAsync(bool refreshFromServer)
    {
        try
        {
            if (refreshFromServer && _authenticationService.IsAuthenticated)
            {
                await _chatService.SyncGlobalMessagesAsync().ConfigureAwait(false);
            }

            var threads = await _threadService.GetThreadSummariesAsync().ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _threads.Clear();
                foreach (var thread in threads)
                {
                    _threads.Add(thread);
                }

                _statusLabel.Text = $"{threads.Count} thread(s)";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => _statusLabel.Text = $"Error: {ex.Message}");
        }
    }
}
