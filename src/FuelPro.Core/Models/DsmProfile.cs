using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmProfile
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmProfileId { get; set; }

    [Required]
    [MaxLength(100)]
    public string DsmName { get; set; } = string.Empty;
}
