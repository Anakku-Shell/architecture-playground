namespace Shop.Layered.Business.Catalog;

/// <summary>Money rules shared by the services: prices and amounts have at most two decimals.</summary>
public static class PriceRules
{
    /// <summary>True when rounding to cents does not change the value (10.50 and 10.500 pass, 10.999 does not).</summary>
    public static bool HasAtMostTwoDecimals(decimal value) => decimal.Round(value, 2) == value;
}
