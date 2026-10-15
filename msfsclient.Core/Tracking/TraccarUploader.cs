namespace msfsclient.Core.Tracking;

public abstract record FlushResult
{
    public sealed record Done(int Sent) : FlushResult;
    /// <summary>Stopped on the first failure; remaining points stay queued for the next attempt.</summary>
    public sealed record Failed(int Sent, string? Cause) : FlushResult;
}

/// <summary>
/// Drains <see cref="PositionQueue"/> to Traccar's OsmAnd endpoint in time order. <paramref name="client"/> must NOT add the
/// Firebase Authorization header: Traccar is a different host and never gets the pilot's token.
/// </summary>
public sealed class TraccarUploader(HttpClient client, PositionQueue queue, string baseUrl = "https://traccar.airsports.no/", long batchSize = 200)
{
    public async Task<FlushResult> FlushAsync(CancellationToken ct = default)
    {
        var sent = 0;
        while (true)
        {
            var batch = queue.Oldest(batchSize);
            if (batch.Count == 0) return new FlushResult.Done(sent);
            foreach (var row in batch)
            {
                bool ok;
                try
                {
                    ok = await SendAsync(row, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    return new FlushResult.Failed(sent, e.Message);
                }
                if (!ok) return new FlushResult.Failed(sent, null);
                queue.Remove(row.Id);
                sent++;
            }
        }
    }

    private async Task<bool> SendAsync(QueuedPosition row, CancellationToken ct)
    {
        var query = OsmAnd.Query(OsmAnd.Parameters(row.DeviceId, row.TimestampMillis, row.Latitude, row.Longitude, row.SpeedKnots,
            row.Bearing, row.Altitude, row.Accuracy, row.BatteryPercent));
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "?" + query);
        using var response = await client.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }
}
