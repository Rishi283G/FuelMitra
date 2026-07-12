using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class TankDailyStock
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "HSD"; // "HSD", "MS-I", "MS-II", "CNG"

    public double OpeningStock { get; set; } // manual outstanding stock in litres/kg

    // Calculated / cached values for historical records
    public double DaySaleLitres { get; set; }
    public double TestingLitres { get; set; }
    public double PurchasedLitres { get; set; }
    public double ClosingStock { get; set; }
    public double DipMm { get; set; }
    public double ManualStock { get; set; }

    public DateTime LastUpdated { get; set; } = DateTime.Now;

    public int? ShiftId { get; set; }

    [ForeignKey(nameof(ShiftId))]
    public Shift? Shift { get; set; }
}
