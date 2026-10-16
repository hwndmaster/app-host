using Genius.AppHost.Configuration;
using Genius.AppHost.Resources;

namespace Genius.AppHost.Types;

/// <summary>
/// One shape of application, named by <c>type</c> in app.json. It checks the definition and turns it into
/// resources, filling in everything its applications have in common.
/// </summary>
internal interface IAppType
{
    string Name { get; }

    /// <summary>Throws <see cref="ConfigurationException"/> naming the field when the definition is unusable.</summary>
    void Validate(AppDefinition app);

    /// <summary>Every port the application publishes on the server, so collisions between apps are caught.</summary>
    IEnumerable<int> HostPorts(AppDefinition app);

    void AddResources(
        IDistributedApplicationBuilder builder, AppDefinition app, IResourceBuilder<AppGroupResource> group, HostSettings host);
}

internal static class AppTypes
{
    private static readonly IAppType[] All = [new AtomWebAppType()];

    public static IEnumerable<string> Names => All.Select(type => type.Name);

    public static IAppType? Find(string name) =>
        All.FirstOrDefault(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase));
}
