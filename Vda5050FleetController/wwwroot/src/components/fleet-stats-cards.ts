export class FleetStatsCards extends HTMLElement {
  private vehicleCountEl: HTMLElement | null = null;
  private activeOrdersEl: HTMLElement | null = null;
  private pendingOrdersEl: HTMLElement | null = null;

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.vehicleCountEl = this.querySelector("#vehicleCount");
    this.activeOrdersEl = this.querySelector("#activeOrders");
    this.pendingOrdersEl = this.querySelector("#pendingOrders");
  }

  private render(): void {
    this.innerHTML = `
      <div class="cards">
        <div class="card">Fahrzeuge: <strong id="vehicleCount">0</strong></div>
        <div class="card">Aktive Aufträge: <strong id="activeOrders">0</strong></div>
        <div class="card">Wartende Aufträge: <strong id="pendingOrders">0</strong></div>
      </div>
    `;
  }

  public updateStats(vehicleCount: number, activeOrders: number, pendingOrders: number): void {
    if (this.vehicleCountEl) {
      this.vehicleCountEl.textContent = vehicleCount.toString();
    }
    if (this.activeOrdersEl) {
      this.activeOrdersEl.textContent = activeOrders.toString();
    }
    if (this.pendingOrdersEl) {
      this.pendingOrdersEl.textContent = pendingOrders.toString();
    }
  }
}

customElements.define("fleet-stats-cards", FleetStatsCards);
