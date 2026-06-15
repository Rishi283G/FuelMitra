using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmProfile
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmProfileId { get; set; }

    [Required]
    [MaxLength(100)]
    public string DsmName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string SalaryType { get; set; } = "FixedMonthly"; // "FixedMonthly", "PerShift", "PerDay"

    public double BaseSalary { get; set; } = 12000.0;

    public DateTime? JoiningDate { get; set; }
}
