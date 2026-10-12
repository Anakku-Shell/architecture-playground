namespace Shop.ContractTests;

/// <summary>Base of the three contract test classes: gives each test a <see cref="ShopClient"/> for the version under test.</summary>
public abstract class ContractTests(IShopApi api)
{
    protected ShopClient Shop { get; } = new(api.CreateClient(), api.IsAsynchronous);
}
