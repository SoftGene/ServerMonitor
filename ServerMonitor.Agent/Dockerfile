# Provided for completeness, and deliberately absent from docker-compose.yml.
#
# An agent in a container measures the container, not the host: memory is capped by the cgroup
# and the disk is an image layer rather than the machine's drive. The numbers would look
# plausible and be wrong, which is the worst kind of monitoring failure.
#
# To watch the host from a container you need --pid=host, /proc mounted read-only, and you still
# inherit caveats. On the machine you actually want to watch, run the agent as a normal program
# or a systemd unit.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ServerMonitor.Domain/*.csproj ServerMonitor.Domain/
COPY ServerMonitor.Collection/*.csproj ServerMonitor.Collection/
COPY ServerMonitor.Agent/*.csproj ServerMonitor.Agent/
RUN dotnet restore ServerMonitor.Agent/ServerMonitor.Agent.csproj

COPY ServerMonitor.Domain/ ServerMonitor.Domain/
COPY ServerMonitor.Collection/ ServerMonitor.Collection/
COPY ServerMonitor.Agent/ ServerMonitor.Agent/
RUN dotnet publish ServerMonitor.Agent/ServerMonitor.Agent.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
USER $APP_UID

ENTRYPOINT ["dotnet", "ServerMonitor.Agent.dll"]
