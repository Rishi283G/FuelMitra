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
    MS_II,  // Motor Spirit Tank 2
    CNG     // Compressed Natural Gas
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
        ShiftType.A => "A — Morning (07:00 AM – 02:30 PM)",
        ShiftType.B => "B — Afternoon (02:30 PM – 11:00 PM)",
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
    /// <summary>
    /// Returns the canonical internal name used in DB storage and filtering (e.g. "MS-I", "MS-II", "HSD", "CNG").
    /// Do NOT change these values — they are stored in the NozzleReadings table.
    /// </summary>
    public static string ToDisplayName(this FuelType fuelType) => fuelType switch
    {
        FuelType.HSD => "HSD",
        FuelType.MS_I => "MS-I",
        FuelType.MS_II => "MS-II",
        FuelType.CNG => "CNG",
        _ => fuelType.ToString()
    };

    /// <summary>
    /// Returns the user-friendly label for UI display (e.g. "Petrol", "Power", "Diesel", "CNG").
    /// Use this for XAML text labels, print headers, and DataGrid display columns.
    /// </summary>
    public static string ToFriendlyLabel(this FuelType fuelType) => fuelType switch
    {
        FuelType.HSD => "Diesel",
        FuelType.MS_I => "Petrol",
        FuelType.MS_II => "Petrol",
        FuelType.CNG => "CNG",
        _ => fuelType.ToString()
    };

    /// <summary>
    /// Converts a canonical display name (e.g. "MS-II") to its friendly label (e.g. "Petrol").
    /// </summary>
    public static string ToFriendlyLabel(string canonicalName) => canonicalName switch
    {
        "HSD" => "Diesel",
        "MS-I" => "Petrol",
        "MS-II" => "Petrol",
        "CNG" => "CNG",
        _ => canonicalName
    };

    /// <summary>
    /// Returns the unit label for this fuel type (L for liquids, Kg for CNG).
    /// </summary>
    public static string GetUnitLabel(this FuelType fuelType) => fuelType switch
    {
        FuelType.CNG => "Kg",
        _ => "L"
    };

    /// <summary>
    /// Returns the unit label based on canonical fuel name string.
    /// </summary>
    public static string GetUnitLabel(string canonicalName) => canonicalName switch
    {
        "CNG" => "Kg",
        _ => "L"
    };
}
