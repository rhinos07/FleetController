import { OrderSummary, TopologyNode, ACTIVE_ORDER_STATUSES } from "../types/models";

export class FleetOrderList extends HTMLElement {
  private orders: OrderSummary[] = [];
  private nodes: TopologyNode[] = [];
  private tableBodyEl: HTMLElement | null = null;
  private editingOrderId: string | null = null;

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.tableBodyEl = this.querySelector("#orderRows");
    this.setupEventListeners();
  }

  // Called by the dashboard whenever fleet status updates
  public updateOrders(orders: OrderSummary[]): void {
    this.orders = orders ?? [];
    this.refreshTable();
  }

  public updateNodes(nodes: TopologyNode[]): void {
    this.nodes = nodes ?? [];
    this.refreshNodeDatalist();
  }

  // ── Rendering ──────────────────────────────────────────────────────────────

  private render(): void {
    this.innerHTML = `
      <div class="order-config">
        <div class="order-section-header">
          <h2>Order List</h2>
          <button class="btn-primary" id="addOrderBtn">+ Add Order</button>
        </div>
        <p class="muted">Active and pending transport orders.</p>

        <!-- Add/Edit Order Form (hidden by default) -->
        <div id="orderForm" class="topology-form hidden">
          <h4 id="orderFormTitle">Add Order</h4>
          <div class="form-row">
            <label>Source Station
              <input type="text" id="orderSource" placeholder="e.g. IN-A" list="nodeList" required>
            </label>
            <label>Destination Station
              <input type="text" id="orderDest" placeholder="e.g. OUT-B" list="nodeList" required>
            </label>
            <label>Load ID (optional)
              <input type="text" id="orderLoad" placeholder="e.g. PAL-42">
            </label>
          </div>
          <datalist id="nodeList"></datalist>
          <div class="form-actions">
            <button class="btn-primary" id="saveOrderBtn">Save</button>
            <button class="btn-secondary" id="cancelOrderFormBtn">Cancel</button>
          </div>
          <div id="orderFormError" class="form-error hidden"></div>
        </div>

        <table>
          <thead>
            <tr>
              <th>Order ID</th>
              <th>Load</th>
              <th>Source</th>
              <th>Destination</th>
              <th>Status</th>
              <th>Vehicle</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody id="orderRows">
            <tr><td colspan="7" class="muted">Waiting for live data …</td></tr>
          </tbody>
        </table>
      </div>
    `;
  }

  // ── Datalist refresh ───────────────────────────────────────────────────────

  private refreshNodeDatalist(): void {
    const datalist = this.querySelector<HTMLDataListElement>("#nodeList");
    if (!datalist) return;
    datalist.innerHTML = this.nodes
      .map((n) => `<option value="${this.esc(n.nodeId)}">`)
      .join("");
  }

  // ── Table refresh ──────────────────────────────────────────────────────────

  private refreshTable(): void {
    const tbody = this.querySelector<HTMLElement>("#orderRows");
    if (!tbody) return;

    const active = this.orders.filter((o) =>
      (ACTIVE_ORDER_STATUSES as string[]).includes(o.status)
    );

    if (active.length === 0) {
      tbody.innerHTML =
        '<tr><td colspan="7" class="muted">No active orders</td></tr>';
      return;
    }

    tbody.innerHTML = active
      .map(
        (o) => `
          <tr data-order-id="${this.esc(o.orderId)}">
            <td>${this.esc(o.orderId)}</td>
            <td>${o.loadId ? this.esc(o.loadId) : "<span class='muted'>—</span>"}</td>
            <td>${this.esc(o.sourceId)}</td>
            <td>${this.esc(o.destId)}</td>
            <td><span class="status-badge status-${o.status.toLowerCase()}">${this.esc(o.status)}</span></td>
            <td>${o.vehicleId ? this.esc(o.vehicleId) : "<span class='muted'>—</span>"}</td>
            <td class="action-cell">
              ${o.status === "Pending" ? `
                <button class="btn-edit" data-action="editOrder" data-id="${o.orderId}">Edit</button>
                <button class="btn-danger" data-action="cancelOrder" data-id="${o.orderId}">Cancel</button>
              ` : ""}
            </td>
          </tr>
        `
      )
      .join("");
  }

  // ── Event listeners ────────────────────────────────────────────────────────

  private setupEventListeners(): void {
    // Delegate table button clicks
    this.addEventListener("click", (e) => {
      const target = e.target as HTMLElement;
      const action = target.dataset.action;
      const id     = target.dataset.id;
      if (!action || !id) return;

      switch (action) {
        case "editOrder":   this.openOrderForm(id);    break;
        case "cancelOrder": this.cancelOrder(id);      break;
      }
    });

    this.querySelector("#addOrderBtn")?.addEventListener("click", () =>
      this.openOrderForm(null));

    this.querySelector("#saveOrderBtn")?.addEventListener("click", () =>
      this.saveOrder());
    this.querySelector("#cancelOrderFormBtn")?.addEventListener("click", () =>
      this.closeOrderForm());
  }

  // ── Order form ─────────────────────────────────────────────────────────────

  private openOrderForm(orderId: string | null): void {
    const form  = this.querySelector<HTMLElement>("#orderForm");
    const title = this.querySelector<HTMLElement>("#orderFormTitle");
    if (!form || !title) return;

    if (orderId) {
      const order = this.orders.find((o) => o.orderId === orderId);
      if (!order) return;
      this.editingOrderId   = orderId;
      title.textContent     = "Edit Order";
      this.setInput("orderSource", order.sourceId);
      this.setInput("orderDest",   order.destId);
      this.setInput("orderLoad",   order.loadId ?? "");
    } else {
      this.editingOrderId = null;
      title.textContent   = "Add Order";
      this.setInput("orderSource", "");
      this.setInput("orderDest",   "");
      this.setInput("orderLoad",   "");
    }

    this.hideError("orderFormError");
    form.classList.remove("hidden");
    this.querySelector<HTMLInputElement>("#orderSource")?.focus();
  }

  private closeOrderForm(): void {
    this.editingOrderId = null;
    this.querySelector("#orderForm")?.classList.add("hidden");
  }

  private async saveOrder(): Promise<void> {
    const sourceId = this.getInput("orderSource").trim();
    const destId   = this.getInput("orderDest").trim();
    const loadId   = this.getInput("orderLoad").trim() || undefined;

    if (!sourceId) { this.showError("orderFormError", "Source Station is required."); return; }
    if (!destId)   { this.showError("orderFormError", "Destination Station is required."); return; }

    try {
      if (this.editingOrderId) {
        // Update existing pending order
        const res = await fetch(
          `/fleet/orders/${encodeURIComponent(this.editingOrderId)}`,
          {
            method:  "PUT",
            headers: { "Content-Type": "application/json" },
            body:    JSON.stringify({
              sourceStationId: sourceId,
              destStationId:   destId,
              loadId:          loadId ?? null
            })
          }
        );
        if (!res.ok) throw new Error(`Server error: ${res.status}`);
      } else {
        // Create new order
        const res = await fetch("/fleet/orders", {
          method:  "POST",
          headers: { "Content-Type": "application/json" },
          body:    JSON.stringify({
            sourceStationId: sourceId,
            destStationId:   destId,
            loadId:          loadId ?? null
          })
        });
        if (!res.ok) throw new Error(`Server error: ${res.status}`);
      }
      this.closeOrderForm();
    } catch (err) {
      this.showError("orderFormError", String(err));
    }
  }

  // ── Cancel order ───────────────────────────────────────────────────────────

  private async cancelOrder(orderId: string): Promise<void> {
    if (!confirm(`Cancel order "${orderId}"?`)) return;
    try {
      const res = await fetch(`/fleet/orders/${encodeURIComponent(orderId)}`, {
        method: "DELETE"
      });
      if (!res.ok) throw new Error(`Server error: ${res.status}`);
    } catch (err) {
      alert(`Failed to cancel order: ${err}`);
    }
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  private getInput(id: string): string {
    return (this.querySelector<HTMLInputElement>(`#${id}`)?.value ?? "");
  }

  private setInput(id: string, value: string): void {
    const el = this.querySelector<HTMLInputElement>(`#${id}`);
    if (el) el.value = value;
  }

  private showError(id: string, message: string): void {
    const el = this.querySelector<HTMLElement>(`#${id}`);
    if (!el) return;
    el.textContent = message;
    el.classList.remove("hidden");
  }

  private hideError(id: string): void {
    const el = this.querySelector<HTMLElement>(`#${id}`);
    if (!el) return;
    el.textContent = "";
    el.classList.add("hidden");
  }

  private esc(str: string): string {
    return str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }
}

