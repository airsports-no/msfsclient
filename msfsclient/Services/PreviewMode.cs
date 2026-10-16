using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using msfsclient.Contracts.Services;
using msfsclient.Core.Api;
using msfsclient.Core.Auth;
using msfsclient.Models;

namespace msfsclient.Services
{
    /// <summary>
    /// Start the client with <c>--preview [screen]</c> to run it against built-in sample data: signed in, one task being tracked
    /// now, two upcoming flights, a simulator that flies a circle, no network. For screenshots and checking the screens
    /// without an account or MSFS (port of the mobile app's PreviewMode); never shown to pilots.
    /// </summary>
    public static class PreviewMode
    {
        /// <summary>The screen asked for after <c>--preview</c> ("home" when none), or null when not in preview mode.</summary>
        public static string? Requested()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.FindIndex(args, a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
            if (i < 0) return null;
            return i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1].ToLowerInvariant() : "home";
        }

        public sealed class Auth : IAuthService, IAuthTokenProvider
        {
            private bool _signedIn = Requested() != "login"; // "--preview login" starts signed out
            public IAuthTokenProvider Tokens => this;
            public Task<string?> IdTokenAsync(bool forceRefresh) => Task.FromResult<string?>("preview");
            public bool IsSignedIn() => _signedIn;
            public bool IsEmailVerified() => true;
            public string? Email() => "pilot@example.com";
            public Task SignInWithPasswordAsync(string email, string password) { _signedIn = true; return Task.CompletedTask; }
            public Task SignInWithCustomTokenAsync(string customToken) { _signedIn = true; return Task.CompletedTask; }
            public Task CreateAccountAsync(string email, string password) => Task.CompletedTask;
            public Task SendPasswordResetAsync(string email) => Task.CompletedTask;
            public Task ResendVerificationAsync() => Task.CompletedTask;
            public Task<bool> CheckVerifiedAsync() => Task.FromResult(true);
            public void SignOut() => _signedIn = false;
            public string DescribeError(Exception e) => e.Message;
        }

        /// <summary>Answers like the server, with times around now.</summary>
        /// <summary>"--preview quickstart": a pilot without flights (the practice flight is offered); "quickstart-registered": registered for it.</summary>
        public static bool NoFlights => Requested() is "quickstart" or "quickstart-registered";

