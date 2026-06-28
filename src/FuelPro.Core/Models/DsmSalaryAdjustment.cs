using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmSalaryAdjustment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string DsmName { get; set; } = string.Empty;

    public int Year { get; set; }
    public int Month { get; set; }

    public double AdvancePaid { get; set; }
    public double OtherAdjustments { get; set; }
    public double PendingAdvanceDeduction { get; set; } = 0.0;

    [MaxLength(500)]
    public string Remarks { get; set; } = string.Empty;
}
