using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Genius.AppHost.Configuration;
using Genius.AppHost.Resources;
using Genius.AppHost.Types;

namespace Genius.AppHost.Tests.Types;

public sealed class AtomWebAppTypeTests
{
    private static readonly HostSettings Host = new()
    {
        AppsFile = "/config/apps.json",
        AppsRoot = "/apps",
        Registry = "ghcr.io/hwndmaster",
        BindAddress = "0.0.0.0",
        PublicHost = "192.168.1.20",
        OtlpGrpcEndpoint = "http://host.docker.internal:21100",
        OtlpHttpEndpoint = "http://host.docker.internal:21101",
        ImagePullPolicy = ImagePullPolicy.Missing,
    };

    private readonly AtomWebAppType _type = new();

    [Fact]
    public void AddResources_GroupsPersistentApiAndWebUnderTheApp()
    {
        var (builder, group) = Build(PrepperBox());

        var api = Container(builder, "prepper-box-api");
        var web = Container(builder, "prepper-box-web");

        foreach (var container in new[] { api, web })
        {
            Assert.Equal(container.Name, container.Annotations.OfType<ContainerNameAnnotation>().Single().Name);
            Assert.Equal(ContainerLifetime.Persistent, container.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime);
            Assert.Same(group.Resource, container.Annotations.OfType<ResourceRelationshipAnnotation>().Single(r => r.Type == "Parent").Resource);
        }

        var image = api.Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("ghcr.io/hwndmaster/prepper-box-api", image.Image);
        Assert.Equal("1.2.3", image.Tag);
    }

