using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

public interface IAuditLogService
{
    /// <summary>
    /// Log a single field change.
    /// </summary>
    Task LogAsync(string tableName, int recordId, string action, string? fieldName,
        string? oldValue, string? newValue, string modifiedBy, string? reason = null);

    /// <summary>
    /// Log a create operation with entity snapshot.
    /// </summary>
    Task LogCreateAsync(string tableName, int recordId, string entitySnapshot, string modifiedBy);

    /// <summary>
    /// Log multiple field changes in a single update operation.
    /// </summary>
    Task LogUpdateAsync(string tableName, int recordId,
        Dictionary<string, (string? OldValue, string? NewValue)> changes,
        string modifiedBy, string? reason = null);

    /// <summary>
    /// Log a delete operation.
    /// </summary>
    Task LogDeleteAsync(string tableName, int recordId, string entitySnapshot, string modifiedBy, string? reason = null);

    /// <summary>
    /// Retrieve audit trail for a specific record.
    /// </summary>
    Task<List<AuditLog>> GetAuditTrailAsync(string tableName, int recordId);

    /// <summary>
    /// Retrieve audit trail for a date range.
    /// </summary>
    Task<List<AuditLog>> GetAuditTrailByDateAsync(DateTime startDate, DateTime endDate);
}
