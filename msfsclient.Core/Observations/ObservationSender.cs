using msfsclient.Core.Api;

namespace msfsclient.Core.Observations;

/// <summary>What one pass over the waiting observations did.</summary>
/// <param name="Sent">Acknowledged by the server, in the order they were marked.</param>
/// <param name="Rejected">Given up on because the server refuses them for good (photo id and the server's reason).</param>
/// <param name="StillWaiting">No connection or a server problem; tried again later.</param>
public sealed record SendResult(IReadOnlyList<PhotoObservation> Sent, IReadOnlyList<(int PhotoId, string Reason)> Rejected, int StillWaiting);

/// <summary>Sends the waiting observations. Stops at the first problem that may go away (connectivity, server error) so order is kept.</summary>
public sealed class ObservationSender(AirsportsApi api, ObservationQueue queue)
{
    public async Task<SendResult> FlushAsync()
    {
        var sent = new List<PhotoObservation>();
        var rejected = new List<(int, string)>();
        var pending = queue.All();
        var waiting = 0;
        for (var index = 0; index < pending.Count; index++)
        {
            var item = pending[index];
            try
            {
                sent.Add(await api.RegisterObservationAsync((int)item.ContestantId,
                    new PhotoObservationRequest { Photo = (int)item.PhotoId, Time = item.TimeIso, Latitude = item.Latitude, Longitude = item.Longitude }));
                queue.Remove(item.Id);
            }
            catch (ApiException e) when (IsPermanent(e.Status))
            {
                queue.Remove(item.Id);
                rejected.Add(((int)item.PhotoId, string.IsNullOrWhiteSpace(e.Detail) ? "Rejected by the server" : e.Detail));
            }
            catch (Exception)
            {
                waiting = pending.Count - index;
                break;
            }
        }
        return new SendResult(sent, rejected, waiting);
    }

    /// <summary>400 (invalid or outside the flight), 403 (not allowed or switched off), 404 and 409 (not currently tracked) do not fix themselves.</summary>
    private static bool IsPermanent(int status) => status is 400 or 403 or 404 or 409;
}
