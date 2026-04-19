# FleetController — Agent Instructions

## Language

**English only** — all code, identifiers, comments, commit messages, and documentation.

## What This Project Is

A VDA5050 v2 fleet controller for industrial AGVs. Vehicles communicate over MQTT
using the VDA5050 protocol. The controller dispatches transport orders, tracks vehicle
state, and persists everything to PostgreSQL. The UI is a real-time WebComponents
dashboard connected via SignalR.

## Before Making Changes

1. **Understand the flow**: MQTT → `Vda5050MqttService` → `FleetController` → `VehicleRegistry`
   / `TransportOrderQueue` → `IFleetPersistenceService` + `IFleetStatusPublisher`
2. **Check both sides of every DTO**: C# records in `Application/FleetController.cs` and
   TypeScript interfaces in `wwwroot/src/types/models.ts` must stay in sync
3. **Topology is dual-state**: `TopologyMap` (in-memory singleton) + `topology_nodes` /
   `topology_edges` tables (PostgreSQL). Every topology change must update both

## Critical Files

| File | Role |
|------|------|
| `Vda5050FleetController/Program.cs` | DI wiring, all Minimal API endpoints, `TopologyStartupLoader` with demo seeder |
| `Application/FleetController.cs` | All domain logic, `FleetStatus`, all summary DTOs |
| `Domain/Models/DomainModels.cs` | `Vehicle`, `TransportOrder`, `TopologyMap` — core aggregates |
| `Domain/Models/Vda5050Models.cs` | VDA5050 wire format records; JSON property names must match the spec |
| `Infrastructure/Mqtt/Vda5050MqttService.cs` | `Vda5050Topic` helper, QoS levels, subscriptions |
| `wwwroot/src/types/models.ts` | TypeScript mirror of C# DTOs — keep in sync manually |
| `agv-simulator/simulator.py` | Demo simulator; fetches topology from REST API at startup |
| `docker/docker-compose.demo.yml` | Overlay that adds simulator and sets `SEED_DEMO_TOPOLOGY=true` |

## Invariants — Never Break These

- `IFleetRepository` and `FleetDbContext` are **scoped** — always use `IServiceScopeFactory`
  to create a scope before resolving them; never inject directly into singletons
- `TopologyMap` is a **singleton**; topology mutations (add/remove node or edge) must also
  persist to PostgreSQL via `IFleetPersistenceService`
- VDA5050 JSON property names are **camelCase** per spec; all `Vda5050Models.cs` records use
  `[JsonPropertyName]` — do not change this serialisation without updating the simulator too
- The simulator detects charging stations by the `CHG` prefix in `node_id`; keep this
  convention when adding nodes, or update `_is_charging_node()` in `simulator.py`
- `SEED_DEMO_TOPOLOGY` only seeds when the topology table is **empty**; it is safe to leave
  it enabled in `docker-compose.demo.yml`

## Patterns To Follow

### Adding a new API endpoint
1. Add the route in `Program.cs` using the Minimal API style already there
2. If it returns new data, add a property to `FleetStatus` and the relevant summary record
3. Mirror any new DTO fields in `wwwroot/src/types/models.ts`
4. Rebuild the frontend (`npm run build` in `wwwroot/`)

### Adding a new VDA5050 message type
1. Add a record in `Vda5050Models.cs` with `[JsonPropertyName]` attributes
2. Add topic helper in `Vda5050Topic` class in `Vda5050MqttService.cs`
3. Subscribe / publish in `Vda5050MqttService.StartAsync`

### Adding a new topology field (e.g. max speed per node)
1. Add column to `SchemaInitializer.cs` (idempotent `ALTER TABLE … IF NOT EXISTS`)
2. Add property to `NodeRecord` (persistence entity)
3. Add property to `TopologyNode` record (DTO) and `TopologyMap` (in-memory)
4. Update `TopologyStartupLoader`, `PostgresFleetRepository`, `PostgresFleetPersistenceService`
5. Mirror in `wwwroot/src/types/models.ts`

### Changing the demo topology
Edit `DemoNodes` / `DemoEdges` arrays in `TopologyStartupLoader` inside `Program.cs`.
Also update the matching node positions in `agv-simulator/simulator.py` `NODES` fallback
dict (used only when the node has no `nodePosition` in the order message).

## What NOT To Do

- Do not mock `IFleetRepository` in new tests — use `FakeFleetPersistenceService` from
  `FleetManager.Tests/Fakes/` instead
- Do not add `if (demoMode)` branches in production code — demo behaviour belongs in the
  simulator or the overlay compose file
- Do not hardcode topology nodes in `Program.cs` — the demo seeder in `TopologyStartupLoader`
  is the only place allowed to seed topology
- Do not skip the frontend rebuild — TypeScript changes are not picked up at runtime without
  `npm run build`; the Docker build does this automatically

## Running Locally (without Docker)

```bash
# 1. Start infrastructure
docker compose -f docker/docker-compose.yml up postgres mosquitto

# 2. Build & run backend
dotnet run --project Vda5050FleetController

# 3. (Optional) Build frontend in watch mode
cd Vda5050FleetController/wwwroot && npm run build -- --watch

# 4. (Optional) Run simulator against local backend
cd agv-simulator
pip install -r requirements.txt
FLEET_CONTROLLER_URL=http://localhost:8080 MQTT_HOST=localhost python simulator.py
```

## Demo Mode

```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up --build
```

On first start with an empty database: the fleet controller seeds 8 nodes and 15 edges
(`SEED_DEMO_TOPOLOGY=true` in the overlay). The AGV simulator waits for the fleet
controller healthcheck to pass, then fetches the topology and starts 3 simulated vehicles.

Demo topology (`DEMO-WAREHOUSE` map):
- `IN-A`, `IN-B`, `IN-C` — input/pickup stations (x=5, y=8/16/24)
- `OUT-A`, `OUT-B`, `OUT-C` — output/delivery stations (x=54, y=8/16/24)
- `CHG-1`, `CHG-2` — charging stations (top-left and top-right)
