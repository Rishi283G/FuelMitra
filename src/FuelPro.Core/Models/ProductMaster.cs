using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class ProductMaster
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string Category { get; set; } = "Oil"; // "Oil" or "DEF"

    [Required]
    [MaxLength(50)]
    public string Unit { get; set; } = "Litre"; // "Litre", "Bottle", "Piece", etc.

    public double DefaultSaleRate { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Dynamic runtime property: current remaining inventory for this product.
    /// </summary>
    [NotMapped]
    public double CurrentStock { get; set; }
}
