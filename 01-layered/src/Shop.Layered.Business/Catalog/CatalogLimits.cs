namespace Shop.Layered.Business.Catalog;

/// <summary>
/// The upper limits of the catalog. Business rules, and also a guard for the storage: without them a price
/// of 10^17 passes "positive, two decimals" and then overflows the numeric(18,2) column, and stock near
/// int.MaxValue wraps around to a negative number. Guide: §3.11, "Limits".
/// </summary>
public static class CatalogLimits
{
    public const decimal MaxPrice = 1_000_000.00m;

    public const int MaxStock = 1_000_000;

    /// <summary>The largest number of units one stock adjustment may add or remove.</summary>
    public const int MaxAdjustment = 1_000_000;
}
