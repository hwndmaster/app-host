using System.Text.Json.Serialization;

namespace Genius.AppHost.Configuration;

/// <summary>
/// apps.json: which applications this app-host runs, and where their folders are. Everything about how an
/// application runs lives in that folder's app.json, owned by the application's own repository.
/// </summary>
internal sealed record AppsFile
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public required IReadOnlyList<AppRegistration> Apps { get; init; }
}

internal sealed record AppRegistration
{
    /// <summary>
    /// Optional label for messages, such as the folder name. The application's own name, which names its
    /// containers, comes from its app.json.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>The application's folder, relative to the apps root.</summary>
    public required string Path { get; init; }

    /// <summary>False keeps the entry but leaves the application alone: not started, not shown.</summary>
    public bool Enabled { get; init; } = true;

    public string Label => Name ?? Path;
}
