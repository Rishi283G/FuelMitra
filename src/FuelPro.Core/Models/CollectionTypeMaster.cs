using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FuelPro.Core.Models;

/// <summary>
/// Configurable master for all collection methods (Online, Digital, Card, Cash, etc.).
/// Allows the Developer to add, edit, toggle, and reorder payment collection types.
/// </summary>
public class CollectionTypeMaster : ObservableObject
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int CollectionTypeId { get; set; }

    private string _code = string.Empty;
    /// <summary>
    /// Unique code/identifier (e.g., "PHONEPE", "CREDIT_CARD", "PETROCARD", "SBI_REDEEM", "PAYTM", "QR", "MOBIKWIK", "OTHERS", "CASH_DEPOSIT").
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    private string _displayName = string.Empty;
    /// <summary>
    /// Display name shown across the application (e.g. "SBI Redeem", "Paytm", "QR / Online", "Mobikwik").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    private string _category = "Online";
    /// <summary>
    /// Category: "Online", "Card", "Cash", "Other".
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    private bool _hasTidBatch = false;
    /// <summary>
    /// Whether this collection method requires Terminal ID (TID) and Batch reconciliation in the TID Sheet.
    /// </summary>
    public bool HasTidBatch
    {
        get => _hasTidBatch;
        set => SetProperty(ref _hasTidBatch, value);
    }

    private int _displayOrder = 0;
    /// <summary>
    /// Display order in collection tables and grids.
    /// </summary>
    public int DisplayOrder
    {
        get => _displayOrder;
        set => SetProperty(ref _displayOrder, value);
    }

    private bool _isActive = true;
    /// <summary>
    /// Whether this collection type is active and available for entry.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>
    /// Whether this is a core system type (prevents accidental deletion while still allowing enable/disable).
    /// </summary>
    public bool IsSystem { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
