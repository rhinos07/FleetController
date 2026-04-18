import {
  FleetStatus,
  TopologyNode,
  TopologyEdge,
  OrderSummary,
  VehicleSummary,
  ACTIVE_ORDER_STATUSES,
  VEHICLE_STATUS_COLORS
} from "../types/models";

interface GraphOptions {
  showNodes: boolean;
  showEdges: boolean;
  showVehicles: boolean;
  showOrders: boolean;
}

export class FleetTopologyGraph extends HTMLElement {
  private canvas: HTMLCanvasElement | null = null;
  private ctx: CanvasRenderingContext2D | null = null;
  private currentStatus: FleetStatus | null = null;
  private options: GraphOptions = {
    showNodes: true,
    showEdges: true,
    showVehicles: true,
    showOrders: true
  };

  // Graph rendering constants
  private readonly SCALE = 15; // pixels per meter
  private readonly OFFSET_X = 50;
  private readonly OFFSET_Y = 50;
  private readonly NODE_RADIUS = 8;
  private readonly VEHICLE_RADIUS = 12;
  private readonly ORDER_ID_DISPLAY_LENGTH = 8;

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.canvas = this.querySelector("#graphCanvas");
    this.ctx = this.canvas?.getContext("2d") ?? null;
    this.setupEventListeners();
  }

  private render(): void {
    this.innerHTML = `
      <div class="graph-container">
        <h2>Topologie-Graph</h2>
        <div class="graph-controls">
          <label>
            <input type="checkbox" id="showNodes" checked> Knoten anzeigen
          </label>
          <label>
            <input type="checkbox" id="showEdges" checked> Kanten anzeigen
          </label>
          <label>
            <input type="checkbox" id="showVehicles" checked> Fahrzeuge anzeigen
          </label>
          <label>
            <input type="checkbox" id="showOrders" checked> Aufträge anzeigen
          </label>
        </div>
        <canvas id="graphCanvas" width="900" height="500"></canvas>
        <div class="legend">
          <div class="legend-item">
            <div class="legend-color" style="background: #3b82f6;"></div>
            <span>Knoten</span>
          </div>
          <div class="legend-item">
            <div class="legend-color" style="background: #10b981;"></div>
            <span>Fahrzeug (Idle)</span>
          </div>
          <div class="legend-item">
            <div class="legend-color" style="background: #f59e0b;"></div>
            <span>Fahrzeug (Busy/Driving)</span>
          </div>
          <div class="legend-item">
            <div class="legend-color" style="background: #8b5cf6;"></div>
            <span>Fahrzeug (Charging)</span>
          </div>
          <div class="legend-item">
            <div class="legend-color" style="background: #ef4444;"></div>
            <span>Fahrzeug (Error)</span>
          </div>
          <div class="legend-item">
            <div class="legend-color" style="background: #ef4444; border-radius: 50%;"></div>
            <span>Aktiver Auftrag</span>
          </div>
        </div>
      </div>
    `;
  }

  private setupEventListeners(): void {
    const showNodesCheckbox = this.querySelector("#showNodes") as HTMLInputElement;
    const showEdgesCheckbox = this.querySelector("#showEdges") as HTMLInputElement;
    const showVehiclesCheckbox = this.querySelector("#showVehicles") as HTMLInputElement;
    const showOrdersCheckbox = this.querySelector("#showOrders") as HTMLInputElement;

    showNodesCheckbox?.addEventListener("change", () => {
      this.options.showNodes = showNodesCheckbox.checked;
      this.drawGraph();
    });

    showEdgesCheckbox?.addEventListener("change", () => {
      this.options.showEdges = showEdgesCheckbox.checked;
      this.drawGraph();
    });

    showVehiclesCheckbox?.addEventListener("change", () => {
      this.options.showVehicles = showVehiclesCheckbox.checked;
      this.drawGraph();
    });

    showOrdersCheckbox?.addEventListener("change", () => {
      this.options.showOrders = showOrdersCheckbox.checked;
      this.drawGraph();
    });
  }

  public updateGraph(status: FleetStatus): void {
    this.currentStatus = status;
    this.drawGraph();
  }

  private drawGraph(): void {
    if (!this.currentStatus || !this.canvas || !this.ctx) return;

    const { canvas, ctx } = this;
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    // Create node lookup for edge drawing
    const nodeMap = new Map<string, TopologyNode>();
    this.currentStatus.nodes?.forEach((node) => {
      nodeMap.set(node.nodeId, node);
    });

    // Draw edges first (so they appear behind nodes)
    if (this.options.showEdges && this.currentStatus.edges) {
      this.drawEdges(this.currentStatus.edges, nodeMap);
    }

    // Draw nodes
    if (this.options.showNodes && this.currentStatus.nodes) {
      this.drawNodes(this.currentStatus.nodes);
    }

    // Draw active orders on source nodes
    if (this.options.showOrders && this.currentStatus.orders) {
      this.drawOrders(this.currentStatus.orders, nodeMap);
    }

    // Draw vehicles
    if (this.options.showVehicles && this.currentStatus.vehicles) {
      this.drawVehicles(this.currentStatus.vehicles);
    }
  }

  private drawEdges(edges: TopologyEdge[], nodeMap: Map<string, TopologyNode>): void {
    if (!this.ctx) return;

    this.ctx.strokeStyle = "#9ca3af";
    this.ctx.lineWidth = 2;

    edges.forEach((edge) => {
      const fromNode = nodeMap.get(edge.from);
      const toNode = nodeMap.get(edge.to);

      if (fromNode && toNode) {
        this.ctx!.beginPath();
        this.ctx!.moveTo(this.toCanvasX(fromNode.x), this.toCanvasY(fromNode.y));
        this.ctx!.lineTo(this.toCanvasX(toNode.x), this.toCanvasY(toNode.y));
        this.ctx!.stroke();
      }
    });
  }

  private drawNodes(nodes: TopologyNode[]): void {
    if (!this.ctx) return;

    nodes.forEach((node) => {
      this.ctx!.fillStyle = "#3b82f6";
      this.ctx!.beginPath();
      this.ctx!.arc(
        this.toCanvasX(node.x),
        this.toCanvasY(node.y),
        this.NODE_RADIUS,
        0,
        2 * Math.PI
      );
      this.ctx!.fill();

      // Draw node label
      this.ctx!.fillStyle = "#1f2937";
      this.ctx!.font = "10px Arial";
      this.ctx!.textAlign = "center";
      this.ctx!.fillText(
        node.nodeId,
        this.toCanvasX(node.x),
        this.toCanvasY(node.y) - this.NODE_RADIUS - 3
      );
    });
  }

  private drawOrders(orders: OrderSummary[], nodeMap: Map<string, TopologyNode>): void {
    if (!this.ctx) return;

    orders.forEach((order) => {
      if (ACTIVE_ORDER_STATUSES.includes(order.status)) {
        const sourceNode = nodeMap.get(order.sourceId);
        if (sourceNode) {
          // Draw red indicator near source node
          this.ctx!.fillStyle = "#ef4444";
          this.ctx!.beginPath();
          this.ctx!.arc(
            this.toCanvasX(sourceNode.x) + 12,
            this.toCanvasY(sourceNode.y) - 12,
            6,
            0,
            2 * Math.PI
          );
          this.ctx!.fill();

          // Draw order ID
          this.ctx!.fillStyle = "#ef4444";
          this.ctx!.font = "9px Arial";
          this.ctx!.textAlign = "left";
          const displayOrderId =
            order.orderId.length > this.ORDER_ID_DISPLAY_LENGTH
              ? order.orderId.substring(0, this.ORDER_ID_DISPLAY_LENGTH)
              : order.orderId;
          this.ctx!.fillText(
            displayOrderId,
            this.toCanvasX(sourceNode.x) + 20,
            this.toCanvasY(sourceNode.y) - 10
          );
        }
      }
    });
  }

  private drawVehicles(vehicles: VehicleSummary[]): void {
    if (!this.ctx) return;

    vehicles.forEach((vehicle) => {
      if (vehicle.position) {
        // Choose color based on status
        const color = VEHICLE_STATUS_COLORS[vehicle.status] || "#10b981";

        this.ctx!.fillStyle = color;
        this.ctx!.beginPath();
        this.ctx!.arc(
          this.toCanvasX(vehicle.position.x),
          this.toCanvasY(vehicle.position.y),
          this.VEHICLE_RADIUS,
          0,
          2 * Math.PI
        );
        this.ctx!.fill();

        // Draw vehicle outline
        this.ctx!.strokeStyle = "#1f2937";
        this.ctx!.lineWidth = 2;
        this.ctx!.stroke();

        // Draw vehicle label
        this.ctx!.fillStyle = "#1f2937";
        this.ctx!.font = "bold 11px Arial";
        this.ctx!.textAlign = "center";
        this.ctx!.fillText(
          this.formatVehicleId(vehicle.vehicleId),
          this.toCanvasX(vehicle.position.x),
          this.toCanvasY(vehicle.position.y) + this.VEHICLE_RADIUS + 12
        );
      }
    });
  }

  private formatVehicleId(vehicleId: string): string {
    if (!vehicleId) return vehicleId;
    const parts = vehicleId.split("/");
    return parts.length > 1 ? parts[1] : vehicleId;
  }

  private toCanvasX(x: number): number {
    return x * this.SCALE + this.OFFSET_X;
  }

  private toCanvasY(y: number): number {
    // Flip Y axis (canvas Y increases downward, but coordinate system increases upward)
    if (!this.canvas) return 0;
    return this.canvas.height - (y * this.SCALE + this.OFFSET_Y);
  }
}

customElements.define("fleet-topology-graph", FleetTopologyGraph);
