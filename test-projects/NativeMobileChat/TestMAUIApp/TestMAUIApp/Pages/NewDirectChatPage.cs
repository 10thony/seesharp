using System.Collections.ObjectModel;
using TestMAUIApp.Models;
using TestMAUIApp.Services;
using TestMAUIApp.Ui;

namespace TestMAUIApp.Pages;

public class NewDirectChatPage : ContentPage
{
    private readonly INavigationService _navigationService;
    private readonly UserService _userService;
    private readonly ConversationService _conversationService;

    private readonly ObservableCollection<ChatUserOption> _users = [];
    private readonly CollectionView _usersView;
    private readonly Label _statusLabel;
    private readonly Button _cancelButton;

    public NewDirectChatPage(
        INavigationService navigationService,
        UserService userService,
        ConversationService conversationService)
    {
        _navigationService = navigationService;
        _userService = userService;
        _conversationService = conversationService;

        Title = "New chat";
        Padding = new Thickness(12);
        SharedUiFactory.ApplyPageChrome(this, AppPalette.FlyoutBackground);

        _statusLabel = SharedUiFactory.Caption("Choose someone to message.");
        _cancelButton = SharedUiFactory.SecondaryButton("Cancel", OnCancelClicked);

        _usersView = new CollectionView
        {
            ItemsSource = _users,
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var title = SharedUiFactory.EmphasisLabel();
                title.SetBinding(Label.TextProperty, nameof(ChatUserOption.Label));

                var subtitle = SharedUiFactory.MutedLabel();
                subtitle.SetBinding(Label.TextProperty, nameof(ChatUserOption.UserName));

                return SharedUiFactory.Card(title, subtitle);
            }),
        };

        _usersView.SelectionChanged += OnUserSelected;

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            RowSpacing = 12,
        };

        var title = SharedUiFactory.Title("Start a direct chat");
        Grid.SetRow(title, 0);
        layout.Children.Add(title);

        Grid.SetRow(_statusLabel, 1);
        layout.Children.Add(_statusLabel);

        _usersView.VerticalOptions = LayoutOptions.Fill;
        Grid.SetRow(_usersView, 2);
        layout.Children.Add(_usersView);

        Grid.SetRow(_cancelButton, 3);
        layout.Children.Add(_cancelButton);

        Content = layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadUsersAsync().ConfigureAwait(false);
    }

    private async void OnCancelClicked(object? sender, EventArgs e) =>
        await Navigation.PopAsync().ConfigureAwait(false);

    private async void OnUserSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ChatUserOption user)
        {
            return;
        }

        try
        {
            _usersView.SelectedItem = null;
            _statusLabel.Text = $"Opening chat with {user.Label}...";

            var thread = await _conversationService
                .StartDirectThreadAsync(user.UserId, user.Label)
                .ConfigureAwait(false);

            await Navigation.PopAsync().ConfigureAwait(false);
            await _navigationService.NavigateToChatAsync(thread).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
    }

    private async Task LoadUsersAsync()
    {
        try
        {
            var users = await _userService.GetChatUsersAsync().ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _users.Clear();
                foreach (var user in users)
                {
                    _users.Add(user);
                }

                _statusLabel.Text = users.Count == 0
                    ? "No users available. Sign in and ensure the API is running."
                    : $"{users.Count} user(s) available";
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => _statusLabel.Text = $"Error: {ex.Message}");
        }
    }
}
