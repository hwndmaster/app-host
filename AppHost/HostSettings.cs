using Microsoft.Extensions.Configuration;

namespace Genius.AppHost;

/// <summary>
/// The app-host's own settings, from the <c>AppHost:*</c> configuration section (compose passes them as
/// <c>AppHost__PublicHost</c> and friends). Defaults match Deployment/docker-compose.yml, so only what is
/// specific to a server needs setting there.
/// </summary>
internal sealed record HostSettings
{
    /// <summary>apps.json, as mounted into the container.</summary>
    public required string AppsFile { get; init; }

    /// <summary>The apps root, as mounted into the container; each application's folder lies inside it.</summary>
    public required string AppsRoot { get; init; }

    /// <summary>Registry for applications whose app.json names none.</summary>
    public required string Registry { get; init; }

    /// <summary>
    /// Address the applications' ports are published on. DCP always publishes its own endpoints on
    /// 127.0.0.1 and offers no way to change that, so the reachable publish is added separately.
    /// </summary>
    public required string BindAddress { get; init; }

    /// <summary>
    /// Host the Resources page's links are shown under: the address browsers reach the server by. The
    /// app-host runs in a container and cannot find that out for itself.
    /// </summary>
    public required string PublicHost { get; init; }

    /// <summary>
    /// Where the APIs send telemetry. Over the host gateway, not the app-host's container name: DCP owns the
    /// network the applications sit on, and the app-host is not on it.
    /// </summary>
    public required string OtlpGrpcEndpoint { get; init; }

    /// <summary>Where nginx in each web container forwards the browser's telemetry. Same reasoning.</summary>
    public required string OtlpHttpEndpoint { get; init; }

    /// <summary>
    /// How hard DCP tries to refresh images. Defaults to Missing, and Always is close to unusable against a
    /// private registry: DCP runs the docker CLI inside this container, where the server's registry login
    /// does not exist, so its pulls go out anonymous and get "unauthorized". Each application's update.ps1
    /// pulls on the server instead.
    /// </summary>
    public required ImagePullPolicy ImagePullPolicy { get; init; }

    public string PublicUrl(string scheme, int port) => $"{scheme}://{PublicHost}:{port}";

    public static HostSettings From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new HostSettings
        {
            AppsFile = Read(configuration, "AppsFile", "/config/apps.json"),
            AppsRoot = Read(configuration, "AppsRoot", "/apps"),
            Registry = Read(configuration, "Registry", "ghcr.io/hwndmaster"),
            BindAddress = Read(configuration, "BindAddress", "0.0.0.0"),
            PublicHost = Read(configuration, "PublicHost", "localhost"),
            OtlpGrpcEndpoint = Read(configuration, "OtlpGrpcEndpoint", "http://host.docker.internal:21100"),
            OtlpHttpEndpoint = Read(configuration, "OtlpHttpEndpoint", "http://host.docker.internal:21101"),
            ImagePullPolicy = ReadPullPolicy(configuration),
        };
    }

    // Parsed rather than switched over a fixed list so the setting keeps working if Aspire adds policies;
    // anything unrecognized is rejected loudly instead of silently falling back.
    private static ImagePullPolicy ReadPullPolicy(IConfiguration configuration)
    {
        var value = configuration["AppHost:ImagePullPolicy"];
        if (string.IsNullOrWhiteSpace(value))
        {
            return ImagePullPolicy.Missing;
        }

        return Enum.TryParse<ImagePullPolicy>(value.Trim(), ignoreCase: true, out var policy)
            ? policy
            : throw new InvalidOperationException(
                $"AppHost:ImagePullPolicy is not a known pull policy: '{value}'. "
                + $"Valid values: {string.Join(", ", Enum.GetNames<ImagePullPolicy>())}.");
    }

    private static string Read(IConfiguration configuration, string key, string defaultValue)
    {
        var value = configuration[$"AppHost:{key}"];
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }
}
