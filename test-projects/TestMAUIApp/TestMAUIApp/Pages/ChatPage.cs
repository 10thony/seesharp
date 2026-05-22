using System.Collections.ObjectModel;

using TestMAUIApp.Models;

using TestMAUIApp.Services;

using TestMAUIApp.Ui;



namespace TestMAUIApp.Pages;



public class ChatPage : ContentPage

{

    private readonly ChatThreadSummary _thread;

    private readonly ChatService _chatService;

    private readonly RealtimeChatService _realtimeChatService;

    private readonly AuthenticationService _authenticationService;



    private readonly ObservableCollection<ChatMessageRecord> _messages = [];

    private readonly CollectionView _messagesView;

    private readonly Label _statusLabel;

    private readonly Entry _messageEntry;

    private readonly Button _sendButton;



    public ChatPage(

        ChatThreadSummary thread,

        ChatService chatService,

        RealtimeChatService realtimeChatService,

        AuthenticationService authenticationService)

    {

        _thread = thread;

        _chatService = chatService;

        _realtimeChatService = realtimeChatService;

        _authenticationService = authenticationService;



        ConversationId = thread.ConversationId;

        Title = thread.RecipientName;

        Padding = new Thickness(16);

        SharedUiFactory.ApplyPageChrome(this);



        _statusLabel = SharedUiFactory.BodyLabel(GetThreadDescription(thread));



        _messageEntry = SharedUiFactory.Entry("Type a message...");

        _sendButton = SharedUiFactory.PrimaryButton("Send", OnSendClicked);



        _messagesView = new CollectionView

        {

            ItemsSource = _messages,

            SelectionMode = SelectionMode.None,

            ItemTemplate = new DataTemplate(() =>

            {

                var author = SharedUiFactory.EmphasisLabel();

                author.FontSize = 13;

                author.SetBinding(Label.TextProperty, nameof(ChatMessageRecord.SenderName));



                var body = SharedUiFactory.BodyLabel();

                body.FontSize = 15;

                body.SetBinding(Label.TextProperty, nameof(ChatMessageRecord.Content));



                var timestamp = SharedUiFactory.MutedLabel();

                timestamp.SetBinding(Label.TextProperty, new Binding(

                    nameof(ChatMessageRecord.SentAtUtc),

                    converter: new SentTimeValueConverter()));



                return SharedUiFactory.Card(author, body, timestamp);

            }),

            VerticalOptions = LayoutOptions.Fill,

        };



        var layout = new Grid

        {

            RowDefinitions =

            {

                new RowDefinition(GridLength.Auto),

                new RowDefinition(GridLength.Auto),

                new RowDefinition(GridLength.Star),

                new RowDefinition(GridLength.Auto),

                new RowDefinition(GridLength.Auto),

            },

            RowSpacing = 12,

        };



        var threadCaption = SharedUiFactory.Caption($"Thread: {_thread.ConversationId}");

        Grid.SetRow(threadCaption, 0);

        layout.Children.Add(threadCaption);



        Grid.SetRow(_statusLabel, 1);

        layout.Children.Add(_statusLabel);



        Grid.SetRow(_messagesView, 2);

        layout.Children.Add(_messagesView);



        Grid.SetRow(_messageEntry, 3);

        layout.Children.Add(_messageEntry);



        Grid.SetRow(_sendButton, 4);

        layout.Children.Add(_sendButton);



        Content = layout;



        _chatService.MessagesSynced += OnMessagesSynced;

        _realtimeChatService.MessageReceived += OnRealtimeMessage;

    }



    public string ConversationId { get; }

    private static string GetThreadDescription(ChatThreadSummary thread) => thread.Kind switch
    {
        ChatThreadKind.Global => "Global room — all participants can see these messages.",
        ChatThreadKind.Group => thread.MemberCount > 0
            ? $"Group chat with {thread.MemberCount} member(s). Messages stay on this device."
            : "Group chat — messages stay on this device.",
        _ => $"Direct thread with {thread.RecipientName}. Messages appear in the shared room.",
    };



    protected override async void OnAppearing()

    {

        base.OnAppearing();

        await LoadMessagesAsync(refreshFromServer: true).ConfigureAwait(false);

    }



    protected override void OnDisappearing()

    {

        base.OnDisappearing();

        _chatService.MessagesSynced -= OnMessagesSynced;

        _realtimeChatService.MessageReceived -= OnRealtimeMessage;

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

            await _chatService.SendMessageAsync(_thread.ConversationId, text).ConfigureAwait(false);

            _messageEntry.Text = string.Empty;

            await LoadMessagesAsync(refreshFromServer: false).ConfigureAwait(false);

        });

    }



    private void OnMessagesSynced(object? sender, EventArgs e) =>

        _ = MainThread.InvokeOnMainThreadAsync(() => LoadMessagesAsync(refreshFromServer: false));



    private void OnRealtimeMessage(object? sender, EventArgs e) =>

        _ = MainThread.InvokeOnMainThreadAsync(() => LoadMessagesAsync(refreshFromServer: true));



    private async Task LoadMessagesAsync(bool refreshFromServer)

    {

        try

        {

            var records = await _chatService

                .GetMessagesAsync(_thread.ConversationId, refreshFromServer)

                .ConfigureAwait(false);



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



                _statusLabel.Text = $"{records.Count} message(s) in this thread.";

            });

        }

        catch (Exception ex)

        {

            await MainThread.InvokeOnMainThreadAsync(() => _statusLabel.Text = $"Error: {ex.Message}");

        }

    }



    private async Task RunBusyAsync(Func<Task> action)

    {

        _sendButton.IsEnabled = false;

        try

        {

            await action().ConfigureAwait(false);

        }

        catch (Exception ex)

        {

            await MainThread.InvokeOnMainThreadAsync(() => _statusLabel.Text = $"Error: {ex.Message}");

        }

        finally

        {

            _sendButton.IsEnabled = _authenticationService.IsAuthenticated;

        }

    }

}


