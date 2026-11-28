using System;
using System.Collections.Generic;
using System.Globalization;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;

namespace FuelPro.Sync;

/// <summary>
/// Semantic classification for DateTime properties synchronized between local SQLite and Supabase PostgreSQL.
/// </summary>
public enum SyncTimestampSemantics
{
    /// <summary>
    /// Calendar business date (e.g. ShiftDate, LogDate, RepaymentDate).
    /// Must preserve exact YYYY-MM-DD operational day without timezone date shifting.
    /// </summary>
    BusinessDate,

    /// <summary>
    /// Instant initialized and stored locally as local wall-clock time (DateTime.Now / SQLite Unspecified).
    /// Serialized to UTC ISO-8601 on push and converted back to local time on pull.
    /// </summary>
    LocalInstant,

    /// <summary>
    /// Instant initialized and stored natively in UTC (DateTime.UtcNow).
    /// Serialized to UTC ISO-8601 on push without double-conversion and preserved as UTC on pull.
    /// </summary>
    UtcInstant
}

/// <summary>
/// Centralizes explicit, column-verified UTC serialization and culture-invariant deserialization
/// for synchronization payloads, watermarks, and SQLite/PostgreSQL timestamp interop.
/// </summary>
public static class SyncTimestampHelper
{
    public const string UtcFormat = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

