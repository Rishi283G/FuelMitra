using System.Threading.Tasks;

namespace FuelPro.Core.Services;

public interface ICredentialFileService
{
    Task WriteCredentialAsync(string username, string pin);
    string GetCredentialFilePath();
}
