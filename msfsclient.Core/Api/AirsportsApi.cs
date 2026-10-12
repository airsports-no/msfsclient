using System.Net;
using System.Net.Http.Headers;
using System.Text;
using msfsclient.Core.App;
using msfsclient.Core.Auth;

namespace msfsclient.Core.Api;

public sealed class NotSignedInException() : Exception("Not signed in");

/// <summary>An HTTP error from the API; <see cref="Detail"/> is the server's explanation (its "detail" field when it sends one).</summary>
public sealed class ApiException(int status, string detail) : Exception($"HTTP {status}: {detail}")
{
    public int Status { get; } = status;
    public string Detail { get; } = detail;
}

/// <summary>
/// Client for the Airsports REST API (<c>https://airsports.no/api/v1/</c>). Port of api/AirsportsApi.kt.
/// Auth is <c>Authorization: JWT &lt;firebase id token&gt;</c>. On 401/403 the token is force-refreshed and the request retried
/// exactly once. Traccar uploads use a separate, unauthenticated client.
/// </summary>
public sealed class AirsportsApi(HttpClient client, IAuthTokenProvider tokens, string baseUrl = "https://airsports.no/api/v1/", DeviceInfo? appInfo = null)
{
    private readonly string _baseUrl = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";

    public async Task<Person> ProfileAsync() =>
        await BodyAsync<Person>(await AuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + "userprofile/retrieve_profile/")));

    public async Task<Person> UpdateProfileAsync(PersonUpdate update) =>
        await BodyAsync<Person>(await AuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Patch, _baseUrl + "userprofile/partial_update_profile/") { Content = Json(update) }));

    /// <summary>
    /// Permanently deletes the signed-in account. <paramref name="email"/> is the confirmation the backend requires. Throws
    /// <see cref="ApiException"/> whose detail is the backend's explanation (e.g. organizer accounts must go through support).
    /// </summary>
    public async Task DeleteAccountAsync(string email) =>
        await ExplainedAsync(await AuthorizedRawAsync(() => new HttpRequestMessage(HttpMethod.Delete, _baseUrl + "userprofile/delete_account/") { Content = Json(new DeleteAccountRequest(email)) }));

    /// <summary>Replaces the profile photo (a JPEG); returns the updated profile whose <c>picture</c> is the new URL.</summary>
    public async Task<Person> UploadProfilePictureAsync(byte[] jpeg) =>
        await BodyAsync<Person>(await AuthorizedAsync(() =>
        {
            var image = new ByteArrayContent(jpeg);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            var form = new MultipartFormDataContent { { image, "picture", "profile.jpg" } };
            return new HttpRequestMessage(HttpMethod.Patch, _baseUrl + "userprofile/partial_update_profile/") { Content = form };
        }));

    /// <summary>Public (no sign-in needed), so it also works before login to force an update.</summary>
    public async Task<MobileConfig> MobileConfigAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + "mobile/config/");
        return await BodyAsync<MobileConfig>(await CheckedAsync(await client.SendAsync(request)));
    }

    /// <summary>Cancels the pilot's own registered flight (the server only allows it for self-managed tasks and the flight's pilot).</summary>
    public async Task CancelFlightAsync(int contestId, int navigationTaskId, int contestantId) =>
        await ExplainedAsync(await AuthorizedRawAsync(() => new HttpRequestMessage(HttpMethod.Delete,
            _baseUrl + $"contests/{contestId}/navigationtasks/{navigationTaskId}/delete_self_managed_contestant/{contestantId}/")));

    /// <summary>The photos the crew of <paramref name="contestantId"/> can mark and what they have marked so far.</summary>
    public async Task<ObservationPhotos> ObservationPhotosAsync(int contestantId) =>
        await BodyAsync<ObservationPhotos>(await AuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + $"contestant/{contestantId}/observation_photos/")));

    /// <summary>
    /// Registers a marked photo; the server is idempotent per (flight, photo), so retrying is harmless. Throws
    /// <see cref="ApiException"/> with the server's reason otherwise.
    /// </summary>
    public async Task<PhotoObservation> RegisterObservationAsync(int contestantId, PhotoObservationRequest request)
    {
        var response = await AuthorizedRawAsync(() => new HttpRequestMessage(HttpMethod.Post, _baseUrl + $"contestant/{contestantId}/photo_observations/") { Content = Json(request) });
        await ExplainedAsync(response);
        return await BodyAsync<PhotoObservation>(response);
    }

    /// <summary>Cancels a mark (the photo counts as not marked again).</summary>
    public async Task CancelObservationAsync(int contestantId, int observationId) =>
        await ExplainedAsync(await AuthorizedRawAsync(() => new HttpRequestMessage(HttpMethod.Delete, _baseUrl + $"contestant/{contestantId}/photo_observations/{observationId}/")));

    public async Task<IReadOnlyList<Flight>> MyFutureFlightsAsync() =>
        await BodyAsync<List<Flight>>(await AuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + "userprofile/my_future_flights/")));

    public async Task<IReadOnlyList<Flight>> MyPreviousFlightsAsync() =>
        await BodyAsync<List<Flight>>(await AuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + "userprofile/my_previous_flights/")));

    /// <summary>The practice flight offered to new simulator pilots, or null when none is offered (404).</summary>
    public async Task<Quickstart?> QuickstartAsync()
    {
        var response = await AuthorizedRawAsync(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + "userprofile/msfs_quickstart/"));
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await BodyAsync<Quickstart>(await CheckedAsync(response));
    }

    /// <summary>
    /// One-press registration for the practice flight with the server's defaults; answers the quickstart with the flight. Null
    /// when the server has no such registration (404/405: an older server); <see cref="ApiException"/> with its reason when it refuses.
    /// </summary>
    public async Task<Quickstart?> RegisterQuickstartAsync()
    {
        var response = await AuthorizedRawAsync(() => new HttpRequestMessage(HttpMethod.Post, _baseUrl + "userprofile/msfs_quickstart_register/") { Content = Json(new { }) });
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) return null;
        await ExplainedAsync(response);
        return await BodyAsync<Quickstart>(response);
    }

    /// <summary>The organizers' announcements that concern this pilot and are still showing (newest first).</summary>
    public async Task<IReadOnlyList<Announcement>> AnnouncementsAsync() =>
        await BodyAsync<List<Announcement>>(await AuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + "userprofile/announcements/")));

    /// <summary>The contests with these ids (names and their tasks), following the backend's cursor pagination.</summary>
    public async Task<IReadOnlyList<ContestSummary>> ContestsByIdsAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return [];
        var all = new List<ContestSummary>();
        string? next = _baseUrl + "contests/?pks=" + Uri.EscapeDataString(string.Join(",", ids)) + "&exclude_teams=true";
        while (next != null)
        {
            var url = next;
            var page = await BodyAsync<ContestPage>(await AuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, url)));
            all.AddRange(page.Results);
            next = page.Next;
        }
        return all;
    }

    /// <summary>
    /// The flights scheduled for the pilot's SIMULATOR tracking id right now, or empty (backend answers 404). The phone app uses
    /// <c>get_current_app_navigation_task</c>; the server formats both the same way.
    /// </summary>
    public async Task<IReadOnlyList<CurrentTask>> CurrentTasksAsync()
    {
        var response = await AuthorizedRawAsync(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + "userprofile/get_current_sim_navigation_task/"));
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        return await BodyAsync<List<CurrentTask>>(await CheckedAsync(response));
    }

    private async Task<HttpResponseMessage> AuthorizedAsync(Func<HttpRequestMessage> build) => await CheckedAsync(await AuthorizedRawAsync(build));

    /// <summary>Sends with the current token; on 401/403 refreshes the token once and sends the request again.</summary>
    private async Task<HttpResponseMessage> AuthorizedRawAsync(Func<HttpRequestMessage> build)
    {
        var token = await tokens.IdTokenAsync(forceRefresh: false) ?? throw new NotSignedInException();
        var first = await client.SendAsync(Authorize(build(), token));
        if (first.StatusCode != HttpStatusCode.Unauthorized && first.StatusCode != HttpStatusCode.Forbidden) return first;
        first.Dispose();
        var fresh = await tokens.IdTokenAsync(forceRefresh: true) ?? throw new NotSignedInException();
        return await client.SendAsync(Authorize(build(), fresh));
    }

    private HttpRequestMessage Authorize(HttpRequestMessage request, string token)
    {
        request.Headers.TryAddWithoutValidation("Authorization", "JWT " + token);
        if (appInfo != null)
        {
            request.Headers.TryAddWithoutValidation("X-App-Platform", appInfo.Platform);
            if (!string.IsNullOrWhiteSpace(appInfo.VersionName)) request.Headers.TryAddWithoutValidation("X-App-Version", appInfo.VersionName);
            request.Headers.TryAddWithoutValidation("X-App-Build", appInfo.AppVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return request;
    }

    private static async Task<HttpResponseMessage> CheckedAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new ApiException((int)response.StatusCode, await SafeBodyAsync(response));
        return response;
    }

    /// <summary>Throws with the server's "detail" (or the raw body) when the request failed.</summary>
    private static async Task ExplainedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await SafeBodyAsync(response);
        throw new ApiException((int)response.StatusCode, AirsportsJson.Detail(body) ?? body);
    }

    private static async Task<string> SafeBodyAsync(HttpResponseMessage response)
    {
        try { return await response.Content.ReadAsStringAsync(); }
        catch (Exception) { return ""; }
    }

    private static async Task<T> BodyAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            return AirsportsJson.Deserialize<T>(text) ?? throw new ApiException((int)response.StatusCode, "Empty response");
        }
    }

    private static StringContent Json<T>(T body) => new(AirsportsJson.Serialize(body), Encoding.UTF8, "application/json");
}
