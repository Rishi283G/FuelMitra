using System.ComponentModel.DataAnnotations;

namespace FuelPro.Core.Models;

public class AppMeta
{
    [Key]
    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
