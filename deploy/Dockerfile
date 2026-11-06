# The game server. Build from the repository root:
#   docker build -f deploy/Dockerfile -t chaos-idle-server .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/ChaosIdle.Server/ChaosIdle.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# The image's built-in unprivileged user.
USER app
ENTRYPOINT ["dotnet", "ChaosIdle.Server.dll"]
