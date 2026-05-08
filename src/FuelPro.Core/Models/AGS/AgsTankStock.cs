using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models.AGS;

public class AgsTankStock
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int AgsTankStockId { get; set; }

    [Required]
    public int AgsShiftImportId { get; set; }

    /// <summary>1 = HSD, 2 = MS-I, 3 = MS-II</summary>
    public int TankNumber { get; set; }

    /// <summary>"HSD", "MS-I", or "MS-II"</summary>
    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "HSD";

    public double OpeningDipMM { get; set; }
    public double ClosingDipMM { get; set; }
    public double OpeningStockLitres { get; set; }
    public double ClosingStockLitres { get; set; }

    /// <summary>Stored computed: OpeningStockLitres - ClosingStockLitres.</summary>
    public double FuelDispensedLitres { get; set; }

    /// <summary>Any receipt received during this shift (tanker delivery).</summary>
    public double ReceiptLitres { get; set; }

    // Navigation
    [ForeignKey(nameof(AgsShiftImportId))]
    public AgsShiftImport? AgsShiftImport { get; set; }
}
