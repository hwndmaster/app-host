using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Genius.AppHost.Configuration;

/// <summary>
/// An application's app.json, after <c>${NAME}</c> expansion from its .env. <see cref="Type"/> decides how
/// the rest is turned into resources; see <c>Types/</c>.
/// </summary>
internal sealed record AppDefinition
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    /// <summary>
    /// Lower-case letters, digits and hyphens. Prefixes the container names (<c>&lt;name&gt;-api</c>) and the
    /// image names, and labels the containers as this compose project in Docker Desktop.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Heads the application's group on the Resources page. Defaults to <see cref="Name"/>.</summary>
    public string? DisplayName { get; init; }

    public required string Type { get; init; }

    /// <summary>Defaults to the app-host's <c>AppHost:Registry</c>.</summary>
    public string? Registry { get; init; }

    public string ImageTag { get; init; } = "latest";

    public ContainerDefinition? Api { get; init; }

    public ContainerDefinition? Web { get; init; }
}

internal sealed record ContainerDefinition
{
    public required int ContainerPort { get; init; }

    /// <summary>The port published on the server, bound to the app-host's <c>AppHost:BindAddress</c>.</summary>
    public required int HostPort { get; init; }

    /// <summary>Docker's notation: <c>384m</c>, <c>1g</c>. Unset leaves the container unlimited.</summary>
    public string? Memory { get; init; }

    public IReadOnlyList<VolumeDefinition> Volumes { get; init; } = [];

    /// <summary>Entries whose value expands to an empty string are left out, so an optional secret that is
    /// not set in .env keeps its integration disabled instead of configuring it with an empty value.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = ImmutableDictionary<string, string>.Empty;

    /// <summary>Further published ports, e.g. a web container's HTTPS listener.</summary>
    public IReadOnlyList<PortDefinition> Ports { get; init; } = [];
}

internal sealed record VolumeDefinition
{
    /// <summary>A path on the server. The daemon resolves it, so on Docker Desktop for Windows it is a
    /// Windows path such as <c>C:/Docker/MyApp/Data</c>.</summary>
    public required string Host { get; init; }

    public required string Container { get; init; }

    public bool ReadOnly { get; init; }
}

internal sealed record PortDefinition
{
    public required int ContainerPort { get; init; }

    public required int HostPort { get; init; }

    /// <summary><c>http</c> or <c>https</c>; only shapes the link shown on the Resources page.</summary>
    public string Scheme { get; init; } = "http";
}
