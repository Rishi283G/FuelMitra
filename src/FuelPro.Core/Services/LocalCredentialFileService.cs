using System;
using System.IO;
using System.Threading.Tasks;

namespace FuelPro.Core.Services;

public class LocalCredentialFileService : ICredentialFileService
{
    private readonly string _folderPath;

    public LocalCredentialFileService()
    {
        var env = Environment.GetEnvironmentVariable("FUELPRO_ENV");
        if (string.Equals(env, "TEST", StringComparison.OrdinalIgnoreCase))
        {
            _folderPath = Path.Combine(Path.GetTempPath(), "FuelPro_Test_" + Guid.NewGuid().ToString("N"));
        }
        else
        {
            _folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro");
        }
    }

    public LocalCredentialFileService(string customFolderPath)
    {
        _folderPath = customFolderPath;
    }

    public async Task WriteCredentialAsync(string username, string pin)
    {
        Directory.CreateDirectory(_folderPath);
        var filePath = GetCredentialFilePath();
        var text = $"Rashtra Technologies - PyroSync\n" +
                   $"Developer Account Security Credential\n" +
                   $"=====================================\n" +
                   $"Username: {username}\n" +
                   $"Generated PIN: {pin}\n" +
                   $"Generated On: {DateTime.Now:dd MMM yyyy hh:mm:ss tt}\n" +
                   $"This PIN is required to access Developer settings and tools.\n" +
                   $"Keep this file secure and do not share it with users.\n";
        await File.WriteAllTextAsync(filePath, text);
    }

    public string GetCredentialFilePath()
    {
        return Path.Combine(_folderPath, "dev_credential.txt");
    }
}
