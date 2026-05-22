using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TestMAUIApp.Models;
using TestMAUIApp.Services;
using TestMAUIApp.Ui;

namespace TestMAUIApp.Pages;

public class NewGroupChatPage : ContentPage
{
    private readonly INavigationService _navigationService;
    private readonly UserService _userService;
    private readonly ConversationService _conversationService;

    private readonly ObservableCollection<SelectableUserRow> _users = [];
    private readonly CollectionView _usersView;
    private readonly Label _statusLabel;
    private readonly Entry _groupNameEntry;
    private readonly Button _createButton;
    private readonly Button _cancelButton;

    public NewGroupChatPage(
        INavigationService navigationService,
        UserService userService,
        ConversationService conversationService)
    {
        _navigationService = navigationService;
        _userService = userService;
        _conversationService = conversationService;

        Title = "New group";
        Padding = new Thickness(12);
        SharedUiFactory.ApplyPageChrome(this, AppPalette.FlyoutBackground);

        _statusLabel = SharedUiFactory.Caption("Select members, then create the group.");
        _groupNameEntry = SharedUiFactory.Entry("Group name");
        _createButton = SharedUiFactory.PrimaryButton("Create group", OnCreateClicked);
        _cancelButton = SharedUiFactory.SecondaryButton("Cancel", OnCancelClicked);

        _usersView = new CollectionView
        {
            ItemsSource = _users,
            SelectionMode = SelectionMode.None,
            ItemTemplate = new DataTemplate(() =>
            {
                var title = SharedUiFactory.EmphasisLabel();
                title.SetBinding(Label.TextProperty, nameof(SelectableUserRow.Label));

                var subtitle = SharedUiFactory.MutedLabel();
                subtitle.SetBinding(Label.TextProperty, nameof(SelectableUserRow.UserName));

                var toggle = new Switch
                {
                    HorizontalOptions = LayoutOptions.End,
                };
                toggle.SetBinding(
                    Switch.IsToggledProperty,
                    nameof(SelectableUserRow.IsSelected),
                    mode: BindingMode.TwoWay);
                toggle.Toggled += (_, _) => UpdateSelectionStatus();

                return SharedUiFactory.Card(title, subtitle, toggle);
            }),
        };

        var layout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
            RowSpacing = 12,
        };

        var title = SharedUiFactory.Title("Create a group");
        Grid.SetRow(title, 0);
        layout.Children.Add(title);

        Grid.SetRow(_statusLabel, 1);
        layout.Children.Add(_statusLabel);

        Grid.SetRow(_groupNameEntry, 2);
        layout.Children.Add(_groupNameEntry);

        var hint = SharedUiFactory.Caption("Toggle members to include in the group.");
        Grid.SetRow(hint, 3);
        layout.Children.Add(hint);

        _usersView.VerticalOptions = LayoutOptions.Fill;
        Grid.SetRow(_usersView, 4);
        layout.Children.Add(_usersView);

        Grid.SetRow(_createButton, 5);
        layout.Children.Add(_createButton);

        Grid.SetRow(_cancelButton, 6);
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

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var name = _groupNameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _statusLabel.Text = "Enter a group name.";
            return;
        }

        var members = _users
            .Where(u => u.IsSelected)
            .Select(u => new ChatUserOption
            {
                UserId = u.UserId,
                UserName = u.UserName,
                DisplayName = u.DisplayName,
            })
            .ToList();

        if (members.Count == 0)
        {
            _statusLabel.Text = "Select at least one member.";
            return;
        }

        try
        {
            _createButton.IsEnabled = false;
            var thread = await _conversationService
                .CreateGroupAsync(name, members)
                .ConfigureAwait(false);

            await Navigation.PopAsync().ConfigureAwait(false);
            await _navigationService.NavigateToChatAsync(thread).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _createButton.IsEnabled = true;
        }
    }

    private void UpdateSelectionStatus()
    {
        var count = _users.Count(u => u.IsSelected);
        _statusLabel.Text = count == 0
            ? "No members selected"
            : $"{count} member(s) selected";
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
                    _users.Add(new SelectableUserRow(user));
                }

                UpdateSelectionStatus();
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => _statusLabel.Text = $"Error: {ex.Message}");
        }
    }

    private sealed class SelectableUserRow : INotifyPropertyChanged
    {
        private bool _isSelected;

        public SelectableUserRow(ChatUserOption user)
        {
            UserId = user.UserId;
            UserName = user.UserName;
            DisplayName = user.DisplayName;
        }

        public string UserId { get; }

        public string UserName { get; }

        public string DisplayName { get; }

        public string Label => string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
