using FuelPro.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FuelPro.Data.Services;

public static class DsmOilDefSalePersistence
{
    public static async Task<OilDefDailyLog> AddOrAccumulateAsync(
        FuelProDbContext context,
        int shiftId,
        DateTime shiftDate,
        ProductMaster product,
        double quantity,
        double saleRate,
        CancellationToken cancellationToken = default)
    {
        var log = await context.OilDefDailyLogs.FirstOrDefaultAsync(
            item => item.ProductId == product.Id
                && item.ShiftId == shiftId
                && item.LogDate.Date == shiftDate.Date,
            cancellationToken);

        if (log == null)
        {
            log = new OilDefDailyLog
            {
                LogDate = shiftDate.Date,
                ShiftId = shiftId,
                ProductId = product.Id,
                ProductType = product.Category,
                SoldQuantity = quantity,
                OverrideSaleRate = Math.Abs(saleRate - product.DefaultSaleRate) > 0.01 ? saleRate : null
            };
            context.OilDefDailyLogs.Add(log);
        }
        else
        {
            log.SoldQuantity += quantity;
            if (Math.Abs(saleRate - product.DefaultSaleRate) > 0.01)
                log.OverrideSaleRate = saleRate;
        }

        await context.SaveChangesAsync(cancellationToken);
        return log;
    }
}
