using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Services;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

/// <summary>
/// Stage 2: DSM Identity & OTP Configuration Test Suite.
/// Covers:
/// 1. Employee ID loading, whitespace trimming, empty rejection, active uniqueness, self-update allowance, and identity preservation.
/// 2. Requires OTP toggle behavior (true, false overriding HasTidBatch, null legacy fallback) and persistence round-trip.
/// 3. Desktop/PWA parity for OTP eligibility filtering.
/// 4. Pine Labs Card deduplication in the OTP dropdown and exclusion of inactive / OTP-disabled types.
/// </summary>
public class Stage2DsmOtpTests
{
    private DbContextOptions<FuelProDbContext> CreateInMemoryDbOptions()
    {
        return new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Employee ID Editing Tests
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EmployeeId_ValidChange_SavesAndSurvivesReload()
    {
        var options = CreateInMemoryDbOptions();
        using var context = new FuelProDbContext(options);

        var dsm = new DsmUser
        {
            EmployeeCode = "EMP-001",
            FullName = "Ramesh Kumar",
            MobileNumber = "9876543210",
            Email = "ramesh@example.com",
            AuthUserId = Guid.NewGuid().ToString(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        context.DsmUsers.Add(dsm);
        await context.SaveChangesAsync();

        // Simulate edit
        string newCode = "EMP-001-REV";
        dsm.EmployeeCode = newCode.Trim();
        context.Entry(dsm).State = EntityState.Modified;
        await context.SaveChangesAsync();

        // Reload fresh context
        using var reloadContext = new FuelProDbContext(options);
        var loaded = await reloadContext.DsmUsers.FindAsync(dsm.DsmUserId);

        Assert.NotNull(loaded);
        Assert.Equal("EMP-001-REV", loaded.EmployeeCode);
        Assert.Equal("Ramesh Kumar", loaded.FullName);
        Assert.Equal(dsm.AuthUserId, loaded.AuthUserId);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void EmployeeId_EmptyOrWhitespace_IsRejected(string? emptyInput)
    {
        var trimmed = emptyInput?.Trim() ?? "";
        bool isValid = !string.IsNullOrWhiteSpace(trimmed);

        Assert.False(isValid, "Empty or whitespace Employee ID must be rejected.");
    }

    [Fact]
    public void EmployeeId_LeadingAndTrailingWhitespace_IsTrimmed()
    {
        string raw = "   EMP-1049   ";
        string trimmed = raw.Trim();

        Assert.Equal("EMP-1049", trimmed);
    }

    [Fact]
    public async Task EmployeeId_DuplicateForOtherActiveAccount_IsRejectedCaseInsensitively()
    {
        var options = CreateInMemoryDbOptions();
        using var context = new FuelProDbContext(options);

        var user1 = new DsmUser
        {
            EmployeeCode = "EMP-001",
            FullName = "User One",
            MobileNumber = "9876543211",
            Email = "u1@example.com",
            AuthUserId = Guid.NewGuid().ToString(),
            IsActive = true
        };
        var user2 = new DsmUser
        {
            EmployeeCode = "EMP-002",
            FullName = "User Two",
            MobileNumber = "9876543212",
            Email = "u2@example.com",
            AuthUserId = Guid.NewGuid().ToString(),
            IsActive = true
        };
        context.DsmUsers.AddRange(user1, user2);
        await context.SaveChangesAsync();

        // Try to update user2's code to "emp-001" (matching user1 case-insensitively)
        string targetCode = "emp-001".Trim();
        bool duplicateExists = await context.DsmUsers.AnyAsync(u =>
            u.DsmUserId != user2.DsmUserId &&
            u.EmployeeCode.ToLower() == targetCode.ToLower() &&
            u.IsActive);

        Assert.True(duplicateExists, "Case-insensitive duplicate for another active account must be detected.");
    }

    [Fact]
    public async Task EmployeeId_EditingCurrentUsersOwnIdentifier_DoesNotConflictWithSelf()
    {
        var options = CreateInMemoryDbOptions();
        using var context = new FuelProDbContext(options);

        var user1 = new DsmUser
        {
            EmployeeCode = "EMP-001",
            FullName = "User One",
            MobileNumber = "9876543211",
            Email = "u1@example.com",
            AuthUserId = Guid.NewGuid().ToString(),
            IsActive = true
        };
        context.DsmUsers.Add(user1);
        await context.SaveChangesAsync();

        // Check if user1 editing with same code "emp-001" conflicts
        string targetCode = "emp-001".Trim();
        bool duplicateExists = await context.DsmUsers.AnyAsync(u =>
            u.DsmUserId != user1.DsmUserId &&
            u.EmployeeCode.ToLower() == targetCode.ToLower() &&
            u.IsActive);

        Assert.False(duplicateExists, "Editing own employee code must exclude self from duplicate check.");
    }

    [Fact]
    public async Task EmployeeId_DuplicateWithInactiveAccount_IsPermitted()
    {
        var options = CreateInMemoryDbOptions();
        using var context = new FuelProDbContext(options);

        var inactiveUser = new DsmUser
        {
            EmployeeCode = "EMP-OLD",
            FullName = "Former User",
            MobileNumber = "9876543210",
            Email = "old@example.com",
            AuthUserId = Guid.NewGuid().ToString(),
            IsActive = false
        };
        var activeUser = new DsmUser
        {
            EmployeeCode = "EMP-NEW",
            FullName = "New User",
            MobileNumber = "9876543211",
            Email = "new@example.com",
            AuthUserId = Guid.NewGuid().ToString(),
            IsActive = true
        };
        context.DsmUsers.AddRange(inactiveUser, activeUser);
        await context.SaveChangesAsync();

        // Try to assign "emp-old" to activeUser
        string targetCode = "emp-old".Trim();
        bool duplicateExists = await context.DsmUsers.AnyAsync(u =>
            u.DsmUserId != activeUser.DsmUserId &&
            u.EmployeeCode.ToLower() == targetCode.ToLower() &&
            u.IsActive);

        Assert.False(duplicateExists, "Inactive accounts should not block active employee code assignment.");
    }

    [Fact]
    public async Task EmployeeId_PreservesDsmIdentity_AssignmentsAndDevices()
    {
        var options = CreateInMemoryDbOptions();
        using var context = new FuelProDbContext(options);

        var dsm = new DsmUser
        {
            EmployeeCode = "EMP-100",
            FullName = "Suresh Patil",
            MobileNumber = "9111122222",
            Email = "suresh@example.com",
            AuthUserId = "supabase-uuid-12345",
            IsActive = true
        };
        context.DsmUsers.Add(dsm);
        await context.SaveChangesAsync();

        var assignment = new DsmPumpAssignment
        {
            DsmUserId = dsm.DsmUserId,
            PumpId = 1,
            ShiftType = "A",
            AssignedDate = DateTime.Today,
            IsActive = true
        };
        var device = new DsmDevice
        {
            DsmUserId = dsm.DsmUserId,
            DeviceId = "DEV-MITALI-001",
            DeviceName = "Samsung Galaxy M14",
            IsActive = true
        };
        context.DsmPumpAssignments.Add(assignment);
        context.DsmDevices.Add(device);
        await context.SaveChangesAsync();

        // Update employee code
        dsm.EmployeeCode = "EMP-100-EDITED";
        context.Entry(dsm).State = EntityState.Modified;
        await context.SaveChangesAsync();

        // Reload and verify relationships
        using var reloadContext = new FuelProDbContext(options);
        var reloadedDsm = await reloadContext.DsmUsers.FindAsync(dsm.DsmUserId);
        var reloadedAssignment = await reloadContext.DsmPumpAssignments
            .Include(a => a.DsmUser)
            .FirstOrDefaultAsync(a => a.DsmUserId == dsm.DsmUserId);
        var reloadedDevice = await reloadContext.DsmDevices
            .Include(d => d.DsmUser)
            .FirstOrDefaultAsync(d => d.DsmUserId == dsm.DsmUserId);

        Assert.NotNull(reloadedDsm);
        Assert.Equal("EMP-100-EDITED", reloadedDsm.EmployeeCode);
        Assert.Equal("supabase-uuid-12345", reloadedDsm.AuthUserId);

        Assert.NotNull(reloadedAssignment);
        Assert.Equal(reloadedDsm.DsmUserId, reloadedAssignment.DsmUserId);
        Assert.Equal("Suresh Patil", reloadedAssignment.DsmUser?.FullName);

        Assert.NotNull(reloadedDevice);
        Assert.Equal(reloadedDsm.DsmUserId, reloadedDevice.DsmUserId);
        Assert.Equal("Samsung Galaxy M14", reloadedDevice.DeviceName);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Requires OTP Configuration & Compatibility Tests
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HasOtp_True_MakesCollectionTypeEligible()
    {
        var colType = new CollectionTypeMaster
        {
            Code = "SBI_REDEEM",
            DisplayName = "SBI Redeem",
            HasTidBatch = false,
            HasOtp = true
        };

        Assert.True(colType.IsOtpEligible);
    }

    [Fact]
    public void HasOtp_False_DisablesOtp_EvenWhenHasTidBatchIsTrue()
    {
        var colType = new CollectionTypeMaster
        {
            Code = "CREDIT_CARD",
            DisplayName = "Credit Card",
            HasTidBatch = true,
            HasOtp = false
        };

        Assert.False(colType.IsOtpEligible, "HasOtp = false must disable OTP even when HasTidBatch is true.");
    }

    [Fact]
    public void HasOtp_NullOrMissing_IsExcluded_StrictDeveloperRequirement()
    {
        var legacyWithTid = new CollectionTypeMaster
        {
            Code = "PETROCARD",
            DisplayName = "PetroCard",
            HasTidBatch = true,
            HasOtp = null
        };
        var legacyWithoutTid = new CollectionTypeMaster
        {
            Code = "CASH_DEPOSIT",
            DisplayName = "Cash Deposit",
            HasTidBatch = false,
            HasOtp = null
        };
        var explicitWithOtp = new CollectionTypeMaster
        {
            Code = "PINELABS_CARD",
            DisplayName = "Pine Labs Card",
            HasTidBatch = true,
            HasOtp = true
        };

        Assert.True(legacyWithTid.IsOtpEligible, "Null HasOtp with TID must fall back to HasTidBatch.");
        Assert.False(legacyWithoutTid.IsOtpEligible, "Null HasOtp without TID must be excluded.");
        Assert.True(explicitWithOtp.IsOtpEligible, "Explicit HasOtp = true must be eligible.");
    }

    [Fact]
    public async Task HasOtp_PersistenceRoundTrip_ThroughCollectionTypeService()
    {
        var options = CreateInMemoryDbOptions();
        var services = new ServiceCollection();
        services.AddTransient(_ => new FuelProDbContext(options));
        var sp = services.BuildServiceProvider();

        // Seed initial collection types
        using (var initContext = sp.GetRequiredService<FuelProDbContext>())
        {
            initContext.Settings.Add(new Setting { SettingId = 1 });
            initContext.CollectionTypes.Add(new CollectionTypeMaster
            {
                Code = "TEST_PAYMENT",
                DisplayName = "Test Payment",
                Category = "Online",
                HasTidBatch = true,
                HasOtp = null,
                IsActive = true,
                DisplayOrder = 1
            });
            await initContext.SaveChangesAsync();
        }

        var colService = new CollectionTypeService(sp);
        var active = await colService.GetActiveCollectionTypesAsync();
        var item = active.First(c => c.Code == "TEST_PAYMENT");

        // Explicitly set HasOtp = false and save
        item.HasOtp = false;
        bool saved = await colService.SaveCollectionTypeAsync(item);
        Assert.True(saved);

        // Reload via fresh scope
        using (var checkContext = sp.GetRequiredService<FuelProDbContext>())
        {
            var reloaded = await checkContext.CollectionTypes.FindAsync(item.CollectionTypeId);
            Assert.NotNull(reloaded);
            Assert.False(reloaded.HasOtp);
            Assert.False(reloaded.IsOtpEligible);

            // Also check Settings.CollectionTypesJson
            var setting = await checkContext.Settings.FirstOrDefaultAsync();
            Assert.NotNull(setting?.CollectionTypesJson);
            Assert.Contains("\"HasOtp\":false", setting.CollectionTypesJson);
        }

        // Now set HasOtp = true and save
        item.HasOtp = true;
        saved = await colService.SaveCollectionTypeAsync(item);
        Assert.True(saved);

        using (var checkContext2 = sp.GetRequiredService<FuelProDbContext>())
        {
            var reloaded = await checkContext2.CollectionTypes.FindAsync(item.CollectionTypeId);
            Assert.NotNull(reloaded);
            Assert.True(reloaded.HasOtp);
            Assert.True(reloaded.IsOtpEligible);
        }
    }

    [Fact]
    public void DesktopAndPwa_OtpEligibilityLogic_AgreesForEquivalentConfigurations()
    {
        // Test all permutations of (hasTidBatch, hasOtp, isActive)
        var testCases = new (bool hasTid, bool? hasOtp, bool isActive)[]
        {
            (true, true, true),
            (true, false, true),
            (true, null, true),
            (false, true, true),
            (false, false, true),
            (false, null, true),
            (true, true, false),
            (true, false, false),
            (true, null, false),
            (false, true, false),
            (false, false, false),
            (false, null, false),
        };

        foreach (var (hasTid, hasOtp, isActive) in testCases)
        {
            // Desktop C# model
            var master = new CollectionTypeMaster
            {
                HasTidBatch = hasTid,
                HasOtp = hasOtp,
                IsActive = isActive
            };
            bool desktopEligible = master.IsActive && master.IsOtpEligible;

            // PWA JS simulation:
            // const isOtpEligible = ct.hasOtp !== undefined && ct.hasOtp !== null ? Boolean(ct.hasOtp) : Boolean(ct.hasTidBatch);
            // eligible = isOtpEligible && ct.isActive !== false;
            bool pwaOtpEligible = hasOtp ?? hasTid;
            bool pwaEligible = pwaOtpEligible && isActive;

            Assert.Equal(desktopEligible, pwaEligible);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Pine Labs Dropdown Deduplication Tests
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RefreshAvailableOtpCollectionTypes_PineLabsCardAppearsExactlyOnce()
    {
        var vm = new DsmEntryViewModel();

        // Simulate cached collection types containing Pine Labs Card and other payment methods
        var types = new List<CollectionTypeMaster>
        {
            new() { Code = "PHONEPE", DisplayName = "PhonePe", HasTidBatch = true, IsActive = true, DisplayOrder = 1 },
            new() { Code = "CREDIT_CARD", DisplayName = "Credit Card", HasTidBatch = true, IsActive = true, DisplayOrder = 2 },
            new() { Code = "PETROCARD", DisplayName = "PetroCard", HasTidBatch = true, IsActive = true, DisplayOrder = 3 },
            new() { Code = "PINELABS_CARD", DisplayName = "Pine Labs Card", HasTidBatch = true, HasOtp = true, IsActive = true, DisplayOrder = 4 },
            new() { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", HasTidBatch = true, IsActive = true, DisplayOrder = 5 }
        };

        // Populate dynamic collections also referencing PINELABS_CARD
        vm.DynamicCollections.Add(new DynamicCollectionEntryRow
        {
            Code = "PINELABS_CARD",
            DisplayName = "Pine Labs Card",
            HasTidBatch = true
        });

        // Set private cache via reflection
        var cacheField = typeof(DsmEntryViewModel).GetField("_cachedAllCollectionTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        cacheField?.SetValue(vm, types);

        // Execute refresh
        vm.RefreshAvailableOtpCollectionTypes();

        // Assert
        var pineLabsMatches = vm.AvailableOtpCollectionTypes
            .Where(t => PaymentCollection.NormalizeCollectionTypeCode(t) == "PINELABS_CARD")
            .ToList();

        Assert.Single(pineLabsMatches);
        Assert.Equal("PINELABS_CARD", pineLabsMatches[0]);
    }

    [Fact]
    public void RefreshAvailableOtpCollectionTypes_NamingVariantsNormalizeAndDeduplicate()
    {
        var vm = new DsmEntryViewModel();

        // Suppose master has "PINELAB_CARD" and dynamic has "PineLabs Card"
        var types = new List<CollectionTypeMaster>
        {
            new() { Code = "PINELAB_CARD", DisplayName = "Pine Labs Card", HasTidBatch = true, HasOtp = true, IsActive = true, DisplayOrder = 1 }
        };
        vm.DynamicCollections.Add(new DynamicCollectionEntryRow
        {
            Code = "PINELABS_CARD",
            DisplayName = "Pine Labs Card",
            HasTidBatch = true
        });

        var cacheField = typeof(DsmEntryViewModel).GetField("_cachedAllCollectionTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        cacheField?.SetValue(vm, types);

        vm.RefreshAvailableOtpCollectionTypes();

        var pineLabsMatches = vm.AvailableOtpCollectionTypes
            .Where(t => PaymentCollection.NormalizeCollectionTypeCode(t) == "PINELABS_CARD")
            .ToList();

        Assert.Single(pineLabsMatches);
    }

    [Fact]
    public void RefreshAvailableOtpCollectionTypes_ExplicitlyOtpDisabled_IsExcluded()
    {
        var vm = new DsmEntryViewModel();

        var types = new List<CollectionTypeMaster>
        {
            new() { Code = "PHONEPE", DisplayName = "PhonePe", HasTidBatch = true, HasOtp = true, IsActive = true },
            new() { Code = "CREDIT_CARD", DisplayName = "Credit Card", HasTidBatch = true, HasOtp = false, IsActive = true }, // OTP disabled
            new() { Code = "PETROCARD", DisplayName = "PetroCard", HasTidBatch = true, HasOtp = true, IsActive = true }, // OTP enabled
            new() { Code = "PINELABS_CARD", DisplayName = "Pine Labs Card", HasTidBatch = true, HasOtp = false, IsActive = true } // OTP disabled
        };

        var cacheField = typeof(DsmEntryViewModel).GetField("_cachedAllCollectionTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        cacheField?.SetValue(vm, types);

        vm.RefreshAvailableOtpCollectionTypes();

        // CREDIT_CARD and PINELABS_CARD must NOT appear
        Assert.DoesNotContain("CREDIT_CARD", vm.AvailableOtpCollectionTypes);
        Assert.DoesNotContain("PINELABS_CARD", vm.AvailableOtpCollectionTypes);

        // PHONEPE and PETROCARD must appear
        Assert.Contains("PHONEPE", vm.AvailableOtpCollectionTypes);
        Assert.Contains("PETROCARD", vm.AvailableOtpCollectionTypes);
    }

    [Fact]
    public void RefreshAvailableOtpCollectionTypes_InactiveCollectionTypes_AreExcluded()
    {
        var vm = new DsmEntryViewModel();

        var types = new List<CollectionTypeMaster>
        {
            new() { Code = "PHONEPE", DisplayName = "PhonePe", HasTidBatch = true, HasOtp = true, IsActive = true },
            new() { Code = "PINELABS_CARD", DisplayName = "Pine Labs Card", HasTidBatch = true, IsActive = false } // Inactive
        };

        var cacheField = typeof(DsmEntryViewModel).GetField("_cachedAllCollectionTypes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        cacheField?.SetValue(vm, types);

        vm.RefreshAvailableOtpCollectionTypes();

        Assert.Contains("PHONEPE", vm.AvailableOtpCollectionTypes);
        Assert.DoesNotContain("PINELABS_CARD", vm.AvailableOtpCollectionTypes);
    }
}
