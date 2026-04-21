import { FleetStatus } from "../types/models";
import { FleetStatusService } from "../services/fleet-status.service";
import { FleetStatsCards } from "./fleet-stats-cards";
import { FleetTopologyGraph } from "./fleet-topology-graph";
import { FleetTopologyConfig } from "./fleet-topology-config";
import { FleetVehicleTable } from "./fleet-vehicle-table";
import { FleetConnectionStatus } from "./fleet-connection-status";
import { FleetNavMenu, NavView } from "./fleet-nav-menu";
import { FleetOrderList } from "./fleet-order-list";
import { FleetOrderHistory } from "./fleet-order-history";

export class FleetDashboardApp extends HTMLElement {
  private service: FleetStatusService;
  private navMenu: FleetNavMenu | null = null;
  private statsCards: FleetStatsCards | null = null;
  private topologyGraph: FleetTopologyGraph | null = null;
  private topologyConfig: FleetTopologyConfig | null = null;
  private vehicleTable: FleetVehicleTable | null = null;
  private connectionStatus: FleetConnectionStatus | null = null;
  private orderList: FleetOrderList | null = null;
  private orderHistory: FleetOrderHistory | null = null;

  constructor() {
    super();
    this.service = new FleetStatusService();
  }

  async connectedCallback(): Promise<void> {
    this.render();
    this.setupComponents();
    this.setupNavigation();
    this.setupServiceListeners();
    await this.startService();
  }

  disconnectedCallback(): void {
    this.service.stop();
  }

  private render(): void {
    this.innerHTML = `
      <div class="app-layout">
        <fleet-nav-menu></fleet-nav-menu>
        <div class="main-content">
          <div class="view-header">
            <fleet-connection-status></fleet-connection-status>
          </div>

          <div class="view" data-view="dashboard">
            <h1>Dashboard</h1>
            <fleet-stats-cards></fleet-stats-cards>
            <fleet-topology-graph></fleet-topology-graph>
          </div>

          <div class="view hidden" data-view="topology">
            <fleet-topology-config></fleet-topology-config>
          </div>

          <div class="view hidden" data-view="orders">
            <fleet-order-list></fleet-order-list>
          </div>

          <div class="view hidden" data-view="order-history">
            <fleet-order-history></fleet-order-history>
          </div>

          <div class="view hidden" data-view="vehicles">
            <h1>Vehicle Details</h1>
            <fleet-vehicle-table></fleet-vehicle-table>
          </div>
        </div>
      </div>
    `;
  }

  private setupComponents(): void {
    this.navMenu = this.querySelector("fleet-nav-menu");
    this.statsCards = this.querySelector("fleet-stats-cards");
    this.topologyGraph = this.querySelector("fleet-topology-graph");
    this.topologyConfig = this.querySelector("fleet-topology-config");
    this.vehicleTable = this.querySelector("fleet-vehicle-table");
    this.connectionStatus = this.querySelector("fleet-connection-status");
    this.orderList = this.querySelector("fleet-order-list");
    this.orderHistory = this.querySelector("fleet-order-history");
  }

  private setupNavigation(): void {
    this.addEventListener("nav-change", (e: Event) => {
      const { view } = (e as CustomEvent<{ view: NavView }>).detail;
      this.showView(view);
    });
  }

  private showView(view: NavView): void {
    this.querySelectorAll<HTMLElement>(".view").forEach((el) => {
      el.classList.toggle("hidden", el.dataset.view !== view);
    });

    if (view === "order-history") {
      this.orderHistory?.loadHistory();
    }
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
    this.vehicleTable?.updateNodes(status.nodes);
    this.vehicleTable?.updateBatteryThreshold(status.lowBatteryThreshold);

    // Update order list and history
    this.orderList?.updateOrders(status.orders);
    this.orderList?.updateNodes(status.nodes);
  }
}

customElements.define("fleet-dashboard-app", FleetDashboardApp);
