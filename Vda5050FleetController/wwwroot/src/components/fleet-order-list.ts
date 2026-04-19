import { OrderSummary, ACTIVE_ORDER_STATUSES } from "../types/models";

export class FleetOrderList extends HTMLElement {
  private tableBodyEl: HTMLElement | null = null;

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.tableBodyEl = this.querySelector("#orderRows");
  }

  private render(): void {
    this.innerHTML = `
      <h2>Order List</h2>
      <p class="muted">Active and pending transport orders.</p>
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
        <tbody id="orderRows">
          <tr><td colspan="5" class="muted">Waiting for live data …</td></tr>
        </tbody>
      </table>
    `;
  }

  public updateOrders(orders: OrderSummary[]): void {
    if (!this.tableBodyEl) return;

    const active = orders.filter((o) =>
      (ACTIVE_ORDER_STATUSES as string[]).includes(o.status)
    );

    if (active.length === 0) {
      this.tableBodyEl.innerHTML =
        '<tr><td colspan="5" class="muted">No active orders</td></tr>';
      return;
    }

    this.tableBodyEl.innerHTML = active
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

customElements.define("fleet-order-list", FleetOrderList);
