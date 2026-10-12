using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shop.Micro.Contracts;
using Shop.Micro.Ordering.Application.Ports;
using Shop.Micro.Ordering.Domain;
using Shop.Micro.Ordering.Infrastructure;
using Xunit;

namespace Shop.Micro.ArchitectureTests;

/// <summary>
/// The rules that keep these services independent: no service compiles against another, they share only
/// the message contracts and two pieces of plumbing, the shared pieces know no service, and the Ordering
/// service keeps its clean rules inside. If these break, the result is a distributed monolith: the cost of
/// microservices without the independence. Guide: §8.7.
/// </summary>
public sealed class ServiceRulesTests
{
    private const string Prefix = "Shop.Micro.";
    private const string AppHost = "Shop.Micro.AppHost";
    private const string Gateway = "Shop.Micro.Gateway";
    private const string Contracts = "Shop.Micro.Contracts";
    private const string Messaging = "Shop.Micro.Messaging";
    private const string ServiceDefaults = "Shop.Micro.ServiceDefaults";

    private static readonly string[] Shared = [Contracts, Messaging, ServiceDefaults];

    private static readonly Assembly OrderingDomain = typeof(Order).Assembly;
    private static readonly Assembly OrderingApplication = typeof(IOrderRepository).Assembly;
    private static readonly Assembly OrderingInfrastructure = typeof(OrderingInfrastructure).Assembly;

    /// <summary>
    /// Every project in <c>src/</c>, read from the folders, so a new service is checked without editing this
    /// file. A project that is not the AppHost, the gateway or a shared piece belongs to the service named by
    /// its second part: <c>Shop.Micro.Ordering.Domain</c> belongs to <c>Ordering</c>.
    /// </summary>
    private static readonly string[] SourceProjects =
        [.. Directory.GetDirectories(Path.Combine(SolutionFolder(), "src")).Select(d => Path.GetFileName(d)!)];

    private static readonly Dictionary<string, string[]> Services = SourceProjects
        .Where(p => p != AppHost && p != Gateway && !Shared.Contains(p))
        .GroupBy(p => p.Split('.')[2])
        .ToDictionary(g => g.Key, g => g.ToArray());

