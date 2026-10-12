using System;
using System.IO;
using System.Net.Http;
using Microsoft.UI.Xaml;
using msfsclient.Contracts.Services;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Auth;
using msfsclient.Core.Data;
using msfsclient.Core.History;
using msfsclient.Core.Observations;
using msfsclient.Core.Tracking;
using msfsclient.Services;
using msfsclient.ViewModels;

namespace msfsclient
{
    public partial class App : Application
    {
        private AirsportsDatabase? _database;

        public MainWindow? Window { get; private set; }

        /// <summary>The window's view model; set before the window is created.</summary>
        public static ShellViewModel Shell { get; private set; } = null!;

        public App()
        {
            this.UnhandledException += App_UnhandledException;
            InitializeComponent();
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"UNHANDLED EXCEPTION: {e.Message}");
            System.Diagnostics.Debug.WriteLine($"Stack Trace: {e.Exception?.StackTrace}");
            // A local record as well, for a pilot reporting a problem without crash reporting configured.
            try
            {
                File.AppendAllText(Path.Combine(new AppConfigService().LocalFolder, "crash.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n\n");
            }
            catch (Exception)
            {
            }
            if (e.Exception != null)
            {
                Sentry.SentrySdk.CaptureException(e.Exception);
                Sentry.SentrySdk.Flush(TimeSpan.FromSeconds(2));
            }
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var config = new AppConfigService();
            var preview = PreviewMode.Requested();
            if (preview == null) StartCrashReporting(config);

            // One HttpClient for the API and sign-in, a separate one for Traccar: the Firebase token never goes to Traccar.
            var http = preview != null ? new HttpClient(new PreviewMode.Server()) : new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var traccarHttp = preview != null ? new HttpClient(new PreviewMode.Server()) : new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            _database = new AirsportsDatabase(Path.Combine(config.LocalFolder, preview != null ? "preview.db" : "airsports.db"));
            var settings = new AppSettings(_database);
            if (preview != null) settings.TourSeen = preview != "tour";
            var queue = new PositionQueue(_database);
            var trackLog = new TrackLog(_database);
            var observationQueue = new ObservationQueue(_database);

            IAuthService auth = preview != null ? new PreviewMode.Auth() : new AuthService(http, legacyUserFile: Path.Combine(config.LocalFolder, "user.json"));
            var device = new DeviceInfo(DeviceInfo.MsfsPlatform, config.BuildNumber, DeviceInfo.SharedVersionName);
            var api = new AirsportsApi(http, auth.Tokens, config.ApiBaseUrl, device);

            ISimConnectService simConnect = preview != null ? new PreviewMode.Simulator() : new SimConnectService();
            var sim = new SimPositionSource(simConnect);
            var controller = new AppController(api, auth, device, new NtpClient(), observationQueue, sim, settings);
            var uploader = new TraccarUploader(traccarHttp, queue, config.TraccarBaseUrl);

            MainWindow? window = null;
            var platform = new PlatformServices(() => window?.Handle ?? IntPtr.Zero);
            Shell = new ShellViewModel(controller, simConnect, sim, queue, uploader, trackLog, platform, config);

            window = new MainWindow();
            Window = window;
            window.Closed += (_, _) =>
            {
                Shell.Stop();
                _database?.Dispose();
                Sentry.SentrySdk.Close();
            };
            window.Activate();
            Shell.Start(window.Handle);
            if (preview != null) _ = OpenPreviewAsync(preview);
        }

        /// <summary>Preview mode: start tracking and open the screen asked for once the sample data has loaded.</summary>
        private static async System.Threading.Tasks.Task OpenPreviewAsync(string screen)
        {
            while (!Shell.State.HomeLoaded && Shell.State.Screen != Screen.Tour) await System.Threading.Tasks.Task.Delay(100);
            if (screen is "home" or "timer" or "photos" or "debug" or "training") Shell.StartTracking();
            await System.Threading.Tasks.Task.Delay(1500);
            var c = Shell.Controller;
            switch (screen)
            {
                case "flights": await c.OpenFlightsAsync(); break;
                case "timer": await c.OpenTimerAsync(); break;
                case "photos": await c.OpenObservationsAsync(); break;
                case "preflight": await c.OpenPreflightAsync(); break;
                case "history": c.OpenHistory(); break;
                case "profile": c.OpenProfile(); break;
                case "debug": c.OpenDebug(); break;
                case "training": c.SetTrainingMode(true); break;
            }
        }

        /// <summary>Crash reports go to Sentry in release builds, and only when a DSN is configured (AIRSPORTS_SENTRY_DSN).</summary>
        private static void StartCrashReporting(IAppConfigService config)
        {
#if !DEBUG
            if (config.CrashReportingDsn is { } dsn)
            {
                Sentry.SentrySdk.Init(o =>
                {
                    o.Dsn = dsn;
                    o.Release = $"msfsclient@{config.AppVersion}";
                    o.IsGlobalModeEnabled = true;
                    o.SendDefaultPii = false;
                });
            }
#endif
        }
    }
}
