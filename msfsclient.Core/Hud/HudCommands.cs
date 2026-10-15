using msfsclient.Core.App;

namespace msfsclient.Core.Hud;

/// <summary>A request from an in-sim display: <c>{"action":"mark","photoId":3}</c>.</summary>
public sealed record HudCommand
{
    /// <summary>startTracking, stopTracking, startTimer, resetTimer, mark, setTraining, dismissAnnouncement.</summary>
    public string Action { get; init; } = "";
    public int? PhotoId { get; init; }
    public int? Id { get; init; }
    public bool? On { get; init; }
    /// <summary>Stopping and resetting need it: the display has asked the pilot first, as the client does.</summary>
    public bool Confirm { get; init; }
}

public sealed record HudResult(bool Ok, string? Message = null);

/// <summary>What the server needs from the app: the current snapshot and a way to act on commands.</summary>
public interface IHudHost
{
    HudSnapshot Snapshot();
    Task<HudResult> ExecuteAsync(HudCommand command);
}

/// <summary>
/// Carries out display commands on the app, with the same rules as the client's own buttons: a mark needs a fresh position,
/// stopping and resetting need confirmation, training mode only where the server offers it.
/// </summary>
public sealed class HudHost(AppController controller, Func<FlightStatus> status, Action startTracking, Action stopTracking) : IHudHost
{
    public HudSnapshot Snapshot() => HudSnapshot.Of(status(), controller.AppVersionLabel);

    public async Task<HudResult> ExecuteAsync(HudCommand command)
    {
        var now = status();
        var s = now.State;
        if (s.Person == null || s.IsOutside) return new HudResult(false, "Sign in to the Airsports client first.");
        switch (command.Action)
        {
            case "startTracking":
                if (now.Tracking.Running) return new HudResult(true);
                if (!now.CanStartTracking) return new HudResult(false, "Your profile has no simulator tracking id yet.");
                startTracking();
                return new HudResult(true, "Tracking started.");
            case "stopTracking":
                if (!now.Tracking.Running) return new HudResult(true);
                if (!command.Confirm) return new HudResult(false, "Stopping needs confirmation.");
                stopTracking();
                return new HudResult(true, "Tracking stopped.");
            case "startTimer":
                if (!s.ActiveContestants.Any(c => c.AdaptiveStart)) return new HudResult(false, "The timer is for adaptive-start tasks.");
                if (s.TimerOriginMillis != null) return new HudResult(false, "The timer is already running. Reset it first.");
                controller.StartTimer(now.NowMillis);
                return new HudResult(true);
            case "resetTimer":
                if (!command.Confirm) return new HudResult(false, "Resetting needs confirmation.");
                controller.ResetTimer();
                return new HudResult(true);
            case "mark":
            {
                await controller.EnsureObservationsAsync();
                var photo = controller.State.Observations.Photos.FirstOrDefault(p => p.Id == command.PhotoId);
                if (photo == null) return new HudResult(false, "No such photo in this flight.");
                var fix = HudSnapshot.FreshFix(status());
                var blocker = Observations.ObservationRules.Blocker(controller.State.Observations, now.Tracking.Running, fix != null, now.NowMillis);
                if (blocker != null || fix == null) return new HudResult(false, blocker ?? "No position to stamp the mark with.");
                await controller.MarkObservationAsync(photo, fix);
                return new HudResult(true, $"{photo.Name} marked.");
            }
            case "setTraining":
                if (command.On == true && !Training.GateFeedback.TrainingAvailable(s.Tasks)) return new HudResult(false, "Training mode is not available for this flight.");
                controller.SetTrainingMode(command.On == true);
                return new HudResult(true);
            case "dismissAnnouncement":
                if (command.Id is not { } id) return new HudResult(false, "Which announcement?");
                controller.DismissAnnouncement(id);
                return new HudResult(true);
            default:
                return new HudResult(false, $"Unknown action \"{command.Action}\".");
        }
    }
}
