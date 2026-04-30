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
            InitializeComponent();
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // HTTP Client
            services.AddSingleton<HttpClient>();

            // Services
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
