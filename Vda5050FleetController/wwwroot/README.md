# FleetManager Frontend

This directory contains the TypeScript-based WebComponents UI for the FleetManager dashboard.

## Architecture

The frontend is built using:
- **TypeScript** for type safety
- **WebComponents** for modular, reusable UI components
- **esbuild** for fast bundling
- **SignalR** for real-time communication with the backend

## Directory Structure

```
wwwroot/
├── src/
│   ├── components/          # WebComponent definitions
│   │   ├── fleet-stats-cards.ts
│   │   ├── fleet-connection-status.ts
│   │   ├── fleet-vehicle-table.ts
│   │   ├── fleet-topology-graph.ts
│   │   └── fleet-dashboard-app.ts
│   ├── services/            # Business logic services
│   │   └── fleet-status.service.ts
│   ├── types/               # TypeScript type definitions
│   │   └── models.ts
│   ├── styles/              # CSS stylesheets
│   │   └── main.css
│   └── main.ts              # Application entry point
├── dist/                    # Build output (generated)
├── index.html               # HTML entry point
├── package.json             # npm dependencies
└── tsconfig.json            # TypeScript configuration
```

## Building

### Development

```bash
# Install dependencies
npm install

# Build TypeScript and bundle
npm run build

# Watch mode for development
npm run dev
```

### Production

The Dockerfile automatically builds the frontend during the Docker image build process.

## Components

### fleet-dashboard-app
Main application component that orchestrates all other components and manages the SignalR connection.

### fleet-stats-cards
Displays summary statistics (vehicle count, active orders, pending orders).

### fleet-topology-graph
Canvas-based visualization of the topology map showing nodes, edges, vehicles, and orders.

### fleet-vehicle-table
Tabular display of all vehicles with their current status, battery, position, etc.

### fleet-connection-status
Displays the current SignalR connection state.

## Type Safety

All backend models are replicated in TypeScript (`src/types/models.ts`) to ensure type safety across the stack. The models match the C# domain models from the backend.

## Real-time Updates

The `FleetStatusService` manages the SignalR connection and provides a clean API for components to subscribe to fleet status updates and connection state changes.
