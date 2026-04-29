using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using msfsclient.ViewModels;

namespace msfsclient.Views
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; }

        public MainPage()
        {
            ViewModel = App.GetService<MainViewModel>();
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.InitializeAsync();
            
            if (ViewModel.Profile == null)
            {
                // Optionally show a message if profile fails to load
            }
        }
    }
}
