using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class OilDefInventory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }

    [Required]
    [MaxLength(10)]
    public string ProductType { get; set; } = "Oil"; // "Oil" or "DEF"

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public ProductMaster? Product { get; set; }

    public double OpeningStock { get; set; }
    public double ClosingStock { get; set; }
    public double SalePrice { get; set; }
}
