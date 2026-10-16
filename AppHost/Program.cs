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

foreach (var entry in catalog)
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
        Console.Error.WriteLine($"{entry.Registration.Name}: {error}");

        builder.AddResource(new AppGroupResource(entry.Registration.Name))
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
