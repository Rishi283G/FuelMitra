using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FuelPro.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FuelProDbContext>
{
    public FuelProDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FuelProDbContext>();
        optionsBuilder.UseSqlite("Data Source=design_time.db");
        return new FuelProDbContext(optionsBuilder.Options);
    }
}
