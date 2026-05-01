using Microsoft.UI.Xaml;
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
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
