using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class Setting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SettingId { get; set; }

    public double HsdRate { get; set; } = 90.35;

    public double MsIRate { get; set; } = 103.81;
    public double MsIIRate { get; set; } = 103.81;

    [MaxLength(300)]
    public string PumpStationName { get; set; } = "Shree Mahakaleshwar Petroleum";

    [NotMapped]
    public string StationDisplayName
    {
        get => PumpStationName;
        set => PumpStationName = value;
    }

    public DateTime LastUpdated { get; set; } = DateTime.Now;
}
