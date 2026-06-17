using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class OilDefPurchase
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(10)]
    public string ProductType { get; set; } = "Oil"; // "Oil" or "DEF"

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public ProductMaster? Product { get; set; }

    [Required]
    [MaxLength(200)]
    public string SupplierName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime PurchaseDate { get; set; }

    public double Quantity { get; set; }
    public double UnitPrice { get; set; }
    public double TotalCost { get; set; }

    [NotMapped]
    public bool IsEditable => PurchaseDate.Date == DateTime.Today;
}