    // An Aspire endpoint without a fixed port gets a new random port on every start, which recreates the
    // persistent containers each time the app-host restarts. Ports are published with -p instead.
    [Fact]
    public void AddResources_DeclaresNoEndpoints_AndLinksThePublicUrls()
    {
        var (builder, _) = Build(PrepperBox());

        var api = Container(builder, "prepper-box-api");
        var web = Container(builder, "prepper-box-web");

        Assert.Empty(api.Annotations.OfType<EndpointAnnotation>());
        Assert.Empty(web.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal(["http://192.168.1.20:5095"], api.Annotations.OfType<ResourceUrlAnnotation>().Select(url => url.Url));
        Assert.Equal(
            ["http://192.168.1.20:5096", "https://192.168.1.20:5097"],
            web.Annotations.OfType<ResourceUrlAnnotation>().Select(url => url.Url));
    }

    [Fact]
    public async Task AddResources_ApiRuntimeArgs_PublishMountLimitLabelAndRotateLogs()
    {
        var (builder, _) = Build(PrepperBox());

        var args = await RuntimeArgs(Container(builder, "prepper-box-api"));

        AssertPair(args, "-p", "0.0.0.0:5095:8045");
        AssertPair(args, "-v", "C:/PrepperBox/Data:/app/Data");
        AssertPair(args, "--memory", "384m");
        AssertPair(args, "--add-host", "host.docker.internal:host-gateway");
        AssertPair(args, "--label", "com.docker.compose.project=prepper-box");
        AssertPair(args, "--label", "com.docker.compose.service=prepper-box-api");
        AssertPair(args, "--log-opt", "max-size=10m");
        AssertPair(args, "--log-opt", "max-file=3");
        Assert.DoesNotContain("--network", args);
    }

    [Fact]
    public async Task AddResources_WebRuntimeArgs_IncludeExtraPortsAndReadOnlyMounts()
    {
        var (builder, _) = Build(PrepperBox());

        var args = await RuntimeArgs(Container(builder, "prepper-box-web"));

        AssertPair(args, "-p", "0.0.0.0:5096:8046");
        AssertPair(args, "-p", "0.0.0.0:5097:8443");
        AssertPair(args, "-v", "C:/PrepperBox/certs:/etc/nginx/certs:ro");
        Assert.DoesNotContain("--memory", args);
    }

    [Fact]
    public async Task AddResources_ApiEnvironment_SendsTelemetryAsItsResourceName()
    {
        var (builder, _) = Build(PrepperBox());

        var environment = await Environment(Container(builder, "prepper-box-api"));

        Assert.Equal("http://host.docker.internal:21100", environment["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Equal("grpc", environment["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        Assert.Equal("prepper-box-api", environment["OTEL_SERVICE_NAME"]);
        Assert.Equal("serp-key", environment["ImageSearch__SerpApiKey"]);
        // Unset optional secrets stay out, so their integration remains disabled.
        Assert.False(environment.ContainsKey("Telegram__BotToken"));
    }

    [Fact]
    public async Task AddResources_WebEnvironment_PointsNginxAtApiAndAppHost()
    {
        var (builder, _) = Build(PrepperBox());

        var environment = await Environment(Container(builder, "prepper-box-web"));

        Assert.Equal("http://prepper-box-api:8045", environment["PREPPER_BOX_API_UPSTREAM"]);
        Assert.Equal("http://host.docker.internal:21101", environment["PREPPER_BOX_OTLP_UPSTREAM"]);
    }

    [Fact]
    public void HostPorts_ListsEveryPublishedPort()
    {
        Assert.Equal([5095, 5096, 5097], _type.HostPorts(PrepperBox()).Order());
    }

    [Fact]
    public void Validate_ValidDefinition_Passes()
    {
        _type.Validate(PrepperBox());
    }

    private static readonly Dictionary<string, AppDefinition> InvalidDefinitions = new(StringComparer.Ordinal)
    {
        ["no web"] = PrepperBox() with { Web = null },
        ["empty tag"] = PrepperBox() with { ImageTag = " " },
        ["bad memory"] = PrepperBox() with { Api = PrepperBox().Api! with { Memory = "lots" } },
        ["port out of range"] = PrepperBox() with { Api = PrepperBox().Api! with { HostPort = 70000 } },
        ["port twice"] = PrepperBox() with { Web = PrepperBox().Web! with { HostPort = 5095 } },
        ["bad scheme"] = PrepperBox() with { Web = PrepperBox().Web! with { Ports = [new() { ContainerPort = 8443, HostPort = 5097, Scheme = "ftp" }] } },
        ["relative mount"] = PrepperBox() with { Api = PrepperBox().Api! with { Volumes = [new() { Host = "C:/x", Container = "app/Data" }] } },
        ["bad variable"] = PrepperBox() with { Api = PrepperBox().Api! with { Environment = new Dictionary<string, string> { ["bad-name"] = "x" } } },
    };

    [Theory]
    [InlineData("no web", "needs a 'web' section")]
    [InlineData("empty tag", "imageTag is empty")]
    [InlineData("bad memory", "api.memory must be a docker memory size")]
    [InlineData("port out of range", "api.hostPort must be a port number")]
    [InlineData("port twice", "host port 5095 more than once")]
    [InlineData("bad scheme", "scheme must be http or https")]
    [InlineData("relative mount", "absolute container path")]
    [InlineData("bad variable", "'bad-name' is not a valid variable name")]
    public void Validate_InvalidDefinition_NamesTheField(string definition, string expected)
    {
        var exception = Assert.Throws<ConfigurationException>(() => _type.Validate(InvalidDefinitions[definition]));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    private (IDistributedApplicationBuilder Builder, IResourceBuilder<AppGroupResource> Group) Build(AppDefinition app)
    {
        var builder = DistributedApplication.CreateBuilder();
        var group = builder.AddResource(new AppGroupResource(app.DisplayName!));
        _type.AddResources(builder, app, group, Host);
        return (builder, group);
    }

    private static ContainerResource Container(IDistributedApplicationBuilder builder, string name) =>
        builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == name);

    private static async Task<List<string>> RuntimeArgs(ContainerResource container)
    {
        var args = new List<object>();
        foreach (var annotation in container.Annotations.OfType<ContainerRuntimeArgsCallbackAnnotation>())
        {
            await annotation.Callback(new ContainerRuntimeArgsCallbackContext(args, CancellationToken.None));
        }

        return args.Select(arg => arg.ToString()!).ToList();
    }

    private static async Task<Dictionary<string, string>> Environment(ContainerResource container)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run), container, values, CancellationToken.None);
        foreach (var annotation in container.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return values.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()!, StringComparer.Ordinal);
    }

    private static void AssertPair(List<string> args, string flag, string value)
    {
        var found = Enumerable.Range(0, args.Count - 1).Any(i => args[i] == flag && args[i + 1] == value);
        Assert.True(found, $"Expected '{flag} {value}' in: {string.Join(' ', args)}");
    }

    private static AppDefinition PrepperBox() => new()
    {
        Name = "prepper-box",
        DisplayName = "PrepperBox",
        Type = "atom-webapp",
        ImageTag = "1.2.3",
        Api = new ContainerDefinition
        {
            ContainerPort = 8045,
            HostPort = 5095,
            Memory = "384m",
            Volumes = [new() { Host = "C:/PrepperBox/Data", Container = "/app/Data" }],
            Environment = new Dictionary<string, string>
            {
                ["ImageSearch__SerpApiKey"] = "serp-key",
                ["Telegram__BotToken"] = string.Empty,
            },
        },
        Web = new ContainerDefinition
        {
            ContainerPort = 8046,
            HostPort = 5096,
            Ports = [new() { ContainerPort = 8443, HostPort = 5097, Scheme = "https" }],
            Volumes = [new() { Host = "C:/PrepperBox/certs", Container = "/etc/nginx/certs", ReadOnly = true }],
        },
    };
}
