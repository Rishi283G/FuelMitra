using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class TestingEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int TestingId { get; set; }

    [Required]
    public int DsmEntryId { get; set; }

    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "MS"; // MS or HSD

    public double Litres { get; set; }
    public double Rate { get; set; }
    public double Amount { get; set; }

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }
}
