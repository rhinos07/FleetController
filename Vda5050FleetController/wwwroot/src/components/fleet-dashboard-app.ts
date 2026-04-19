import { FleetStatus } from "../types/models";
import { FleetStatusService } from "../services/fleet-status.service";
import { FleetStatsCards } from "./fleet-stats-cards";
import { FleetTopologyGraph } from "./fleet-topology-graph";
import { FleetTopologyConfig } from "./fleet-topology-config";
import { FleetVehicleTable } from "./fleet-vehicle-table";
import { FleetConnectionStatus } from "./fleet-connection-status";

export class FleetDashboardApp extends HTMLElement {
  private service: FleetStatusService;
  private statsCards: FleetStatsCards | null = null;
  private topologyGraph: FleetTopologyGraph | null = null;
  private topologyConfig: FleetTopologyConfig | null = null;
  private vehicleTable: FleetVehicleTable | null = null;
  private connectionStatus: FleetConnectionStatus | null = null;

  constructor() {
    super();
    this.service = new FleetStatusService();
  }

  async connectedCallback(): Promise<void> {
    this.render();
    this.setupComponents();
    this.setupServiceListeners();
    await this.startService();
  }

  disconnectedCallback(): void {
    this.service.stop();
  }

  private render(): void {
    this.innerHTML = `
      <h1>FleetManager Live-Dashboard</h1>
      <fleet-stats-cards></fleet-stats-cards>
      <fleet-topology-graph></fleet-topology-graph>
      <fleet-topology-config></fleet-topology-config>
      <fleet-vehicle-table></fleet-vehicle-table>
      <fleet-connection-status></fleet-connection-status>
    `;
  }

  private setupComponents(): void {
    this.statsCards = this.querySelector("fleet-stats-cards");
    this.topologyGraph = this.querySelector("fleet-topology-graph");
    this.topologyConfig = this.querySelector("fleet-topology-config");
    this.vehicleTable = this.querySelector("fleet-vehicle-table");
    this.connectionStatus = this.querySelector("fleet-connection-status");
  }

  private setupServiceListeners(): void {
    // Listen for fleet status updates
    this.service.onStatusUpdate((status: FleetStatus) => {
      this.handleStatusUpdate(status);
    });

    // Listen for connection state changes
    this.service.onConnectionStateChange((state) => {
      this.connectionStatus?.updateState(state);
    });
  }

  private async startService(): Promise<void> {
    try {
      await this.service.start();
    } catch (error) {
      console.error("Failed to start FleetStatusService:", error);
      this.connectionStatus?.showError(String(error));
    }
  }

  private handleStatusUpdate(status: FleetStatus): void {
    // Update stats cards
    this.statsCards?.updateStats(
      status.vehicles.length,
      status.activeOrders,
      status.pendingOrders
    );

    // Update topology graph
    this.topologyGraph?.updateGraph(status);

    // Update topology config list view
    this.topologyConfig?.updateTopology(status.nodes, status.edges);

    // Update vehicle table
    this.vehicleTable?.updateVehicles(status.vehicles);
  }
}

customElements.define("fleet-dashboard-app", FleetDashboardApp);
