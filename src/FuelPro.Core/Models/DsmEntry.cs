using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmEntryId { get; set; }

    [Required]
    public int ShiftId { get; set; }

    [Required]
    [MaxLength(200)]
    public string DsmName { get; set; } = string.Empty;

    [Required]
    public int PumpId { get; set; }
    public int? ConnectedPumpId { get; set; }
    public int? ReconciledToPumpId { get; set; }

    public decimal GrossSales { get; set; }
    public decimal TotalInDirect { get; set; }
    public decimal TotalCreditors { get; set; }
    public decimal TotalCollection { get; set; }
    public decimal Mismatch { get; set; }

    /// <summary>
    /// Whether all creditor/debit entries in this DSM have been reconciled (returned).
    /// Used by the Creditor Return Tracker on the Dashboard.
    /// </summary>
    public bool IsReconciled { get; set; }

    [MaxLength(50)]
    public string? StartTime { get; set; }

    [MaxLength(50)]
    public string? EndTime { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    // Navigation
    [ForeignKey(nameof(ShiftId))]
    public Shift? Shift { get; set; }

    public ICollection<NozzleReading> NozzleReadings { get; set; } = new List<NozzleReading>();
    public PaymentCollection? PaymentCollection { get; set; }
    public ICollection<DebitEntry> DebitEntries { get; set; } = new List<DebitEntry>();
    public ICollection<TestingEntry> TestingEntries { get; set; } = new List<TestingEntry>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public ICollection<CashDenomination> CashDenominations { get; set; } = new List<CashDenomination>();
    public ICollection<DsmPersonalDebtor> PersonalDebtors { get; set; } = new List<DsmPersonalDebtor>();
    public ICollection<KhandharePetroleumEntry> KhandharePetroleumEntries { get; set; } = new List<KhandharePetroleumEntry>();
    public ICollection<DsmQrPaymentEntry> QrPayments { get; set; } = new List<DsmQrPaymentEntry>();
}
