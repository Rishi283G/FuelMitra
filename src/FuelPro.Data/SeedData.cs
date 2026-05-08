using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using BCrypt.Net;

namespace FuelPro.Data;

/// <summary>
/// Seeds default data on first run: admin user and default settings.
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(FuelProDbContext context)
    {
        // Ensure database is created and migrated
        await context.Database.MigrateAsync();

        // Seed default admin user if no users exist
        if (!await context.Users.AnyAsync())
        {
            var adminUser = new User
            {
                Username = "Admin",
                PinHash = BCrypt.Net.BCrypt.HashPassword("1234"),
                Role = "Admin",
                IsActive = true,
                MustChangePin = true,
                CreatedAt = DateTime.Now
            };

            context.Users.Add(adminUser);
        }

        // Seed default settings if none exist
        if (!await context.Settings.AnyAsync())
        {
            var defaultSettings = new Setting
            {
                HsdRate = 90.35,
                MsIRate = 103.81,
                MsIIRate = 103.81,
                PumpStationName = "VKD Petroleum",
                LastUpdated = DateTime.Now
            };

            context.Settings.Add(defaultSettings);
        }

        await context.SaveChangesAsync();
    }
}
