using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class FuelProfitMargin
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateTime EffectiveDate { get; set; }

    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "HSD"; // HSD, MS-I, MS-II

    public double MarginPerLitre { get; set; }
}
