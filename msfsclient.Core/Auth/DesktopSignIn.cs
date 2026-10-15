using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using msfsclient.Core.Api;

namespace msfsclient.Core.Auth;

/// <summary>A sign-in that could not be completed, with text for the pilot.</summary>
public sealed class SignInException(string message) : Exception(message);

/// <summary>
/// Sign-in with Google or Apple through the website, the usual way for desktop apps (RFC 8252). The providers' own
/// sign-in cannot run inside the client (Google refuses embedded browsers, Apple only returns to registered https pages):
/// <list type="number">
/// <item>Listen on a free port on 127.0.0.1 and open <c>/accounts/desktop_sign_in/</c> in the browser with that port, a random
/// state and the SHA-256 of a secret kept here (the code challenge, as in OAuth PKCE).</item>
/// <item>The pilot signs in with Google or Apple on that page; the website sends the browser to
/// <c>http://127.0.0.1:&lt;port&gt;/callback?code=..&amp;state=..</c>, which is answered with a redirect to the "you are signed in" page.</item>
/// <item>Trade the code and the secret for a Firebase custom token (<c>/accounts/desktop_sign_in/token/</c>) and sign in to
/// Firebase with it: the same account as on the website and in the apps.</item>
/// </list>
/// </summary>
public sealed class DesktopSignIn(HttpClient http, Func<string> siteBase, Action<string> openBrowser, IAuthService auth)
{
    /// <summary>How long the client waits for the pilot to finish in the browser.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>Signs in with "google" or "apple". Throws <see cref="SignInException"/> with text for the pilot, or
    /// <see cref="OperationCanceledException"/> when cancelled or timed out.</summary>
    public async Task SignInAsync(string provider, CancellationToken cancellation = default)
    {
        var verifier = RandomText();
        var state = RandomText();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var site = siteBase().TrimEnd('/');
            openBrowser($"{site}/accounts/desktop_sign_in/?port={port}&state={state}&code_challenge={Challenge(verifier)}&provider={provider}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(Timeout);
            var code = await WaitForCallbackAsync(listener, state, $"{site}/accounts/desktop_sign_in/done/", timeout.Token);
            var customToken = await TradeAsync(site, code, verifier, cancellation);
            await auth.SignInWithCustomTokenAsync(customToken);
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>43 characters of URL-safe randomness (32 bytes).</summary>
    public static string RandomText() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The code challenge the website keeps for <paramref name="verifier"/>: base64url(SHA-256(verifier)).</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Answers requests on the port until the callback with our state arrives; returns its code.</summary>
    private static async Task<string> WaitForCallbackAsync(TcpListener listener, string state, string donePage, CancellationToken cancellation)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation);
            using var stream = client.GetStream();
            var target = await ReadRequestTargetAsync(stream, cancellation);
            var query = ParseQuery(target);
            if (target?.Split('?', 2)[0] != "/callback")
            {
                await RespondAsync(stream, "404 Not Found", null, "Not found.", cancellation); // e.g. the browser asking for a favicon
                continue;
            }
            if (query.GetValueOrDefault("state") != state || string.IsNullOrEmpty(query.GetValueOrDefault("code")))
            {
                await RespondAsync(stream, "400 Bad Request", null, "This sign-in is not the one the Airsports client started. Start it again in the client.", cancellation);
                continue;
            }
            await RespondAsync(stream, "302 Found", donePage, "You are signed in. Go back to the Airsports client.", cancellation);
            return query["code"];
        }
    }

    /// <summary>The path and query of the request line ("GET /callback?code=.. HTTP/1.1"), or null.</summary>
    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellation)
    {
        var buffer = new byte[8192];
        var length = 0;
        using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        read.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (length < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(length), read.Token);
                if (n == 0) break;
                length += n;
                var text = Encoding.ASCII.GetString(buffer, 0, length);
                var end = text.IndexOf("\r\n", StringComparison.Ordinal);
                if (end < 0) continue;
                var parts = text[..end].Split(' ');
                return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
            }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            // A connection that sends nothing is dropped; keep listening.
        }
        return null;
    }

    private static Dictionary<string, string> ParseQuery(string? target)
    {
        var result = new Dictionary<string, string>();
        var q = target?.IndexOf('?') ?? -1;
        if (target == null || q < 0) return result;
        foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0) result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return result;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string? location, string text, CancellationToken cancellation)
    {
        var body = Encoding.UTF8.GetBytes(text);
        var head = $"HTTP/1.1 {status}\r\n{(location != null ? $"Location: {location}\r\n" : "")}Content-Type: text/plain; charset=utf-8\r\n" +
                   $"Content-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancellation);
        await stream.WriteAsync(body, cancellation);
    }

    private sealed record TokenRequest(string Code, string CodeVerifier);

    private async Task<string> TradeAsync(string site, string code, string verifier, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(AirsportsJson.Serialize(new TokenRequest(code, verifier)), Encoding.UTF8, "application/json");
            response = await http.PostAsync($"{site}/accounts/desktop_sign_in/token/", content, cancellation);
        }
        catch (HttpRequestException)
        {
            throw new SignInException("No connection to Airsports. Check the internet connection and try again.");
        }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellation);
            string? token = null, error = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("custom_token", out var t) && t.ValueKind == JsonValueKind.String) token = t.GetString();
                    if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) error = e.GetString();
                }
            }
            catch (JsonException)
            {
            }
            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(token)) return token;
            throw new SignInException(error ?? $"The sign-in could not be completed (HTTP {(int)response.StatusCode}). Please try again.");
        }
    }
}
