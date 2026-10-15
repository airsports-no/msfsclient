namespace msfsclient.Contracts.Services
{
    public interface IAppConfigService
    {
        /// <summary>The REST API root, e.g. https://airsports.no/api/v1/.</summary>
        string ApiBaseUrl { get; }
        string WebBaseUrl { get; }
        string TraccarBaseUrl { get; }
        /// <summary>Full package (or assembly) version, e.g. 1.0.30.0.</summary>
        string AppVersion { get; }
        /// <summary>This client's own build number (the package version's third part), compared with the server's minimum version.</summary>
        int BuildNumber { get; }
        bool IsTestMode { get; }
        /// <summary>Where the app keeps its database and other local files.</summary>
        string LocalFolder { get; }
        /// <summary>Crash reporting address (Sentry DSN) for release builds; null switches crash reporting off.</summary>
        string? CrashReportingDsn { get; }

        /// <summary>Port of the local server for in-sim displays (<c>--hud-port</c>; 47911 by default).</summary>
        int HudPort { get; }
    }
}
