using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Services;

namespace FuelPro.Data.Services;

public class AuditLogService : IAuditLogService
{
    private readonly FuelProDbContext _context;

    public AuditLogService(FuelProDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(string tableName, int recordId, string action, string? fieldName,
        string? oldValue, string? newValue, string modifiedBy, string? reason = null)
    {
        var log = new AuditLog
        {
            TableName = tableName,
            RecordId = recordId,
            Action = action,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ModifiedBy = modifiedBy,
            ModifiedAt = DateTime.Now,
            Reason = reason
        };

        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();
    }

    public async Task LogCreateAsync(string tableName, int recordId, string entitySnapshot, string modifiedBy)
    {
        await LogAsync(tableName, recordId, "Create", null, null, entitySnapshot, modifiedBy);
    }

    public async Task LogUpdateAsync(string tableName, int recordId,
        Dictionary<string, (string? OldValue, string? NewValue)> changes,
        string modifiedBy, string? reason = null)
    {
        foreach (var change in changes)
        {
            await LogAsync(
                tableName,
                recordId,
                "Update",
                change.Key,
                change.Value.OldValue,
                change.Value.NewValue,
                modifiedBy,
                reason
            );
        }
    }

    public async Task LogDeleteAsync(string tableName, int recordId, string entitySnapshot, string modifiedBy, string? reason = null)
    {
        await LogAsync(tableName, recordId, "Delete", null, entitySnapshot, null, modifiedBy, reason);
    }

    public async Task<List<AuditLog>> GetAuditTrailAsync(string tableName, int recordId)
    {
        return await _context.AuditLogs
            .Where(l => l.TableName == tableName && l.RecordId == recordId)
            .OrderByDescending(l => l.ModifiedAt)
            .ToListAsync();
    }

    public async Task<List<AuditLog>> GetAuditTrailByDateAsync(DateTime startDate, DateTime endDate)
    {
        return await _context.AuditLogs
            .Where(l => l.ModifiedAt >= startDate && l.ModifiedAt <= endDate)
            .OrderByDescending(l => l.ModifiedAt)
            .ToListAsync();
    }
}