        public sealed class Server : HttpMessageHandler
        {
            private DateTimeOffset? _registeredAt;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var now = DateTimeOffset.UtcNow;
                string Iso(DateTimeOffset t) => IsoTime.Format(t.ToUnixTimeMilliseconds() / 1000 * 1000);
                var started = Iso(now.AddMinutes(-10));
                var ends = Iso(now.AddHours(1));
                var later = Iso(now.AddHours(26));
                var laterEnd = Iso(now.AddHours(28));
                var received = Iso(now.AddSeconds(-4));
                var path = request.RequestUri?.AbsolutePath ?? "";
                string? json = null;
                var status = HttpStatusCode.OK;
                if (path.Contains("retrieve_profile") || path.Contains("partial_update_profile"))
                    json = """{"email":"pilot@example.com","validated":true,"first_name":"Pia","last_name":"Pilot","biography":"Flying since 2009.","simulator_tracking_id":"preview"}""";
                else if (path.Contains("msfs_quickstart"))
                {
                    // "Go flying" registers at once; the flight order is "made" 8 s later.
                    if (path.Contains("msfs_quickstart_register")) { _registeredAt ??= now; status = HttpStatusCode.Created; }
                    var order = _registeredAt is { } at && now - at > TimeSpan.FromSeconds(8) ? "\"/display/maplink/preview/\"" : "null";
                    var registered = Requested() == "quickstart-registered"
                        ? $$"""{"id":7,"takeoff_time":"{{later}}","tracker_start_time":"{{started}}","finished_by_time":"{{laterEnd}}","adaptive_start":true,"air_speed":70.0,"flight_order_path":"/display/maplink/preview/"}"""
                        : _registeredAt != null
                            ? $$"""{"id":8,"takeoff_time":"{{Iso(now)}}","tracker_start_time":"{{Iso(now)}}","finished_by_time":"{{Iso(now.AddHours(2))}}","adaptive_start":true,"air_speed":70.0,"flight_order_path":{{order}}}"""
                            : "null";
                    json = $$"""
                        {"id":1,"title":"Practice around Kjeller","description":"A short practice route near Oslo to learn how tracking, the start and the gates work. Fly it whenever you like.",
                         "contest_id":20,"contest_name":"MSFS practice","navigation_task_id":200,"navigation_task_name":"Practice","start_location":"Kjeller (ENKJ), runway 30",
                         "start_instructions":"Take off, climb to 1500 ft and fly to the starting point east of the field.",
                         "ground_start":{"latitude":59.9693,"longitude":11.0361,"heading":300.0},"air_start":{"latitude":59.97,"longitude":11.13,"altitude_ft":2500,"heading":90.0},
                         "registration_path":"/schedule-flight?contestId=20&navigationTaskId=200","air_speed":70.0,"aircraft_registration":"MSFS","club_name":"MSFS pilots",
                         "registered_flight":{{registered}}}
                        """;
                }
                else if (NoFlights && (path.Contains("get_current_sim_navigation_task") || path.Contains("my_future_flights")))
                {
                    if (path.Contains("my_future_flights")) json = "[]"; else status = HttpStatusCode.NotFound;
                }
                else if (path.Contains("get_current_sim_navigation_task"))
                    json = $$"""
                        [{"navigation_task":{"pk":100,"name":"Day 1 - Precision","tracking_link":"https://app.airsports.no/competition-map/10/100"},
                          "active_contestants":[{"id":1,"contest_id":10,"tracker_start_time":"{{started}}","takeoff_time":"{{started}}","finished_by_time":"{{ends}}",
                          "last_received_time":"{{received}}","calculator_finished":false,"calculator_running":true,"adaptive_start":true,"observation_registration":true,
                          "air_speed":80,"wind_speed":12,"wind_direction":270,"contestant_number":4,"score":17,"training_available":true,
                          "training_feedback":{"last_gate":"TP3","last_gate_time_offset":-3.4},"flight_order_path":"/display/flightorder/1/",
                          "map_path":"/competition-map/10/100?contestantIds=1"}]}]
                        """;
                else if (path.Contains("observation_photos"))
                    json = $$"""
                        {"enabled":true,"may_register":true,"registration_closes_at":"{{Iso(now.AddMinutes(65))}}","registration_grace_minutes":5,
                         "photos":[{"id":1,"name":"Church","file":"ms-appx:///Assets/Preview/church.jpg"},{"id":2,"name":"Lake","file":"ms-appx:///Assets/Preview/lake.jpg"},{"id":3,"name":"Bridge","file":"ms-appx:///Assets/Preview/bridge.jpg"}],
                         "observations":[{"id":1,"photo":2,"photo_name":"Lake","time":"{{started}}"}]}
                        """;
                else if (path.Contains("photo_observations"))
                    json = $$"""{"id":5,"photo":1,"photo_name":"Church","time":"{{Iso(now)}}"}""";
                else if (path.Contains("my_future_flights"))
                    json = $$"""
                        [{"id":1,"contest_id":10,"navigation_task":100,"takeoff_time":"{{started}}","tracker_start_time":"{{started}}","finished_by_time":"{{ends}}","can_cancel":true},
                         {"id":2,"contest_id":10,"navigation_task":101,"takeoff_time":"{{later}}","tracker_start_time":"{{later}}","finished_by_time":"{{laterEnd}}","can_cancel":true}]
                        """;
                else if (path.Contains("my_previous_flights"))
                    json = """[{"id":3,"contest_id":10,"navigation_task":99,"takeoff_time":"2026-05-01T09:00:00Z","navigation_task_name":"Warm-up","map_viewable":false}]""";
                else if (path.Contains("announcements"))
                    json = """[{"id":1,"contest_name":"Spring Cup","task_name":"Day 1 - Precision","message":"Briefing at 08:30 in the club house."}]""";
                else if (path.EndsWith("/contests/", StringComparison.Ordinal))
                    json = """
                        {"results":[{"id":10,"name":"Spring Cup","logo":"ms-appx:///Assets/Square150x150Logo.scale-200.png","location":"59.9,10.7","country":"NO","time_zone":"Europe/Oslo","contest_website":"https://airsports.no",
                         "navigationtask_set":[{"pk":100,"name":"Day 1 - Precision","allow_self_management":true,"task_subtype_definition":{"display_name":"Precision flying"}},
                         {"pk":101,"name":"Day 2 - Navigation","allow_self_management":true}]}],"next":null}
                        """;
                else if (path.Contains("client_notifications")) json = "[]";
                else if (path.Contains("mobile/config")) status = HttpStatusCode.NotFound;
                // Anything else (the Traccar position upload): accepted.
                var response = new HttpResponseMessage(status) { Content = new StringContent(json ?? "", Encoding.UTF8, "application/json") };
                return Task.FromResult(response);
            }
        }

        /// <summary>A simulator that is always connected and flies a slow circle near Oslo at 100 knots.</summary>
        public sealed class Simulator : ISimConnectService
        {
            private long _last;

            public bool IsConnected { get; private set; }
            public event EventHandler<FlightDataStruct>? FlightDataReceived;
            public event EventHandler<bool>? ConnectionStatusChanged;
            public event EventHandler<string>? ErrorOccurred { add { } remove { } }

            public void Connect(IntPtr windowHandle)
            {
                IsConnected = true;
                ConnectionStatusChanged?.Invoke(this, true);
            }

            public void Disconnect()
            {
                IsConnected = false;
                ConnectionStatusChanged?.Invoke(this, false); // as SimConnectService does
            }

            public bool MoveAircraft(double latitude, double longitude, double altitudeFt, double headingDegrees, bool onGround, uint airspeedKnots)
            {
                System.Diagnostics.Debug.WriteLine($"Preview: aircraft moved to {latitude}, {longitude} ({(onGround ? "ground" : $"{altitudeFt} ft")}, heading {headingDegrees})");
                return IsConnected;
            }

            /// <summary>No simulator to print on: kept for the debug view.</summary>
            public void ShowText(string text, msfsclient.Core.Hud.SimMessageKind kind, float seconds) =>
                System.Diagnostics.Debug.WriteLine($"Simulator text ({kind}, {seconds} s): {text}");

            public void ReceiveMessage()
            {
                var now = Environment.TickCount64;
                if (!IsConnected || now - _last < 1000) return;
                _last = now;
                var angle = now / 1000.0 / 120.0 * 2 * Math.PI;
                FlightDataReceived?.Invoke(this, new FlightDataStruct
                {
                    Latitude = 59.91 + 0.02 * Math.Sin(angle),
                    Longitude = 10.75 + 0.04 * Math.Cos(angle),
                    Altitude = 1500,
                    GroundVelocity = 100,
                    HeadingRadians = (angle + Math.PI) % (2 * Math.PI),
                });
            }
        }
    }
}
