using System.Collections.Generic;
using System.Linq;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.History;
using msfsclient.Core.Observations;
using msfsclient.Core.Preflight;
using msfsclient.Core.Tracking;
using msfsclient.Core.Ui;

namespace msfsclient.Views
{
    // Rows of the lists on the screens, with everything ready to display (x:Bind in item templates binds to these).

    /// <summary>A message from the organizers, with a way to dismiss it.</summary>
    public sealed record AnnouncementItem(int Id, string Source, string Message)
    {
        public static AnnouncementItem From(Announcement a)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(a.ContestName)) parts.Add(a.ContestName);
            if (!string.IsNullOrWhiteSpace(a.TaskName)) parts.Add(a.TaskName);
            return new AnnouncementItem(a.Id, parts.Count > 0 ? string.Join(" - ", parts) : "From the organizers", a.Message);
        }
    }

    /// <summary>One step of the practice flight: a number (or a tick when done, dots while it is under way) and what happens.</summary>
    public sealed record QuickstartStep(string Marker, string Text, Microsoft.UI.Xaml.Media.Brush Accent);

    /// <summary>
    /// An air-traffic-control style flight progress strip: a colour tab at the left (green while the window is open), the
    /// contest and task in condensed type, the facts in boxed cells, then the links.
    /// </summary>
    public sealed record StripItem(TrackingTarget Target, bool Live, MyFlight? Cancellable, bool Cancelling, bool Past = false)
    {
        /// <summary>The map button: the live map, or the results of a flight in the past.</summary>
        public string MapText => Past ? "Results" : "Live map";
        public string PrivateNote => Past ? "Results are not available to you: this task is private to the organizer." : "The live map of this task is private to the organizer.";
        /// <summary>Only a flight that can still be followed live is worth sharing.</summary>
        public bool CanShare => !Past && HasMap;
        public string ContestName => Target.ContestName ?? "";
        public bool HasContest => !string.IsNullOrWhiteSpace(Target.ContestName);
        public string TaskName => Target.TaskName;
        public string TaskType => Target.TaskType ?? "";
        public bool HasTaskType => !string.IsNullOrWhiteSpace(Target.TaskType);
        public IReadOnlyList<Fact> Facts => Target.Facts;
        public string? LogoUrl => Target.LogoUrl;
        public bool HasLogo => Target.LogoUrl != null;
        public bool MapPrivate => Target.MapPrivate;
        public string NotActiveNote => Target.NotActiveNote ?? "";
        public bool NotActive => Target.NotActiveNote != null;
        public bool HasMap => Target.MapLink != null;
        public bool HasFlightOrders => Target.FlightOrderPath != null;
        public bool HasDirections => Target.DirectionsUrl != null;
        public bool HasWebsite => Target.WebsiteUrl != null;
        public bool HasContestPage => Target.ContestPagePath != null;
        public bool CanCancel => Cancellable != null;
        public string CancelText => Cancelling ? "Cancelling…" : "Cancel flight";
        public bool CancelEnabled => !Cancelling;
        public bool NotLive => !Live;
    }

    /// <summary>The flights of one day in My flights, under their heading ("Today", "Sat 24 Oct").</summary>
    public sealed record DayGroup(string Heading, IReadOnlyList<StripItem> Flights);

    /// <summary>A contest with tasks open for registration today.</summary>
    public sealed record VenueItem(OpenEvent Venue, long NowMillis)
    {
        public string ContestName => Venue.ContestName;
        public string Place => Venue.Place;
        public bool HasPlace => Venue.Place.Length > 0;
        public bool HasDirections => Venue.DirectionsUrl != null;
        public IReadOnlyList<OpenTaskItem> Tasks { get; } = Venue.Tasks.Select(t => new OpenTaskItem(t, TimeFormat.ZoneOrDevice(Venue.TimeZone), NowMillis)).ToList();
    }

    /// <summary>One task open for registration: its name, the window and the button that opens the website's form.</summary>
    public sealed record OpenTaskItem(OpenRegistrationTask Task, System.TimeZoneInfo Zone, long NowMillis)
    {
        public string Name => Task.NavigationTaskName;
        public string Window => $"Flights can be registered between {TimeFormat.RegistrationWindow(Task.StartTime, Task.FinishTime, NowMillis, Zone)}";
        public bool OpenNow => Task.IsOpenNow;
    }

    public sealed record CheckItem(PreflightCheck Check)
    {
        public string Title => Check.Title;
        public string Detail => Check.Detail;
        public bool HasAction => Check.Action != null;
        public string ActionText => Check.Action switch
        {
            CheckAction.OpenClockSettings => "Open date and time settings",
            CheckAction.ConnectSimulator => "Connect again",
            _ => "",
        };
        public string DotBrushKey => Check.Status switch
        {
            CheckStatus.Ok => "LiveGreenBrush",
            CheckStatus.Warning => "AmberBrush",
            CheckStatus.Problem => "ErrorBrush",
            _ => "StrongOutlineBrush",
        };
        public string StatusName => Check.Status switch
        {
            CheckStatus.Ok => "OK",
            CheckStatus.Warning => "Look at this",
            CheckStatus.Problem => "Fix this",
            _ => "Not checked",
        };
    }

    public sealed record HistoryItem(TrackSessionSummary Session)
    {
        public string Started => TimeFormat.FormatLocal(Gpx.Time(Session.StartedAtMillis));
        public string Summary => Session.Recording
            ? $"Recording now, {Session.PointCount} positions"
            : $"{TimeFormat.FormatDuration(Session.DurationMillis)}, {Session.PointCount} positions";
        public bool Finished => !Session.Recording;
        public bool HasPoints => Session.PointCount > 0;
    }

    /// <summary>
    /// A photo on the Photos screen. One item per photo for as long as the screen shows the same photos; a change (marked,
    /// sent, marking closed) updates it in place. Replacing the items made the list reuse its card elements, and some cards
    /// kept their old state (a mark not shown, or a Mark button still there after marking closed).
    /// </summary>
    public sealed class PhotoItem(ObservationPhoto photo) : System.ComponentModel.INotifyPropertyChanged
    {
        public ObservationPhoto Photo { get; } = photo;
        public string Name => Photo.Name;
        public string? File => Photo.File;
        public string State { get; private set; } = "";
        public bool Done { get; private set; }
        public bool Pending { get; private set; }
        public bool CanMark { get; private set; }
        public bool Marked => Done || Pending;
        public string MarkText => Marked ? "Mark again" : "Mark";

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public void Set(string state, bool done, bool pending, bool canMark)
        {
            if (state == State && done == Done && pending == Pending && canMark == CanMark) return;
            (State, Done, Pending, CanMark) = (state, done, pending, canMark);
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
        }
    }

    public sealed record DebugRow(string Label, string Value);
}
