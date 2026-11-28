using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Data;

namespace FuelPro.Sync;

/// <summary>
/// Enforces historical compatibility and reassignment protection for SyncIdMappings (Stage 2.3).
/// </summary>
public static class SyncMappingPreservationHelper
{
    /// <summary>
    /// Registers or preserves a deterministic SyncGuid in SyncIdMappings.
    /// Strictly guarantees historical compatibility:
    /// 1. If the local entity already has a remote mapping, that existing mapping is ALWAYS preserved.
    /// 2. If the deterministic GUID is already mapped to another entity, it is NEVER reassigned.
    /// 3. Only if neither exists is the new deterministic mapping created.
    /// </summary>
    public static async Task<string> RegisterOrPreserveDeterministicMappingAsync(
        FuelProDbContext context,
        string tableName,
        int localId,
        string deterministicGuid)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be null or whitespace.", nameof(tableName));
        if (localId <= 0)
            return deterministicGuid;
        if (string.IsNullOrWhiteSpace(deterministicGuid))
            throw new ArgumentException("Deterministic GUID cannot be null or whitespace.", nameof(deterministicGuid));

        // 1. Check if local entity already has a remote mapping (historical preservation)
        var existingByLocal = await context.SyncIdMappings
            .FirstOrDefaultAsync(m => m.TableName == tableName && m.LocalId == localId);
        if (existingByLocal != null && !string.IsNullOrWhiteSpace(existingByLocal.RemoteGuid))
        {
            return existingByLocal.RemoteGuid;
        }

        // 2. Check if the deterministic GUID is already mapped to another entity (reassignment protection)
        var existingByRemote = await context.SyncIdMappings
            .FirstOrDefaultAsync(m => m.TableName == tableName && m.RemoteGuid == deterministicGuid);
        if (existingByRemote != null)
        {
            return existingByRemote.RemoteGuid;
        }

        // 3. Register new deterministic mapping
        context.SyncIdMappings.Add(new SyncIdMapping
        {
            TableName = tableName,
            LocalId = localId,
            RemoteGuid = deterministicGuid
        });

        return deterministicGuid;
    }
}