    private static readonly IReadOnlyDictionary<(Type ModelType, string PropertyName), SyncTimestampSemantics> ExplicitColumnMap =
        new Dictionary<(Type, string), SyncTimestampSemantics>
        {
            // ── Calendar Business Dates (NEVER shift across dates via timezone conversion) ──
            [(typeof(AgsDailySummary), nameof(AgsDailySummary.SummaryDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(AgsShiftImport), nameof(AgsShiftImport.ImportDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(CreditorRepayment), nameof(CreditorRepayment.RepaymentDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(DayLock), nameof(DayLock.LockDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(DsmAttendance), nameof(DsmAttendance.AttendanceDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(DsmPersonalDebtor), nameof(DsmPersonalDebtor.Date))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(DsmPersonalDebtorRepayment), nameof(DsmPersonalDebtorRepayment.Date))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(DsmProfile), nameof(DsmProfile.JoiningDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(DsmQrPaymentEntry), nameof(DsmQrPaymentEntry.Date))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(DsmSalaryHistory), nameof(DsmSalaryHistory.ChangeDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(DsmSalaryPayment), nameof(DsmSalaryPayment.PaymentDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(FuelProfitMargin), nameof(FuelProfitMargin.EffectiveDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(FuelTanker), nameof(FuelTanker.TankerDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(KhandharePetroleumEntry), nameof(KhandharePetroleumEntry.Date))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(OilDefDailyLog), nameof(OilDefDailyLog.LogDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(OilDefPurchase), nameof(OilDefPurchase.PurchaseDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(OpeningBalance), nameof(OpeningBalance.OpeningDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(OuterExpense), nameof(OuterExpense.ExpenseDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(PettyCashTransaction), nameof(PettyCashTransaction.Date))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(PumpExpense), nameof(PumpExpense.ExpenseDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(Shift), nameof(Shift.ShiftDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(ShiftFuelRate), nameof(ShiftFuelRate.ShiftDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(ShiftOtherCash), nameof(ShiftOtherCash.ShiftDate))] = SyncTimestampSemantics.BusinessDate,
            [(typeof(TankDailyStock), nameof(TankDailyStock.Date))] = SyncTimestampSemantics.BusinessDate,

            // ── Native UTC Instants (DateTime.UtcNow) ─────────────────────────────────────────
            [(typeof(StationDevice), nameof(StationDevice.PairedAt))] = SyncTimestampSemantics.UtcInstant,
            [(typeof(StationDevice), nameof(StationDevice.LastSeen))] = SyncTimestampSemantics.UtcInstant,
            [(typeof(StationPairingToken), nameof(StationPairingToken.CreatedAt))] = SyncTimestampSemantics.UtcInstant,
            [(typeof(StationPairingToken), nameof(StationPairingToken.ExpiresAt))] = SyncTimestampSemantics.UtcInstant,

            // ── Local-Stored Audit / Event Instants (DateTime.Now -> SQLite Unspecified) ─────
            [(typeof(AgsDailySummary), nameof(AgsDailySummary.LastUpdatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(AgsShiftImport), nameof(AgsShiftImport.ImportedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(AppFeatureSetting), nameof(AppFeatureSetting.UpdatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(AuditLog), nameof(AuditLog.ModifiedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(CollectionTypeMaster), nameof(CollectionTypeMaster.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(Creditor), nameof(Creditor.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(CreditorRepayment), nameof(CreditorRepayment.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DayLock), nameof(DayLock.LockedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DayLock), nameof(DayLock.UnlockedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DebitEntry), nameof(DebitEntry.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DebtorVehicle), nameof(DebtorVehicle.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmApprovalAudit), nameof(DsmApprovalAudit.ApprovedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmAttendance), nameof(DsmAttendance.ClockInTime))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmAttendance), nameof(DsmAttendance.ClockOutTime))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmDevice), nameof(DsmDevice.LastLogin))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmDevice), nameof(DsmDevice.LastSeen))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmEntry), nameof(DsmEntry.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmEntry), nameof(DsmEntry.UpdatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmPersonalDebtor), nameof(DsmPersonalDebtor.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmPersonalDebtorRepayment), nameof(DsmPersonalDebtorRepayment.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmPumpAssignment), nameof(DsmPumpAssignment.AssignedDate))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmPumpAssignment), nameof(DsmPumpAssignment.CompletedDate))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmQrPaymentEntry), nameof(DsmQrPaymentEntry.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmSalaryPayment), nameof(DsmSalaryPayment.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmSalaryPayment), nameof(DsmSalaryPayment.UpdatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(DsmUser), nameof(DsmUser.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(ExpenseCategory), nameof(ExpenseCategory.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(FuelTanker), nameof(FuelTanker.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(FuelTanker), nameof(FuelTanker.UpdatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(KhandharePetroleumEntry), nameof(KhandharePetroleumEntry.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(OpeningBalance), nameof(OpeningBalance.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(OpeningBalance), nameof(OpeningBalance.UpdatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(OuterExpense), nameof(OuterExpense.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(PettyCashTransaction), nameof(PettyCashTransaction.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(PumpExpense), nameof(PumpExpense.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(PumpExpenseCategoryItem), nameof(PumpExpenseCategoryItem.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(PumpMapping), nameof(PumpMapping.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(Setting), nameof(Setting.LastUpdated))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(Shift), nameof(Shift.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(ShiftOtherCash), nameof(ShiftOtherCash.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(SoftwareVersionHistory), nameof(SoftwareVersionHistory.DeployedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(StationLayoutPreset), nameof(StationLayoutPreset.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(StationLayoutPreset), nameof(StationLayoutPreset.UpdatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(SyncChangeLog), nameof(SyncChangeLog.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(SyncChangeLog), nameof(SyncChangeLog.SyncedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(TankDailyStock), nameof(TankDailyStock.LastUpdated))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(TankDefinition), nameof(TankDefinition.CreatedAt))] = SyncTimestampSemantics.LocalInstant,
            [(typeof(User), nameof(User.CreatedAt))] = SyncTimestampSemantics.LocalInstant
        };

    public static bool IsExplicitlyClassified(Type entityType, string propertyName)
        => ExplicitColumnMap.ContainsKey((entityType, propertyName));

    public static SyncTimestampSemantics Classify(Type entityType, string propertyName)
    {
        if (ExplicitColumnMap.TryGetValue((entityType, propertyName), out var semantics))
        {
            return semantics;
        }

        // Unmapped audit timestamps default conservatively to LocalInstant (never naive string parsing)
        return SyncTimestampSemantics.LocalInstant;
    }

    /// <summary>
    /// Converts a DateTime to an explicit UTC DateTime using <paramref name="assumedLocalTimeZone"/>
    /// when Kind is Unspecified (or Local when converting cross-TZ). Never double-shifts DateTimeKind.Utc.
    /// </summary>
    public static DateTime ToUtcDateTime(DateTime value, SyncTimestampSemantics semantics = SyncTimestampSemantics.LocalInstant, TimeZoneInfo? assumedLocalTimeZone = null)
    {
        if (value == DateTime.MinValue) return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        if (value == DateTime.MaxValue) return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);

        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Local)
        {
            return value.ToUniversalTime();
        }

        // DateTimeKind.Unspecified
        if (semantics == SyncTimestampSemantics.UtcInstant)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        var localTz = assumedLocalTimeZone ?? TimeZoneInfo.Local;
        var unspecified = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, localTz);
    }

    /// <summary>
    /// Formats an entity DateTime property for outbound Supabase JSON payloads.
    /// Business dates retain their calendar date (midnight without UTC shift).
    /// Audit and sync instants are converted cleanly to UTC ISO-8601 ('Z').
    /// </summary>
    public static string FormatForPush(Type entityType, string propertyName, DateTime value, TimeZoneInfo? assumedLocalTimeZone = null)
    {
        var semantics = Classify(entityType, propertyName);
        if (semantics == SyncTimestampSemantics.BusinessDate)
        {
            return value.Date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        var utc = ToUtcDateTime(value, semantics, assumedLocalTimeZone);
        return utc.ToString(UtcFormat, CultureInfo.InvariantCulture);
    }

    public static string FormatUtcIso(DateTime utcOrLocalValue, TimeZoneInfo? assumedLocalTimeZone = null)
    {
        var utc = ToUtcDateTime(utcOrLocalValue, SyncTimestampSemantics.LocalInstant, assumedLocalTimeZone);
        return utc.ToString(UtcFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses a remote timestamp value (from Supabase JSON) in an invariant, culture-independent manner
    /// according to the target property's semantic classification.
    /// </summary>
    public static DateTime ParseForPull(Type entityType, string propertyName, object rawValue, TimeZoneInfo? localTimeZone = null)
    {
        var semantics = Classify(entityType, propertyName);
        var localTz = localTimeZone ?? TimeZoneInfo.Local;

        if (semantics == SyncTimestampSemantics.BusinessDate)
        {
            return ParseBusinessDate(rawValue, localTz);
        }

        if (rawValue is DateTime dt)
        {
            if (semantics == SyncTimestampSemantics.UtcInstant)
            {
                return ToUtcDateTime(dt, SyncTimestampSemantics.UtcInstant, localTz);
            }

            return ConvertInstantForLocalStorage(dt, localTz);
        }

        if (rawValue is DateTimeOffset dto)
        {
            if (semantics == SyncTimestampSemantics.UtcInstant)
            {
                return dto.UtcDateTime;
            }

            var convertedLocal = TimeZoneInfo.ConvertTimeFromUtc(dto.UtcDateTime, localTz);
            return DateTime.SpecifyKind(convertedLocal, DateTimeKind.Local);
        }

        var str = rawValue?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(str))
        {
            throw new FormatException($"Cannot parse empty timestamp for {entityType.Name}.{propertyName}.");
        }

        if (HasExplicitOffsetOrUtcSuffix(str))
        {
            if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDto))
            {
                if (semantics == SyncTimestampSemantics.UtcInstant)
                {
                    return parsedDto.UtcDateTime;
                }

                var convertedLocal = TimeZoneInfo.ConvertTimeFromUtc(parsedDto.UtcDateTime, localTz);
                return DateTime.SpecifyKind(convertedLocal, DateTimeKind.Local);
            }
        }

        // Legacy/offsetless string on wire: parse strictly with InvariantCulture
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedNoOffset))
        {
            if (semantics == SyncTimestampSemantics.UtcInstant)
            {
                return DateTime.SpecifyKind(parsedNoOffset, DateTimeKind.Utc);
            }

            // Preserve already-local wall-clock value without double conversion
            return DateTime.SpecifyKind(parsedNoOffset, DateTimeKind.Unspecified);
        }

        throw new FormatException($"Unrecognized timestamp format '{str}' for {entityType.Name}.{propertyName}.");
    }

    /// <summary>
    /// Parses a calendar business date without unintended cross-day timezone drift.
    /// Converts offset-shifted timestamps (e.g. legacy midnight+05:30 stored as 18:30Z) back to local date,
    /// while preserving exact YYYY-MM-DD for pure dates and midnight UTC strings (00:00:00Z).
    /// </summary>
    public static DateTime ParseBusinessDate(object rawValue, TimeZoneInfo? localTimeZone = null)
    {
        var localTz = localTimeZone ?? TimeZoneInfo.Local;

        if (rawValue is DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Utc)
            {
                if (dt.TimeOfDay == TimeSpan.Zero)
                    return DateTime.SpecifyKind(dt.Date, DateTimeKind.Unspecified);

                return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(dt, localTz).Date, DateTimeKind.Unspecified);
            }

            if (dt.Kind == DateTimeKind.Local)
            {
                return DateTime.SpecifyKind(dt.Date, DateTimeKind.Unspecified);
            }

            return DateTime.SpecifyKind(dt.Date, DateTimeKind.Unspecified);
        }

        if (rawValue is DateTimeOffset dtoVal)
        {
            if (dtoVal.Offset == TimeSpan.Zero && dtoVal.TimeOfDay == TimeSpan.Zero)
            {
                return DateTime.SpecifyKind(dtoVal.Date, DateTimeKind.Unspecified);
            }
            return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(dtoVal.UtcDateTime, localTz).Date, DateTimeKind.Unspecified);
        }

        var str = rawValue?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(str))
        {
            throw new FormatException("Cannot parse empty business date.");
        }

        if (HasExplicitOffsetOrUtcSuffix(str) &&
            DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            if (dto.Offset == TimeSpan.Zero && dto.TimeOfDay == TimeSpan.Zero)
            {
                return DateTime.SpecifyKind(dto.Date, DateTimeKind.Unspecified);
            }

            var localDate = TimeZoneInfo.ConvertTimeFromUtc(dto.UtcDateTime, localTz).Date;
            return DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        }

        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDate))
        {
            return DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Unspecified);
        }

        throw new FormatException($"Unrecognized business date format '{str}'.");
    }

    /// <summary>
    /// Parses a remote updated_at/cursor timestamp strictly in UTC, treating offsetless strings as UTC.
    /// </summary>
    public static bool TryParseRemoteUtcCursor(object? rawValue, out string formattedUtc, out DateTime utcDateTime)
    {
        formattedUtc = string.Empty;
        utcDateTime = default;
        if (rawValue == null) return false;

        if (rawValue is DateTime dt)
        {
            utcDateTime = dt.Kind == DateTimeKind.Local
                ? dt.ToUniversalTime()
                : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            formattedUtc = utcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture);
            return true;
        }

        if (rawValue is DateTimeOffset dto)
        {
            utcDateTime = dto.UtcDateTime;
            formattedUtc = utcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture);
            return true;
        }

        var str = rawValue.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(str)) return false;

        if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDto))
        {
            utcDateTime = parsedDto.UtcDateTime;
            formattedUtc = utcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture);
            return true;
        }

        if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDt))
        {
            utcDateTime = parsedDt.Kind == DateTimeKind.Utc ? parsedDt : parsedDt.ToUniversalTime();
            formattedUtc = utcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static DateTime ConvertInstantForLocalStorage(DateTime dt, TimeZoneInfo localTz)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(dt, localTz);
            return DateTime.SpecifyKind(local, DateTimeKind.Local);
        }

        if (dt.Kind == DateTimeKind.Local)
        {
            if (localTz.Equals(TimeZoneInfo.Local))
                return dt;

            var utc = dt.ToUniversalTime();
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, localTz);
            return DateTime.SpecifyKind(local, DateTimeKind.Local);
        }

        return dt;
    }

    private static bool HasExplicitOffsetOrUtcSuffix(string value)
    {
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
            return true;

        int tIndex = value.IndexOfAny(new[] { 'T', ' ' });
        if (tIndex < 0) return false;

        string timePart = value.Substring(tIndex + 1);
        return timePart.Contains('+') || timePart.Contains('-');
    }
}
