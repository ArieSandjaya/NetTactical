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
# Optional: PowerShell 7 for "Push install" (WinRM). PSWSMan adds the WSMan client libraries that PowerShell on Linux does not ship with. docker compose build --build-arg WITH_POWERSHELL=true
ARG WITH_POWERSHELL=false
ARG PWSH_VERSION=7.4.6
USER root
RUN if [ "$WITH_POWERSHELL" = "true" ]; then \
      apt-get update && apt-get install -y --no-install-recommends curl ca-certificates libicu72 gss-ntlmssp \
      && curl -fsSL -o /tmp/pwsh.tgz "https://github.com/PowerShell/PowerShell/releases/download/v${PWSH_VERSION}/powershell-${PWSH_VERSION}-linux-x64.tar.gz" \
      && mkdir -p /opt/microsoft/powershell/7 && tar -xzf /tmp/pwsh.tgz -C /opt/microsoft/powershell/7 && chmod +x /opt/microsoft/powershell/7/pwsh \
      && ln -s /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh && rm -f /tmp/pwsh.tgz && rm -rf /var/lib/apt/lists/*; \
    fi
# PSWSMan adds the WSMan client libraries (libmi, libpsrpclient) that PowerShell on Linux lacks. This step is separate and checks its own result:
# without it every push install fails with "no supported WSMan client library was found". If it fails, the build stops here with the reason.
RUN if [ "$WITH_POWERSHELL" = "true" ]; then \
      pwsh -NoProfile -NonInteractive -Command "\$ErrorActionPreference='Stop'; \$ProgressPreference='SilentlyContinue'; Set-PSRepository -Name PSGallery -InstallationPolicy Trusted; Save-Module -Name PSWSMan -Repository PSGallery -Path /opt/microsoft/powershell/7/Modules -Force -AcceptLicense; Import-Module PSWSMan -Force; Install-WSMan" \
      && pwsh -NoProfile -NonInteractive -Command "try { Test-WSMan -ComputerName localhost -ErrorAction Stop | Out-Null } catch { if (\$_.Exception.Message -match 'WSMan client library|no supported WSMan') { Write-Error 'PSWSMan is not working'; exit 1 } }" \
      || { echo 'ERROR: the WSMan client for PowerShell could not be installed. Build with WITH_POWERSHELL=false in .env to skip Push install.'; exit 1; }; \
    fi
# Shared with the NATS container: the API writes nats-rmm.conf here. A new named volume copies this ownership.
RUN mkdir -p /data/nats && chown app:app /data/nats
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Tactical__NatsConfPath=/data/nats/nats-rmm.conf
EXPOSE 8080
ENTRYPOINT ["dotnet", "TacticalRmm.Api.dll"]
