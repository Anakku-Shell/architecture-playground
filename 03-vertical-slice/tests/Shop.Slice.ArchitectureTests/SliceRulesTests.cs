using System.Text.RegularExpressions;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Shop.Slice.Api.Common;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using Assembly = System.Reflection.Assembly;

namespace Shop.Slice.ArchitectureTests;

/// <summary>
/// The rules of the vertical-slice version. It is a single project, so no project reference stops anything:
/// every boundary below exists only because a test checks it. Rules about what code <i>does</i> (calls,
/// used types) read the compiled IL through <see cref="CompiledCode"/>, because the slices do their work
/// inside async lambdas that ArchUnitNET does not see into. Guide: §6.6.
/// </summary>
public sealed class SliceRulesTests
{
    private const string Root = "Shop.Slice.Api";
    private const string Features = Root + ".Features";

    private static readonly Assembly ApiAssembly = typeof(IEndpoint).Assembly;

    private static readonly Architecture Architecture = new ArchLoader().LoadAssemblies(ApiAssembly).Build();

    private static readonly IReadOnlyList<TypeUsage> Code = CompiledCode.Read(ApiAssembly);

    /// <summary>
    /// Every slice namespace: <c>Shop.Slice.Api.Features.&lt;Context&gt;.&lt;UseCase&gt;</c> (and anything below it).
    /// Types directly in <c>Features.&lt;Context&gt;</c> (the shared response shapes) are not slices.
    /// </summary>
    private static readonly IReadOnlyList<string> Slices = [.. Code
        .Select(t => t.Namespace)
        .Where(ns => ns.StartsWith(Features + ".", StringComparison.Ordinal) && ns.Split('.').Length >= 6)
        .Select(ns => string.Join('.', ns.Split('.').Take(6)))
        .Distinct()
        .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Why: the point of slicing by use case is that a change to one use case touches one slice. A slice that
    /// uses another couples them again. Shared needs go to Domain (rules), Infrastructure (technology),
    /// Common (cross-cutting) or the context's response shapes, never into another slice.
    /// </summary>
    [Fact]
    public void Features_DoNotReferenceOtherFeatures()
    {
        Assert.True(Slices.Count >= 10, $"Expected the ten slices, found: {string.Join(", ", Slices)}");

        var violations =
            from slice in Slices
            from type in Code.Where(t => t.InNamespace(slice))
            from used in type.UsedTypes
            let usedSlice = SliceOf(used)
            where usedSlice is not null && usedSlice != slice
            select $"{type.Type} uses {used}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: shared code must stay shared. If Infrastructure, Common, Domain or a context's response shapes
    /// used a slice, deleting or changing that slice would break code that every other slice depends on.
    /// </summary>
    [Fact]
    public void SharedCode_DoesNotDependOnSlices()
    {
        var violations =
            from type in Code.Where(t => SliceOf(t.Type) is null)
            from used in type.UsedTypes
            where SliceOf(used) is not null
            select $"{type.Type} uses {used}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: the rules worth a model live in Domain, and they must not know the slices that use them nor the
    /// technologies around them. Same rule as 02's "Domain depends on nothing", here inside one project.
    /// </summary>
    [Fact]
    public void Domain_DoesNotDependOnFeaturesOrEfCore()
    {
        var forbidden = new Regex($@"^({Regex.Escape(Root)}\.(Features|Infrastructure|Common)|Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|Npgsql)\.");
        var violations =
            from type in Code.Where(t => t.InNamespace(Root + ".Domain"))
            from used in type.UsedTypes
            where forbidden.IsMatch(used)
            select $"{type.Type} uses {used}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: light CQRS (Guide §3.8). A query reads and changes nothing, so it can be optimised (projections,
    /// a read replica one day) without any risk to the write side. Query slices are the ones named Get* or
    /// List*. They call nothing that writes: no SaveChanges, no bulk ExecuteUpdate/ExecuteDelete, no raw
    /// SQL commands, no Add/Update/Remove on a set, no change tracker and no retry helper.
    /// </summary>
    [Fact]
    public void Queries_DoNotModifyState()
    {
        var writes = new Regex(@"^((SaveChanges|ExecuteUpdate|ExecuteDelete|ExecuteSql(Raw|Interpolated)?|Add|AddRange|Update|UpdateRange|Remove|RemoveRange|Attach)(Async)?|RetryOnConflictAsync|get_ChangeTracker)$");
        var queries = Slices.Where(s => s.Split('.')[^1] is var name && (name.StartsWith("Get", StringComparison.Ordinal) || name.StartsWith("List", StringComparison.Ordinal))).ToList();
        Assert.NotEmpty(queries);

        var violations =
            from query in queries
            from type in Code.Where(t => t.InNamespace(query))
            from call in type.CalledMethods
            let declaringType = call[..call.IndexOf("::", StringComparison.Ordinal)]
            let method = call[(call.IndexOf("::", StringComparison.Ordinal) + 2)..]
            where IsDataAccess(declaringType) && writes.IsMatch(method)
            select $"{type.Type} calls {call}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: <c>Rehydrate</c> skips the value objects' rules, which is only safe for data read back from
    /// storage (the EF Core converters in Infrastructure). A slice calling it would create invalid values.
    /// </summary>
    [Fact]
    public void OnlyInfrastructure_RehydratesValueObjects()
    {
        var violations =
            from type in Code.Where(t => !t.InNamespace(Root + ".Infrastructure"))
            from call in type.CalledMethods
            where call.StartsWith(Root + ".Domain.", StringComparison.Ordinal) && call.EndsWith("::Rehydrate", StringComparison.Ordinal)
            select $"{type.Type} calls {call}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: endpoint discovery maps every <see cref="IEndpoint"/> it finds. Keeping them sealed and inside
    /// a slice namespace means a route always lives next to the code that handles it. (A rule about
    /// declarations, not behaviour, so ArchUnitNET sees everything it needs.)
    /// </summary>
    [Fact]
    public void Endpoints_LiveInsideASlice()
    {
        Classes().That().ImplementInterface(typeof(IEndpoint)).Should()
            .ResideInNamespaceMatching($@"^{Regex.Escape(Features)}\.[^.]+\.[^.]+(\..+)?$")
            .AndShould().BeSealed()
            .Check(Architecture);
    }

    /// <summary>The slice a type name belongs to, or null when it is not inside a slice.</summary>
    private static string? SliceOf(string typeFullName) =>
        Slices.FirstOrDefault(s => typeFullName.StartsWith(s + ".", StringComparison.Ordinal));

    private static bool IsDataAccess(string declaringType) =>
        declaringType.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
        || declaringType.StartsWith(Root + ".Infrastructure.", StringComparison.Ordinal);
}
