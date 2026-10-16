using Genius.AppHost.Configuration;

namespace Genius.AppHost.Tests.Configuration;

public sealed class AppCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "app-host-tests", Guid.NewGuid().ToString("N"));

    public AppCatalogTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Load_ValidApp_ExpandsVariablesFromItsEnv()
    {
        WriteApps("""{ "apps": [ { "name": "my-app", "path": "my-app" } ] }""");
        WriteApp("my-app", SampleApp("my-app"),
            """
            MY_APP_DATA_PATH=C:/MyApp/Data
            MY_APP_API_PORT=6090
            """);

        var entry = Assert.Single(Load());

        Assert.Null(entry.Error);
        Assert.Equal("MyApp", entry.GroupName);
        Assert.Equal("atom-webapp", entry.Type!.Name);
        Assert.Equal(6090, entry.Definition!.Api!.HostPort);
        Assert.Equal("latest", entry.Definition.ImageTag);
        Assert.Equal("C:/MyApp/Data", entry.Definition.Api.Volumes[0].Host);
    }

    [Fact]
    public void Load_DisabledApp_IsLeftOut()
    {
        WriteApps("""{ "apps": [ { "name": "my-app", "path": "my-app", "enabled": false } ] }""");

        Assert.Empty(Load());
    }

    [Fact]
    public void Load_MissingAppsFile_StopsTheHost()
    {
        var exception = Assert.Throws<ConfigurationException>(Load);

        Assert.Contains("apps.example.json", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "apps": [ { "path": "a" }, { "path": "A/" } ] }""", "more than once")]
    [InlineData("""{ "apps": [ { "name": "a", "path": "a", "colour": "blue" } ] }""", "colour")]
    [InlineData("""{ "apps": [ { "name": "a" } ] }""", "path")]
    public void Load_BrokenAppsFile_StopsTheHost(string json, string expected)
    {
        WriteApps(json);

        var exception = Assert.Throws<ConfigurationException>(Load);

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_OneBrokenApp_LeavesTheOthersLoaded()
    {
        WriteApps("""{ "apps": [ { "name": "missing", "path": "missing" }, { "name": "my-app", "path": "my-app" } ] }""");
        WriteApp("my-app", SampleApp("my-app"), "MY_APP_DATA_PATH=C:/Data");

        var entries = Load();

        Assert.Contains("app.json does not exist", entries[0].Error, StringComparison.Ordinal);
        Assert.Null(entries[1].Error);
    }

    [Theory]
    [InlineData(null, ".env.example")]
    [InlineData("OTHER=1", "MY_APP_DATA_PATH is not set")]
    public void Load_EnvProblems_AreReportedForTheApp(string? env, string expected)
    {
        WriteApps("""{ "apps": [ { "name": "my-app", "path": "my-app" } ] }""");
        WriteApp("my-app", SampleApp("my-app"), env);

        var entry = Assert.Single(Load());

        Assert.Contains(expected, entry.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_UnresolvedVariable_NamesTheJsonPath()
    {
        WriteApps("""{ "apps": [ { "name": "my-app", "path": "my-app" } ] }""");
        WriteApp("my-app", SampleApp("my-app"), "OTHER=1");

        var entry = Assert.Single(Load());

        Assert.Contains("$.api.volumes[0].host", entry.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "apps": [ { "name": "My-App-Folder", "path": "my-app" } ] }""", "My-App-Folder")]
    [InlineData("""{ "apps": [ { "path": "my-app" } ] }""", "my-app")]
    public void Load_NameInAppsJson_IsOnlyALabel(string json, string label)
    {
        WriteApps(json);
        WriteApp("my-app", SampleApp("my-app"), "MY_APP_DATA_PATH=C:/Data");

        var entry = Assert.Single(Load());

        Assert.Null(entry.Error);
        Assert.Equal("my-app", entry.Definition!.Name);
        Assert.Equal(label, entry.Registration.Label);
    }

    [Fact]
    public void Load_InvalidAppName_IsReported()
    {
        WriteApps("""{ "apps": [ { "path": "bad" } ] }""");
        WriteApp("bad", SampleApp("My_App"), "MY_APP_DATA_PATH=C:/Data");

        var entry = Assert.Single(Load());

        Assert.Contains("name 'My_App' is not valid", entry.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_SameAppNameInTwoFolders_RejectsTheLaterOne()
    {
        WriteApps("""{ "apps": [ { "path": "one" }, { "path": "two" } ] }""");
        WriteApp("one", SampleApp("my-app"), "MY_APP_DATA_PATH=C:/a");
        WriteApp("two", SampleApp("my-app", displayName: "Other"), "MY_APP_DATA_PATH=C:/b\nMY_APP_API_PORT=6090\nMY_APP_WEB_PORT=6091");

        var entries = Load();

        Assert.Null(entries[0].Error);
        Assert.Contains("The name 'my-app' is already used by 'one'", entries[1].Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"type\": \"atom-webapp\"", "\"colour\": \"blue\", \"type\": \"atom-webapp\"", "colour")]
    [InlineData("\"type\": \"atom-webapp\"", "\"type\": \"spa\"", "unknown type 'spa'")]
    [InlineData("\"displayName\": \"MyApp\"", "\"displayName\": \"My App\"", "cannot name a resource")]
    public void Load_InvalidAppFile_IsReported(string original, string replacement, string expected)
    {
        WriteApps("""{ "apps": [ { "name": "my-app", "path": "my-app" } ] }""");
        WriteApp("my-app", SampleApp("my-app").Replace(original, replacement, StringComparison.Ordinal), "MY_APP_DATA_PATH=C:/Data");

        var entry = Assert.Single(Load());

        Assert.Contains(expected, entry.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_SameHostPortInTwoApps_RejectsTheLaterOne()
    {
        WriteApps("""{ "apps": [ { "name": "first", "path": "first" }, { "name": "second", "path": "second" } ] }""");
        WriteApp("first", SampleApp("first", displayName: "First"), "FIRST_DATA_PATH=C:/a");
        WriteApp("second", SampleApp("second", displayName: "Second"), "SECOND_DATA_PATH=C:/b");

        var entries = Load();

        Assert.Null(entries[0].Error);
        Assert.Contains("Host port 5090 is already published by 'first'", entries[1].Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_PathOutsideTheRoot_IsReported()
    {
        WriteApps("""{ "apps": [ { "name": "escape", "path": "../elsewhere" } ] }""");

        var entry = Assert.Single(Load());

        Assert.Contains("leads outside the apps root", entry.Error, StringComparison.Ordinal);
    }

    private IReadOnlyList<CatalogEntry> Load() =>
        AppCatalog.Load(Path.Combine(_root, "apps.json"), Path.Combine(_root, "apps"));

    private void WriteApps(string json) =>
        File.WriteAllText(Path.Combine(_root, "apps.json"), json);

    private void WriteApp(string folder, string appJson, string? env)
    {
        var path = Path.Combine(_root, "apps", folder);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "app.json"), appJson);
        if (env is not null)
        {
            File.WriteAllText(Path.Combine(path, ".env"), env);
        }
    }

    private static string SampleApp(string name, string displayName = "MyApp")
    {
        var prefix = name.ToUpperInvariant().Replace('-', '_');
        return $$"""
            {
              // comments and trailing commas are allowed
              "$schema": "../app-host/app.schema.json",
              "name": "{{name}}",
              "displayName": "{{displayName}}",
              "type": "atom-webapp",
              "imageTag": "${{{prefix}}_IMAGE_TAG:-latest}",
              "api": {
                "containerPort": 8041,
                "hostPort": "${{{prefix}}_API_PORT:-5090}",
                "memory": "512m",
                "volumes": [ { "host": "${{{prefix}}_DATA_PATH}", "container": "/app/Data" } ],
              },
              "web": { "containerPort": 8042, "hostPort": "${{{prefix}}_WEB_PORT:-5091}", "memory": "64m" },
            }
            """;
    }
}
