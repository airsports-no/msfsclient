using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Tracking;
using msfsclient.Core.Ui;
using msfsclient.Helpers;

namespace msfsclient.Views
{
    /// <summary>The flight dialogs shared by Home and My flights, worded as in the mobile app.</summary>
    public static class FlightDialogs
    {
        /// <summary>Asks before a flight is cancelled; a flight that has already started is ended instead of removed.</summary>
        public static Task<bool> ConfirmCancelAsync(XamlRoot root, MyFlight f, long nowMillis)
        {
            var underway = f.IsUnderway(nowMillis);
            var name = string.Join(" - ", new[] { f.ContestName, f.TaskName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var text = $"{name}\nTake-off {TimeFormat.FormatLocal(f.TakeoffTime)}\n\n" + (underway
                ? "This flight has started. Cancelling ends it now: its finish time is set to the current time and its scoring stops, so you can go on to your next task."
                : "Your registration for this flight is removed. You can register again if the task is still open.");
            return Dialogs.ConfirmAsync(root, underway ? "Cancel the flight in progress?" : "Cancel this flight?", text, "Cancel flight",
                underway ? "Keep flying" : "Keep flight", danger: true);
        }

        /// <summary>
        /// Shown after tracking stops for a flight that was matched to a navigation task: offers the flight on the live map when
        /// the pilot may see it, otherwise says that the results have to be awaited.
        /// </summary>
        public static async Task ShowFinishedAsync(XamlRoot root, FinishedFlight flight, AppController controller)
        {
            var canView = flight.Viewable != false && flight.MapPath != null;
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = string.Join(" - ", new[] { flight.ContestName, flight.TaskName }.Where(s => !string.IsNullOrWhiteSpace(s))),
                Style = (Style)Application.Current.Resources["StrongText"],
            });
            if (flight.Score is { } score)
            {
                panel.Children.Add(new TextBlock { Text = $"Score so far: {TrackingTargets.ScoreText(score)} points", Style = (Style)Application.Current.Resources["CondensedTitle"] });
                panel.Children.Add(new TextBlock { Text = "The final result can change when the flight has been fully scored.", Style = (Style)Application.Current.Resources["SmallText"] });
            }
            panel.Children.Add(new TextBlock
            {
                Style = (Style)Application.Current.Resources["BodyText"],
                Text = canView
                    ? "Your positions were received and matched to this task. You can look at your flight on the live tracking map."
                    : "Your positions were received and matched to this task. You do not have permission to view the live tracking map for it yet, so you will have to wait until the results are published to review your flight.",
            });
            var view = await Dialogs.ConfirmAsync(root, "Flight recorded", panel, canView ? "View your flight" : "OK", canView ? "Close" : null);
            // The offer is shown once.
            controller.DismissFinishedFlight();
            if (canView && view && flight.MapPath is { } path) controller.OpenMap(path);
        }
    }
}
