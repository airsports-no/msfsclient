namespace msfsclient.Contracts.Services
{
    public interface IAppConfigService
    {
        string ApiBaseUrl { get; }
        string WebBaseUrl { get; }
        string TraccarBaseUrl { get; }
        string AppVersion { get; }
        bool IsTestMode { get; }
    }
}
