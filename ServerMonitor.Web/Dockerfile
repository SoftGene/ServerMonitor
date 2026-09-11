FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The whole project goes in before the restore, not the .csproj on its own as in the API's
# Dockerfile. The SDK adds the package that carries the Blazor script, _framework/blazor.web.js, only
# when the restore can see the project's components. Restored from the .csproj alone, the site was
# published without the script, and no button on any page did anything. The price is a restore that
# is no longer cached apart from the code.
COPY ServerMonitor.Web/ ServerMonitor.Web/
RUN dotnet publish ServerMonitor.Web/ServerMonitor.Web.csproj -c Release -o /app

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
