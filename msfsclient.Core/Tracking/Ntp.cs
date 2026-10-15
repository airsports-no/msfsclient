using System.Net;
using System.Net.Sockets;

namespace msfsclient.Core.Tracking;

/// <summary>One NTP exchange: <see cref="OffsetMillis"/> = network time minus PC clock (positive: the PC is behind), <see cref="DelayMillis"/> = round trip.</summary>
public sealed record NtpMeasurement(long OffsetMillis, long DelayMillis);

/// <summary>Where the app gets an independent opinion of the time (the PC clock is compared against it).</summary>
public interface INetworkClock
{
    /// <summary>Null when no time server answered.</summary>
    Task<NtpMeasurement?> MeasureAsync();
}

/// <summary>NTP v4 packet building and parsing (RFC 5905, client mode). No I/O, so it is fully unit-testable.</summary>
public static class Ntp
{
    public const int PacketSize = 48;
    private const long UnixEpochInNtpSeconds = 2_208_988_800L;

    /// <summary>A client request that carries <paramref name="transmitMillis"/> as its transmit timestamp (echoed back as the originate timestamp).</summary>
    public static byte[] Request(long transmitMillis)
    {
        var packet = new byte[PacketSize];
        packet[0] = 0x23; // leap indicator 0, version 4, mode 3 (client)
        WriteTimestamp(packet, 40, transmitMillis);
        return packet;
    }

    /// <summary>
    /// Offset and delay from a server <paramref name="response"/> to <paramref name="request"/>, with t1 = <paramref name="sentMillis"/>
    /// and t4 = <paramref name="receivedMillis"/> on the PC clock. Null for anything that is not a trustworthy answer: wrong size
    /// or mode, an unsynchronised server, a kiss-o-death (stratum 0), or a reply that does not echo our transmit timestamp.
    /// </summary>
    public static NtpMeasurement? Measure(byte[] request, byte[] response, long sentMillis, long receivedMillis)
    {
        if (response.Length < PacketSize) return null;
        var header = response[0];
        var leap = header >> 6;
        var mode = header & 0x7;
        var stratum = response[1];
        if (mode != 4 || leap == 3 || stratum == 0 || stratum > 15) return null;
        // The originate timestamp (bytes 24..31) must be what we sent as transmit (bytes 40..47).
        for (var i = 0; i < 8; i++) if (response[24 + i] != request[40 + i]) return null;
        if (RawSeconds(response, 32) == 0 || RawSeconds(response, 40) == 0) return null;
        var t2 = ReadTimestamp(response, 32);
        var t3 = ReadTimestamp(response, 40);
        var offset = ((t2 - sentMillis) + (t3 - receivedMillis)) / 2;
        var delay = Math.Max(0, (receivedMillis - sentMillis) - (t3 - t2));
        return new NtpMeasurement(offset, delay);
    }

    public static void WriteTimestamp(byte[] buffer, int at, long epochMillis)
    {
        var seconds = FloorDiv(epochMillis, 1000) + UnixEpochInNtpSeconds;
        var fraction = (FloorMod(epochMillis, 1000) << 32) / 1000;
        for (var i = 0; i < 4; i++)
        {
            buffer[at + i] = (byte)(seconds >> (24 - 8 * i));
            buffer[at + 4 + i] = (byte)(fraction >> (24 - 8 * i));
        }
    }

    public static long ReadTimestamp(byte[] buffer, int at)
    {
        long fraction = 0;
        for (var i = 0; i < 4; i++) fraction = (fraction << 8) | buffer[at + 4 + i];
        // Rounded to the nearest millisecond: truncating loses up to 1 ms, which would bias every measurement.
        return (RawSeconds(buffer, at) - UnixEpochInNtpSeconds) * 1000L + ((fraction * 1000L + (1L << 31)) >> 32);
    }

    private static long RawSeconds(byte[] buffer, int at)
    {
        long seconds = 0;
        for (var i = 0; i < 4; i++) seconds = (seconds << 8) | buffer[at + i];
        return seconds;
    }

    private static long FloorDiv(long a, long b) => a / b - (a % b != 0 && (a < 0) != (b < 0) ? 1 : 0);
    private static long FloorMod(long a, long b) => ((a % b) + b) % b;
}

/// <summary>
/// Asks public NTP servers what time it is, over UDP. Used only to WARN a pilot whose PC clock is off (it never changes
/// timestamps). Takes the lowest-delay sample of a few per server and tries the next server on failure.
/// </summary>
public sealed class NtpClient(
    IReadOnlyList<string>? servers = null, int port = 123, int samplesPerServer = 3, int timeoutMillis = 1_500, Func<long>? now = null) : INetworkClock
{
    private readonly IReadOnlyList<string> _servers = servers ?? ["time.windows.com", "time.google.com", "pool.ntp.org"];
    private readonly Func<long> _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public async Task<NtpMeasurement?> MeasureAsync()
    {
        foreach (var server in _servers)
        {
            try
            {
                var best = await SampleAsync(server);
                if (best != null) return best;
            }
            catch (Exception)
            {
                // Next server.
            }
        }
        return null;
    }

    private async Task<NtpMeasurement?> SampleAsync(string server)
    {
        NtpMeasurement? best = null;
        // The name is looked up without blocking and within the same time limit as an answer (a slow resolver would
        // otherwise hold up the caller, e.g. the pre-flight check).
        IPAddress[] addresses;
        using (var lookup = new CancellationTokenSource(timeoutMillis)) addresses = await Dns.GetHostAddressesAsync(server, lookup.Token);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
        if (address == null) return null;
        using var udp = new UdpClient(address.AddressFamily);
        udp.Connect(address, port);
        for (var i = 0; i < samplesPerServer; i++)
        {
            var sent = _now();
            var request = Ntp.Request(sent);
            using (var sending = new CancellationTokenSource(timeoutMillis)) await udp.SendAsync(request, sending.Token);
            using var cts = new CancellationTokenSource(timeoutMillis);
            UdpReceiveResult received;
            try { received = await udp.ReceiveAsync(cts.Token); }
            catch (OperationCanceledException) { continue; }
            catch (SocketException) { continue; }
            var measurement = Ntp.Measure(request, received.Buffer, sent, _now());
            if (measurement != null && (best == null || measurement.DelayMillis < best.DelayMillis)) best = measurement;
        }
        return best;
    }
}
