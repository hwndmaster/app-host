using System.Text.RegularExpressions;
using Genius.AppHost;
using Genius.AppHost.Configuration;
using Genius.AppHost.Resources;

var builder = DistributedApplication.CreateBuilder(args);
var host = HostSettings.From(builder.Configuration);

IReadOnlyList<CatalogEntry> catalog;
try
{
    catalog = AppCatalog.Load(host.AppsFile, host.AppsRoot);
}
catch (ConfigurationException exception)
{
    // Without a readable apps.json there is nothing to orchestrate. The applications keep running: their
    // containers are persistent. This line is what `docker logs app-host` shows.
    await Console.Error.WriteLineAsync($"app-host cannot start: {exception.Message}").ConfigureAwait(false);
    return 1;
}

// Working applications first, so their resource names are taken before an error row picks one.
foreach (var entry in catalog.OrderBy(entry => entry.Error is not null))
{
    AddApp(builder, entry, host);
}

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;

static void AddApp(IDistributedApplicationBuilder builder, CatalogEntry entry, HostSettings host)
{
    if (entry.Error is { } error)
    {
        // Reported, not fatal: the other applications are still orchestrated, and this one's containers keep
        // running unmanaged until its configuration is fixed and the app-host restarted.
        Console.Error.WriteLine($"{entry.Registration.Label}: {error}");

        builder.AddResource(new AppGroupResource(UniqueResourceName(builder, entry.GroupName)))
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "Application",
                State = new ResourceStateSnapshot("Configuration error", KnownResourceStateStyles.Error),
                Properties = [new("Folder", entry.Folder), new("Error", error)],
            })
            .ExcludeFromManifest();
        return;
    }

    var group = builder.AddResource(new AppGroupResource(entry.GroupName))
        .WithInitialState(new CustomResourceSnapshot
        {
            ResourceType = "Application",
            State = new ResourceStateSnapshot(KnownResourceStates.Active, KnownResourceStateStyles.Info),
            Properties = [new("Folder", entry.Folder), new("Type", entry.Type!.Name)],
        })
        .ExcludeFromManifest();

    entry.Type.AddResources(builder, entry.Definition!, group, host);
}

// An error row is named after whatever is known (often the folder), which need not suit Aspire's resource
// names or be unique.
static string UniqueResourceName(IDistributedApplicationBuilder builder, string label)
{
    var name = Regex.Replace(label, "[^A-Za-z0-9]+", "-").Trim('-');
    if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
    {
        name = $"app-{name}".TrimEnd('-');
    }

    var unique = name;
    for (var i = 2; builder.Resources.Any(resource => string.Equals(resource.Name, unique, StringComparison.OrdinalIgnoreCase)); i++)
    {
        unique = $"{name}-{i}";
    }

    return unique;
}
