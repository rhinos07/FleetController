# FleetManager

Minimal .NET Fleet Manager for AGV orchestration with:
- VDA5050-oriented AGV state endpoint (`/api/agv/vda5050/state`)
- Route graph and order intake with HU, source, and destination (`/api/orders`)
- Zone-blocking control (`/api/zones/{zoneId}/block`)
- SignalR dashboard hub (`/hubs/dashboard`) with Web Components UI (`/`)
- PostgreSQL + Linq2db data model scaffolding (`FleetDb`)
