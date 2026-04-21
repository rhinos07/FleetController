import { OrderHistoryDto, OrderSummary } from "../types/models";

const HISTORY_STATUSES = ["Completed", "Failed"] as const;

export class FleetOrderHistory extends HTMLElement {
  private tableBodyEl: HTMLElement | null = null;

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.tableBodyEl = this.querySelector("#historyRows");
  }

  private render(): void {
    this.innerHTML = `
      <h2>Order History</h2>
      <p class="muted">Completed and failed transport orders.</p>
      <table>
        <thead>
          <tr>
            <th>Order ID</th>
            <th>Source</th>
            <th>Destination</th>
            <th>Status</th>
            <th>Vehicle</th>
            <th>Created</th>
            <th>Started</th>
            <th>Completed</th>
          </tr>
        </thead>
        <tbody id="historyRows">
          <tr><td colspan="8" class="muted">Waiting for live data …</td></tr>
        </tbody>
      </table>
    `;
  }

  public async loadHistory(): Promise<void> {
    if (!this.tableBodyEl) return;

    try {
      const response = await fetch("/fleet/orders/history");
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const orders: OrderHistoryDto[] = await response.json();
      this.renderRows(orders);
    } catch (err) {
      if (this.tableBodyEl) {
        this.tableBodyEl.innerHTML =
          '<tr><td colspan="8" class="muted">Failed to load order history.</td></tr>';
      }
    }
  }

  public updateOrders(orders: OrderSummary[]): void {
    const historical = orders.filter((o) =>
      (HISTORY_STATUSES as readonly string[]).includes(o.status)
    );

    if (!this.tableBodyEl) return;

    if (historical.length === 0) {
      this.tableBodyEl.innerHTML =
        '<tr><td colspan="8" class="muted">No order history available</td></tr>';
      return;
    }

    this.tableBodyEl.innerHTML = historical
      .map(
        (o) => `
          <tr>
            <td>${this.esc(o.orderId)}</td>
            <td>${this.esc(o.sourceId)}</td>
            <td>${this.esc(o.destId)}</td>
            <td><span class="status-badge status-${o.status.toLowerCase()}">${this.esc(o.status)}</span></td>
            <td>${o.vehicleId ? this.esc(o.vehicleId) : "<span class='muted'>—</span>"}</td>
            <td><span class="muted">—</span></td>
            <td><span class="muted">—</span></td>
            <td><span class="muted">—</span></td>
          </tr>
        `
      )
      .join("");
  }

  private renderRows(orders: OrderHistoryDto[]): void {
    if (!this.tableBodyEl) return;

    if (orders.length === 0) {
      this.tableBodyEl.innerHTML =
        '<tr><td colspan="8" class="muted">No order history available</td></tr>';
      return;
    }

    this.tableBodyEl.innerHTML = orders
      .map(
        (o) => `
          <tr>
            <td>${this.esc(o.orderId)}</td>
            <td>${this.esc(o.sourceId)}</td>
            <td>${this.esc(o.destId)}</td>
            <td><span class="status-badge status-${o.finalStatus.toLowerCase()}">${this.esc(o.finalStatus)}</span></td>
            <td>${o.assignedVehicleId ? this.esc(o.assignedVehicleId) : "<span class='muted'>—</span>"}</td>
            <td>${this.formatDate(o.createdAt)}</td>
            <td>${o.startedAt ? this.formatDate(o.startedAt) : "<span class='muted'>—</span>"}</td>
            <td>${this.formatDate(o.completedAt)}</td>
          </tr>
        `
      )
      .join("");
  }

  private formatDate(iso: string): string {
    return new Date(iso).toLocaleString();
  }

  private esc(str: string): string {
    return str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }
}

customElements.define("fleet-order-history", FleetOrderHistory);
