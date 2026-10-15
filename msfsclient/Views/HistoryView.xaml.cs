using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using msfsclient.Core.History;
using msfsclient.Core.Ui;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    public sealed partial class HistoryView : ShellPart
    {
        private bool _running;
        private long _lastReload;

        public HistoryView()
        {
            this.InitializeComponent();
            Loaded += (_, _) => Reload();
        }

        public IReadOnlyList<HistoryItem> Sessions { get; private set; } = [];

        public bool IsEmpty => Sessions.Count == 0;

        public bool HasFinished => Sessions.Any(s => s.Finished);

        /// <summary>The list is read again when tracking starts or stops, and every few seconds while a session is recording.</summary>
        protected override void Refresh()
        {
            var running = Vm.Tracking.Running;
            if (running != _running || (running && Environment.TickCount64 - _lastReload > 5_000)) Reload();
        }

        private void Reload()
        {
            _running = Vm.Tracking.Running;
            _lastReload = Environment.TickCount64;
            Sessions = Vm.TrackLog.Sessions().Select(s => new HistoryItem(s)).ToList();
            Notify();
        }

        private async void Gpx_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not HistoryItem item) return;
            var s = item.Session;
            try
            {
                var gpx = Gpx.Build($"Airsports {Gpx.Time(s.StartedAtMillis)}", Vm.TrackLog.Points(s.Id));
                if (await Vm.Platform.SaveGpxAsync(Gpx.FileName(s.StartedAtMillis), gpx))
                {
                    Notice.Text = "GPX file saved.";
                    Notice.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                _ = Dialogs.InfoAsync(XamlRoot, "Not saved", $"The GPX file could not be saved: {ex.Message}");
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not HistoryItem item) return;
            var s = item.Session;
            if (await Dialogs.ConfirmAsync(XamlRoot, "Delete this track?", $"{TimeFormat.FormatLocal(Gpx.Time(s.StartedAtMillis))}, {s.PointCount} positions. This cannot be undone.",
                    "Delete", "Keep", danger: true))
            {
                // async void: a database error here would otherwise end the app.
                try { Vm.TrackLog.Delete(s.Id); }
                catch (Exception ex) { _ = Dialogs.InfoAsync(XamlRoot, "Not deleted", $"The track could not be deleted: {ex.Message}"); }
                Reload();
            }
        }

        private async void DeleteAll_Click(object sender, RoutedEventArgs e)
        {
            if (await Dialogs.ConfirmAsync(XamlRoot, "Delete the whole history?",
                    "All recorded tracks on this PC are deleted. This cannot be undone. A track being recorded now is kept.", "Delete all", "Keep", danger: true))
            {
                try { Vm.TrackLog.DeleteAllFinished(); }
                catch (Exception ex) { _ = Dialogs.InfoAsync(XamlRoot, "Not deleted", $"The history could not be deleted: {ex.Message}"); }
                Reload();
            }
        }
    }
}
