using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shop.Modular.BuildingBlocks;
using Shop.Modular.BuildingBlocks.Infrastructure.Modules;
using Shop.Modular.Ordering.Application.Ports;
using Shop.Modular.Ordering.Domain;
using Shop.Modular.Ordering.Infrastructure;
using Xunit;

namespace Shop.Modular.ArchitectureTests;

/// <summary>
/// The rules that make this a MODULAR monolith rather than a monolith: modules meet only through their
/// contracts, keep their internals internal and own their schema, the shared parts know no module, and the
/// Ordering module keeps version 02's clean rules inside. Guide: §7.6.
/// </summary>
public sealed class ModuleRulesTests
{
    private const string Prefix = "Shop.Modular.";
    private const string Host = "Shop.Modular.Host";
    private const string BuildingBlocksProject = "Shop.Modular.BuildingBlocks";
    private const string BuildingBlocksInfrastructureProject = "Shop.Modular.BuildingBlocks.Infrastructure";

    private static readonly Assembly BuildingBlocks = typeof(IEventBus).Assembly;
    private static readonly Assembly OrderingDomain = typeof(Order).Assembly;
    private static readonly Assembly OrderingApplication = typeof(IOrderRepository).Assembly;
    private static readonly Assembly OrderingInfrastructure = typeof(OrderingModule).Assembly;

    /// <summary>
    /// Every project in <c>src/</c>, read from the folders, so a new module is checked without editing this file.
    /// A project that is not the Host, a building block or a contract belongs to the module named by its second
    /// part: <c>Shop.Modular.Ordering.Domain</c> belongs to <c>Ordering</c>.
    /// </summary>
    private static readonly string[] SourceProjects =
        [.. Directory.GetDirectories(Path.Combine(SolutionFolder(), "src")).Select(d => Path.GetFileName(d)!)];

    private static readonly Dictionary<string, string[]> Modules = SourceProjects
        .Where(p => p != Host && !p.StartsWith(BuildingBlocksProject, StringComparison.Ordinal) && !p.EndsWith(".Contracts", StringComparison.Ordinal))
        .GroupBy(p => p.Split('.')[2])
        .ToDictionary(g => g.Key, g => g.ToArray());

