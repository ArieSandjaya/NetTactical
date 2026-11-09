# syntax=docker/dockerfile:1
# NetTactical API (ASP.NET Core 8). Build context = repository root.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
# Restore first so the layer is cached until a csproj changes.
COPY src/TacticalRmm.Domain/*.csproj          src/TacticalRmm.Domain/
COPY src/TacticalRmm.Application/*.csproj     src/TacticalRmm.Application/
COPY src/TacticalRmm.Infrastructure/*.csproj  src/TacticalRmm.Infrastructure/
COPY src/TacticalRmm.Api/*.csproj             src/TacticalRmm.Api/
RUN dotnet restore src/TacticalRmm.Api/TacticalRmm.Api.csproj
COPY src/ src/
RUN dotnet publish src/TacticalRmm.Api/TacticalRmm.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Shared with the NATS container: the API writes nats-rmm.conf here. A new named volume copies this ownership.
RUN mkdir -p /data/nats && chown app:app /data/nats
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Tactical__NatsConfPath=/data/nats/nats-rmm.conf
EXPOSE 8080
ENTRYPOINT ["dotnet", "TacticalRmm.Api.dll"]
