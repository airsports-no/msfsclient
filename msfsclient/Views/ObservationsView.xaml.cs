using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.Core.Api;
using msfsclient.Core.Observations;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    public sealed partial class ObservationsView : ShellPart
    {
        private string _key = "";

        public ObservationsView()
        {
            this.InitializeComponent();
        }

        public IReadOnlyList<PhotoItem> Photos { get; private set; } = [];

        public string RuleText => ObservationRules.RuleText(Vm.State.Observations);

        public string BlockerText => Vm.ObservationBlocker ?? "Position OK: marks are stamped with your position and time.";

        public string EmptyText
        {
            get
            {
                var ui = Vm.State.Observations;
                if (ui.Loading && ui.Photos.Count == 0) return "Loading…";
                if (ui.Photos.Count == 0) return ui.Error != null ? "The photos could not be loaded." : "There are no photos to mark in this flight.";
                return "";
            }
        }

        public bool ShowRetry => Vm.State.Observations is { Photos.Count: 0, Error: not null, Loading: false };

        protected override void Refresh()
        {
            var ui = Vm.State.Observations;
            ThumbsTab.IsChecked = ui.Thumbnails;
            ListTab.IsChecked = !ui.Thumbnails;
            var canMark = Vm.ObservationBlocker == null;
            // Rebuilt only when what the rows show changed (not on every 1 s refresh).
            var key = $"{ui.Thumbnails}|{canMark}|{string.Join(",", ui.Photos.Select(p => p.Id))}|{string.Join(",", ui.Registered.Select(r => $"{r.Key}:{r.Value.Time}"))}|{string.Join(",", ui.Pending)}";
            if (key == _key) return;
            _key = key;
            PhotoList.ItemTemplate = (DataTemplate)Resources[ui.Thumbnails ? "TileTemplate" : "RowTemplate"];
            // Thumbnails two to a row (more on a wide window), stretched to the width; the list one under the other.
            PhotoList.Layout = ui.Thumbnails
                ? new UniformGridLayout { MinItemWidth = 200, MinColumnSpacing = 12, MinRowSpacing = 12, ItemsStretch = UniformGridLayoutItemsStretch.Fill }
                : new StackLayout { Spacing = 8 };
            Photos = ui.Photos.Select(p =>
            {
                var (text, done) = ObservationRules.MarkState(p, ui);
                return new PhotoItem(p, text, done, ui.Pending.Contains(p.Id), canMark);
            }).ToList();
        }

        private static ObservationPhoto? Photo(object sender) => ((sender as FrameworkElement)?.Tag as PhotoItem)?.Photo;

        private void Thumbs_Click(object sender, RoutedEventArgs e) { Vm.Controller.ShowObservationThumbnails(true); Refresh(); }

        private void List_Click(object sender, RoutedEventArgs e) { Vm.Controller.ShowObservationThumbnails(false); Refresh(); }

        private void Retry_Click(object sender, RoutedEventArgs e) => _ = Vm.Controller.LoadObservationsAsync();

        private void Mark_Click(object sender, RoutedEventArgs e)
        {
            if (Photo(sender) is { } photo) Vm.MarkObservation(photo);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (Photo(sender) is { } photo) _ = Vm.Controller.CancelObservationAsync(photo);
        }

        /// <summary>The photo, large, with Mark (and Close).</summary>
        private async void Open_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not PhotoItem item) return;
            var content = new Image { Source = Ui.Image(item.File), Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, MaxHeight = 480 };
            var mark = await Dialogs.ConfirmAsync(XamlRoot, item.Name, content, item.MarkText, "Close");
            if (mark && Vm.ObservationBlocker == null) Vm.MarkObservation(item.Photo);
        }

        /// <summary>Lets the crew type the time of day they saw the photo, instead of marking it now.</summary>
        private async void SetTime_Click(object sender, RoutedEventArgs e)
        {
            if (Photo(sender) is not { } photo) return;
            var now = Vm.NowMillis;
            var reference = IsoTime.EpochMillisOrNull(Vm.State.Observations.Registered.GetValueOrDefault(photo.Id)?.Time) ?? now;
            var box = new TextBox { Header = "Time (hh:mm:ss)", Text = ObservationRules.TimeOfDayText(reference), Style = (Style)Application.Current.Resources["FormBox"] };
            var problem = new TextBlock { Style = (Style)Application.Current.Resources["ErrorText"] };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Style = (Style)Application.Current.Resources["BodyText"],
                Text = "Enter the time you saw the photo, as hh:mm:ss in your local time. Your position is taken from your position reports at that time, and replaces an earlier mark.",
            });
            panel.Children.Add(box);
            panel.Children.Add(problem);
            long? Parsed() => ObservationRules.ParseTimeOfDay(box.Text, reference);
            void Check()
            {
                var millis = Parsed();
                problem.Text = millis == null ? "Not a valid time." : millis > now + 120_000 ? "That time has not happened yet." : "";
            }
            box.TextChanged += (_, _) => Check();
            Check();
            if (!await Dialogs.ConfirmAsync(XamlRoot, $"{photo.Name}: time", panel, "Set", "Cancel")) return;
            if (Parsed() is { } time && time <= now + 120_000) _ = Vm.Controller.MarkObservationAtTimeAsync(photo, time);
        }
    }
}
