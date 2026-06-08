using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AssetMap.Database;

/// <summary>
/// Design-time factory — používá se pro generování migrací přes CLI.
/// Spustit z adresáře AssetMap.Database:
///   dotnet ef migrations add InitialCreate --startup-project ../AssetMap.API
/// </summary>
public class DbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=assetmap-design.db")
            .Options;
        return new AppDbContext(opts);
    }
}
