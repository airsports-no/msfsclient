using System;
using System.IO;
using System.Linq;
using msfsclient.Contracts.Services;

namespace msfsclient.Services
{
    public class AppConfigService : IAppConfigService
    {
        public string ApiBaseUrl { get; private set; } = "https://airsports.no/api/v1/";
        public string WebBaseUrl { get; private set; } = "https://app.airsports.no";
        public string TraccarBaseUrl { get; private set; } = "https://traccar.airsports.no/";
        public string AppVersion { get; private set; }
        public int BuildNumber { get; private set; }
        public bool IsTestMode { get; private set; }
        public string LocalFolder { get; }
        public string? CrashReportingDsn { get; }

        public AppConfigService()
        {
            (AppVersion, BuildNumber) = GetAppVersion();
            LocalFolder = GetLocalFolder();
            var dsn = Environment.GetEnvironmentVariable("AIRSPORTS_SENTRY_DSN");
            CrashReportingDsn = string.IsNullOrWhiteSpace(dsn) ? null : dsn;

            var args = Environment.GetCommandLineArgs();

            // Check for --base-url switch
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--base-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var overrideUrl = args[i + 1].TrimEnd('/');

                    WebBaseUrl = overrideUrl;
                    ApiBaseUrl = $"{overrideUrl}/api/v1/";

                    // If we are overriding base URL, we consider it test mode
                    IsTestMode = true;

                    System.Diagnostics.Debug.WriteLine($"AppConfigService: Local Test Mode Active. Base URL: {overrideUrl}");
                }
            }

            // Optional: Support a simple --test-mode flag if they just want a boolean but default URLs
            if (args.Any(a => a.Equals("--test-mode", StringComparison.OrdinalIgnoreCase)))
            {
                IsTestMode = true;
            }
        }

        private static (string Version, int Build) GetAppVersion()
        {
            try
            {
                // Try to get version from Package Identity (MSIX)
                var version = Windows.ApplicationModel.Package.Current.Id.Version;
                return ($"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}", version.Build);
            }
            catch (Exception)
            {
                // Fallback for unpackaged execution
                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                return (version?.ToString() ?? "1.0.0.0", Math.Max(0, version?.Build ?? 0));
            }
        }

        private static string GetLocalFolder()
        {
            // For testing: keep the database and sign-in apart from the real ones.
            var overrideFolder = Environment.GetEnvironmentVariable("MSFSCLIENT_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(overrideFolder))
            {
                Directory.CreateDirectory(overrideFolder);
                return overrideFolder;
            }
            try
            {
                // Try to get the packaged app local folder
                return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception)
            {
                // Fallback for unpackaged mode
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "msfsclient");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }
    }
}
