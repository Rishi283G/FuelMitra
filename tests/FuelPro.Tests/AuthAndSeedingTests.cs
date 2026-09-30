using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using Xunit;

namespace FuelPro.Tests;

public class AuthAndSeedingTests
{
    public AuthAndSeedingTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
    }

    private DbContextOptions<FuelProDbContext> CreateNewInMemoryDatabaseOptions()
    {
        return new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public async Task SeedData_ShouldInitializeDefaultUsersAndSettings()
    {
        // Arrange
        var options = CreateNewInMemoryDatabaseOptions();
        using var context = new FuelProDbContext(options);
        
        var tempFolder = Path.Combine(Path.GetTempPath(), "FuelPro_Test_" + Guid.NewGuid().ToString("N"));
        var credentialService = new LocalCredentialFileService(tempFolder);

        try
        {
            // Act
            await SeedData.InitializeAsync(context, credentialService);

            // Assert
            // 1. Check Manager user
            var manager = await context.Users.FirstOrDefaultAsync(u => u.Role == "Manager");
            Assert.NotNull(manager);
            Assert.Equal("Admin", manager.Username);
            Assert.True(BCrypt.Net.BCrypt.Verify("1234", manager.PinHash));

            // 2. Check Owner user
            var owner = await context.Users.FirstOrDefaultAsync(u => u.Role == "Owner");
            Assert.NotNull(owner);
            Assert.Equal("Owner", owner.Username);
            Assert.True(BCrypt.Net.BCrypt.Verify("5678", owner.PinHash));

            // 3. Check Developer user
            var developer = await context.Users.FirstOrDefaultAsync(u => u.Role == "Developer");
            Assert.NotNull(developer);
            Assert.Equal("Developer", developer.Username);

            // 4. Verify dev_credential.txt file was written and contains the correct PIN
            var filePath = credentialService.GetCredentialFilePath();
            Assert.True(File.Exists(filePath), $"dev_credential.txt was not found at {filePath}");

            var fileContent = await File.ReadAllTextAsync(filePath);
            Assert.Contains("Username: Developer", fileContent);
            
            // Extract PIN from file
            var lines = fileContent.Split('\n');
            var pinLine = lines.FirstOrDefault(l => l.StartsWith("Generated PIN:"));
            Assert.NotNull(pinLine);
            var pin = pinLine.Replace("Generated PIN:", "").Trim();
            Assert.Equal(6, pin.Length);

            // Verify PIN hash matches
            Assert.True(BCrypt.Net.BCrypt.Verify(pin, developer.PinHash));

            // 5. Verify Settings
            var settings = await context.Settings.FirstOrDefaultAsync();
            Assert.NotNull(settings);
            Assert.Equal("Mitali Service Station", settings.PumpStationName);
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }

    [Fact]
    public async Task AuthService_ShouldCorrectlyMapAndAuthorizeRoles()
    {
        // Arrange
        var options = CreateNewInMemoryDatabaseOptions();
        using var context = new FuelProDbContext(options);

        // Seed some custom users with legacy and new roles
        var users = new[]
        {
            new User { Username = "LegacyAdmin", PinHash = BCrypt.Net.BCrypt.HashPassword("1111"), Role = "Admin" },
            new User { Username = "LegacyOperator", PinHash = BCrypt.Net.BCrypt.HashPassword("2222"), Role = "Operator" },
            new User { Username = "ModernManager", PinHash = BCrypt.Net.BCrypt.HashPassword("3333"), Role = "Manager" },
            new User { Username = "ModernOwner", PinHash = BCrypt.Net.BCrypt.HashPassword("4444"), Role = "Owner" },
            new User { Username = "TechDeveloper", PinHash = BCrypt.Net.BCrypt.HashPassword("5555"), Role = "Developer" }
        };
        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        var userRepo = new UserRepository(context);
        var authService = new AuthService(userRepo);

        // Act & Assert for Legacy Admin -> Manager
        var loginAdmin = await authService.LoginAsync("LegacyAdmin", "1111");
        Assert.True(loginAdmin.Success);
        Assert.True(authService.IsManager);
        Assert.False(authService.IsOwner);
        Assert.False(authService.IsDeveloper);
        authService.Logout();

        // Act & Assert for Legacy Operator -> Manager
        var loginOperator = await authService.LoginAsync("LegacyOperator", "2222");
        Assert.True(loginOperator.Success);
        Assert.True(authService.IsManager);
        Assert.False(authService.IsOwner);
        Assert.False(authService.IsDeveloper);
        authService.Logout();

        // Act & Assert for Modern Manager -> Manager
        var loginManager = await authService.LoginAsync("ModernManager", "3333");
        Assert.True(loginManager.Success);
        Assert.True(authService.IsManager);
        authService.Logout();

        // Act & Assert for Modern Owner -> Owner
        var loginOwner = await authService.LoginAsync("ModernOwner", "4444");
        Assert.True(loginOwner.Success);
        Assert.False(authService.IsManager);
        Assert.True(authService.IsOwner);
        Assert.False(authService.IsDeveloper);
        authService.Logout();

        // Act & Assert for Tech Developer -> Developer
        var loginDeveloper = await authService.LoginAsync("TechDeveloper", "5555");
        Assert.True(loginDeveloper.Success);
        Assert.False(authService.IsManager);
        Assert.False(authService.IsOwner);
        Assert.True(authService.IsDeveloper);
        authService.Logout();
    }
}

