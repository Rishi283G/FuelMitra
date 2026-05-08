using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DebitEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DebitId { get; set; }

    [Required]
    public int DsmEntryId { get; set; }

    [Required]
    [MaxLength(200)]
    public string DebtorName { get; set; } = string.Empty;

    public double Amount { get; set; }

    [MaxLength(100)]
    public string? ChequeNo { get; set; }

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }
}
