using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmApprovalAudit
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmApprovalAuditId { get; set; }

    [Required]
    public Guid SubmissionId { get; set; }

    [Required]
    public string OriginalDataJson { get; set; } = string.Empty;

    [Required]
    public string ApprovedDataJson { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ApprovedBy { get; set; } = string.Empty;

    public DateTime ApprovedAt { get; set; } = DateTime.Now;

    public string? Remarks { get; set; }
}
