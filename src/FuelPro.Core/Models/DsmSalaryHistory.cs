using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmSalaryHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmSalaryHistoryId { get; set; }

    [Required]
    public int DsmProfileId { get; set; }

    public double OldBaseSalary { get; set; }
    public double NewBaseSalary { get; set; }

    [Required]
    [MaxLength(50)]
    public string OldSalaryType { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string NewSalaryType { get; set; } = string.Empty;

    public DateTime ChangeDate { get; set; } = DateTime.Now;

    // Navigation
    [ForeignKey(nameof(DsmProfileId))]
    public DsmProfile? DsmProfile { get; set; }
}
