namespace msfsclient.Contracts.Services
{
    public interface IAppConfigService
    {
        string ApiBaseUrl { get; }
        string WebBaseUrl { get; }
        string TraccarBaseUrl { get; }
        bool IsTestMode { get; }
    }
}
