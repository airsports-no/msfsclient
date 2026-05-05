using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using msfsclient.Contracts.Services;
using msfsclient.Services;
using msfsclient.ViewModels;
using System;
using System.Net.Http;

namespace msfsclient
{
    public partial class App : Application
    {
        public Window? Window { get; private set; }

        public IServiceProvider Services { get; }

        public App()
        {
            this.UnhandledException += App_UnhandledException;
            InitializeComponent();
            Services = ConfigureServices();
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Log the exception
            System.Diagnostics.Debug.WriteLine($"UNHANDLED EXCEPTION: {e.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack Trace: {e.Exception?.StackTrace}");
            
            // In some cases we might want to mark it as handled to prevent crash
            // but for WinUI 3, many exceptions are unrecoverable.
            // e.Handled = true; 
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // HTTP Client
            services.AddSingleton<HttpClient>();

            // Services
            services.AddSingleton<IAppConfigService, AppConfigService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IProfileService, ProfileService>();
            services.AddSingleton<ISimConnectService, SimConnectService>();
            services.AddSingleton<ITraccarService, TraccarService>();

            // ViewModels
            services.AddTransient<LoginViewModel>();
            services.AddTransient<RegistrationViewModel>();
            services.AddTransient<MainViewModel>();

            return services.BuildServiceProvider();
        }

        public static T GetService<T>() where T : class
        {
            if (((App)Current).Services.GetService(typeof(T)) is not T service)
            {
                throw new ArgumentException($"{typeof(T)} not founded in DI");
            }

            return service;
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            Window = new MainWindow();
            Window.Activate();
        }
    }
}
