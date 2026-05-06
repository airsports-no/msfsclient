using Microsoft.UI.Xaml;
using msfsclient.Contracts.Services;
using System;

namespace msfsclient.Views
{
    public sealed partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            this.InitializeComponent();
            
            // Set window size for the about dialog
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 700));

            var appConfig = App.GetService<IAppConfigService>();
            VersionText.Text = $"Client Version: {appConfig.AppVersion}";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
