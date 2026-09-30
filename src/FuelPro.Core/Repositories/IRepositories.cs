using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;

namespace FuelPro.Core.Repositories;

public interface ISettingsRepository
{
    Task<Result<Setting>> GetSettingsAsync();
    Task<Result> SaveSettingsAsync(Setting settings);
}

public interface IUserRepository
{
    Task<Result<User>> GetByUsernameAsync(string username);
    Task<Result<List<User>>> GetAllUsersAsync();
    Task<Result<User>> CreateUserAsync(User user);
    Task<Result> UpdateUserAsync(User user);
    Task<Result> DeleteUserAsync(int userId);
}

public interface IShiftRepository
{
    Task<Result<Shift>> GetOrCreateShiftAsync(DateTime date, string shiftType);
    Task<Result<Shift>> GetShiftAsync(DateTime date, string shiftType);
    Task<Result<Shift>> GetShiftByIdAsync(int shiftId);
    Task<Result<List<Shift>>> GetShiftsForDateAsync(DateTime date);
    Task<Result> LockShiftAsync(int shiftId);
    Task<Result<bool>> IsShiftLockedAsync(int shiftId);
    Task<Result<List<Shift>>> GetShiftsByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Result> UpdateShiftAsync(Shift shift);
}

public interface IDsmEntryRepository
{
    Task<Result<DsmEntry>> GetByIdAsync(int dsmEntryId);
    Task<Result<DsmEntry>> GetFullEntryAsync(int dsmEntryId);
    Task<Result<List<DsmEntry>>> GetEntriesForShiftAsync(int shiftId);
    Task<Result<DsmEntry>> SaveEntryAsync(DsmEntry entry);
    Task<Result> DeleteEntryAsync(int dsmEntryId);
    Task<Result<bool>> IsDuplicateAsync(int shiftId, int pumpId, string dsmName, int? excludeId = null);
    Task<Result<List<string>>> GetDistinctDsmNamesAsync();
    Task<Result<List<DsmEntry>>> GetEntriesForDsmAndMonthAsync(string dsmName, int year, int month);
    Task<Result<List<DsmEntry>>> GetEntriesForMonthAsync(int year, int month);
    Task<Result<List<DsmEntry>>> GetEntriesForDateRangeAsync(DateTime startDate, DateTime endDate);
}

public interface INozzleReadingRepository
{
    Task<Result<List<NozzleReading>>> GetByDsmEntryIdAsync(int dsmEntryId);
    Task<Result> SaveReadingsAsync(int dsmEntryId, List<NozzleReading> readings);
    Task<Result<Dictionary<int, double>>> GetPreviousShiftClosingsAsync(DateTime date, string shiftType, int pumpId, int? currentDsmEntryId = null);
}

public interface IPaymentRepository
{
    Task<Result<PaymentCollection>> GetByDsmEntryIdAsync(int dsmEntryId);
    Task<Result> SavePaymentAsync(PaymentCollection payment);
}

public interface IDebitEntryRepository
{
    Task<Result<List<DebitEntry>>> GetByDsmEntryIdAsync(int dsmEntryId);
    Task<Result> SaveDebitsAsync(int dsmEntryId, List<DebitEntry> debits);
}

public interface ITestingEntryRepository
{
    Task<Result<List<TestingEntry>>> GetByDsmEntryIdAsync(int dsmEntryId);
    Task<Result> SaveTestingEntriesAsync(int dsmEntryId, List<TestingEntry> entries);
}

public interface IExpenseRepository
{
    Task<Result<List<Expense>>> GetByDsmEntryIdAsync(int dsmEntryId);
    Task<Result<List<Expense>>> GetByShiftIdAsync(int shiftId);
    Task<Result> SaveExpensesAsync(int dsmEntryId, List<Expense> expenses);
    Task<Result<Expense>> AddShiftExpenseAsync(int shiftId, string description, double amount);
    Task<Result> DeleteExpenseAsync(int expenseId);
    Task<Result<List<Expense>>> GetExpensesByShiftIdsAsync(List<int> shiftIds);
}

public interface ICashDenominationRepository
{
    Task<Result<List<CashDenomination>>> GetByDsmEntryIdAsync(int dsmEntryId);
    Task<Result> SaveCashDenominationsAsync(int dsmEntryId, List<CashDenomination> denominations);
}

public interface IShiftOtherCashRepository
{
    Task<Result<List<ShiftOtherCash>>> GetByShiftAsync(DateTime date, string shiftNumber);
    Task<Result<ShiftOtherCash>> AddAsync(ShiftOtherCash entry);
    Task<Result> DeleteAsync(int shiftOtherCashId);
    Task<Result<List<ShiftOtherCash>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
}

public interface IShiftFuelRateRepository
{
    Task<Result<List<ShiftFuelRate>>> GetByShiftAsync(DateTime date, string shiftNumber);
    Task<Result> SaveRateAsync(ShiftFuelRate rate);
}

