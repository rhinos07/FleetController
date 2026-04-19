export type NavView = "dashboard" | "topology" | "orders" | "order-history" | "vehicles";

export class FleetNavMenu extends HTMLElement {
  private activeView: NavView = "dashboard";

  connectedCallback(): void {
    this.render();
    this.setupEventListeners();
  }

  private render(): void {
    this.innerHTML = `
      <nav class="nav-menu">
        <div class="nav-logo">FleetManager</div>
        <ul class="nav-list">
          <li class="nav-item active" data-view="dashboard">
            <span class="nav-icon">&#128202;</span>
            <span class="nav-label">Dashboard</span>
          </li>
          <li class="nav-item" data-view="topology">
            <span class="nav-icon">&#128506;</span>
            <span class="nav-label">Topology Modify</span>
          </li>
          <li class="nav-item" data-view="orders">
            <span class="nav-icon">&#128203;</span>
            <span class="nav-label">Order List</span>
          </li>
          <li class="nav-item" data-view="order-history">
            <span class="nav-icon">&#128221;</span>
            <span class="nav-label">Order History</span>
          </li>
          <li class="nav-item" data-view="vehicles">
            <span class="nav-icon">&#128663;</span>
            <span class="nav-label">Vehicle Details</span>
          </li>
        </ul>
      </nav>
    `;
  }

  private setupEventListeners(): void {
    this.querySelectorAll<HTMLElement>(".nav-item").forEach((item) => {
      item.addEventListener("click", () => {
        const view = item.dataset.view as NavView;
        if (view) {
          this.setActiveView(view);
        }
      });
    });
  }

  public setActiveView(view: NavView): void {
    this.activeView = view;
    this.querySelectorAll<HTMLElement>(".nav-item").forEach((item) => {
      item.classList.toggle("active", item.dataset.view === view);
    });
    this.dispatchEvent(
      new CustomEvent<{ view: NavView }>("nav-change", {
        detail: { view },
        bubbles: true,
      })
    );
  }

  public getActiveView(): NavView {
    return this.activeView;
  }
}

customElements.define("fleet-nav-menu", FleetNavMenu);
