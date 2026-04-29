using Microsoft.UI.Xaml;
using msfsclient.Views;
using System;
using msfsclient.Contracts.Services;

namespace msfsclient
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _simConnectTimer;
        private readonly ISimConnectService _simConnectService;

        public IntPtr WindowHandle { get; private set; }

        public MainWindow()
        {
            this.InitializeComponent();

            WindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            _simConnectService = App.GetService<ISimConnectService>();

            // Set window size
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(450, 650));
            
            // Center the window
            var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
            var centerX = (displayArea.WorkArea.Width - 450) / 2;
            var centerY = (displayArea.WorkArea.Height - 650) / 2;
            this.AppWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));

            // SimConnect Polling Timer (100ms)
            _simConnectTimer = new DispatcherTimer();
            _simConnectTimer.Interval = TimeSpan.FromMilliseconds(100);
            _simConnectTimer.Tick += SimConnectTimer_Tick;
            _simConnectTimer.Start();

            RootFrame.Navigate(typeof(LoginPage));
        }

        private void SimConnectTimer_Tick(object? sender, object e)
        {
            _simConnectService.ReceiveMessage();
        }
    }
}
