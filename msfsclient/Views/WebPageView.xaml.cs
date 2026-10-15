using System;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using msfsclient.Core.Api;
using msfsclient.Core.App;

namespace msfsclient.Views
{
    /// <summary>
    /// The embedded web app (port of WebScreen). Signs in by sending the Firebase token in the Authorization header of the
    /// first request to <c>https://app.airsports.no/firebase_login/?next=&lt;path&gt;</c> (never in the URL), so the session
    /// cookie lands where the React app runs. <c>airsportsapp://</c> navigations are messages from the page; links to other
    /// sites open in the browser.
    /// </summary>
    public sealed partial class WebPageView : ShellPart
    {
        private readonly string _path;

        public WebPageView()
        {
            this.InitializeComponent();
            _path = Vm.State.MapPath;
            Loaded += async (_, _) => await OpenAsync();
        }

        private async System.Threading.Tasks.Task OpenAsync()
        {
            try
            {
                await Web.EnsureCoreWebView2Async();
                var core = Web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.NavigationStarting += OnNavigationStarting;
                core.NewWindowRequested += OnNewWindowRequested;
                core.NavigationCompleted += (_, _) => Spinner.Visibility = Visibility.Collapsed;

                var token = await Vm.Controller.IdTokenForWebAsync();
                if (token == null)
                {
                    ShowProblem("Sign in again to open this page.");
                    return;
                }
                var request = core.Environment.CreateWebResourceRequest(Urls.FirebaseLoginUrl(_path), "GET", null, $"Authorization: JWT {token}");
                core.NavigateWithWebResourceRequest(request);
            }
            catch (Exception ex)
            {
                // The WebView2 runtime is part of Windows 11; on an older Windows it may be missing.
                ShowProblem($"The page could not be opened: {ex.Message}");
            }
        }

        private void ShowProblem(string text)
        {
            Spinner.Visibility = Visibility.Collapsed;
            Problem.Text = text;
            Problem.Visibility = Visibility.Visible;
        }

        private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            var uri = args.Uri;
            // A page telling the app something (e.g. a flight was registered); never loaded.
            if (AppEvent.IsAppEventUrl(uri))
            {
                args.Cancel = true;
                if (AppEvent.Parse(uri) is { } appEvent) _ = Vm.Controller.OnAppEventAsync(appEvent);
                return;
            }
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return;
            if (parsed.Scheme == "about") return;
            var host = parsed.Host.ToLowerInvariant();
            var site = new Uri(Urls.SiteBase);
            var ours = host == Urls.AppHost || host == "airsports.no" || host.EndsWith(".airsports.no", StringComparison.Ordinal);
            // The signed-in site stays in the app only over https (or exactly as configured, e.g. a local test server).
            if ((host == site.Host.ToLowerInvariant() && parsed.Scheme == site.Scheme) || (ours && parsed.Scheme == Uri.UriSchemeHttps)) return;
            args.Cancel = true;
            if (ours && parsed.Scheme == Uri.UriSchemeHttp)
            {
                // A plain-http link to our site: load the https page instead, never content that could be altered on the way.
                sender.Navigate(new UriBuilder(parsed) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri.ToString());
                return;
            }
            // Anywhere else leaves the app, in the browser.
            Vm.Platform.OpenUri(uri);
        }

        private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            Vm.Platform.OpenUri(args.Uri);
        }
    }
}
