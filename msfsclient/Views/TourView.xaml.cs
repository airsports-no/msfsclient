using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Shapes;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    public sealed partial class TourView : ShellPart
    {
        /// <summary>What the tour says, in order: the mobile app's pages, worded for the simulator. Plain text on purpose.</summary>
        private static readonly (string Title, string Body)[] Pages =
        [
            ("Your flight at a glance",
                "Home shows the flight you are tracking now, or your next one: contest and task, tracking window, take-off, airspeed and wind. " +
                "The same account and flights as on the website."),
            ("Start tracking",
                "Load your flight in MSFS, then press play in the bar at the bottom before your tracking window opens. The client sends the " +
                "aircraft's position every second and keeps it if the connection drops. Stop it yourself when you have landed."),
            ("Check before you fly",
                "Open Pre-flight on Home for one list of what can go wrong: the simulator connection, internet, and the PC clock that your " +
                "positions are stamped with. Each problem says how to fix it."),
            ("Tools while you fly",
                "The switch at the bottom moves between Flight, Timer and Photos. Timer is for adaptive-start tasks and Photos for tasks with " +
                "photo observations. Training mode shows how early or late you were at each gate."),
            ("After the flight",
                "Every tracking session is saved on this PC. Open Track history to download it as a GPX file or delete it. " +
                "Your profile is under your picture at the top."),
        ];

        private int _index;

        public TourView()
        {
            this.InitializeComponent();
            Show();
        }

        private void Show()
        {
            var last = _index == Pages.Length - 1;
            var (title, body) = Pages[_index];
            Counter.Text = $"{_index + 1} of {Pages.Length}";
            Title.Text = title;
            Body.Text = body;
            Skip.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
            BackButton.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
            Next.Content = last ? "Get started" : "Next";
            Dots.Children.Clear();
            for (var i = 0; i < Pages.Length; i++)
            {
                Dots.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Ui.Brush(i == _index ? "IndigoBrush" : "OutlineBrush") });
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_index == Pages.Length - 1) Vm.Controller.FinishTour();
            else { _index++; Show(); }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_index > 0) { _index--; Show(); }
        }

        private void Done_Click(object sender, RoutedEventArgs e) => Vm.Controller.FinishTour();
    }
}
