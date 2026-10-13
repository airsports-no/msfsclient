using System.Globalization;
using msfsclient.Core.Data;

namespace msfsclient.Core.App;

/// <summary>Pilot preferences kept on this PC (port of app/AppSettings.kt). Training mode is deliberately not kept: it is off at every start.</summary>
public sealed class AppSettings(AirsportsDatabase db)
{
    public const int MaxTimerBufferMinutes = 30;

    private const string Dismissed = "dismissed_announcements";
    private const string TourSeenKey = "tour_seen";
    private const string CachedProfile = "cached_profile";
    private const string TimerBuffer = "timer_buffer_minutes";

    /// <summary>Whole minutes added in front of the start minute when the timer starts (the timer then starts at -N:00). Default 0.</summary>
    public int TimerBufferMinutes
    {
        get => int.TryParse(Get(TimerBuffer), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, MaxTimerBufferMinutes) : 0;
        set => Put(TimerBuffer, Math.Clamp(value, 0, MaxTimerBufferMinutes).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The signed-in pilot's profile as last loaded, so Home can show at once at the next start. Cleared on sign-out.</summary>
    public string? CachedProfileJson
    {
        get => Get(CachedProfile);
        set { if (value == null) Remove(CachedProfile); else Put(CachedProfile, value); }
    }

    /// <summary>The first-start tour has been shown (or skipped); it is offered again from the profile.</summary>
    public bool TourSeen
    {
        get => Get(TourSeenKey) == "1";
        set => Put(TourSeenKey, value ? "1" : "0");
    }

    /// <summary>Announcements the pilot has dismissed (the newest 200; the server drops old ones itself).</summary>
    public IReadOnlySet<int> DismissedAnnouncementIds
    {
        get => (Get(Dismissed) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? (int?)id : null)
            .Where(id => id != null).Select(id => id!.Value).ToHashSet();
        set => Put(Dismissed, string.Join(",", value.TakeLast(200).Select(i => i.ToString(CultureInfo.InvariantCulture))));
    }

    /// <summary>Practice flights (quickstarts) the pilot said "Not now" to; they are not offered on Home again.</summary>
    public IReadOnlySet<int> DismissedQuickstartIds
    {
        get => (Get(DismissedQuickstarts) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? (int?)id : null)
            .Where(id => id != null).Select(id => id!.Value).ToHashSet();
        set => Put(DismissedQuickstarts, string.Join(",", value.TakeLast(50).Select(i => i.ToString(CultureInfo.InvariantCulture))));
    }

    private const string DismissedQuickstarts = "dismissed_quickstarts";

    /// <summary>Print tracking events, gate results and announcements on the simulator's screen. On unless the pilot turns it off.</summary>
    public bool SimMessages
    {
        get => Get(SimMessagesKey) != "0";
        set => Put(SimMessagesKey, value ? "1" : "0");
    }

    private const string SimMessagesKey = "sim_messages";

    /// <summary>The newest flight notification already seen (userprofile/client_notifications); null before the first check.</summary>
    public int? LastClientNotificationId
    {
        get => int.TryParse(Get(LastClientNotificationKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        set { if (value == null) Remove(LastClientNotificationKey); else Put(LastClientNotificationKey, value.Value.ToString(CultureInfo.InvariantCulture)); }
    }

    private const string LastClientNotificationKey = "client_notification_after";

    private string? Get(string key) => db.Scalar("SELECT value FROM Setting WHERE key = $k", ("$k", key)) as string;

    private void Put(string key, string value) => db.Execute("INSERT OR REPLACE INTO Setting(key, value) VALUES ($k, $v)", ("$k", key), ("$v", value));

    private void Remove(string key) => db.Execute("DELETE FROM Setting WHERE key = $k", ("$k", key));
}
