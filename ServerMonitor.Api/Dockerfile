# Build context is the repository root: the projects reference each other, so a narrower
# context would not see them.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first and restore. This layer is cached as long as no dependency changes,
# so editing code does not re-download every package.
COPY ServerMonitor.Domain/*.csproj ServerMonitor.Domain/
COPY ServerMonitor.Infrastructure/*.csproj ServerMonitor.Infrastructure/
COPY ServerMonitor.Api/*.csproj ServerMonitor.Api/
RUN dotnet restore ServerMonitor.Api/ServerMonitor.Api.csproj

COPY ServerMonitor.Domain/ ServerMonitor.Domain/
COPY ServerMonitor.Infrastructure/ ServerMonitor.Infrastructure/
COPY ServerMonitor.Api/ ServerMonitor.Api/
RUN dotnet publish ServerMonitor.Api/ServerMonitor.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# TLS is the reverse proxy's job; inside the network this speaks plain HTTP.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Not root: a container process that does not need to write anywhere should not be able to.
USER $APP_UID

ENTRYPOINT ["dotnet", "ServerMonitor.Api.dll"]
