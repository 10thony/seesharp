using TestMAUIApp.Models;
using TestMAUIApp.Pages;
using TestMAUIApp.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace TestMAUIApp.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private NavigationPage? _rootNavigationPage;
    private NavigationPage? _detailNavigationPage;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    public NavigationPage RootNavigationPage =>
        _rootNavigationPage ?? throw new InvalidOperationException("Navigation has not been initialized.");

    public NavigationPage DetailNavigationPage =>
        _detailNavigationPage ?? throw new InvalidOperationException("Detail navigation is not available until the main shell is shown.");

    public FlyoutPage? CurrentFlyout { get; private set; }

    public NavigationPage InitializeRoot(Page loginPage)
    {
        _rootNavigationPage = CreateStyledNavigationPage(loginPage);
        _detailNavigationPage = CreateStyledNavigationPage(CreatePlaceholderDetail());
        return _rootNavigationPage;
    }

    public async Task NavigateToMainShellAsync(CancellationToken cancellationToken = default)
    {
        var flyoutShell = _services.GetRequiredService<MainFlyoutShell>();
        flyoutShell.Configure(_detailNavigationPage!);

        CurrentFlyout = flyoutShell;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Application.Current?.Windows.FirstOrDefault() is Window window)
            {
                window.Page = flyoutShell;
            }
        });

        var threadService = _services.GetRequiredService<ThreadService>();
        var threads = await threadService.GetThreadSummariesAsync(cancellationToken)
            .ConfigureAwait(false);

        var initialThread = threads.FirstOrDefault(t => t.ConversationId == Constants.GlobalConversationId)
            ?? threads.FirstOrDefault();

        if (initialThread is not null)
        {
            await NavigateToChatAsync(initialThread, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task NavigateToChatAsync(ChatThreadSummary thread, CancellationToken cancellationToken = default)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (CurrentFlyout is not null)
            {
                CurrentFlyout.IsPresented = false;
            }

            var factory = _services.GetRequiredService<ChatPageFactory>();
            var chatPage = factory.Create(thread);
            _detailNavigationPage = CreateStyledNavigationPage(chatPage);

            if (CurrentFlyout is not null)
            {
                CurrentFlyout.Detail = _detailNavigationPage;
            }
        });
    }

    public async Task NavigateToLoginAsync(CancellationToken cancellationToken = default)
    {
        var loginPage = _services.GetRequiredService<LoginPage>();

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            CurrentFlyout = null;
            _rootNavigationPage = CreateStyledNavigationPage(loginPage);
            _detailNavigationPage = CreateStyledNavigationPage(CreatePlaceholderDetail());

            if (Application.Current?.Windows.FirstOrDefault() is Window window)
            {
                window.Page = _rootNavigationPage;
            }
        });
    }

    private static NavigationPage CreateStyledNavigationPage(Page rootPage) =>
        new(rootPage)
        {
            BarBackgroundColor = AppPalette.NavBarBackground,
            BarTextColor = AppPalette.NavBarText,
        };

    private static ContentPage CreatePlaceholderDetail() =>
        new()
        {
            Title = "Chat",
            BackgroundColor = AppPalette.PageBackground,
            Content = new Label
            {
                Text = "Select a conversation from the menu.",
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(24),
                TextColor = AppPalette.CaptionText,
                BackgroundColor = Colors.Transparent,
            },
        };
}
