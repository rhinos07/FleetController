import { TopologyNode, TopologyEdge } from "../types/models";

export class FleetTopologyConfig extends HTMLElement {
  private nodes: TopologyNode[] = [];
  private edges: TopologyEdge[] = [];
  private editingNode: TopologyNode | null = null;
  private editingEdge: TopologyEdge | null = null;

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.setupEventListeners();
  }

  // Called by the dashboard whenever fleet status updates
  public updateTopology(nodes: TopologyNode[], edges: TopologyEdge[]): void {
    this.nodes = nodes ?? [];
    this.edges = edges ?? [];
    this.refreshNodeTable();
    this.refreshEdgeTable();
  }

  // ── Rendering ──────────────────────────────────────────────────────────────

  private render(): void {
    this.innerHTML = `
      <div class="topology-config">
        <h2>Topology Configuration</h2>

        <!-- Nodes Section -->
        <div class="topology-section">
          <div class="topology-section-header">
            <h3>Nodes</h3>
            <button class="btn-primary" id="addNodeBtn">+ Add Node</button>
          </div>

          <!-- Node Form (hidden by default) -->
          <div id="nodeForm" class="topology-form hidden">
            <h4 id="nodeFormTitle">Add Node</h4>
            <div class="form-row">
              <label>Node ID
                <input type="text" id="nodeId" placeholder="e.g. STATION-IN-01" required>
              </label>
              <label>X (m)
                <input type="number" id="nodeX" step="0.1" value="0">
              </label>
              <label>Y (m)
                <input type="number" id="nodeY" step="0.1" value="0">
              </label>
              <label>Theta (rad)
                <input type="number" id="nodeTheta" step="0.01" value="0">
              </label>
              <label>Map ID
                <input type="text" id="nodeMapId" placeholder="e.g. FLOOR-1" value="FLOOR-1">
              </label>
            </div>
            <div class="form-actions">
              <button class="btn-primary" id="saveNodeBtn">Save</button>
              <button class="btn-secondary" id="cancelNodeBtn">Cancel</button>
            </div>
            <div id="nodeFormError" class="form-error hidden"></div>
          </div>

          <table>
            <thead>
              <tr>
                <th>Node ID</th>
                <th>X (m)</th>
                <th>Y (m)</th>
                <th>Theta (rad)</th>
                <th>Map ID</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody id="nodeRows">
              <tr><td colspan="6" class="muted">No nodes configured</td></tr>
            </tbody>
          </table>
        </div>

        <!-- Edges Section -->
        <div class="topology-section">
          <div class="topology-section-header">
            <h3>Edges</h3>
            <button class="btn-primary" id="addEdgeBtn">+ Add Edge</button>
          </div>

          <!-- Edge Form (hidden by default) -->
          <div id="edgeForm" class="topology-form hidden">
            <h4 id="edgeFormTitle">Add Edge</h4>
            <div class="form-row">
              <label>Edge ID
                <input type="text" id="edgeId" placeholder="e.g. E-IN01-OUT01" required>
              </label>
              <label>From Node
                <input type="text" id="edgeFrom" placeholder="Source node ID">
              </label>
              <label>To Node
                <input type="text" id="edgeTo" placeholder="Destination node ID">
              </label>
            </div>
            <div class="form-actions">
              <button class="btn-primary" id="saveEdgeBtn">Save</button>
              <button class="btn-secondary" id="cancelEdgeBtn">Cancel</button>
            </div>
            <div id="edgeFormError" class="form-error hidden"></div>
          </div>

          <table>
            <thead>
              <tr>
                <th>Edge ID</th>
                <th>From</th>
                <th>To</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody id="edgeRows">
              <tr><td colspan="4" class="muted">No edges configured</td></tr>
            </tbody>
          </table>
        </div>
      </div>
    `;
  }

  // ── Table refresh ──────────────────────────────────────────────────────────

  private refreshNodeTable(): void {
    const tbody = this.querySelector<HTMLElement>("#nodeRows");
    if (!tbody) return;

    if (this.nodes.length === 0) {
      tbody.innerHTML = '<tr><td colspan="6" class="muted">No nodes configured</td></tr>';
      return;
    }

    tbody.innerHTML = this.nodes
      .map(
        (n) => `
          <tr data-node-id="${this.esc(n.nodeId)}">
            <td>${this.esc(n.nodeId)}</td>
            <td>${n.x.toFixed(2)}</td>
            <td>${n.y.toFixed(2)}</td>
            <td>${n.theta.toFixed(4)}</td><!-- 4 decimals: radians need more precision than meters -->
            <td>${this.esc(n.mapId)}</td>
            <td class="action-cell">
              <button class="btn-edit" data-action="editNode" data-id="${this.esc(n.nodeId)}">Edit</button>
              <button class="btn-danger" data-action="deleteNode" data-id="${this.esc(n.nodeId)}">Delete</button>
            </td>
          </tr>
        `
      )
      .join("");
  }

  private refreshEdgeTable(): void {
    const tbody = this.querySelector<HTMLElement>("#edgeRows");
    if (!tbody) return;

    if (this.edges.length === 0) {
      tbody.innerHTML = '<tr><td colspan="4" class="muted">No edges configured</td></tr>';
      return;
    }

    tbody.innerHTML = this.edges
      .map(
        (e) => `
          <tr data-edge-id="${this.esc(e.edgeId)}">
            <td>${this.esc(e.edgeId)}</td>
            <td>${this.esc(e.from)}</td>
            <td>${this.esc(e.to)}</td>
            <td class="action-cell">
              <button class="btn-edit" data-action="editEdge" data-id="${this.esc(e.edgeId)}">Edit</button>
              <button class="btn-danger" data-action="deleteEdge" data-id="${this.esc(e.edgeId)}">Delete</button>
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
        case "editNode":   this.openNodeForm(id);   break;
        case "deleteNode": this.deleteNode(id);      break;
        case "editEdge":   this.openEdgeForm(id);   break;
        case "deleteEdge": this.deleteEdge(id);      break;
      }
    });

    // Add-node button
    this.querySelector("#addNodeBtn")?.addEventListener("click", () =>
      this.openNodeForm(null));

    // Save/cancel node form
    this.querySelector("#saveNodeBtn")?.addEventListener("click", () =>
      this.saveNode());
    this.querySelector("#cancelNodeBtn")?.addEventListener("click", () =>
      this.closeNodeForm());

    // Add-edge button
    this.querySelector("#addEdgeBtn")?.addEventListener("click", () =>
      this.openEdgeForm(null));

    // Save/cancel edge form
    this.querySelector("#saveEdgeBtn")?.addEventListener("click", () =>
      this.saveEdge());
    this.querySelector("#cancelEdgeBtn")?.addEventListener("click", () =>
      this.closeEdgeForm());
  }

  // ── Node form ──────────────────────────────────────────────────────────────

  private openNodeForm(nodeId: string | null): void {
    const form   = this.querySelector<HTMLElement>("#nodeForm");
    const title  = this.querySelector<HTMLElement>("#nodeFormTitle");
    const idInput = this.querySelector<HTMLInputElement>("#nodeId");
    if (!form || !title || !idInput) return;

    if (nodeId) {
      const node = this.nodes.find((n) => n.nodeId === nodeId);
      if (!node) return;
      this.editingNode         = node;
      title.textContent        = "Edit Node";
      idInput.value            = node.nodeId;
      idInput.readOnly         = true;
      this.setInput("nodeX",     String(node.x));
      this.setInput("nodeY",     String(node.y));
      this.setInput("nodeTheta", String(node.theta));
      this.setInput("nodeMapId", node.mapId);
    } else {
      this.editingNode  = null;
      title.textContent = "Add Node";
      idInput.value     = "";
      idInput.readOnly  = false;
      this.setInput("nodeX",     "0");
      this.setInput("nodeY",     "0");
      this.setInput("nodeTheta", "0");
      this.setInput("nodeMapId", "FLOOR-1");
    }

    this.hideError("nodeFormError");
    form.classList.remove("hidden");
    idInput.focus();
  }

  private closeNodeForm(): void {
    this.editingNode = null;
    this.querySelector("#nodeForm")?.classList.add("hidden");
  }

  private async saveNode(): Promise<void> {
    const nodeId = this.getInput("nodeId").trim();
    const x      = parseFloat(this.getInput("nodeX"));
    const y      = parseFloat(this.getInput("nodeY"));
    const theta  = parseFloat(this.getInput("nodeTheta"));
    const mapId  = this.getInput("nodeMapId").trim();

    if (!nodeId) { this.showError("nodeFormError", "Node ID is required."); return; }
    if (isNaN(x) || isNaN(y) || isNaN(theta)) {
      this.showError("nodeFormError", "X, Y and Theta must be valid numbers.");
      return;
    }
    if (!mapId) { this.showError("nodeFormError", "Map ID is required."); return; }

    const node: TopologyNode = { nodeId, x, y, theta, mapId };
    try {
      const res = await fetch("/fleet/topology/nodes", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify(node)
      });
      if (!res.ok) throw new Error(`Server error: ${res.status}`);
      this.closeNodeForm();
    } catch (err) {
      this.showError("nodeFormError", String(err));
    }
  }

  // ── Edge form ──────────────────────────────────────────────────────────────

  private openEdgeForm(edgeId: string | null): void {
    const form   = this.querySelector<HTMLElement>("#edgeForm");
    const title  = this.querySelector<HTMLElement>("#edgeFormTitle");
    const idInput = this.querySelector<HTMLInputElement>("#edgeId");
    if (!form || !title || !idInput) return;

    if (edgeId) {
      const edge = this.edges.find((e) => e.edgeId === edgeId);
      if (!edge) return;
      this.editingEdge         = edge;
      title.textContent        = "Edit Edge";
      idInput.value            = edge.edgeId;
      idInput.readOnly         = true;
      this.setInput("edgeFrom", edge.from);
      this.setInput("edgeTo",   edge.to);
    } else {
      this.editingEdge  = null;
      title.textContent = "Add Edge";
      idInput.value     = "";
      idInput.readOnly  = false;
      this.setInput("edgeFrom", "");
      this.setInput("edgeTo",   "");
    }

    this.hideError("edgeFormError");
    form.classList.remove("hidden");
    idInput.focus();
  }

  private closeEdgeForm(): void {
    this.editingEdge = null;
    this.querySelector("#edgeForm")?.classList.add("hidden");
  }

  private async saveEdge(): Promise<void> {
    const edgeId = this.getInput("edgeId").trim();
    const from   = this.getInput("edgeFrom").trim();
    const to     = this.getInput("edgeTo").trim();

    if (!edgeId) { this.showError("edgeFormError", "Edge ID is required."); return; }
    if (!from)   { this.showError("edgeFormError", "From Node is required."); return; }
    if (!to)     { this.showError("edgeFormError", "To Node is required."); return; }

    const edge: TopologyEdge = { edgeId, from, to };
    try {
      const res = await fetch("/fleet/topology/edges", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify(edge)
      });
      if (!res.ok) throw new Error(`Server error: ${res.status}`);
      this.closeEdgeForm();
    } catch (err) {
      this.showError("edgeFormError", String(err));
    }
  }

  // ── Delete ─────────────────────────────────────────────────────────────────

  private async deleteNode(nodeId: string): Promise<void> {
    if (!confirm(`Delete node "${nodeId}"?`)) return;
    try {
      const res = await fetch(`/fleet/topology/nodes/${encodeURIComponent(nodeId)}`, {
        method: "DELETE"
      });
      if (!res.ok) throw new Error(`Server error: ${res.status}`);
    } catch (err) {
      alert(`Failed to delete node: ${err}`);
    }
  }

  private async deleteEdge(edgeId: string): Promise<void> {
    if (!confirm(`Delete edge "${edgeId}"?`)) return;
    try {
      const res = await fetch(`/fleet/topology/edges/${encodeURIComponent(edgeId)}`, {
        method: "DELETE"
      });
      if (!res.ok) throw new Error(`Server error: ${res.status}`);
    } catch (err) {
      alert(`Failed to delete edge: ${err}`);
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

customElements.define("fleet-topology-config", FleetTopologyConfig);
