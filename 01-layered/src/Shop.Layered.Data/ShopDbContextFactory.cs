using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Layered.Data;

/// <summary>
/// Used only by the <c>dotnet ef</c> tool at design time, so migrations can be created from this
/// project alone: <c>dotnet ef migrations add &lt;Name&gt; --project 01-layered/src/Shop.Layered.Data</c>.
/// The connection string is the local development database; creating a migration does not connect.
/// </summary>
public sealed class ShopDbContextFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=shop_layered;Username=shop;Password=shop")
            .Options);
}
