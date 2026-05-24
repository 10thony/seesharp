using TestMAUIApp.Ui;

namespace TestMAUIApp.Pages;

public class MainFlyoutShell : FlyoutPage
{
    public MainFlyoutShell(ThreadFlyoutPage flyoutPage)
    {
        Flyout = new NavigationPage(flyoutPage)
        {
            BarBackgroundColor = AppPalette.NavBarBackground,
            BarTextColor = AppPalette.NavBarText,
        };
        FlyoutLayoutBehavior = FlyoutLayoutBehavior.Default;
        IsGestureEnabled = true;
        IsPresented = false;
    }

    public void Configure(NavigationPage detailNavigationPage)
    {
        Detail = detailNavigationPage;
    }
}
