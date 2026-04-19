# FleetController — Claude Code Guide

## Language

**Use English for all code, comments, identifiers, commit messages, and documentation.**
German is only acceptable in end-user-facing UI strings if explicitly requested.

## Project Overview

VDA5050 v2 Fleet Controller — orchestrates AGV fleets over MQTT.
- Backend: ASP.NET Core 8 (C#), Minimal API, SignalR, LinqToDB, PostgreSQL
- Frontend: TypeScript WebComponents, no framework, bundled with esbuild
- Broker: Eclipse Mosquitto
- Simulator: Python 3.12 (`paho-mqtt`), for demo/development use

## Repository Layout

```
FleetController/
├── Vda5050FleetController/          # ASP.NET Core service
│   ├── Application/                 # Use-cases & interfaces
│   │   ├── FleetController.cs       # Main orchestrator, all domain events
│   │   ├── IFleetPersistenceService.cs
│   │   ├── PostgresFleetPersistenceService.cs
│   │   ├── TransportOrderQueue.cs
│   │   └── VehicleRegistry.cs
│   ├── Domain/Models/
│   │   ├── DomainModels.cs          # Vehicle, TransportOrder, TopologyMap
│   │   └── Vda5050Models.cs         # VDA5050 message records (Order, State, Connection …)
│   ├── Infrastructure/
│   │   ├── Mqtt/Vda5050MqttService.cs  # MQTT pub/sub, topic helpers
│   │   └── Persistence/             # LinqToDB context, entities, repository, schema init
│   ├── Realtime/SignalRFleetStatusPublisher.cs
│   ├── wwwroot/src/                 # TypeScript frontend
│   │   ├── components/              # WebComponent definitions
│   │   ├── services/fleet-status.service.ts  # SignalR client
│   │   ├── types/models.ts          # TypeScript mirrors of C# DTOs
│   │   └── main.ts
│   └── Program.cs                   # DI, Minimal API endpoints, hosted services
├── FleetManager.Tests/              # xUnit unit tests
├── agv-simulator/                   # Demo AGV simulator (Python)
│   ├── simulator.py
│   ├── Dockerfile
│   └── requirements.txt
└── docker/
    ├── docker-compose.yml           # Production stack
    ├── docker-compose.demo.yml      # Demo overlay (adds AGV simulator)
    ├── Dockerfile                   # Multi-stage build for the .NET service
    └── mosquitto/config/
```

## Architecture

**Clean Architecture** with three layers:
- **Domain** — pure business models, no dependencies
- **Application** — use-cases, interfaces, orchestration
- **Infrastructure** — MQTT, PostgreSQL, SignalR implementations

**Event flow:**
1. MQTT message arrives → `Vda5050MqttService` parses and fires event
2. `FleetController` handles event (state update, order dispatch)
3. `VehicleRegistry` / `TransportOrderQueue` mutate in-memory state
4. `IFleetPersistenceService` persists asynchronously
5. `IFleetStatusPublisher` broadcasts via SignalR to UI

## Key Patterns

### DI Registration (Program.cs)
All core services are **singletons** (fleet state must be shared). Only the LinqToDB
`FleetDbContext` and `IFleetRepository` are **scoped** — always access them through
`IServiceScopeFactory.CreateAsyncScope()`.

### Topology
`TopologyMap` (singleton, in-memory) is the authoritative routing graph at runtime.
It is loaded from `topology_nodes` / `topology_edges` in PostgreSQL on startup by
`TopologyStartupLoader`. The demo topology is seeded when `SEED_DEMO_TOPOLOGY=true`
and the DB is empty.

### VDA5050 Topics
```
{interfaceName}/{majorVersion}/{manufacturer}/{serialNumber}/{messageType}
```
Default: `uagv/v2/{manufacturer}/{serialNumber}/{order|state|connection|instantActions}`

### Vehicle availability
A vehicle is dispatchable when: `Status == Idle && Battery > 20% && no FATAL errors`

### Order completion detection
Fleet marks order complete when receiving a `state` with matching `orderId`,
`nodeStates.Count == 0`, `edgeStates.Count == 0`, and `driving == false`.

## API Endpoints

| Method   | Path                                      | Description                        |
|----------|-------------------------------------------|------------------------------------|
| GET      | `/fleet/status`                           | Full fleet snapshot (JSON)         |
| POST     | `/fleet/orders`                           | Submit transport order             |
| POST     | `/fleet/vehicles/{vehicleId}/pause`       | Send `stopPause` instant action    |
| POST     | `/fleet/vehicles/{vehicleId}/resume`      | Send `startPause` instant action   |
| POST     | `/fleet/vehicles/{vehicleId}/charge`      | Send `startCharging` instant action|
| GET/POST | `/fleet/topology/nodes`                   | Read / upsert topology node        |
| DELETE   | `/fleet/topology/nodes/{nodeId}`          | Delete topology node               |
| GET/POST | `/fleet/topology/edges`                   | Read / upsert topology edge        |
| DELETE   | `/fleet/topology/edges/{edgeId}`          | Delete topology edge               |
| WS       | `/hubs/fleet-status` (SignalR)            | Real-time `fleetStatusUpdated`     |
| GET      | `/swagger`                                | Swagger UI (Development only)      |

**Transport order request body:**
```json
{ "sourceStationId": "IN-A", "destStationId": "OUT-B", "loadId": "PAL-42" }
```

## Database Schema

Tables: `vehicles`, `orders`, `order_history`, `topology_nodes`, `topology_edges`
ORM: LinqToDB with `InsertOrReplace` for upserts.
Schema is created on startup by `SchemaInitializer` (hosted service, idempotent).
PostgreSQL is optional — the app runs with `NoOpFleetPersistenceService` if no
connection string is configured.

## Build & Run

### Frontend
```bash
cd Vda5050FleetController/wwwroot
npm install
npm run build       # esbuild, output → dist/
```

### Backend
```bash
dotnet build
dotnet test
dotnet run --project Vda5050FleetController
```

### Docker — Production
```bash
docker compose -f docker/docker-compose.yml up --build
```

### Docker — Demo (with AGV simulator)
```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up --build
```

### Docker — with Seq (structured logs)
```bash
docker compose -f docker/docker-compose.yml --profile monitoring up --build
# Seq UI: http://localhost:5341
```

### Docker — with MQTT Explorer
```bash
docker compose -f docker/docker-compose.yml --profile tools up --build
```

## Coding Conventions

- **Language**: English — all identifiers, comments, strings, commit messages
- **C#**: Records for DTOs/messages, classes for aggregates with behaviour
- **No comments** unless the WHY is non-obvious (hidden constraints, workarounds)
- **No error handling** for impossible cases — trust DI container and framework guarantees
- **TypeScript**: WebComponents pattern, no framework; update `types/models.ts` whenever
  C# DTOs change, keeping both in sync
- **Frontend rebuild required** after any `.ts` change — the `docker/Dockerfile` does this
  automatically; for local dev run `npm run build` manually
- **Topology node IDs**: naming convention used by the demo seeder is `IN-*`, `OUT-*`,
  `CHG-*`; charging detection in the simulator is based on the `CHG` prefix

## Tests

```bash
dotnet test FleetManager.Tests/FleetManager.Tests.csproj
```

Fakes in `FleetManager.Tests/Fakes/`: `FakeMqttService`, `FakeFleetStatusPublisher`,
`FakeFleetPersistenceService` — use these instead of mocking interfaces in new tests.

## Environment Variables (fleet-controller)

| Variable               | Default       | Description                                      |
|------------------------|---------------|--------------------------------------------------|
| `SEED_DEMO_TOPOLOGY`   | _(unset)_     | Set to `true` to seed demo topology if DB empty  |
| `Mqtt__Host`           | `localhost`   | MQTT broker hostname                             |
| `Mqtt__Port`           | `1883`        | MQTT broker port                                 |
| `Mqtt__ClientId`       | `fleet-controller-01` | MQTT client ID                         |
| `Mqtt__InterfaceName`  | `uagv`        | VDA5050 interface name (topic prefix)            |
| `Mqtt__MajorVersion`   | `v2`          | VDA5050 major version (topic segment)            |
| `ConnectionStrings__Fleet` | _(unset)_ | PostgreSQL connection string; omit for no-DB mode|

## Environment Variables (agv-simulator)

| Variable               | Default                          | Description                        |
|------------------------|----------------------------------|------------------------------------|
| `FLEET_CONTROLLER_URL` | `http://fleet-controller:8080`   | Used to fetch topology on startup  |
| `AGV_SERIALS`          | `agv-01,agv-02,agv-03`           | Comma-separated vehicle serials    |
| `AGV_MANUFACTURER`     | `acme`                           | Manufacturer name (topic segment)  |
| `DRIVE_SPEED`          | `2.0`                            | Units/second                       |
| `ACTION_DURATION`      | `3.0`                            | Seconds per pick/drop action       |
