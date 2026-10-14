using System.Net;
using msfsclient.Core.App;
using msfsclient.Core.Auth;
using msfsclient.Core.Data;
using static msfsclient.Core.Tests.MockHandler;

namespace msfsclient.Core.Tests;

/// <summary>Google / Apple sign-in through the website: browser page, callback on 127.0.0.1, code + secret for a custom token.</summary>
public class DesktopSignInTest
{
    private const string Site = "https://app.example.test";
    private readonly FakeAuth _auth = new(signedIn: false, verified: false);
    private readonly List<string> _tokenRequests = [];
    private HttpResponseMessage TokenAnswer() => _tokenAnswer();
    private Func<HttpResponseMessage> _tokenAnswer = () => Json("""{"custom_token":"custom-token-1"}""");

    /// <summary>A browser that answers the sign-in page by calling the client back, as the website does after the sign-in.</summary>
    private sealed class Browser
    {
        public string? Opened;
        public Func<Uri, IEnumerable<string>> Callbacks = page =>
        {
            var q = System.Web.HttpUtility.ParseQueryString(page.Query);
            return [$"http://127.0.0.1:{q["port"]}/callback?code=the-code&state={q["state"]}"];
        };
        public readonly List<(HttpStatusCode Status, string? Location)> Answers = [];
        public Task Done = Task.CompletedTask;

        public void Open(string url)
        {
            Opened = url;
            var page = new Uri(url);
            Done = Task.Run(async () =>
            {
                using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                foreach (var callback in Callbacks(page))
                {
                    using var response = await http.GetAsync(callback);
                    Answers.Add((response.StatusCode, response.Headers.Location?.ToString()));
                }
            });
        }
    }

    private DesktopSignIn SignIn(Browser browser) => new(
        new HttpClient(new MockHandler(r =>
        {
            _tokenRequests.Add($"{r.Method} {r.Url} {r.Body}");
            return TokenAnswer();
        })),
        () => Site, browser.Open, _auth);

    [Fact]
    public async Task TheBrowserSignInEndsWithACustomTokenForTheClient()
    {
        var browser = new Browser();
        await SignIn(browser).SignInAsync("apple");
        await browser.Done;

        var page = new Uri(browser.Opened!);
        Assert.Equal($"{Site}/accounts/desktop_sign_in/", page.GetLeftPart(UriPartial.Path));
        var q = System.Web.HttpUtility.ParseQueryString(page.Query);
        Assert.Equal("apple", q["provider"]);
        Assert.True(int.Parse(q["port"]!) >= 1024);
        Assert.Equal(43, q["state"]!.Length);

        // The callback is answered with the "you are signed in" page.
        Assert.Equal([(HttpStatusCode.Found, $"{Site}/accounts/desktop_sign_in/done/")], browser.Answers);
        // The code is traded with the secret whose hash went to the website, and the client signs in with the result.
        var request = Assert.Single(_tokenRequests);
        Assert.StartsWith($"POST {Site}/accounts/desktop_sign_in/token/ ", request);
        var verifier = System.Text.Json.JsonDocument.Parse(request[(request.IndexOf('{'))..]).RootElement;
        Assert.Equal("the-code", verifier.GetProperty("code").GetString());
        Assert.Equal(q["code_challenge"], DesktopSignIn.Challenge(verifier.GetProperty("code_verifier").GetString()!));
        Assert.Equal(["custom-token-1"], _auth.CustomTokens);
    }

    [Fact]
    public async Task OtherRequestsOnThePortAreAnsweredButIgnored()
    {
        var browser = new Browser
        {
            Callbacks = page =>
            {
                var q = System.Web.HttpUtility.ParseQueryString(page.Query);
                var port = q["port"];
                return
                [
                    $"http://127.0.0.1:{port}/favicon.ico",
                    $"http://127.0.0.1:{port}/callback?code=stolen&state=someone-else",
                    $"http://127.0.0.1:{port}/callback?code=the-code&state={q["state"]}",
                ];
            },
        };
        await SignIn(browser).SignInAsync("google");
        await browser.Done;
        Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.Found], browser.Answers.Select(a => a.Status));
        Assert.Contains("\"code\":\"the-code\"", Assert.Single(_tokenRequests));
    }

    [Fact]
    public async Task TheWebsitesReasonIsShownWhenTheTradeFails()
    {
        _tokenAnswer = () => Json("""{"error":"The sign-in has expired. Please sign in again."}""", HttpStatusCode.BadRequest);
        var browser = new Browser();
        var e = await Assert.ThrowsAsync<SignInException>(() => SignIn(browser).SignInAsync("google"));
        Assert.Equal("The sign-in has expired. Please sign in again.", e.Message);
        Assert.Empty(_auth.CustomTokens);
    }

    [Fact]
    public async Task ThePilotCanGiveUpWhileTheBrowserIsOpen()
    {
        var browser = new Browser { Callbacks = _ => [] }; // the pilot never finishes in the browser
        var controller = new AppController(Api.With(new MockHandler(_ => Json("{}"))), _auth, new DeviceInfo("msfs", 30),
            settings: new AppSettings(AirsportsDatabase.InMemory()));
        controller.DesktopSignIn = SignIn(browser);
        var signingIn = controller.SignInWithProviderAsync("google");
        await controller.WaitFor(s => s.SigningInWith == "Google");
        Assert.True(controller.State.Busy);
        controller.CancelProviderSignIn();
        await signingIn;
        Assert.Null(controller.State.SigningInWith);
        Assert.False(controller.State.Busy);
        Assert.Null(controller.State.Error); // cancelling is not an error
        Assert.False(_auth.SignedIn);
    }

    [Fact]
    public async Task ASuccessfulSignInLoadsTheAccount()
    {
        var browser = new Browser();
        var controller = new AppController(
            Api.With(new MockHandler(r => r.Url.AbsolutePath.Contains("retrieve_profile")
                ? Json("""{"validated":true,"simulator_tracking_id":"sim1","first_name":"Pia"}""")
                : r.Url.AbsolutePath.Contains("get_current_sim_navigation_task") || r.Url.AbsolutePath.Contains("msfs_quickstart") || r.Url.AbsolutePath.Contains("mobile/config")
                    ? Status(HttpStatusCode.NotFound)
                    : r.Url.AbsolutePath.EndsWith("/contests/") ? Json("""{"results":[],"next":null}""") : Json("[]"))),
            _auth, new DeviceInfo("msfs", 30), settings: new AppSettings(AirsportsDatabase.InMemory()) { TourSeen = true });
        controller.DesktopSignIn = SignIn(browser);
        await controller.SignInWithProviderAsync("apple");
        Assert.Equal(["custom-token-1"], _auth.CustomTokens);
        Assert.Equal(Screen.Home, controller.State.Screen);
        Assert.Equal("Pia", controller.State.Person?.FirstName);
    }
}
