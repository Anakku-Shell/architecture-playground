using System.Xml.Linq;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Shop.Layered.Business.Ordering;
using Shop.Layered.Data;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Shop.Layered.ArchitectureTests;

/// <summary>
/// The rules of the layered version, one test per rule. Layered architecture has few rules, and the
/// compiler already enforces most of them through project references (Guide §2.3). These tests state
/// them explicitly, so breaking one fails with a sentence instead of a compiler error far away, and
/// they show how little a layered design actually protects. Guide: §4.6.
/// </summary>
public sealed class LayerRulesTests
{
    private static readonly System.Reflection.Assembly ApiAssembly = typeof(Program).Assembly;
    private static readonly System.Reflection.Assembly BusinessAssembly = typeof(OrderService).Assembly;
    private static readonly System.Reflection.Assembly DataAssembly = typeof(ShopDbContext).Assembly;

    private static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(ApiAssembly, BusinessAssembly, DataAssembly).Build();

    private static readonly IObjectProvider<IType> Api = Types().That().ResideInAssembly(ApiAssembly).As("Api layer");
    private static readonly IObjectProvider<IType> Business = Types().That().ResideInAssembly(BusinessAssembly).As("Business layer");
    private static readonly IObjectProvider<IType> Data = Types().That().ResideInAssembly(DataAssembly).As("Data layer");

    /// <summary>
    /// Why: each layer may only talk to the layer directly below it, so the presentation layer must not
    /// declare a dependency on the database layer. This is checked on the project file, because that is
    /// the only place the rule really holds: Api still compiles against Data's types through Business
    /// (a transitive reference), and it does use the entities and enums to build responses.
    /// </summary>
    [Fact]
    public void Api_DoesNotReferenceData()
    {
        var project = XDocument.Load(Path.Combine(SolutionFolder(), "src", "Shop.Layered.Api", "Shop.Layered.Api.csproj"));

        var references = project.Descendants("ProjectReference").Select(r => (string?)r.Attribute("Include") ?? "");

        Assert.DoesNotContain(references, r => r.Contains("Shop.Layered.Data", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Why: the part of "Api does not reference Data" that can still be enforced. Seeing an entity type
    /// is tolerated in this version; querying or saving from an endpoint is not, or the Business layer's
    /// rules could be bypassed by any endpoint.
    /// </summary>
    [Fact]
    public void Api_DoesNotUseTheDbContext()
    {
        // Read from the compiled IL (CompiledCode), not with ArchUnitNET: the endpoints are async lambdas,
        // and ArchUnitNET does not see what an async lambda's body uses. Guide: §6.6.
        var violations =
            from type in CompiledCode.Read(ApiAssembly)
            from used in type.UsedTypes
            where used == typeof(ShopDbContext).FullName || used.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
            select $"{type.Type} uses {used}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: the business rules must work the same whether they are called from HTTP, a job or a test, so the
    /// Business layer must not know HTTP. Unlike the two rules below, this one can fail without a compiler
    /// error: adding <c>&lt;FrameworkReference Include="Microsoft.AspNetCore.App" /&gt;</c> to Business is enough.
    /// </summary>
    [Fact]
    public void Business_DoesNotDependOnAspNetCore()
    {
        Types().That().Are(Business).Should()
            .NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(\..+)?$")
            .Because("HTTP is the presentation layer's concern")
            .Check(Architecture);
    }

    // The two rules below only restate what the compiler already enforces: Data cannot see Business (and
    // Business cannot see Api) without a circular project reference, which MSBuild rejects. They are kept
    // so the full rule set of a layered design is written down in one place.

    /// <summary>Why: dependencies point down. The Data layer is reused by whoever sits above it and knows none of them.</summary>
    [Fact]
    public void Data_DoesNotReferenceBusiness()
    {
        Types().That().Are(Data).Should().NotDependOnAny(Business).Check(Architecture);
    }

    /// <summary>Why: the Business layer is called by the Api, never the other way round.</summary>
    [Fact]
    public void Business_DoesNotReferenceApi()
    {
        Types().That().Are(Business).Should().NotDependOnAny(Api).Check(Architecture);
    }

    private static string SolutionFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Shop.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("Shop.slnx not found above " + AppContext.BaseDirectory);
    }
}
