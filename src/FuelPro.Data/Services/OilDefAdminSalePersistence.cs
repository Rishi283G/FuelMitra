using FuelPro.Core.Models;

namespace FuelPro.Data.Services;

public static class OilDefAdminSalePersistence
{
    public static async Task<OilDefDailyLog> SaveAsync(
        FuelProDbContext context,
        DateTime logDate,
        ProductMaster product,
        double quantity,
        double saleRate,
        int? shiftId,
        OilDefDailyLog? existing = null,
        CancellationToken cancellationToken = default)
    {
        var log = existing ?? new OilDefDailyLog();
        log.LogDate = logDate;
        log.ProductId = product.Id;
        log.ProductType = product.Category;
        log.SoldQuantity = quantity;
        log.OverrideSaleRate = Math.Abs(saleRate - product.DefaultSaleRate) > 0.01 ? saleRate : null;
        log.ShiftId = shiftId;
        log.AddedQuantity = 0;
        log.AdjustmentQuantity = 0;

        if (existing == null)
            context.OilDefDailyLogs.Add(log);

        await context.SaveChangesAsync(cancellationToken);
        return log;
    }
}