    /// <summary>
    /// Why: this is the rule that keeps the modules separable. If Ordering could use a Catalog class, the two
    /// would grow together, and splitting them into services (version 05) would mean untangling them first.
    /// Checked on the project files (so the compiler enforces it from then on) and on the compiled references.
    /// </summary>
    [Fact]
    public void Modules_ReferenceOtherModulesOnlyThroughContracts()
    {
        Assert.All(SourceProjects, p => Assert.StartsWith(Prefix, p, StringComparison.Ordinal));
        Assert.True(Modules.Count >= 3, $"Expected the three modules, found: {string.Join(", ", Modules.Keys)}.");

        var violations = new List<string>();
        foreach (var (module, projects) in Modules)
        {
            foreach (var project in projects)
            {
                violations.AddRange(ProjectReferences(project)
                    .Where(name => !projects.Contains(name) && !name.EndsWith(".Contracts", StringComparison.Ordinal) && !name.StartsWith(BuildingBlocksProject, StringComparison.Ordinal))
                    .Select(name => $"{module} ({project}) references {name}"));
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: the Host composes modules; it must not use them. If it referenced Ordering.Application, a Host
    /// endpoint could call a use case directly and the module's entry point would no longer be the only way in.
    /// </summary>
    [Fact]
    public void Host_ReferencesOnlyModuleEntryPointsAndBuildingBlocks()
    {
        var allowed = Modules.Values.Select(EntryProject).Append(BuildingBlocksInfrastructureProject).Append(BuildingBlocksProject).ToHashSet();

        Assert.All(ProjectReferences(Host), name => Assert.True(allowed.Contains(name), $"The Host references {name}."));
    }

    /// <summary>
    /// Why: shared plumbing that knew a module would turn into a shared kernel: every module would depend on
    /// code written for one of them, and changing it would mean changing them all.
    /// </summary>
    [Fact]
    public void BuildingBlocksInfrastructure_ReferencesNoModule() =>
        Assert.All(ProjectReferences(BuildingBlocksInfrastructureProject), name => Assert.Equal(BuildingBlocksProject, name));

    /// <summary>
    /// Why: a contract is what other modules compile against. If it pulled in a framework or another module,
    /// every consumer would inherit that dependency. Records of ids and plain values, nothing else.
    /// </summary>
    [Fact]
    public void Contracts_DependOnNothingButBuildingBlocks()
    {
        var contracts = SourceProjects.Where(p => p.EndsWith(".Contracts", StringComparison.Ordinal)).ToList();

        Assert.NotEmpty(contracts);
        foreach (var contract in contracts)
        {
            AssertReferencesOnly(Assembly.Load(contract), BaseLibrary().Append(BuildingBlocksProject));
        }
    }

    /// <summary>
    /// Why: BuildingBlocks is referenced by every contract and by Ordering's inner layers. Anything it
    /// depended on would reach all of them. The framework-bound plumbing lives in BuildingBlocks.Infrastructure.
    /// </summary>
    [Fact]
    public void BuildingBlocks_DependOnNothing() => AssertReferencesOnly(BuildingBlocks, BaseLibrary());

    /// <summary>Why: version 02's first rule, kept inside the Ordering module: the domain is the stable centre.</summary>
    [Fact]
    public void OrderingDomain_DependsOnNothing() => AssertReferencesOnly(OrderingDomain, BaseLibrary());

    /// <summary>
    /// Why: version 02's rule, kept inside the Ordering module: databases and HTTP stay outside the hexagon,
    /// so the use cases are testable with in-memory fakes. Read from the IL: the use cases work inside async lambdas.
    /// </summary>
    [Fact]
    public void OrderingApplication_DoesNotUseEfCoreOrAspNetCore()
    {
        var violations =
            from type in CompiledCode.Read(OrderingApplication)
            from used in type.UsedTypes
            where Regex.IsMatch(used, @"^(Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|Npgsql)\.")
            select $"{type.Type} uses {used}";

        Assert.Empty(violations);
    }

    /// <summary>Why: the dependency rule of 02. Adapters depend on the application, never the other way round.</summary>
    [Fact]
    public void OrderingApplication_DoesNotReferenceInfrastructure()
    {
        var infrastructure = OrderingApplication.GetReferencedAssemblies().Select(a => a.Name ?? "")
            .Where(name => name.EndsWith(".Infrastructure", StringComparison.Ordinal));

        Assert.Empty(infrastructure);
    }

    /// <summary>
    /// Why: <c>internal</c> is the compiler's own module boundary. With every type internal, another module
    /// could not use one even with a project reference; only the <see cref="IModule"/> is public, for the Host.
    /// Each module has exactly one entry project (the one with the IModule). Ordering's Domain and Application
    /// must stay public (its Infrastructure project uses them), so the reference rules protect them instead.
    /// </summary>
    [Fact]
    public void ModuleInternals_AreNotPublic()
    {
        foreach (var projects in Modules.Values)
        {
            var entry = Assembly.Load(EntryProject(projects));

            // EF Core generates migrations and the model snapshot as public classes; they hold no behaviour.
            var exported = entry.GetExportedTypes().Where(t => !typeof(Migration).IsAssignableFrom(t) && !typeof(ModelSnapshot).IsAssignableFrom(t));

            var single = Assert.Single(exported);
            Assert.True(typeof(IModule).IsAssignableFrom(single), $"{entry.GetName().Name} exports {single.FullName}.");
        }
    }

    /// <summary>
    /// Why: a module's data is private like its classes. If two modules mapped the same tables, they would be
    /// coupled through the database even with perfect code boundaries. Each DbContext maps only to the schema
    /// named after its module (read from the EF Core model, built by each module's design-time factory).
    /// Raw SQL is not covered: the two <c>FOR UPDATE</c> statements are reviewed by hand (guide §7.6).
    /// </summary>
    [Fact]
    public void EachModule_MapsOnlyToItsOwnSchema()
    {
        foreach (var projects in Modules.Values)
        {
            var entry = Assembly.Load(EntryProject(projects));
            var module = (IModule)Activator.CreateInstance(entry.GetExportedTypes().Single(t => typeof(IModule).IsAssignableFrom(t)))!;
            var factories = projects.Select(Assembly.Load).SelectMany(a => a.GetTypes())
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>)))
                .ToList();

            Assert.NotEmpty(factories);
            foreach (var factory in factories)
            {
                using var context = (DbContext)factory.GetMethod("CreateDbContext")!.Invoke(Activator.CreateInstance(factory), [Array.Empty<string>()])!;
                var tables = context.Model.GetEntityTypes().Select(e => $"{e.GetSchema()}.{e.GetTableName()}").ToList();

                Assert.NotEmpty(tables);
                Assert.All(tables, table => Assert.StartsWith(module.Name + ".", table, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    /// Why: an integration event is a promise to other modules. Declared anywhere but a Contracts project, it
    /// would either be invisible to them or drag the publisher's internals along.
    /// </summary>
    [Fact]
    public void IntegrationEvents_LiveInContracts()
    {
        var events = AllModularAssemblies().SelectMany(a => a.GetTypes())
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && t != typeof(IIntegrationEvent))
            .ToList();

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.EndsWith(".Contracts", e.Assembly.GetName().Name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Why: as in 02, <c>Rehydrate</c> skips the value objects' rules and is only safe for data coming back from
    /// storage. Only Ordering's persistence adapter may call it.
    /// </summary>
    [Fact]
    public void OnlyOrderingInfrastructure_RehydratesValueObjects()
    {
        var violations =
            from assembly in AllModularAssemblies().Where(a => a != OrderingInfrastructure)
            from type in CompiledCode.Read(assembly)
            from call in type.CalledMethods
            where call.StartsWith("Shop.Modular.Ordering.Domain.", StringComparison.Ordinal) && call.EndsWith("::Rehydrate", StringComparison.Ordinal)
            select $"{type.Type} calls {call}";

        Assert.Empty(violations);
    }

    /// <summary>The one project of a module that holds its <see cref="IModule"/>; a module must have exactly one.</summary>
    private static string EntryProject(string[] projects)
    {
        var entries = projects.Where(p => Assembly.Load(p).GetExportedTypes().Any(t => typeof(IModule).IsAssignableFrom(t))).ToList();
        return entries.Count == 1
            ? entries[0]
            : throw new InvalidOperationException($"Module [{string.Join(", ", projects)}] has {entries.Count} projects with an IModule; it needs exactly one.");
    }

    /// <summary>The Shop.Modular projects a project references, from its .csproj and from its compiled assembly.</summary>
    private static IEnumerable<string> ProjectReferences(string project)
    {
        var csproj = XDocument.Load(Path.Combine(SolutionFolder(), "src", project, project + ".csproj"));
        var declared = csproj.Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(((string?)r.Attribute("Include") ?? "").Replace('\\', '/')));
        var compiled = Assembly.Load(project).GetReferencedAssemblies().Select(a => a.Name ?? "")
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal));
        return declared.Concat(compiled).Distinct();
    }

    private static void AssertReferencesOnly(Assembly assembly, IEnumerable<string> allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        Assert.All(assembly.GetReferencedAssemblies().Select(a => a.Name ?? ""), name => Assert.True(
            allowedSet.Contains(name),
            $"{assembly.GetName().Name} references {name}."));
    }

    /// <summary>
    /// The .NET base library: the assemblies of the shared framework Microsoft.NETCore.App, as the runtime lists
    /// them. A name check ("starts with System") would let packages like System.Reactive through.
    /// </summary>
    private static List<string> BaseLibrary()
    {
        var names = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}Microsoft.NETCore.App{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToList();
        Assert.NotEmpty(names);
        return names;
    }

    private static IEnumerable<Assembly> AllModularAssemblies() => SourceProjects.Select(Assembly.Load);

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
