using FuelPro.Core.Models;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FuelPro.Tests;

public class DsmProfileEditingTests
{
    private static DbContextOptions<FuelProDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    [Fact]
    public async Task UpdateAsync_RenamesSelectedProfileById_AndPropagatesNameAcrossEntities()
    {
        var options = CreateOptions();
        int profileId;
        int entryId;
        int userId;
        int debtorId;

        await using (var setup = new FuelProDbContext(options))
        {
            var shift = new Shift { ShiftDate = DateTime.Today, ShiftType = "A" };
            var profile = new DsmProfile { DsmName = "Old DSM Name" };
            var user = new DsmUser { FullName = "Old DSM Name", MobileNumber = "9876543210" };
            var debtor = new DsmPersonalDebtor { DsmName = "Old DSM Name", Amount = 500, EntryType = "Operational" };
            var openingBal = new OpeningBalance { EntityType = "DsmLoss", EntityIdentifier = "Old DSM Name", Amount = 200, OpeningDate = DateTime.Today };

            setup.Shifts.Add(shift);
            setup.DsmProfiles.Add(profile);
            setup.DsmUsers.Add(user);
            setup.DsmPersonalDebtors.Add(debtor);
            setup.OpeningBalances.Add(openingBal);
            await setup.SaveChangesAsync();

            setup.DsmEntries.Add(new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                DsmName = "Old DSM Name"
            });
            await setup.SaveChangesAsync();
            profileId = profile.DsmProfileId;
            entryId = await setup.DsmEntries.Select(e => e.DsmEntryId).SingleAsync();
            userId = user.DsmUserId;
            debtorId = debtor.Id;
        }

        await using var context = new FuelProDbContext(options);
        var repository = new DsmProfileRepository(context);
        var profileToUpdate = await context.DsmProfiles.SingleAsync(p => p.DsmProfileId == profileId);
        profileToUpdate.DsmName = "Renamed DSM";

        var result = await repository.UpdateAsync(profileToUpdate);

        Assert.True(result.Success, result.Error);
        var renamedProfile = await context.DsmProfiles.AsNoTracking().SingleAsync(p => p.DsmProfileId == profileId);
        var updatedEntry = await context.DsmEntries.AsNoTracking().SingleAsync(e => e.DsmEntryId == entryId);
        var updatedUser = await context.DsmUsers.AsNoTracking().SingleAsync(u => u.DsmUserId == userId);
        var updatedDebtor = await context.DsmPersonalDebtors.AsNoTracking().SingleAsync(d => d.Id == debtorId);
        var updatedBalance = await context.OpeningBalances.AsNoTracking().SingleAsync(b => b.EntityType == "DsmLoss");

        Assert.Equal(profileId, renamedProfile.DsmProfileId);
        Assert.Equal("Renamed DSM", renamedProfile.DsmName);
        Assert.Equal("Renamed DSM", updatedEntry.DsmName);
        Assert.Equal("Renamed DSM", updatedUser.FullName);
        Assert.Equal("Renamed DSM", updatedDebtor.DsmName);
        Assert.Equal("Renamed DSM", updatedBalance.EntityIdentifier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task UpdateAsync_RejectsBlankOrWhitespaceName(string invalidName)
    {
        var options = CreateOptions();
        await using var context = new FuelProDbContext(options);
        var profile = new DsmProfile { DsmName = "Current Name" };
        context.DsmProfiles.Add(profile);
        await context.SaveChangesAsync();

        profile.DsmName = invalidName;
        var repository = new DsmProfileRepository(context);

        var result = await repository.UpdateAsync(profile);

        Assert.False(result.Success);
        Assert.Equal("DSM name is required.", result.Error);
        context.ChangeTracker.Clear();
        var persistedProfile = await context.DsmProfiles.SingleAsync(p => p.DsmProfileId == profile.DsmProfileId);
        Assert.Equal("Current Name", persistedProfile.DsmName);
    }

    [Fact]
    public async Task UpdateAsync_RejectsCaseInsensitiveDuplicateName()
    {
        var options = CreateOptions();
        await using var context = new FuelProDbContext(options);
        var profileToRename = new DsmProfile { DsmName = "First DSM" };
        var existingProfile = new DsmProfile { DsmName = "Second DSM" };
        context.DsmProfiles.AddRange(profileToRename, existingProfile);
        await context.SaveChangesAsync();
        var profileId = profileToRename.DsmProfileId;

        profileToRename.DsmName = "second dSm";
        var repository = new DsmProfileRepository(context);

        var result = await repository.UpdateAsync(profileToRename);

        Assert.False(result.Success);
        Assert.Equal("A DSM profile with this name already exists.", result.Error);
        context.ChangeTracker.Clear();
        var persistedProfile = await context.DsmProfiles.SingleAsync(p => p.DsmProfileId == profileId);
        Assert.Equal("First DSM", persistedProfile.DsmName);
        Assert.Equal(profileId, persistedProfile.DsmProfileId);
    }
}