public interface ICreditorRepository
{
    Task<Result<List<Creditor>>> GetAllAsync();
    Task<Result<List<Creditor>>> GetAllActiveAsync();
    Task<Result<List<Creditor>>> GetAllActiveWithVehiclesAsync();
    Task<Result<Creditor>> AddAsync(Creditor creditor);
    Task<Result> UpdateAsync(Creditor creditor);
    Task<Result> SoftDeleteAsync(int creditorId);
}

public interface IDebtorVehicleRepository
{
    Task<Result<List<DebtorVehicle>>> GetByCreditorIdAsync(int creditorId);
    Task<Result<DebtorVehicle>> AddAsync(DebtorVehicle vehicle);
    Task<Result> DeleteAsync(int debtorVehicleId);
    Task<Result> UpdateVehicleNumberAsync(int vehicleId, string vehicleNumber);
}

public interface IDsmProfileRepository
{
    Task<Result<List<DsmProfile>>> GetAllAsync();
    Task<Result<DsmProfile>> AddAsync(DsmProfile profile);
    Task<Result> UpdateAsync(DsmProfile profile);
    Task<Result> DeleteAsync(int id);
}

public interface ICreditorRepaymentRepository
{
    Task<Result<List<CreditorRepayment>>> GetByDateAsync(DateTime date);
    Task<Result<List<CreditorRepayment>>> GetByMonthAsync(int year, int month);
    Task<Result<CreditorRepayment>> AddAsync(CreditorRepayment repayment);
    Task<Result> DeleteAsync(int id);
    Task<Result<List<CreditorRepayment>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
}

public interface IAgsImportRepository
{
    Task<Result<AgsShiftImport>> SaveShiftImportAsync(AgsShiftImport import);
    Task<Result<AgsShiftImport?>> GetActiveShiftImportAsync(DateTime date, string shiftType);
    Task<Result<List<AgsShiftImport>>> GetShiftsForDateAsync(DateTime date);
    Task<Result> SoftDeleteShiftImportAsync(int agsShiftImportId);
    Task<Result<AgsDailySummary>> SaveDailySummaryAsync(AgsDailySummary summary);
    Task<Result<AgsDailySummary?>> GetDailySummaryAsync(DateTime date);
    Task<Result<List<AgsShiftImport>>> GetImportHistoryAsync(int count = 30);
    Task<Result<List<AgsShiftImport>>> GetActiveImportsFromDateAsync(DateTime startDate);
}

public interface IPumpExpenseRepository
{
    Task<Result<List<PumpExpense>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Result<PumpExpense>> GetByDateAsync(DateTime date);
    Task<Result<PumpExpense>> GetByIdAsync(int id);
    Task<Result<PumpExpense>> AddOrUpdateAsync(PumpExpense expense);
    Task<Result> DeleteAsync(int id);
}

public interface IDsmPersonalDebtorRepository
{
    Task<Result<List<DsmPersonalDebtor>>> GetByDsmEntryIdAsync(int dsmEntryId);
    Task<Result> SavePersonalDebtorsAsync(int dsmEntryId, List<DsmPersonalDebtor> personalDebtors);
    Task<Result<List<DsmPersonalDebtorRepayment>>> GetRepaymentsByDateAsync(DateTime date);
    Task<Result<List<DsmPersonalDebtorRepayment>>> GetRepaymentsByDateRangeAsync(DateTime startDate, DateTime endDate);
}

public interface IFuelTankerRepository
{
    Task<Result<List<FuelTanker>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Result<FuelTanker>> AddAsync(FuelTanker tanker);
    Task<Result<FuelTanker>> UpdateAsync(FuelTanker tanker);
    Task<Result> DeleteAsync(int fuelTankerId);
}

public interface ITankDailyStockRepository
{
    Task<Result<List<TankDailyStock>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate);
    Task<Result<List<TankDailyStock>>> GetByDateAsync(DateTime date);
    Task<Result<TankDailyStock>> UpsertAsync(TankDailyStock stock);
}

public interface IOpeningBalanceRepository
{
    Task<Result<OpeningBalance?>> GetActiveByEntityAsync(string entityType, string entityIdentifier);
    Task<Result<OpeningBalance?>> GetByIdAsync(int openingBalanceId);
    Task<Result<List<OpeningBalance>>> GetAllActiveAsync();
    Task<Result<List<OpeningBalance>>> GetAllByEntityTypeAsync(string entityType);
    Task<Result<OpeningBalance>> SaveOpeningBalanceAsync(OpeningBalance openingBalance);
    Task<Result> DeactivateOpeningBalanceAsync(int openingBalanceId);
    Task<Result<DsmPersonalDebtor?>> GetDsmAnchorAsync(string dsmName);
}
