using System;
using System.Linq;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using msfsclient.Core.Api;

namespace msfsclient.Services
{
    /// <summary>
    /// Windows notifications for organizers' announcements that arrive while the client runs (the desktop has no push).
    /// Clicking one brings the window to the front, where the announcement shows on Home. Works packaged and unpackaged.
    /// </summary>
    public sealed class NotificationService
    {
        private bool _registered;

        /// <summary>Called when the pilot clicks a notification (on a background thread).</summary>
        public event Action? Activated;

        public void Register()
        {
            try
            {
                AppNotificationManager.Default.NotificationInvoked += (_, _) => Activated?.Invoke();
                AppNotificationManager.Default.Register();
                _registered = true;
            }
            catch (Exception ex)
            {
                // Notifications are a convenience: the announcement still shows on Home.
                System.Diagnostics.Debug.WriteLine($"Notifications unavailable: {ex.Message}");
            }
        }

        public void Unregister()
        {
            if (!_registered) return;
            try { AppNotificationManager.Default.Unregister(); }
            catch (Exception) { }
        }

        public void ShowAnnouncement(Announcement a)
        {
            if (!_registered) return;
            try
            {
                var source = string.Join(" - ", new[] { a.ContestName, a.TaskName }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var notification = new AppNotificationBuilder()
                    .AddText(source.Length > 0 ? source : "From the organizers")
                    .AddText(a.Message)
                    .BuildNotification();
                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Notification not shown: {ex.Message}");
            }
        }
    }
}
