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
    Admin,
    Operator
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
