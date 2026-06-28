using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Day-level lock to prevent historical modifications after daily closure.
/// Owners can unlock closed days when required.
/// </summary>
public class DayLock
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DayLockId { get; set; }

    /// <summary>
    /// The date being locked (date only, no time component).
    /// </summary>
    [Required]
    public DateTime LockDate { get; set; }

    /// <summary>
    /// When the day was locked.
    /// </summary>
    public DateTime LockedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Username of the person who locked the day.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string LockedBy { get; set; } = string.Empty;

    /// <summary>
    /// Whether the day is currently locked.
    /// </summary>
    public bool IsLocked { get; set; } = true;

    /// <summary>
    /// When the Owner unlocked the day (null if still locked).
    /// </summary>
    public DateTime? UnlockedAt { get; set; }

    /// <summary>
    /// Username of the Owner who unlocked the day.
    /// </summary>
    [MaxLength(100)]
    public string? UnlockedBy { get; set; }

    /// <summary>
    /// Reason provided by Owner for unlocking a closed day.
    /// </summary>
    [MaxLength(500)]
    public string? UnlockReason { get; set; }
}
