# ── Build Stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Install Node.js for frontend build
RUN apt-get update && apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    rm -rf /var/lib/apt/lists/*

# Copy solution and project files first (layer caching)
COPY Vda5050FleetController.sln ./
COPY Vda5050FleetController/Vda5050FleetController.csproj ./Vda5050FleetController/
COPY FleetManager.Tests/FleetManager.Tests.csproj         ./FleetManager.Tests/

RUN dotnet restore

# Copy the rest of the source
COPY Vda5050FleetController/ ./Vda5050FleetController/
COPY FleetManager.Tests/     ./FleetManager.Tests/

# Build frontend (TypeScript/WebComponents)
WORKDIR /src/Vda5050FleetController/wwwroot
RUN npm install && npm run build

WORKDIR /src

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

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Non-root user for security
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
USER appuser

COPY --from=build /app/publish ./

EXPOSE 8080

ENTRYPOINT ["dotnet", "Vda5050FleetController.dll"]