    /// <summary>
    /// Why: the reason to have services at all is to change and deploy each one alone. A project reference
    /// between two of them means one cannot be built, versioned or released without the other.
    /// </summary>
    [Fact]
    public void Services_DoNotReferenceEachOther()
    {
        Assert.All(SourceProjects, p => Assert.StartsWith(Prefix, p, StringComparison.Ordinal));
        Assert.True(Services.Count >= 3, $"Expected the three services, found: {string.Join(", ", Services.Keys)}.");

        var violations =
            from service in Services
            from project in service.Value
            from reference in ProjectReferences(project)
            from other in Services.Where(s => s.Key != service.Key)
            where other.Value.Contains(reference)
            select $"{service.Key} ({project}) references {other.Key} ({reference})";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: what services share is coupling they must upgrade in step. Only the messages (the contract they
    /// must agree on anyway) and two pieces of plumbing are allowed; business code is never shared. The small
    /// duplication this causes (each service has its own error types) is accepted on purpose.
    /// </summary>
    [Fact]
    public void Services_ShareOnlyContractsMessagingAndServiceDefaults()
    {
        var violations =
            from service in Services
            from project in service.Value
            from reference in ProjectReferences(project)
            where !service.Value.Contains(reference) && !Shared.Contains(reference)
            select $"{service.Key} ({project}) references {reference}";

        Assert.Empty(violations);
    }

    /// <summary>
    /// Why: the gateway routes by URL and must keep working whatever a service does inside. Referencing a
    /// service would let routing logic depend on its classes, and the gateway would be redeployed with it.
    /// </summary>
    [Fact]
    public void Gateway_ReferencesNoService() =>
        Assert.All(ProjectReferences(Gateway), name => Assert.Equal(ServiceDefaults, name));

    /// <summary>
    /// Why: every service compiles against Contracts. A dependency there (a framework, a service) would be
    /// forced on all of them. Records of ids and plain values, nothing else.
    /// </summary>
    [Fact]
    public void Contracts_DependOnNothing() => AssertReferencesOnly(Assembly.Load(Contracts), BaseLibrary());

    /// <summary>
    /// Why: shared plumbing that knew a service would become a shared kernel that changes with that service.
    /// Messaging knows the marker interface of messages, never which messages exist.
    /// </summary>
    [Fact]
    public void Messaging_KnowsNoService() =>
        Assert.All(ProjectReferences(Messaging), name => Assert.Equal(Contracts, name));

    /// <summary>Why: as for Messaging. Hosting defaults are the same for every service, so they know none.</summary>
    [Fact]
    public void ServiceDefaults_KnowsNoService() => Assert.Empty(ProjectReferences(ServiceDefaults));

    /// <summary>Why: version 02's first rule, kept inside the Ordering service: the domain is the stable centre.</summary>
    [Fact]
    public void OrderingDomain_DependsOnNothing() => AssertReferencesOnly(OrderingDomain, BaseLibrary());

    /// <summary>
    /// Why: version 02's rule, kept inside the Ordering service, now including the broker: databases, HTTP and
    /// RabbitMQ stay outside the hexagon, so the use cases and the saga are testable with in-memory fakes.
    /// Read from the IL, so code inside lambdas and async methods counts.
    /// </summary>
    [Fact]
    public void OrderingApplication_DoesNotUseEfCoreAspNetCoreOrTheBroker()
    {
        var violations =
            from type in CompiledCode.Read(OrderingApplication)
            from used in type.UsedTypes
            where Regex.IsMatch(used, @"^(Microsoft\.EntityFrameworkCore|Microsoft\.AspNetCore|Npgsql|RabbitMQ|Shop\.Micro\.Messaging)\.")
            select $"{type.Type} uses {used}";

        Assert.Empty(violations);
    }

    /// <summary>Why: the dependency rule of 02. Adapters depend on the application, never the other way round.</summary>
    [Fact]
    public void OrderingApplication_DoesNotReferenceInfrastructure() =>
        Assert.DoesNotContain(
            OrderingApplication.GetReferencedAssemblies().Select(a => a.Name ?? ""),
            name => name.EndsWith(".Infrastructure", StringComparison.Ordinal) || name.EndsWith(".Api", StringComparison.Ordinal));

    /// <summary>
    /// Why: a message is a promise to other services. Declared anywhere but Contracts, the receiver could not
    /// see it, or would have to reference the sender to get it.
    /// </summary>
    [Fact]
    public void IntegrationMessages_LiveInContracts()
    {
        var messages = AllAssemblies().SelectMany(a => a.GetTypes())
            .Where(t => typeof(IIntegrationMessage).IsAssignableFrom(t) && t != typeof(IIntegrationMessage))
            .ToList();

        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.Equal(Contracts, m.Assembly.GetName().Name));
    }

    /// <summary>
    /// Why: as in 02, <c>Rehydrate</c> skips the value objects' rules and is only safe for data coming back from
    /// storage. Only Ordering's persistence adapter may call it.
    /// </summary>
    [Fact]
    public void OnlyOrderingInfrastructure_RehydratesValueObjects()
    {
        var violations =
            from assembly in AllAssemblies().Where(a => a != OrderingInfrastructure)
            from type in CompiledCode.Read(assembly)
            from call in type.CalledMethods
            where call.StartsWith("Shop.Micro.Ordering.Domain.", StringComparison.Ordinal) && call.EndsWith("::Rehydrate", StringComparison.Ordinal)
            select $"{type.Type} calls {call}";

        Assert.Empty(violations);
    }

    /// <summary>The Shop.Micro projects a project references, from its .csproj and from its compiled assembly.</summary>
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

    /// <summary>Every source assembly but the AppHost (it only describes the system and runs nothing of it).</summary>
    private static IEnumerable<Assembly> AllAssemblies() => SourceProjects.Where(p => p != AppHost).Select(Assembly.Load);

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
