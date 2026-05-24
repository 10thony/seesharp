using TestMAUIApp.Models;

namespace TestMAUIApp.Services;

public interface INavigationService
{
    NavigationPage RootNavigationPage { get; }

    NavigationPage DetailNavigationPage { get; }

    NavigationPage InitializeRoot(Page loginPage);

    Task NavigateToMainShellAsync(CancellationToken cancellationToken = default);

    Task NavigateToChatAsync(ChatThreadSummary thread, CancellationToken cancellationToken = default);

    Task NavigateToLoginAsync(CancellationToken cancellationToken = default);

    FlyoutPage? CurrentFlyout { get; }
}
