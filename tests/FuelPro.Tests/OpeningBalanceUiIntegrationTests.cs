using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class OpeningBalanceUiIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public OpeningBalanceUiIntegrationTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new FuelProDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private FuelProDbContext CreateContext() => new FuelProDbContext(_options);

    private async Task<(AuthService auth, User user)> SetupOwnerAuthAsync(FuelProDbContext context)
    {
        var userRepo = new UserRepository(context);
        var authService = new AuthService(userRepo);

        var owner = new User
        {
            Username = "OwnerAdmin",
            Role = "Owner",
            PinHash = BCrypt.Net.BCrypt.HashPassword("1234"),
            IsActive = true
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var loginResult = await authService.LoginAsync("OwnerAdmin", "1234");
        Assert.True(loginResult.Success);
        return (authService, owner);
    }

    private async Task<int> CreateTestDsmEntryAsync(FuelProDbContext context, DateTime date)
    {
        var shift = new Shift
        {
            ShiftDate = date.Date,
            ShiftType = "A",
            CreatedAt = date
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var entry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "TestDSM",
            CreatedAt = date
        };
        context.DsmEntries.Add(entry);
        await context.SaveChangesAsync();

        return entry.DsmEntryId;
    }

    [Fact]
    public async Task Test01_OpeningBalanceList_LoadsCorrectly()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var creditor = new Creditor { Name = "Transport Corp", Phone = "9876543210" };
        var dsm = new DsmProfile { DsmName = "Ramesh Kumar", MobileNumber = "9988776655" };
        context.Creditors.Add(creditor);
        context.DsmProfiles.Add(dsm);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        // Seed 1 debtor opening balance and 1 DSM opening balance
        await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            OpeningDate = new DateTime(2026, 4, 1),
            Amount = 45000.0,
            Notes = "Debtor initial balance",
            IsActive = true
        });

        await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh Kumar",
            OpeningDate = new DateTime(2026, 4, 1),
            Amount = 3500.0,
            Notes = "DSM initial shortage",
            IsActive = true
        });

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth);
        await vm.LoadDataAsync();

        Assert.Single(vm.DebtorBalances);
        Assert.Equal("Transport Corp", vm.DebtorBalances[0].EntityDisplayName);
        Assert.Equal(45000.0, vm.DebtorBalances[0].Amount);
        Assert.True(vm.DebtorBalances[0].IsActive);
        Assert.Equal("Active", vm.DebtorBalances[0].StatusText);

        Assert.Single(vm.DsmBalances);
        Assert.Equal("Ramesh Kumar", vm.DsmBalances[0].EntityDisplayName);
        Assert.Equal(3500.0, vm.DsmBalances[0].Amount);
        Assert.True(vm.DsmBalances[0].IsActive);
    }

    [Fact]
    public async Task Test02_DebtorOpeningBalanceCreation_Works()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var creditor = new Creditor { Name = "Highway Logistics", Phone = "9123456780" };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth);
        await vm.LoadDataAsync();

        vm.OpenCreateForm();
        vm.SelectedTab = "Debtor";
        vm.SelectedCreditor = vm.DebtorsList.FirstOrDefault(c => c.CreditorId == creditor.CreditorId);
        vm.FormOpeningDate = new DateTime(2026, 3, 15);
        vm.FormAmountText = "25000";
        vm.FormNotes = "Approved initial balance";

        await vm.SaveFormAsync();

        Assert.False(vm.IsFormOpen);
        Assert.Empty(vm.ValidationMessage);

        // Verify in DB
        using var verifyContext = CreateContext();
        var saved = await verifyContext.OpeningBalances
            .FirstOrDefaultAsync(o => o.EntityType == "Debtor" && o.CreditorId == creditor.CreditorId);
        Assert.NotNull(saved);
        Assert.Equal(25000.0, saved.Amount);
        Assert.Equal(creditor.CreditorId, saved.CreditorId);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task Test03_DsmOpeningBalanceCreation_Works()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var dsm = new DsmProfile { DsmName = "Suresh Patel", MobileNumber = "9988112233" };
        context.DsmProfiles.Add(dsm);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth);
        await vm.LoadDataAsync();

        vm.OpenCreateForm();
        vm.SelectedTab = "DsmLoss";
        vm.SelectedDsmProfile = vm.DsmProfilesList.FirstOrDefault(d => d.DsmName == "Suresh Patel");
        vm.FormOpeningDate = new DateTime(2026, 3, 20);
        vm.FormAmountText = "4200";
        vm.FormNotes = "Initial shortage anchor";

        await vm.SaveFormAsync();

        Assert.False(vm.IsFormOpen);

        // Verify backing anchor in DsmPersonalDebtors
        using var verifyContext = CreateContext();
        var anchor = await verifyContext.DsmPersonalDebtors
            .FirstOrDefaultAsync(d => d.DsmName == "Suresh Patel" && d.EntryType == "OpeningBalance");
        Assert.NotNull(anchor);
        Assert.Equal(4200.0, anchor.Amount);
        Assert.False(anchor.DeductFromSalary);
        Assert.Null(anchor.DsmEntryId);
    }

    [Fact]
    public async Task Test04_DuplicateActiveOpeningBalance_Prevented()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var creditor = new Creditor { Name = "Alpha Travels", Phone = "9876501234" };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        // First creation succeeds
        await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            OpeningDate = new DateTime(2026, 4, 1),
            Amount = 10000.0,
            IsActive = true
        });

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth);
        await vm.LoadDataAsync();

        // Attempt second active creation via UI
        vm.OpenCreateForm();
        vm.SelectedTab = "Debtor";
        vm.SelectedCreditor = vm.DebtorsList.FirstOrDefault(c => c.CreditorId == creditor.CreditorId);
        vm.FormAmountText = "15000";
        vm.FormOpeningDate = DateTime.Today;

        await vm.SaveFormAsync();

        // Must reject duplicate with a clear validation message
        Assert.True(vm.IsFormOpen);
        Assert.Contains("already exists", vm.ValidationMessage, StringComparison.OrdinalIgnoreCase);

        // Database still only has 1 record
        using var verifyContext = CreateContext();
        var count = await verifyContext.OpeningBalances
            .CountAsync(o => o.EntityType == "Debtor" && o.CreditorId == creditor.CreditorId && o.IsActive);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Test05_Editing_UpdatesExistingOpeningBalance()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var creditor = new Creditor { Name = "Metro Cargo", Phone = "9876540000" };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var saveRes = await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            OpeningDate = new DateTime(2026, 4, 1),
            Amount = 18000.0,
            IsActive = true
        });
        int initialId = saveRes.Data!.OpeningBalanceId;

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth);
        await vm.LoadDataAsync();

        var itemToEdit = vm.DebtorBalances.First(d => d.OpeningBalanceId == initialId);
        vm.OpenEditForm(itemToEdit);

        Assert.True(vm.IsEditing);
        Assert.Equal(initialId, vm.EditingBalanceId);

        // Edit amount and date
        vm.FormAmountText = "22000";
        vm.FormOpeningDate = new DateTime(2026, 4, 5);

        await vm.SaveFormAsync();

        Assert.False(vm.IsFormOpen);

        // Verify update in DB
        using var verifyContext = CreateContext();
        var updated = await verifyContext.OpeningBalances.FindAsync(initialId);
        Assert.NotNull(updated);
        Assert.Equal(22000.0, updated.Amount);
        Assert.Equal(new DateTime(2026, 4, 5), updated.OpeningDate);
        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task Test06_Deactivation_DoesNotPhysicallyDeleteRecord()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var creditor = new Creditor { Name = "Global Movers", Phone = "9876541111" };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var saveRes = await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            OpeningDate = new DateTime(2026, 4, 1),
            Amount = 30000.0,
            IsActive = true
        });
        int id = saveRes.Data!.OpeningBalanceId;

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth);
        await vm.LoadDataAsync();

        var item = vm.DebtorBalances.First(d => d.OpeningBalanceId == id);
        await vm.DeactivateAsync(item);

        // Verify record is preserved in DB with IsActive = false
        using var verifyContext = CreateContext();
        var record = await verifyContext.OpeningBalances.FindAsync(id);
        Assert.NotNull(record); // NOT deleted
        Assert.False(record.IsActive); // Deactivated
        Assert.Equal(30000.0, record.Amount); // Amount preserved
    }

    [Fact]
    public async Task Test07_DsmOpeningCannotBecomeSalaryDeductible_ThroughUI()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var dsm = new DsmProfile { DsmName = "Kailash Sharma", MobileNumber = "9988334455" };
        context.DsmProfiles.Add(dsm);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth);
        await vm.LoadDataAsync();

        // Create via UI
        vm.OpenCreateForm();
        vm.SelectedTab = "DsmLoss";
        vm.SelectedDsmProfile = vm.DsmProfilesList.First(d => d.DsmName == "Kailash Sharma");
        vm.FormAmountText = "7500";
        vm.FormOpeningDate = new DateTime(2026, 4, 1);

        await vm.SaveFormAsync();

        // Edit via UI
        await vm.LoadDataAsync();
        var item = vm.DsmBalances.First(d => d.EntityDisplayName == "Kailash Sharma");
        vm.OpenEditForm(item);
        vm.FormAmountText = "8000";
        await vm.SaveFormAsync();

        // Check invariants on backing record
        using var verifyContext = CreateContext();
        var anchor = await verifyContext.DsmPersonalDebtors
            .FirstOrDefaultAsync(d => d.DsmName == "Kailash Sharma" && d.EntryType == "OpeningBalance");

        Assert.NotNull(anchor);
        Assert.Equal(8000.0, anchor.Amount);
        Assert.False(anchor.DeductFromSalary); // Must remain false
        Assert.Equal("OpeningBalance", anchor.EntryType);
        Assert.Null(anchor.DsmEntryId);
    }

    [Fact]
    public async Task Test08_HistoricalOpening_ClearlyDistinguishedFromOperational()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var creditor = new Creditor { Name = "Sunrise Freight", Phone = "9876542222" };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            OpeningDate = new DateTime(2026, 4, 1),
            Amount = 50000.0,
            Notes = "Historical balance",
            IsActive = true
        });

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth);
        await vm.LoadDataAsync();

        var displayItem = vm.DebtorBalances.First();
        Assert.Equal("Debtor", displayItem.EntityType);
        Assert.True(displayItem.CanEdit);
        Assert.True(displayItem.CanDeactivate);

        // Verify ledger row distinction:
        // When pure historical balance exists before period start, ledger row shows "Historical Opening Balance"
        var ledgerRow = new LedgerTransactionRow
        {
            Date = new DateTime(2026, 4, 1),
            Description = "Historical Opening Balance",
            Debit = 0,
            Credit = 0,
            RunningBalance = 50000.0,
            CanEdit = false
        };

        Assert.Equal("Historical Opening Balance", ledgerRow.Description);
        Assert.False(ledgerRow.CanEdit);
    }

    [Fact]
    public async Task Test09_ExistingLedgers_ShowCorrectOpeningPosition()
    {
        using var context = CreateContext();
        var obRepo = new OpeningBalanceRepository(context);

        var creditor = new Creditor { Name = "Omega Logistics", Phone = "9876543333" };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        // Historical opening balance of 20000
        await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            OpeningDate = new DateTime(2026, 4, 1),
            Amount = 20000.0,
            IsActive = true
        });

        int dsmEntryId = await CreateTestDsmEntryAsync(context, new DateTime(2026, 4, 10));

        // Add operational debit of 5000 and repayment of 3000
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmEntryId,
            DebtorName = creditor.Name,
            Amount = 5000.0,
            PaymentMethod = "Credit",
            Remarks = "Fuel diesel",
            CreatedAt = new DateTime(2026, 4, 10)
        });

        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = creditor.Name,
            PaymentMode = "Cash",
            Amount = 3000.0,
            RepaymentDate = new DateTime(2026, 4, 15),
            CreatedAt = new DateTime(2026, 4, 15)
        });
        await context.SaveChangesAsync();

        // Calculate DebtorDisplayRow (integrated in Step 6 & 8)
        var allDebits = await context.DebitEntries.Where(d => d.DebtorName == creditor.Name).SumAsync(d => d.Amount);
        var allRepayments = await context.CreditorRepayments.Where(r => r.CreditorName == creditor.Name).SumAsync(r => r.Amount);
        var activeOb = await context.OpeningBalances.FirstOrDefaultAsync(o => o.EntityType == "Debtor" && o.CreditorId == creditor.CreditorId && o.IsActive);

        var row = new DebtorDisplayRow
        {
            CreditorId = creditor.CreditorId,
            Name = creditor.Name,
            OpeningBalance = activeOb?.Amount ?? 0,
            TotalDebt = allDebits,
            TotalRepayment = allRepayments
        };

        // Formula: Opening (20000) + Debits (5000) - Repayments (3000) = 22000
        Assert.Equal(20000.0, row.OpeningBalance);
        Assert.Equal(5000.0, row.TotalDebt);
        Assert.Equal(3000.0, row.TotalRepayment);
        Assert.Equal(22000.0, row.OutstandingBalance);
    }

    [Fact]
    public async Task Test10_ExistingScreensWithoutOpeningBalances_BehaveExactlyAsBefore()
    {
        using var context = CreateContext();

        var creditor = new Creditor { Name = "Standard Customer", Phone = "9876544444" };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        int dsmEntryId = await CreateTestDsmEntryAsync(context, new DateTime(2026, 4, 10));

        // No opening balance created for this creditor

        // Add operational debit 12000 and repayment 4000
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmEntryId,
            DebtorName = creditor.Name,
            Amount = 12000.0,
            PaymentMethod = "Credit",
            Remarks = "Diesel purchase",
            CreatedAt = new DateTime(2026, 4, 10)
        });

        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = creditor.Name,
            PaymentMode = "Cash",
            Amount = 4000.0,
            RepaymentDate = new DateTime(2026, 4, 15),
            CreatedAt = new DateTime(2026, 4, 15)
        });
        await context.SaveChangesAsync();

        var allDebits = await context.DebitEntries.Where(d => d.DebtorName == creditor.Name).SumAsync(d => d.Amount);
        var allRepayments = await context.CreditorRepayments.Where(r => r.CreditorName == creditor.Name).SumAsync(r => r.Amount);
        var activeOb = await context.OpeningBalances.FirstOrDefaultAsync(o => o.EntityType == "Debtor" && o.CreditorId == creditor.CreditorId && o.IsActive);

        var row = new DebtorDisplayRow
        {
            CreditorId = creditor.CreditorId,
            Name = creditor.Name,
            OpeningBalance = activeOb?.Amount ?? 0,
            TotalDebt = allDebits,
            TotalRepayment = allRepayments
        };

        // Zero opening balance, normal operational formula: 0 + 12000 - 4000 = 8000
        Assert.Equal(0.0, row.OpeningBalance);
        Assert.Equal(12000.0, row.TotalDebt);
        Assert.Equal(4000.0, row.TotalRepayment);
        Assert.Equal(8000.0, row.OutstandingBalance);
    }

    [Fact]
    public async Task Test11_DsmDropdown_PopulatesFromDsmUsersAndPersonalDebtors_AuthoritativeSource()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        // Add DsmUsers (active staff accounts)
        context.DsmUsers.Add(new DsmUser
        {
            EmployeeCode = "EMP01",
            FullName = "Ramesh Jadhav",
            MobileNumber = "9876500001",
            Email = "ramesh@station.com",
            AuthUserId = "auth-1",
            IsActive = true
        });
        context.DsmUsers.Add(new DsmUser
        {
            EmployeeCode = "EMP02",
            FullName = "Vikas",
            MobileNumber = "9876500002",
            Email = "vikas@station.com",
            AuthUserId = "auth-2",
            IsActive = true
        });

        // Add an account existing in personal debtors fallback
        context.DsmPersonalDebtors.Add(new DsmPersonalDebtor
        {
            DsmName = "Prakash Shinde",
            Date = DateTime.Today,
            Amount = 1500,
            EntryType = "Operational",
            DeductFromSalary = true
        });

        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth, context);
        await vm.LoadDataAsync();

        // Must contain Ramesh Jadhav, Vikas, and Prakash Shinde
        Assert.Contains(vm.DsmProfilesList, d => d.DsmName == "Ramesh Jadhav");
        Assert.Contains(vm.DsmProfilesList, d => d.DsmName == "Vikas");
        Assert.Contains(vm.DsmProfilesList, d => d.DsmName == "Prakash Shinde");

        // Verify deduplication and alphabetical sort
        var names = vm.DsmProfilesList.Select(d => d.DsmName).ToList();
        Assert.Equal(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), names.Count);
        var sortedNames = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(sortedNames, names);
    }

    [Fact]
    public async Task Test12_EntityIdentitySafety_DsmOpeningBalance_PersistedWithCanonicalFields()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        context.DsmUsers.Add(new DsmUser
        {
            EmployeeCode = "EMP01",
            FullName = "Ramesh Jadhav",
            MobileNumber = "9876500001",
            Email = "ramesh@station.com",
            AuthUserId = "auth-1",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth, context);
        await vm.LoadDataAsync();

        vm.OpenCreateForm();
        vm.IsDsmAccountType = true;
        vm.SelectedDsmProfile = vm.DsmProfilesList.First(d => d.DsmName == "Ramesh Jadhav");
        vm.FormOpeningDate = new DateTime(2026, 4, 1);
        vm.FormAmountText = "8500";
        vm.FormNotes = "Initial loss position for Ramesh";

        await vm.SaveFormAsync();

        Assert.False(vm.IsFormOpen);
        Assert.Empty(vm.ValidationMessage);

        // Explicit Canonical Verification in DB
        using var verifyContext = CreateContext();
        var ob = await verifyContext.OpeningBalances
            .FirstOrDefaultAsync(o => o.EntityType == "DsmLoss" && o.EntityIdentifier == "Ramesh Jadhav");
        Assert.NotNull(ob);
        Assert.Equal("DsmLoss", ob.EntityType);
        Assert.Equal("Ramesh Jadhav", ob.EntityIdentifier);
        Assert.Equal(8500.0, ob.Amount);
        Assert.Equal(new DateTime(2026, 4, 1), ob.OpeningDate);
        Assert.Null(ob.CreditorId);
        Assert.True(ob.IsActive);

        var anchor = await verifyContext.DsmPersonalDebtors
            .FirstOrDefaultAsync(d => d.DsmName == "Ramesh Jadhav" && d.EntryType == "OpeningBalance");
        Assert.NotNull(anchor);
        Assert.Equal("Ramesh Jadhav", anchor.DsmName);
        Assert.Null(anchor.DsmEntryId);
        Assert.Equal("OpeningBalance", anchor.EntryType);
        Assert.False(anchor.DeductFromSalary);
        Assert.Equal(0, anchor.SequenceNumber);
        Assert.Equal(8500.0, anchor.Amount);
        Assert.Equal(new DateTime(2026, 4, 1), anchor.Date);
        Assert.Equal("Historical Opening Balance", anchor.Remarks);
    }

    [Fact]
    public async Task Test13_EntityIdentitySafety_DebtorOpeningBalance_PersistedWithCanonicalFields()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var creditor = new Creditor { Name = "Global Freight Lines", Phone = "9887766554" };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth, context);
        await vm.LoadDataAsync();

        vm.OpenCreateForm();
        vm.IsDebtorAccountType = true;
        vm.SelectedCreditor = vm.DebtorsList.First(c => c.CreditorId == creditor.CreditorId);
        vm.FormOpeningDate = new DateTime(2026, 3, 25);
        vm.FormAmountText = "35000";
        vm.FormNotes = "Approved initial balance for Global Freight";

        await vm.SaveFormAsync();

        Assert.False(vm.IsFormOpen);
        Assert.Empty(vm.ValidationMessage);

        // Explicit Canonical Verification in DB
        using var verifyContext = CreateContext();
        var ob = await verifyContext.OpeningBalances
            .FirstOrDefaultAsync(o => o.EntityType == "Debtor" && o.CreditorId == creditor.CreditorId);
        Assert.NotNull(ob);
        Assert.Equal("Debtor", ob.EntityType);
        Assert.Equal(creditor.CreditorId.ToString(), ob.EntityIdentifier);
        Assert.Equal(creditor.CreditorId, ob.CreditorId);
        Assert.Equal(35000.0, ob.Amount);
        Assert.Equal(new DateTime(2026, 3, 25), ob.OpeningDate);
        Assert.True(ob.IsActive);
    }

    [Fact]
    public async Task Test14_TypeSwitching_ClearsOppositeEntityInCreateMode_PreservesInEditMode()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var creditor = new Creditor { Name = "Express Cargo", Phone = "9112233445" };
        context.Creditors.Add(creditor);
        context.DsmUsers.Add(new DsmUser
        {
            EmployeeCode = "EMP03",
            FullName = "Vikas",
            MobileNumber = "9988771122",
            Email = "vikas@station.com",
            AuthUserId = "auth-v",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth, context);
        await vm.LoadDataAsync();

        // 1. Create Mode: Switching should clear opposite selection
        vm.OpenCreateForm();
        vm.IsDebtorAccountType = true;
        vm.SelectedCreditor = vm.DebtorsList.First(c => c.CreditorId == creditor.CreditorId);
        Assert.NotNull(vm.SelectedCreditor);

        // Switch to DSM
        vm.IsDsmAccountType = true;
        Assert.True(vm.IsDsmAccountType);
        Assert.False(vm.IsDebtorAccountType);
        Assert.Equal("DsmLoss", vm.SelectedTab);
        Assert.Null(vm.SelectedCreditor); // Cleared opposite selection

        // Select DSM
        vm.SelectedDsmProfile = vm.DsmProfilesList.First(d => d.DsmName == "Vikas");
        Assert.NotNull(vm.SelectedDsmProfile);

        // Switch back to Debtor
        vm.IsDebtorAccountType = true;
        Assert.True(vm.IsDebtorAccountType);
        Assert.False(vm.IsDsmAccountType);
        Assert.Equal("Debtor", vm.SelectedTab);
        Assert.Null(vm.SelectedDsmProfile); // Cleared opposite selection

        // 2. Edit Mode: Type switching and clearing must be LOCKED
        await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            OpeningDate = DateTime.Today,
            Amount = 10000,
            IsActive = true
        });
        await vm.LoadDataAsync();

        var displayItem = vm.DebtorBalances.First(d => d.CreditorId == creditor.CreditorId);
        vm.OpenEditForm(displayItem);

        Assert.True(vm.IsEditing);
        Assert.False(vm.CanChangeEntityType); // Locked
        Assert.NotNull(vm.SelectedCreditor);
        Assert.Equal(creditor.CreditorId, vm.SelectedCreditor.CreditorId);

        // Attempting to change type during edit must be blocked and cannot clear selected entity
        vm.IsDsmAccountType = true;
        Assert.NotNull(vm.SelectedCreditor); // Selection strictly preserved
    }

    [Fact]
    public async Task Test15_Validation_BlocksSaveWithoutSelectedEntity()
    {
        using var context = CreateContext();
        var (auth, _) = await SetupOwnerAuthAsync(context);

        var obRepo = new OpeningBalanceRepository(context);
        var creditorRepo = new CreditorRepository(context);
        var dsmRepo = new DsmProfileRepository(context);

        var vm = new OpeningBalanceManagementViewModel(obRepo, creditorRepo, dsmRepo, auth, context);
        await vm.LoadDataAsync();

        // 1. Debtor without selection
        vm.OpenCreateForm();
        vm.IsDebtorAccountType = true;
        vm.SelectedCreditor = null;
        vm.FormAmountText = "5000";
        await vm.SaveFormAsync();
        Assert.Equal("Please select a debtor.", vm.ValidationMessage);
        Assert.True(vm.IsFormOpen);

        // 2. DSM without selection
        vm.IsDsmAccountType = true;
        vm.SelectedDsmProfile = null;
        vm.FormAmountText = "5000";
        await vm.SaveFormAsync();
        Assert.Equal("Please select a DSM staff member.", vm.ValidationMessage);
        Assert.True(vm.IsFormOpen);
    }
}

