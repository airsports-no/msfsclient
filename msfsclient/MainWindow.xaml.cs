using Microsoft.UI.Xaml;
using msfsclient.Views;

namespace msfsclient
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Set window size
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(450, 650));
            
            // Center the window (simplified for WinUI 3)
            var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
            var centerX = (displayArea.WorkArea.Width - 450) / 2;
            var centerY = (displayArea.WorkArea.Height - 650) / 2;
            this.AppWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));

            RootFrame.Navigate(typeof(LoginPage));
        }
    }
}
