using TestMAUIApp.Pages;
using TestMAUIApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TestMAUIApp
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;

        public App(IServiceProvider services)
        {
            _services = services;
            InitializeComponent();
            UserAppTheme = AppTheme.Light;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var navigationService = _services.GetRequiredService<INavigationService>();
            var loginPage = _services.GetRequiredService<LoginPage>();
            var root = navigationService.InitializeRoot(loginPage);
            return new Window(root);
        }
    }
}
