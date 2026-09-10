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
# Where the key ring is kept; compose mounts a volume here. Created as root and handed to the app
# user now, because a volume mounted onto a directory that does not exist in the image comes up
# owned by root — and the app, which does not run as root, could not write its keys there.
RUN mkdir -p /keys && chown $APP_UID /keys
USER $APP_UID

ENTRYPOINT ["dotnet", "ServerMonitor.Web.dll"]
