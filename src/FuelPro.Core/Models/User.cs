using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FuelPro.Core.Common;

namespace FuelPro.Core.Models;

public class User
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string PinHash { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = "Manager"; // Manager, Owner, Operator

    public bool IsActive { get; set; } = true;

    public bool MustChangePin { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Parsed role enum. Handles legacy "Admin" → Manager mapping.
    /// </summary>
    [NotMapped]
    public UserRole ParsedRole => UserRoleExtensions.ParseRole(Role);

    [NotMapped]
    public bool IsManager => ParsedRole == UserRole.Manager;

    [NotMapped]
    public bool IsOwner => ParsedRole == UserRole.Owner;

    [NotMapped]
    public bool IsDeveloper => ParsedRole == UserRole.Developer;
}
