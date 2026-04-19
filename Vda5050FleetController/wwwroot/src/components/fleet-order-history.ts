import { OrderSummary } from "../types/models";

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
          </tr>
        </thead>
        <tbody id="historyRows">
          <tr><td colspan="5" class="muted">Waiting for live data …</td></tr>
        </tbody>
      </table>
    `;
  }

  public updateOrders(orders: OrderSummary[]): void {
    if (!this.tableBodyEl) return;

    const historical = orders.filter((o) =>
      (HISTORY_STATUSES as readonly string[]).includes(o.status)
    );

    if (historical.length === 0) {
      this.tableBodyEl.innerHTML =
        '<tr><td colspan="5" class="muted">No order history available</td></tr>';
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
          </tr>
        `
      )
      .join("");
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
