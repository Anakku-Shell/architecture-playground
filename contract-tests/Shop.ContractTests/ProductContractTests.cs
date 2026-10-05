using System.Net;
using Xunit;

namespace Shop.ContractTests;

/// <summary>Catalog endpoints: products, prices and stock adjustments.</summary>
public abstract class ProductContractTests(IShopApi api) : ContractTests(api)
{
    public static TheoryData<string> InvalidProductCases => new(InvalidProducts.Keys);

    private static readonly Dictionary<string, object> InvalidProducts = new()
    {
        ["zero price"] = new { name = "Pen", sku = ShopClient.UniqueSku(), price = 0m, initialStock = 1 },
        ["negative price"] = new { name = "Pen", sku = ShopClient.UniqueSku(), price = -1m, initialStock = 1 },
        ["empty name"] = new { name = "", sku = ShopClient.UniqueSku(), price = 1m, initialStock = 1 },
        ["name of 201 chars"] = new { name = new string('n', 201), sku = ShopClient.UniqueSku(), price = 1m, initialStock = 1 },
        ["empty sku"] = new { name = "Pen", sku = "", price = 1m, initialStock = 1 },
        ["sku of 51 chars"] = new { name = "Pen", sku = new string('S', 51), price = 1m, initialStock = 1 },
        ["negative initial stock"] = new { name = "Pen", sku = ShopClient.UniqueSku(), price = 1m, initialStock = -1 },
    };

    [Fact]
    public async Task CreateProduct_Returns201_WithLocation_AndBody()
    {
        var sku = ShopClient.UniqueSku().ToLowerInvariant();

        var response = await Shop.CreateProductRaw(new { name = "Notebook", sku, price = 4.50m, initialStock = 7 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var product = await ShopClient.ReadSuccess<ProductResponse>(response);
        Assert.EndsWith($"/api/products/{product.Id}", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Notebook", product.Name);
        Assert.Equal(sku.ToUpperInvariant(), product.Sku);
        Assert.Equal(4.50m, product.Price);
        Assert.Equal(7, product.Stock);
    }

    [Fact]
    public async Task CreateProduct_WithDuplicateSku_Returns409()
    {
        var sku = ShopClient.UniqueSku().ToUpperInvariant();
        await ShopClient.ReadSuccess<ProductResponse>(
            await Shop.CreateProductRaw(new { name = "First", sku, price = 1m, initialStock = 1 }));

        // Same SKU in lower case: SKUs are case-insensitive, so it is a duplicate.
        var response = await Shop.CreateProductRaw(new { name = "Second", sku = sku.ToLowerInvariant(), price = 1m, initialStock = 1 });

        await ProblemAssert.IsProblem(response, HttpStatusCode.Conflict);
    }

    [Theory]
    [MemberData(nameof(InvalidProductCases))]
    public async Task CreateProduct_WithInvalidData_Returns400(string invalidCase)
    {
        var response = await Shop.CreateProductRaw(InvalidProducts[invalidCase]);

        await ProblemAssert.IsValidationProblem(response);
    }

    [Fact]
    public async Task PriceWithThreeDecimals_Returns400()
    {
        var sku = ShopClient.UniqueSku().ToUpperInvariant();

        var response = await Shop.CreateProductRaw(new { name = "Pen", sku, price = 10.999m, initialStock = 1 });

        await ProblemAssert.IsValidationProblem(response);
        Assert.DoesNotContain(await Shop.ListProducts(), p => p.Sku == sku);
    }

    [Fact]
    public async Task GetProduct_Unknown_Returns404()
    {
        var response = await Shop.GetProductRaw(Guid.NewGuid());

        await ProblemAssert.IsProblem(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListProducts_ContainsCreatedProducts_OrderedByName()
    {
        // Other tests create products too, so only the relative order of these two is checked.
        var suffix = Guid.NewGuid().ToString("N");
        var zebra = await Shop.CreateProduct(name: $"Z-{suffix}");
        var apple = await Shop.CreateProduct(name: $"A-{suffix}");

        var products = (await Shop.ListProducts()).Select(p => p.Id).ToList();

        Assert.Contains(apple.Id, products);
        Assert.Contains(zebra.Id, products);
        Assert.True(products.IndexOf(apple.Id) < products.IndexOf(zebra.Id), "Products must be ordered by name.");
    }

    [Fact]
    public async Task ChangePrice_UpdatesPrice()
    {
        var product = await Shop.CreateProduct(price: 10.00m);

        var response = await Shop.ChangePriceRaw(product.Id, 12.50m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12.50m, (await ShopClient.ReadSuccess<ProductResponse>(response)).Price);
        Assert.Equal(12.50m, (await Shop.GetProduct(product.Id)).Price);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10.999")]
    public async Task ChangePrice_ToInvalidPrice_Returns400_AndPriceUnchanged(string invalidPrice)
    {
        var product = await Shop.CreateProduct(price: 10.00m);

        var response = await Shop.ChangePriceRaw(product.Id, decimal.Parse(invalidPrice, System.Globalization.CultureInfo.InvariantCulture));

        await ProblemAssert.IsValidationProblem(response);
        Assert.Equal(10.00m, (await Shop.GetProduct(product.Id)).Price);
    }

    [Fact]
    public async Task ChangePrice_UnknownProduct_Returns404()
    {
        await ProblemAssert.IsProblem(await Shop.ChangePriceRaw(Guid.NewGuid(), 5.00m), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdjustStock_AddsAndRemovesUnits()
    {
        var product = await Shop.CreateProduct(stock: 10);

        var added = await ShopClient.ReadSuccess<ProductResponse>(await Shop.AdjustStockRaw(product.Id, 5));
        var removed = await ShopClient.ReadSuccess<ProductResponse>(await Shop.AdjustStockRaw(product.Id, -15));

        Assert.Equal(15, added.Stock);
        Assert.Equal(0, removed.Stock);
    }

    [Fact]
    public async Task AdjustStock_BelowZero_Returns409_AndStockUnchanged()
    {
        var product = await Shop.CreateProduct(stock: 3);

        var response = await Shop.AdjustStockRaw(product.Id, -4);

        await ProblemAssert.IsProblem(response, HttpStatusCode.Conflict);
        Assert.Equal(3, (await Shop.GetProduct(product.Id)).Stock);
    }

    [Fact]
    public async Task AdjustStock_ByZero_Returns400()
    {
        var product = await Shop.CreateProduct(stock: 3);

        await ProblemAssert.IsValidationProblem(await Shop.AdjustStockRaw(product.Id, 0));
    }

    [Fact]
    public async Task AdjustStock_UnknownProduct_Returns404()
    {
        await ProblemAssert.IsProblem(await Shop.AdjustStockRaw(Guid.NewGuid(), 1), HttpStatusCode.NotFound);
    }
}
