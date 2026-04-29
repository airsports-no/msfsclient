using System.Threading.Tasks;
using msfsclient.Models;

namespace msfsclient.Contracts.Services
{
    public interface ITraccarService
    {
        Task<bool> SendDataAsync(string trackingId, FlightDataStruct data);
    }
}
