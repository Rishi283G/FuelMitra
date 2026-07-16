using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

// ── DTOs returned by the inspection service ─────────────────────────────────

public class DsmEntryDuplicateGroup
{
    public DsmEntry OriginalRecord { get; set; } = null!;
    public List<DsmEntry> DuplicateRecords { get; set; } = new();
    public int DuplicateCount => DuplicateRecords.Count;
    public string Reason { get; set; } = string.Empty;
    public string BusinessKey => $"Shift {OriginalRecord.ShiftId} | DSM: {OriginalRecord.DsmName} | Pump: {OriginalRecord.PumpId}";
}

public class DebitEntryDuplicateGroup
{
    public DebitEntry OriginalRecord { get; set; } = null!;
    public List<DebitEntry> DuplicateRecords { get; set; } = new();
    public int DuplicateCount => DuplicateRecords.Count;
    public string Reason { get; set; } = string.Empty;
}

public class RepaymentDuplicateGroup
{
    public CreditorRepayment OriginalRecord { get; set; } = null!;
    public List<CreditorRepayment> DuplicateRecords { get; set; } = new();
    public int DuplicateCount => DuplicateRecords.Count;
    public string Reason { get; set; } = string.Empty;
}

public class OrphanRecord
{
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class DataIntegrityReport
{
    public int DuplicateDsmEntries { get; set; }
    public int DuplicateDebitEntries { get; set; }
    public int DuplicateRepayments { get; set; }
    public int OrphanRecords { get; set; }
    public int BrokenForeignKeys { get; set; }
    public bool HasIssues =>
        DuplicateDsmEntries > 0 ||
        DuplicateDebitEntries > 0 ||
        DuplicateRepayments > 0 ||
        OrphanRecords > 0 ||
        BrokenForeignKeys > 0;
    public DateTime ScannedAt { get; set; }
    public List<DsmEntryDuplicateGroup> DsmEntryGroups { get; set; } = new();
    public List<DebitEntryDuplicateGroup> DebitEntryGroups { get; set; } = new();
    public List<RepaymentDuplicateGroup> RepaymentGroups { get; set; } = new();
    public List<OrphanRecord> Orphans { get; set; } = new();
}

// ── Interface ────────────────────────────────────────────────────────────────

public interface IDuplicateDataInspectionService
{
    /// <summary>
    /// Runs a complete integrity scan and returns the report.
    /// </summary>
    Task<DataIntegrityReport> RunFullScanAsync();

    /// <summary>
    /// Returns duplicate DsmEntry groups (same ShiftId + PumpId + DsmName).
    /// </summary>
    Task<List<DsmEntryDuplicateGroup>> FindDuplicateDsmEntriesAsync();

    /// <summary>
    /// Returns duplicate DebitEntry groups within the same DsmEntry.
    /// </summary>
    Task<List<DebitEntryDuplicateGroup>> FindDuplicateDebitEntriesAsync();

    /// <summary>
    /// Returns duplicate CreditorRepayment groups (same creditor, date, amount, mode).
    /// </summary>
    Task<List<RepaymentDuplicateGroup>> FindDuplicateRepaymentsAsync();

    /// <summary>
    /// Returns orphan child records whose parent DsmEntry no longer exists.
    /// </summary>
    Task<List<OrphanRecord>> FindOrphanRecordsAsync();

    /// <summary>
    /// Returns the most recent scan result without re-scanning.
    /// </summary>
    DataIntegrityReport? GetCachedReport();
}
