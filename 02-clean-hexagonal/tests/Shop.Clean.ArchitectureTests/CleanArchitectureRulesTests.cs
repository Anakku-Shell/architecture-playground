using System.Reflection;
using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Shop.Clean.Application.Ports;
using Shop.Clean.Domain.Ordering;
using Shop.Clean.Infrastructure;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Shop.Clean.ArchitectureTests;

/// <summary>
/// The dependency rule of Clean / Hexagonal Architecture as tests, one rule each: source dependencies
/// point inwards (Api and Infrastructure → Application → Domain), and the inner layers do not know the
/// technologies of the outer ones. Guide: §5.6.
/// </summary>
public sealed class CleanArchitectureRulesTests
{
    private static readonly Assembly DomainAssembly = typeof(Order).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IOrderRepository).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(InfrastructureServiceCollectionExtensions).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    private static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(DomainAssembly, ApplicationAssembly, InfrastructureAssembly, ApiAssembly).Build();

    private static readonly IObjectProvider<IType> Domain = Types().That().ResideInAssembly(DomainAssembly).As("Domain");
    private static readonly IObjectProvider<IType> Application = Types().That().ResideInAssembly(ApplicationAssembly).As("Application");
    private static readonly IObjectProvider<IType> Infrastructure = Types().That().ResideInAssembly(InfrastructureAssembly).As("Infrastructure");
    private static readonly IObjectProvider<IType> Api = Types().That().ResideInAssembly(ApiAssembly).As("Api");

    /// <summary>
    /// Why: the domain is the most stable, most valuable code. If it depends on nothing, no change in a
    /// database, framework or library can break it, and it can be tested and reused anywhere. Checked twice:
    /// the compiled assembly references only the .NET base library, and no Domain type uses another layer.
    /// </summary>
    [Fact]
    public void Domain_DependsOnNothing()
    {
        // The base library = the assemblies of the shared framework Microsoft.NETCore.App, as the runtime
        // lists them. A name check ("starts with System") would let packages like System.Reactive through.
        var baseLibrary = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Microsoft.NETCore.App{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(baseLibrary);
        var references = DomainAssembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
        Assert.All(references, name => Assert.True(
            baseLibrary.Contains(name),
            $"Domain references {name}; it may only use the .NET base library."));

        Types().That().Are(Domain).Should().NotDependOnAny(Application).AndShould().NotDependOnAny(Infrastructure).AndShould().NotDependOnAny(Api)
            .Check(Architecture);
    }

    /// <summary>Why: use cases describe what the application does; how it talks to the world is plugged in from outside.</summary>
    [Fact]
    public void Application_DependsOnlyOnDomain()
    {
        Types().That().Are(Application).Should().NotDependOnAny(Infrastructure).AndShould().NotDependOnAny(Api)
            .Check(Architecture);
    }

    /// <summary>
    /// Why: the project references alone would allow Application to add an EF Core or ASP.NET Core package and
    /// use it. This rule is what keeps databases and HTTP outside the hexagon, so the use cases stay testable
    /// with in-memory fakes.
    /// </summary>
    [Fact]
    public void Application_DoesNotUseEfCoreOrAspNetCore()
    {
        // Read from the IL (CompiledCode): the use cases do their work inside async lambdas passed to
        // ConcurrencyRetry, which ArchUnitNET does not see into.
        var violations =
            from type in CompiledCode.Read(ApplicationAssembly)
            from used in type.UsedTypes
            where Regex.IsMatch(used, @"^(Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|Npgsql)\.")
            select $"{type.Type} uses {used}";

        Assert.Empty(violations);
    }

    /// <summary>Why: adapters are independent of each other; the database adapter must work whatever drives the application.</summary>
    [Fact]
    public void Infrastructure_DoesNotReferenceApi()
    {
        Types().That().Are(Infrastructure).Should().NotDependOnAny(Api).Check(Architecture);
    }

    /// <summary>
    /// Why: dependency inversion means the inner layer OWNS the abstraction. Ports are interfaces declared
    /// in Application (only exceptions of their contract sit beside them), and no other layer declares
    /// interfaces of its own for the core to depend on.
    /// </summary>
    [Fact]
    public void Ports_AreInterfacesInApplication()
    {
        var portTypes = ApplicationAssembly.GetTypes().Where(t => t.Namespace == typeof(IOrderRepository).Namespace && !t.IsNested);
        Assert.All(portTypes, t => Assert.True(
            t.IsInterface || typeof(Exception).IsAssignableFrom(t),
            $"{t.Name} is in Ports but is neither an interface nor an exception of a port's contract."));

        var foreignInterfaces = DomainAssembly.GetTypes().Concat(InfrastructureAssembly.GetTypes()).Where(t => t.IsInterface);
        Assert.Empty(foreignInterfaces);
    }

    /// <summary>
    /// Why: the composition root (Program) is the only Api code allowed to know Infrastructure. An endpoint
    /// that used an adapter directly would bypass the use cases.
    /// </summary>
    [Fact]
    public void Api_UsesInfrastructureOnlyInTheCompositionRoot()
    {
        // From the IL: the endpoints are async lambdas, and only their bodies would reveal an adapter used inside.
        var violations =
            from type in CompiledCode.Read(ApiAssembly).Where(t => t.Type != typeof(Program).FullName)
            from used in type.UsedTypes
            where used.StartsWith("Shop.Clean.Infrastructure.", StringComparison.Ordinal)
                || used.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
            select $"{type.Type} uses {used}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: <c>Rehydrate</c> skips the value objects' rules, so stored data stays readable when a rule changes.
    /// That shortcut is only safe for data coming back from storage: a use case or an endpoint calling it would
    /// create invalid values.
    /// </summary>
    [Fact]
    public void OnlyPersistenceAdapters_RehydrateValueObjects()
    {
        var violations =
            from assembly in new[] { DomainAssembly, ApplicationAssembly, ApiAssembly }
            from type in CompiledCode.Read(assembly)
            from call in type.CalledMethods
            where call.StartsWith("Shop.Clean.Domain.", StringComparison.Ordinal) && call.EndsWith("::Rehydrate", StringComparison.Ordinal)
            select $"{type.Type} calls {call}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: a rich domain model protects its rules only if nobody can bypass its methods. With a public setter,
    /// any code could write <c>order.Status = Paid</c>, as any code could in version 01.
    /// </summary>
    [Fact]
    public void DomainModel_HasNoPublicSetters()
    {
        var publicSetters = DomainAssembly.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.SetMethod is { IsPublic: true })
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}");

        Assert.Empty(publicSetters);
    }
}
