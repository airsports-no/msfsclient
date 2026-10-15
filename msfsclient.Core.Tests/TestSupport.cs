using System.Net;
using System.Text;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Auth;

namespace msfsclient.Core.Tests;

/// <summary>A request as the fake server saw it (the body is read before the handler answers).</summary>
public sealed record SeenRequest(string Method, Uri Url, string Body, HttpRequestMessage Message)
{
    public string Path => Url.AbsolutePath;
    public string? Header(string name) => Message.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : null;
    public string? ContentType => Message.Content?.Headers.ContentType?.MediaType;
}

/// <summary>Stands in for the server (the Kotlin tests' MockEngine).</summary>
public sealed class MockHandler(Func<SeenRequest, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public MockHandler(Func<SeenRequest, HttpResponseMessage> respond) : this(r => Task.FromResult(respond(r))) { }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        return await respond(new SeenRequest(request.Method.Method, request.RequestUri!, body, request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("") };
}

public sealed class FakeTokens(bool signedIn = true) : IAuthTokenProvider
{
    public bool SignedIn { get; set; } = signedIn;
    public List<bool> Calls { get; } = [];

    public Task<string?> IdTokenAsync(bool forceRefresh)
    {
        lock (Calls) Calls.Add(forceRefresh);
        return Task.FromResult<string?>(!SignedIn ? null : forceRefresh ? "fresh" : "stale");
    }
}

public sealed class FixedTokens(string token = "tok") : IAuthTokenProvider
{
    public Task<string?> IdTokenAsync(bool forceRefresh) => Task.FromResult<string?>(token);
}

public sealed class FakeAuth(bool signedIn = true, bool verified = true) : IAuthService
{
    public bool SignedIn { get; set; } = signedIn;
    public bool Verified { get; set; } = verified;
    public Exception? FailWith { get; set; }

    public IAuthTokenProvider Tokens { get; } = new FixedTokens();
    public bool IsSignedIn() => SignedIn;
    public bool IsEmailVerified() => Verified;
    public string? Email() => "pilot@example.com";

    public Task SignInWithPasswordAsync(string email, string password)
    {
        if (FailWith != null) throw FailWith;
        SignedIn = true;
        return Task.CompletedTask;
    }

    /// <summary>The custom tokens signed in with (Google / Apple sign-in).</summary>
    public List<string> CustomTokens { get; } = [];

    public Task SignInWithCustomTokenAsync(string customToken)
    {
        if (FailWith != null) throw FailWith;
        CustomTokens.Add(customToken);
        SignedIn = true;
        Verified = true;
        return Task.CompletedTask;
    }

    public Task CreateAccountAsync(string email, string password)
    {
        SignedIn = true;
        Verified = false;
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email) => Task.CompletedTask;
    public Task ResendVerificationAsync() => Task.CompletedTask;

    public Task<bool> CheckVerifiedAsync()
    {
        Verified = true;
        return Task.FromResult(true);
    }

    public void SignOut() => SignedIn = false;
    public string DescribeError(Exception e) => "nice: " + e.Message;
}

public static class Api
{
    public static AirsportsApi With(MockHandler handler, IAuthTokenProvider? tokens = null, DeviceInfo? info = null) =>
        new(new HttpClient(handler), tokens ?? new FixedTokens(), appInfo: info);

    public static long Ms(string iso) => IsoTime.EpochMillisOrNull(iso)!.Value;

    /// <summary>FakeAuth never checks a password; this only fills the parameter.</summary>
    public const string FakePassword = "not-a-real-password"; // ggignore
}

public static class ControllerWaits
{
    /// <summary>Waits until the controller's state satisfies <paramref name="condition"/> (the Kotlin tests' <c>state.first { }</c>).</summary>
    public static async Task<UiState> WaitFor(this AppController c, Func<UiState, bool> condition, int timeoutMillis = 10_000)
    {
        var tcs = new TaskCompletionSource<UiState>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(UiState s)
        {
            if (condition(s)) tcs.TrySetResult(s);
        }
        c.StateChanged += Check;
        try
        {
            Check(c.State);
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMillis));
            if (done != tcs.Task) throw new TimeoutException($"Timed out; state = {c.State}");
            return await tcs.Task;
        }
        finally
        {
            c.StateChanged -= Check;
        }
    }
}
