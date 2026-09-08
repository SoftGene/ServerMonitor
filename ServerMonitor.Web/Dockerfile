FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ServerMonitor.Web/*.csproj ServerMonitor.Web/
RUN dotnet restore ServerMonitor.Web/ServerMonitor.Web.csproj

COPY ServerMonitor.Web/ ServerMonitor.Web/
RUN dotnet publish ServerMonitor.Web/ServerMonitor.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "ServerMonitor.Web.dll"]
