# ---- Build Stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from the files that decide it, so editing code does not invalidate the restore layer.
COPY nuget.config Directory.Build.props Directory.Packages.props ./
COPY AppHost/AppHost.csproj AppHost/
RUN --mount=type=cache,id=app-host-nuget-packages,target=/root/.nuget/packages \
	dotnet restore AppHost/AppHost.csproj

COPY AppHost/ AppHost/
# The app host finds DCP and the dashboard through paths into the NuGet cache that are baked in at build
# time. The cache is a build-only mount, so the two Linux packages are copied out of it here, within the same
# mount, and put back at the same path in the runtime image.
RUN --mount=type=cache,id=app-host-nuget-packages,target=/root/.nuget/packages \
	dotnet build AppHost/AppHost.csproj -c Release --no-restore /p:UseAppHost=false /p:DebugType=None /p:DebugSymbols=false \
	&& mkdir -p /aspire-runtime-packages \
	&& for package in aspire.hosting.orchestration.linux-x64 aspire.dashboard.sdk.linux-x64; do \
		if [ ! -d "/root/.nuget/packages/$package" ]; then \
			echo "Required Aspire package not found in the NuGet cache: $package" >&2; \
			exit 1; \
		fi; \
		cp -a "/root/.nuget/packages/$package" /aspire-runtime-packages/; \
	done

# ---- Runtime Stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# DCP creates the applications' containers on the server's Docker daemon, whose socket compose mounts in. It
# detects the container runtime by probing for the CLI, so the binary has to be here. Copied from the
# official CLI image: one static binary, where the docker.io package would drag in a daemon.
COPY --from=docker:28-cli /usr/local/bin/docker /usr/local/bin/docker

COPY --from=build /src/AppHost/bin/Release/net10.0 .
COPY --from=build /aspire-runtime-packages /root/.nuget/packages

ENV ASPNETCORE_ENVIRONMENT=Production \
	DOTNET_ENVIRONMENT=Production \
	ASPNETCORE_URLS=http://+:15100 \
	ASPIRE_ALLOW_UNSECURED_TRANSPORT=true \
	ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://0.0.0.0:21100 \
	ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL=http://0.0.0.0:21101

EXPOSE 15100 21100 21101

ENTRYPOINT ["dotnet", "Genius.AppHost.dll"]
