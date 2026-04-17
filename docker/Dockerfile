# ── Build Stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first (layer caching)
COPY Vda5050FleetController.sln ./
COPY Vda5050FleetController/Vda5050FleetController.csproj ./Vda5050FleetController/
COPY FleetManager.Tests/FleetManager.Tests.csproj         ./FleetManager.Tests/

RUN dotnet restore

# Copy the rest of the source
COPY Vda5050FleetController/ ./Vda5050FleetController/
COPY FleetManager.Tests/     ./FleetManager.Tests/

# Run unit tests during build
RUN dotnet test FleetManager.Tests/FleetManager.Tests.csproj \
    --no-restore \
    --configuration Release \
    --logger "console;verbosity=minimal"

# Publish the main service
RUN dotnet publish Vda5050FleetController/Vda5050FleetController.csproj \
    --no-restore \
    --configuration Release \
    --output /app/publish

# ── Runtime Stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish ./

EXPOSE 8080

ENTRYPOINT ["dotnet", "Vda5050FleetController.dll"]
