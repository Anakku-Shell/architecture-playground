using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Clean.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tool at design time:
/// <c>dotnet ef migrations add &lt;Name&gt; --project 02-clean-hexagonal/src/Shop.Clean.Infrastructure</c>.
/// </summary>
internal sealed class ShopDbContextFactory : IDesignTimeDbContextFactory<ShopDbContext>
{
    public ShopDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ShopDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=shop_clean;Username=shop;Password=shop")
            .Options);
}
