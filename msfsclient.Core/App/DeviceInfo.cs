namespace msfsclient.Core.App;

/// <summary>
/// What the controller needs to know about the running app: <see cref="Platform"/> is "msfs" for this client, and
/// <see cref="AppVersion"/> the build number compared with the server's minimum version.
/// </summary>
public sealed record DeviceInfo(string Platform, int AppVersion, string? VersionName = null)
{
    /// <summary>The platform name sent to the server for the simulator client.</summary>
    public const string MsfsPlatform = "msfs";

    /// <summary>The version pilots know the app by, the same on every platform (mobile repo: version.properties).</summary>
    public const string SharedVersionName = "2.0.0";

    /// <summary>What is shown in the app and quoted in bug reports: "2.0.0 (100)", or "build 100" when the name is not known.</summary>
    public string VersionLabel => string.IsNullOrWhiteSpace(VersionName) ? $"build {AppVersion}" : $"{VersionName} ({AppVersion})";
}
