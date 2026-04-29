using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using msfsclient.Contracts.Services;
using msfsclient.Services;
using msfsclient.ViewModels;
using System;

namespace msfsclient
{
    public partial class App : Application
    {
        private Window? _window;

        public IServiceProvider Services { get; }

        public App()
        {
            InitializeComponent();
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Services
            services.AddSingleton<IAuthService, AuthService>();
            services.AddSingleton<IProfileService, ProfileService>();

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
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
