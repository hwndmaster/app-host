using Genius.AppHost.Configuration;
using Genius.AppHost.Resources;

namespace Genius.AppHost.Types;

/// <summary>
/// The Genius/Atom application: an ASP.NET Core API and an nginx container serving the SPA, from the images
/// <c>&lt;registry&gt;/&lt;name&gt;-api</c> and <c>&lt;registry&gt;/&lt;name&gt;-web</c>.
/// </summary>
internal sealed class AtomWebAppType : IAppType
{
    public string Name => "atom-webapp";

    public void Validate(AppDefinition app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (string.IsNullOrWhiteSpace(app.ImageTag))
        {
            throw new ConfigurationException("app.json: imageTag is empty.");
        }

        ValidateContainer(app.Api ?? throw new ConfigurationException("app.json: an atom-webapp needs an 'api' section."), "api");
        ValidateContainer(app.Web ?? throw new ConfigurationException("app.json: an atom-webapp needs a 'web' section."), "web");

        var duplicate = HostPorts(app).GroupBy(port => port).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ConfigurationException($"app.json publishes host port {duplicate.Key} more than once.");
        }
    }

    public IEnumerable<int> HostPorts(AppDefinition app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return new[] { app.Api!, app.Web! }
            .SelectMany(container => container.Ports.Select(port => port.HostPort).Prepend(container.HostPort));
    }

    public void AddResources(
        IDistributedApplicationBuilder builder, AppDefinition app, IResourceBuilder<AppGroupResource> group, HostSettings host)
    {
        ArgumentNullException.ThrowIfNull(app);

        var registry = app.Registry ?? host.Registry;
        var apiName = $"{app.Name}-api";
        var webName = $"{app.Name}-web";

        var api = AddContainer(builder, app, group, host, apiName, $"{registry}/{apiName}", app.Api!)
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", host.OtlpGrpcEndpoint)
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc")
            .WithEnvironment("OTEL_EXPORTER_OTLP_INSECURE", "true")
            // Aspire injects this for projects but not for containers, and without it every log line and span
            // arrives as "unknown_service:dotnet". Equal to the resource name, which is what ties the resource
            // to its telemetry on the dashboard.
            .WithEnvironment("OTEL_SERVICE_NAME", apiName);

        // nginx in the web image renders its config from <PREFIX>_API_UPSTREAM and <PREFIX>_OTLP_UPSTREAM, the
        // prefix being the name upper-cased with hyphens as underscores (PREPPER_BOX).
        var envPrefix = app.Name.ToUpperInvariant().Replace('-', '_');
        // A string first: an interpolated string passed straight in binds to the ReferenceExpression overload.
        // The api resolves by name: DCP puts every container it owns on one network of its own.
        var apiUpstream = $"http://{apiName}:{app.Api!.ContainerPort}";

        AddContainer(builder, app, group, host, webName, $"{registry}/{webName}", app.Web!)
            .WithEnvironment($"{envPrefix}_API_UPSTREAM", apiUpstream)
            .WithEnvironment($"{envPrefix}_OTLP_UPSTREAM", host.OtlpHttpEndpoint)
            .WaitFor(api);
    }

    private static IResourceBuilder<ContainerResource> AddContainer(
        IDistributedApplicationBuilder builder,
        AppDefinition app,
        IResourceBuilder<AppGroupResource> group,
        HostSettings host,
        string resourceName,
        string image,
        ContainerDefinition definition)
    {
        // No "--network" anywhere. DCP reconciles the networks of the containers it owns, and one it has no
        // record of makes it try to detach the container from it, which fails the start with only "Failed to
        // start" on the Resources page.
        var container = builder.AddContainer(resourceName, image, app.ImageTag)
            .WithParentRelationship(group)
            .WithContainerName(resourceName)
            .WithImagePullPolicy(host.ImagePullPolicy)
            // Persistent: stopping or restarting the app-host must not take the application down with it.
            .WithLifetime(ContainerLifetime.Persistent)
            // Published with -p rather than an Aspire endpoint. DCP gives an endpoint without a fixed port a new
            // random loopback port on every start, which changes the container's configuration and so recreates
            // the persistent container each time the app-host restarts.
            .WithContainerRuntimeArgs("-p", $"{host.BindAddress}:{definition.HostPort}:{definition.ContainerPort}")
            .WithUrl(host.PublicUrl("http", definition.HostPort))
            // Docker Desktop resolves host.docker.internal by itself; a plain Linux daemon needs the mapping.
            .WithContainerRuntimeArgs("--add-host", "host.docker.internal:host-gateway")
            // Docker Desktop groups containers by these labels. Without them a container inherits the labels
            // baked into its image by `docker compose build`, and lands in an unrelated group.
            .WithContainerRuntimeArgs("--label", $"com.docker.compose.project={app.Name}")
            .WithContainerRuntimeArgs("--label", $"com.docker.compose.service={resourceName}")
            // Docker's json-file driver keeps a container's output forever unless told otherwise.
            .WithContainerRuntimeArgs("--log-opt", "max-size=10m", "--log-opt", "max-file=3");

        if (definition.Memory is { } memory)
        {
            container.WithContainerRuntimeArgs("--memory", memory);
        }

        // As raw -v rather than WithBindMount: WithBindMount normalises the path with this container's Linux
        // conventions, so a server path like C:/Docker/my-app/Data became relative and was prefixed with the
        // working directory. As -v the string reaches the daemon, which resolves it, untouched.
        foreach (var volume in definition.Volumes)
        {
            container.WithContainerRuntimeArgs("-v", $"{volume.Host}:{volume.Container}{(volume.ReadOnly ? ":ro" : string.Empty)}");
        }

        foreach (var port in definition.Ports)
        {
            container
                .WithContainerRuntimeArgs("-p", $"{host.BindAddress}:{port.HostPort}:{port.ContainerPort}")
                .WithUrl(host.PublicUrl(port.Scheme, port.HostPort));
        }

        foreach (var (key, value) in definition.Environment)
        {
            if (value.Length > 0)
            {
                container.WithEnvironment(key, value);
            }
        }

        return container;
    }

    private static void ValidateContainer(ContainerDefinition container, string section)
    {
        ValidatePort(container.ContainerPort, $"{section}.containerPort");
        ValidatePort(container.HostPort, $"{section}.hostPort");

        if (container.Memory is { } memory)
        {
            DockerMemorySize.Validate(memory, $"app.json {section}.memory");
        }

        for (var i = 0; i < container.Volumes.Count; i++)
        {
            var volume = container.Volumes[i];
            if (string.IsNullOrWhiteSpace(volume.Host) || !volume.Container.StartsWith('/'))
            {
                throw new ConfigurationException(
                    $"app.json {section}.volumes[{i}] needs a server path in 'host' and an absolute container path in 'container'.");
            }
        }

        for (var i = 0; i < container.Ports.Count; i++)
        {
            var port = container.Ports[i];
            ValidatePort(port.ContainerPort, $"{section}.ports[{i}].containerPort");
            ValidatePort(port.HostPort, $"{section}.ports[{i}].hostPort");
            if (port.Scheme is not ("http" or "https"))
            {
                throw new ConfigurationException($"app.json {section}.ports[{i}].scheme must be http or https, but was '{port.Scheme}'.");
            }
        }

        if (container.Environment.Keys.FirstOrDefault(key => !EnvFile.VariableName().IsMatch(key)) is { } badKey)
        {
            throw new ConfigurationException($"app.json {section}.environment: '{badKey}' is not a valid variable name.");
        }
    }

    private static void ValidatePort(int port, string field)
    {
        if (port is < 1 or > 65535)
        {
            throw new ConfigurationException($"app.json {field} must be a port number between 1 and 65535, but was {port}.");
        }
    }
}
