using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Genius.AppHost.Types;

namespace Genius.AppHost.Configuration;

/// <summary>One registered application: either loaded and valid, or carrying the reason it is not.</summary>
internal sealed record CatalogEntry
{
    public required AppRegistration Registration { get; init; }

    /// <summary>The application's folder as the app-host sees it, e.g. <c>/apps/my-app</c>.</summary>
    public required string Folder { get; init; }

    public AppDefinition? Definition { get; init; }

    public IAppType? Type { get; init; }

    public string? Error { get; init; }

    /// <summary>The resource that groups the application on the Resources page.</summary>
    public string GroupName => Definition?.DisplayName ?? Definition?.Name ?? Registration.Label;
}

/// <summary>
/// Loads apps.json and every enabled application's app.json and .env.
/// <para>
/// A problem with apps.json itself stops the app-host: there is nothing sensible to show without it. A
/// problem with one application does not. That application is reported with its reason on the Resources
/// page and left alone, its containers keep running unmanaged, and every other application is orchestrated
/// as usual. One bad app.json must not take the dashboard away from the rest.
/// </para>
/// </summary>
internal static partial class AppCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // camelCase so that errors name the field as written in the file ("path", not "Path").
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // "5090" binds to an int, so ports can come from .env through ${NAME}.
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        // A misspelt property is an error rather than a silently ignored setting.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public const string AppFileName = "app.json";
    public const string EnvFileName = ".env";

    public static IReadOnlyList<CatalogEntry> Load(string appsFilePath, string appsRoot)
    {
        var registrations = ReadAppsFile(appsFilePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appsRoot)) + Path.DirectorySeparatorChar;

        var entries = registrations
            .Where(registration => registration.Enabled)
            .Select(registration => LoadApp(registration, root))
            .ToList();

        return RejectCollisions(entries);
    }

    private static IReadOnlyList<AppRegistration> ReadAppsFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new ConfigurationException(
                $"{path} does not exist. Create it from apps.example.json next to the app-host's docker-compose.yml.");
        }

        AppsFile? file;
        try
        {
            file = JsonSerializer.Deserialize<AppsFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException($"apps.json: {exception.Message}");
        }

        var registrations = file?.Apps ?? throw new ConfigurationException("apps.json is empty.");

        // Ignoring case: the server's folders are on Windows.
        var duplicate = registrations
            .GroupBy(registration => registration.Path.Trim('/', '\\'), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ConfigurationException($"apps.json lists the folder '{duplicate.Key}' more than once.");
        }

        return registrations;
    }

    private static CatalogEntry LoadApp(AppRegistration registration, string root)
    {
        var folder = Path.GetFullPath(Path.Combine(root, registration.Path));
        var entry = new CatalogEntry { Registration = registration, Folder = folder };

        try
        {
            // root ends with a separator, so "/apps2" does not pass as being inside "/apps".
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!folder.StartsWith(root, comparison))
            {
                throw new ConfigurationException(
                    $"apps.json: the path '{registration.Path}' leads outside the apps root; only folders inside it are mounted.");
            }

            var appFile = Path.Combine(folder, AppFileName);
            if (!File.Exists(appFile))
            {
                throw new ConfigurationException(
                    $"{appFile} does not exist. Copy the application's Deployment folder there.");
            }

            var variables = EnvFile.Load(Path.Combine(folder, EnvFileName));
            var definition = ReadAppFile(appFile, variables);

            if (!AppName().IsMatch(definition.Name))
            {
                throw new ConfigurationException(
                    $"app.json: name '{definition.Name}' is not valid. Use lower-case letters, digits and single hyphens, starting with a letter.");
            }

            if (definition.DisplayName is { } displayName && !ResourceName().IsMatch(displayName))
            {
                throw new ConfigurationException(
                    $"app.json: displayName '{displayName}' cannot name a resource. Use letters, digits and single hyphens, starting with a letter.");
            }

            var type = AppTypes.Find(definition.Type)
                ?? throw new ConfigurationException(
                    $"app.json: unknown type '{definition.Type}'. Known types: {string.Join(", ", AppTypes.Names)}.");
            type.Validate(definition);

            return entry with { Definition = definition, Type = type };
        }
        catch (ConfigurationException exception)
        {
            return entry with { Error = exception.Message };
        }
    }

    private static AppDefinition ReadAppFile(string path, IReadOnlyDictionary<string, string> variables)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path), documentOptions: DocumentOptions);
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException($"app.json: {exception.Message}");
        }

        if (node is not JsonObject)
        {
            throw new ConfigurationException("app.json must hold a JSON object.");
        }

        Expand(node, variables, "$");

        try
        {
            return node.Deserialize<AppDefinition>(JsonOptions)
                ?? throw new ConfigurationException("app.json is empty.");
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException($"app.json: {exception.Message}");
        }
    }

    // Expands ${NAME} in every string value in place. Only strings are touched: a number or a boolean
    // needs no expansion, and replacing a node that already has a parent would throw.
    private static void Expand(JsonNode node, IReadOnlyDictionary<string, string> variables, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    ExpandChild(child, variables, $"{path}.{key}", expanded => obj[key] = expanded);
                }

                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var index = i;
                    ExpandChild(array[i], variables, $"{path}[{i}]", expanded => array[index] = expanded);
                }

                break;
        }
    }

    private static void ExpandChild(
        JsonNode? child, IReadOnlyDictionary<string, string> variables, string path, Action<JsonNode> replace)
    {
        if (child is JsonValue value && value.TryGetValue<string>(out var text))
        {
            string expanded;
            try
            {
                expanded = Interpolator.Interpolate(text, variables);
            }
            catch (ConfigurationException exception)
            {
                throw new ConfigurationException($"app.json {path}: {exception.Message}");
            }

            replace(JsonValue.Create(expanded));
        }
        else if (child is not null)
        {
            Expand(child, variables, path);
        }
    }

    // Two applications cannot publish the same host port, share an app name (it names the containers) or a
    // group name. The first registered keeps it; the later one is reported, so a mistake in a new app.json
    // cannot break one already running.
    private static List<CatalogEntry> RejectCollisions(List<CatalogEntry> entries)
    {
        var portOwners = new Dictionary<int, string>();
        var nameOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Error is not null)
            {
                continue;
            }

            var label = entry.Registration.Label;
            var ports = entry.Type!.HostPorts(entry.Definition!).ToList();
            var names = new[] { entry.Definition!.Name, entry.GroupName }.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var takenPort = ports.FirstOrDefault(portOwners.ContainsKey);
            if (takenPort != 0)
            {
                entries[i] = entry with { Error = $"Host port {takenPort} is already published by '{portOwners[takenPort]}'." };
                continue;
            }

            var takenName = names.FirstOrDefault(nameOwners.ContainsKey);
            if (takenName is not null)
            {
                entries[i] = entry with { Error = $"The name '{takenName}' is already used by '{nameOwners[takenName]}'." };
                continue;
            }

            foreach (var port in ports)
            {
                portOwners[port] = label;
            }

            foreach (var name in names)
            {
                nameOwners[name] = label;
            }
        }

        return entries;
    }

    // Prefixes container, image and resource names, so it has to suit all three.
    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex AppName();

    // Aspire's rule for resource names.
    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*(-[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceName();
}
