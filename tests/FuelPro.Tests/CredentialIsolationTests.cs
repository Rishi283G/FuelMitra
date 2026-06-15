using System;
using System.IO;
using System.Threading.Tasks;
using FuelPro.Core.Services;
using Xunit;

namespace FuelPro.Tests;

public class CredentialIsolationTests
{
    [Fact]
    public async Task DefaultConstructor_ShouldUseTempPath_WhenEnvIsTest()
    {
        // Arrange
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        var service = new LocalCredentialFileService();

        // Act
        var path = service.GetCredentialFilePath();

        // Assert
        Assert.Contains(Path.GetTempPath(), path);
        var productionFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro");
        Assert.False(path.StartsWith(productionFolder, StringComparison.OrdinalIgnoreCase));

        // Act - Verify file creation is isolated
        await service.WriteCredentialAsync("TestUser", "123456");
        Assert.True(File.Exists(path));

        // Clean up
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DefaultConstructor_ShouldUseProductionPath_WhenEnvIsProduction()
    {
        // Arrange
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "PRODUCTION");
        var service = new LocalCredentialFileService();

        // Act
        var path = service.GetCredentialFilePath();

        // Assert
        var expectedProdDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro");
        Assert.StartsWith(expectedProdDir, path);
    }

    [Fact]
    public async Task CustomPathConstructor_ShouldAlwaysUseCustomPath()
    {
        // Arrange
        var customFolder = Path.Combine(Path.GetTempPath(), "FuelPro_Custom_Test_Folder");
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "PRODUCTION"); // Even if set to production
        var service = new LocalCredentialFileService(customFolder);

        // Act
        var path = service.GetCredentialFilePath();

        // Assert
        Assert.StartsWith(customFolder, path);
    }
}
