using System;
using System.Linq;
using msfsclient.Contracts.Services;

namespace msfsclient.Services
{
    public class AppConfigService : IAppConfigService
    {
        public string ApiBaseUrl { get; private set; } = "https://airsports.no/api/v1/userprofile/";
        public string WebBaseUrl { get; private set; } = "https://app.airsports.no";
        public string TraccarBaseUrl { get; private set; } = "https://traccar.airsports.no/";
        public bool IsTestMode { get; private set; }

        public AppConfigService()
        {
            var args = Environment.GetCommandLineArgs();
            
            // Check for --base-url switch
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].Equals("--base-url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    var overrideUrl = args[i + 1].TrimEnd('/');
                    
                    WebBaseUrl = overrideUrl;
                    ApiBaseUrl = $"{overrideUrl}/api/v1/userprofile/";
                    
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
    }
}
