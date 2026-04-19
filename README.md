# FleetManager — VDA5050 Fleet Controller

ASP.NET Core 8 service for orchestrating AGV fleets via the **VDA5050 v2** protocol over MQTT.

## Architecture

```
Vda5050FleetController/
├── Application/          # Fleet orchestration (FleetController, Registry, Queue)
├── Domain/               # Business models (Vehicle, TransportOrder, TopologyMap)
├── Infrastructure/       # MQTT service and background service
├── wwwroot/              # Frontend (TypeScript WebComponents)
│   ├── src/
│   │   ├── components/   # WebComponent definitions
│   │   ├── services/     # SignalR service
│   │   ├── types/        # TypeScript type definitions
│   │   └── styles/       # CSS stylesheets
│   └── dist/             # Bundled output
└── Program.cs            # Minimal API endpoints and DI configuration
```

**Patterns:** Clean Architecture · Event-driven (MQTT Pub/Sub) · Singleton service registration · WebComponents UI

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/` | Live dashboard (SignalR) |
| `GET` | `/fleet/status` | Current fleet status (all vehicles and order queue) |
| `POST` | `/fleet/orders` | Submit a transport order (from WMS/MFR) |
| `POST` | `/fleet/vehicles/{vehicleId}/pause` | Pause a vehicle (`stopPause` instant action) |
| `POST` | `/fleet/vehicles/{vehicleId}/resume` | Resume a vehicle (`startPause` instant action) |
| `POST` | `/fleet/vehicles/{vehicleId}/charge` | Initiate charging (`startCharging` instant action) |

**SignalR Hub:** `/hubs/fleet-status` with event `fleetStatusUpdated`

**Swagger UI** is available at `/swagger` in development mode.

### Transport Order (Request Body)

```json
{
  "sourceStationId": "ST01",
  "destStationId":   "ST05",
  "loadId":          "PAL-42"
}
```

## Configuration

`appsettings.json`:

```json
{
  "Mqtt": {
    "Host":          "localhost",
    "Port":          1883,
    "ClientId":      "fleet-controller-01",
    "InterfaceName": "uagv",
    "MajorVersion":  "v2"
  }
}
```

MQTT topics follow the VDA5050 convention: `{interface}/{version}/{manufacturer}/{serial}/{messageType}`

## Domain Models

- **Vehicle** — Vehicle state (position, battery, status, errors)
- **TransportOrder** — Order state machine: `Pending → Assigned → InProgress → Completed/Failed`
- **TopologyMap** — Graph-based routing; builds VDA5050-compliant node/edge sequences with pick/drop actions

## Demo-Modus

Zum Ausprobieren ohne echte Hardware gibt es einen AGV-Simulator, der VDA5050-konforme
Fahrzeuge simuliert. Er wird als separater Docker-Container per Compose-Overlay gestartet:

```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up --build
```

Danach das Live-Dashboard unter **http://localhost:8080** öffnen und einen
Transportauftrag abschicken — die Fahrzeuge melden sich automatisch an und
fahren den Auftrag ab.

Details zur Konfiguration des Simulators: [`docker/README.md`](docker/README.md)

## Prerequisites

- .NET 8 SDK
- Node.js 20+ (for frontend build)
- MQTT broker (e.g. Mosquitto) on `localhost:1883`

## Build

### Frontend (TypeScript/WebComponents)

```bash
# Build the frontend
./build-frontend.sh

# Or manually:
cd Vda5050FleetController/wwwroot
npm install
npm run build
```

### Backend (.NET)

```bash
dotnet build
```

## Run

```bash
dotnet run --project Vda5050FleetController
```
