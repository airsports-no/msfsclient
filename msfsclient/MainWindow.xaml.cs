using Microsoft.UI.Xaml;
using msfsclient.Views;
using System;

namespace msfsclient
{
    public sealed partial class MainWindow : Window
    {
        public IntPtr WindowHandle { get; private set; }

        public MainWindow()
        {
            this.InitializeComponent();

            WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // Set window size
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(450, 650));
            
            // Center the window
            var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
            var centerX = (displayArea.WorkArea.Width - 450) / 2;
            var centerY = (displayArea.WorkArea.Height - 650) / 2;
            this.AppWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));

            RootFrame.Navigate(typeof(LoginPage));
        }
    }
}
