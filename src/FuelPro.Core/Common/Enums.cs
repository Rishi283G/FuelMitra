namespace FuelPro.Core.Common;

public enum ShiftType
{
    A, // Morning
    B, // Afternoon
    C  // Night
}

public enum FuelType
{
    HSD,    // High Speed Diesel
    MS_I,   // Motor Spirit Tank 1
    MS_II   // Motor Spirit Tank 2
}

public enum CashType
{
    Cash1, // Bank Deposit Cash
    Cash2  // Cash in Hand
}

public enum UserRole
{
    Manager,
    Owner,
    Developer
}

public static class UserRoleExtensions
{
    public static string ToDisplayName(this UserRole role) => role switch
    {
        UserRole.Manager => "Manager",
        UserRole.Owner => "Owner",
        UserRole.Developer => "Developer",
        _ => role.ToString()
    };

    /// <summary>
    /// Checks if the role string matches a known role (case-insensitive).
    /// Handles legacy "Admin" and "Operator" role by mapping them to Manager.
    /// </summary>
    public static UserRole ParseRole(string role)
    {
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Operator", StringComparison.OrdinalIgnoreCase))
            return UserRole.Manager;

        if (string.Equals(role, "Owner", StringComparison.OrdinalIgnoreCase))
            return UserRole.Owner;

        return UserRole.Developer;
    }
}

public static class ShiftTypeExtensions
{
    public static string ToDisplayName(this ShiftType shift) => shift switch
    {
        ShiftType.A => "A — Morning",
        ShiftType.B => "B — Afternoon",
        ShiftType.C => "C — Night",
        _ => shift.ToString()
    };

    public static string ToShortName(this ShiftType shift) => shift switch
    {
        ShiftType.A => "Morning",
        ShiftType.B => "Afternoon",
        ShiftType.C => "Night",
        _ => shift.ToString()
    };
}

public static class FuelTypeExtensions
{
    public static string ToDisplayName(this FuelType fuelType) => fuelType switch
    {
        FuelType.HSD => "HSD",
        FuelType.MS_I => "MS-I",
        FuelType.MS_II => "MS-II",
        _ => fuelType.ToString()
    };
}
