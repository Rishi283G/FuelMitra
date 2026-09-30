using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using Serilog;

namespace FuelPro.Data.Repositories;

/// <summary>
/// Repository for managing historical Opening Balances and DSM loss backing anchors.
/// Enforces atomicity, deterministic anchor identity, and non-destructive deactivation.
/// </summary>
public class OpeningBalanceRepository : IOpeningBalanceRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<OpeningBalanceRepository>();

    public OpeningBalanceRepository(FuelProDbContext context)
    {
        _context = context;
    }

    public async Task<Result<OpeningBalance?>> GetActiveByEntityAsync(string entityType, string entityIdentifier)
    {
        try
        {
            var trimmedType = entityType?.Trim() ?? string.Empty;
            var trimmedIdentifier = entityIdentifier?.Trim() ?? string.Empty;

            var ob = await _context.OpeningBalances
                .AsNoTracking()
                .Include(o => o.Creditor)
                .FirstOrDefaultAsync(o => o.EntityType == trimmedType && o.EntityIdentifier == trimmedIdentifier && o.IsActive);

            return Result<OpeningBalance?>.Ok(ob);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get active opening balance for {EntityType}: {EntityIdentifier}", entityType, entityIdentifier);
            return Result<OpeningBalance?>.Fail($"Failed to load opening balance: {ex.Message}");
        }
    }

    public async Task<Result<OpeningBalance?>> GetByIdAsync(int openingBalanceId)
    {
        try
        {
            var ob = await _context.OpeningBalances
                .AsNoTracking()
                .Include(o => o.Creditor)
                .FirstOrDefaultAsync(o => o.OpeningBalanceId == openingBalanceId);

            return Result<OpeningBalance?>.Ok(ob);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get opening balance by id {Id}", openingBalanceId);
            return Result<OpeningBalance?>.Fail($"Failed to load opening balance: {ex.Message}");
        }
    }

    public async Task<Result<List<OpeningBalance>>> GetAllActiveAsync()
    {
        try
        {
            var list = await _context.OpeningBalances
                .AsNoTracking()
                .Include(o => o.Creditor)
                .Where(o => o.IsActive)
                .OrderBy(o => o.EntityType)
                .ThenBy(o => o.EntityIdentifier)
                .ToListAsync();

            return Result<List<OpeningBalance>>.Ok(list);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get all active opening balances");
            return Result<List<OpeningBalance>>.Fail($"Failed to load opening balances: {ex.Message}");
        }
    }

    public async Task<Result<List<OpeningBalance>>> GetAllByEntityTypeAsync(string entityType)
    {
        try
        {
            var trimmedType = entityType?.Trim() ?? string.Empty;
            var list = await _context.OpeningBalances
                .AsNoTracking()
                .Include(o => o.Creditor)
                .Where(o => o.EntityType == trimmedType && o.IsActive)
                .OrderBy(o => o.EntityIdentifier)
                .ToListAsync();

            return Result<List<OpeningBalance>>.Ok(list);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get opening balances for entity type {EntityType}", entityType);
            return Result<List<OpeningBalance>>.Fail($"Failed to load opening balances: {ex.Message}");
        }
    }

    public async Task<Result<DsmPersonalDebtor?>> GetDsmAnchorAsync(string dsmName)
    {
        try
        {
            var trimmedName = dsmName?.Trim() ?? string.Empty;
            var anchor = await _context.DsmPersonalDebtors
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DsmName == trimmedName && 
                    (d.EntryType == "OpeningBalance" || d.EntryType == "DeactivatedOpening"));

            return Result<DsmPersonalDebtor?>.Ok(anchor);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get DSM anchor for {DsmName}", dsmName);
            return Result<DsmPersonalDebtor?>.Fail($"Failed to load DSM anchor: {ex.Message}");
        }
    }

    public async Task<Result<OpeningBalance>> SaveOpeningBalanceAsync(OpeningBalance openingBalance)
    {
        if (openingBalance == null)
            return Result<OpeningBalance>.Fail("Opening balance cannot be null.");

        var trimmedType = openingBalance.EntityType?.Trim() ?? string.Empty;
        var trimmedIdentifier = openingBalance.EntityIdentifier?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmedType))
            return Result<OpeningBalance>.Fail("EntityType is required.");

        if (string.IsNullOrEmpty(trimmedIdentifier))
            return Result<OpeningBalance>.Fail("EntityIdentifier is required.");

        if (trimmedType != "Debtor" && trimmedType != "DsmLoss")
            return Result<OpeningBalance>.Fail($"Invalid EntityType '{trimmedType}'. Supported values: 'Debtor', 'DsmLoss'.");

        if (openingBalance.Amount < 0)
            return Result<OpeningBalance>.Fail("Opening balance amount cannot be negative.");

        openingBalance.EntityType = trimmedType;
        openingBalance.EntityIdentifier = trimmedIdentifier;

        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Locate existing active record if any (preventing duplicates)
            OpeningBalance? targetEntity = null;

            if (openingBalance.OpeningBalanceId > 0)
            {
                targetEntity = await _context.OpeningBalances
                    .FirstOrDefaultAsync(o => o.OpeningBalanceId == openingBalance.OpeningBalanceId);
            }

            if (targetEntity == null)
            {
                targetEntity = await _context.OpeningBalances
                    .FirstOrDefaultAsync(o => o.EntityType == trimmedType && o.EntityIdentifier == trimmedIdentifier && o.IsActive);
            }

            if (targetEntity != null)
            {
                // Update existing record
                targetEntity.Amount = openingBalance.Amount;
                targetEntity.OpeningDate = openingBalance.OpeningDate;
                targetEntity.Notes = openingBalance.Notes;
                targetEntity.CreditorId = openingBalance.CreditorId;
                targetEntity.IsActive = true;
                targetEntity.UpdatedAt = DateTime.Now;
                if (!string.IsNullOrEmpty(openingBalance.CreatedBy))
                {
                    targetEntity.CreatedBy = openingBalance.CreatedBy;
                }
            }
            else
            {
                // Insert new record
                if (string.IsNullOrWhiteSpace(openingBalance.SyncGuid))
                {
                    openingBalance.SyncGuid = Guid.NewGuid().ToString();
                }
                openingBalance.IsActive = true;
                openingBalance.CreatedAt = DateTime.Now;
                openingBalance.UpdatedAt = DateTime.Now;
                _context.OpeningBalances.Add(openingBalance);
                targetEntity = openingBalance;
            }

            // 2. Atomically maintain backing anchor if EntityType == "DsmLoss"
            if (trimmedType == "DsmLoss")
            {
                var anchor = await _context.DsmPersonalDebtors
                    .FirstOrDefaultAsync(d => d.DsmName == trimmedIdentifier && 
                        (d.EntryType == "OpeningBalance" || d.EntryType == "DeactivatedOpening"));

                if (anchor != null)
                {
                    // Update existing anchor in-place deterministically
                    anchor.Amount = openingBalance.Amount;
                    anchor.Date = openingBalance.OpeningDate;
                    anchor.Time = openingBalance.OpeningDate.ToString("HH:mm");
                    anchor.EntryType = "OpeningBalance";
                    anchor.DsmEntryId = null;
                    anchor.DeductFromSalary = false; // Always false for opening balance
                    anchor.Remarks = "Historical Opening Balance";
                    anchor.FuelProduct = "Opening Balance";
                    // RepaidAmount is strictly preserved so existing repayment history is never corrupted
                }
                else
                {
                    // Create new anchor with deterministic identity
                    var newAnchor = new DsmPersonalDebtor
                    {
                        SyncGuid = Guid.NewGuid().ToString(),
                        DsmEntryId = null,
                        DsmName = trimmedIdentifier,
                        Date = openingBalance.OpeningDate,
                        Time = openingBalance.OpeningDate.ToString("HH:mm"),
                        Amount = openingBalance.Amount,
                        FuelProduct = "Opening Balance",
                        Remarks = "Historical Opening Balance",
                        PaymentMethod = "Cash",
                        SequenceNumber = 0,
                        RepaidAmount = 0.0,
                        DeductFromSalary = false, // Critical: exempt from payroll auto-deduction
                        EntryType = "OpeningBalance", // Critical: authoritative discriminator
                        CreatedAt = DateTime.Now
                    };
                    _context.DsmPersonalDebtors.Add(newAnchor);
                }
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.Information("Successfully saved opening balance for {EntityType}: {EntityIdentifier} with amount {Amount}",
                trimmedType, trimmedIdentifier, openingBalance.Amount);

            return Result<OpeningBalance>.Ok(targetEntity);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.Error(ex, "Transaction rolled back while saving opening balance for {EntityType}: {EntityIdentifier}",
                trimmedType, trimmedIdentifier);
            return Result<OpeningBalance>.Fail($"Failed to save opening balance: {ex.Message}");
        }
    }

    public async Task<Result> DeactivateOpeningBalanceAsync(int openingBalanceId)
    {
        using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var ob = await _context.OpeningBalances
                .FirstOrDefaultAsync(o => o.OpeningBalanceId == openingBalanceId);

            if (ob == null)
            {
                await tx.RollbackAsync();
                return Result.Fail($"Opening balance with Id {openingBalanceId} was not found.");
            }

            ob.IsActive = false;
            ob.UpdatedAt = DateTime.Now;

            // If DSM Loss, handle anchor non-destructively
            if (ob.EntityType == "DsmLoss")
            {
                var anchor = await _context.DsmPersonalDebtors
                    .FirstOrDefaultAsync(d => d.DsmName == ob.EntityIdentifier && 
                        (d.EntryType == "OpeningBalance" || d.EntryType == "DeactivatedOpening"));

                if (anchor != null)
                {
                    // Critical safety requirement: Never physically delete anchor because repayments cascade-delete!
                    anchor.Amount = 0.0;
                    anchor.EntryType = "DeactivatedOpening";
                    anchor.DsmEntryId = null;
                    anchor.DeductFromSalary = false;
                }
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.Information("Successfully deactivated opening balance Id {Id} for {EntityType}: {EntityIdentifier}",
                openingBalanceId, ob.EntityType, ob.EntityIdentifier);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.Error(ex, "Transaction rolled back while deactivating opening balance Id {Id}", openingBalanceId);
            return Result.Fail($"Failed to deactivate opening balance: {ex.Message}");
        }
    }
}
