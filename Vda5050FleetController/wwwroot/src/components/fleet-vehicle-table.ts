import { TopologyNode, VehicleSummary } from "../types/models";

export class FleetVehicleTable extends HTMLElement {
  private tableBodyEl: HTMLElement | null = null;
  private vehicleSelect: HTMLSelectElement | null = null;
  private nodeSelect: HTMLSelectElement | null = null;
  private sendBtn: HTMLButtonElement | null = null;
  private statusEl: HTMLElement | null = null;

  private vehicles: VehicleSummary[] = [];
  private nodes: TopologyNode[] = [];

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.tableBodyEl = this.querySelector("#vehicleRows");
    this.vehicleSelect = this.querySelector("#repoVehicle");
    this.nodeSelect = this.querySelector("#repoNode");
    this.sendBtn = this.querySelector("#repoSend");
    this.statusEl = this.querySelector("#repoStatus");

    this.sendBtn?.addEventListener("click", () => this.sendReposition());
  }

  private render(): void {
    this.innerHTML = `
      <h2>Fahrzeugtabelle</h2>
      <table>
        <thead>
          <tr>
            <th>Fahrzeug</th>
            <th>Status</th>
            <th>Batterie (%)</th>
            <th>Order</th>
            <th>Position</th>
            <th>Last Seen (UTC)</th>
          </tr>
        </thead>
        <tbody id="vehicleRows">
          <tr><td colspan="6" class="muted">Warte auf Live-Daten …</td></tr>
        </tbody>
      </table>

      <div class="reposition-panel">
        <h3>Fahrzeug manuell positionieren</h3>
        <div class="reposition-controls">
          <label>
            Fahrzeug
            <select id="repoVehicle">
              <option value="">-- Fahrzeug wählen --</option>
            </select>
          </label>
          <label>
            Zielknoten
            <select id="repoNode">
              <option value="">-- Ziel wählen --</option>
            </select>
          </label>
          <button id="repoSend" type="button">Fahren</button>
        </div>
        <p id="repoStatus" class="repo-status"></p>
      </div>
    `;
  }

  public updateVehicles(vehicles: VehicleSummary[]): void {
    this.vehicles = vehicles;
    this.renderTable();
    this.refreshVehicleSelect();
  }

  public updateNodes(nodes: TopologyNode[]): void {
    this.nodes = nodes;
    this.refreshNodeSelect();
  }

  private renderTable(): void {
    if (!this.tableBodyEl) return;

    if (this.vehicles.length === 0) {
      this.tableBodyEl.innerHTML = '<tr><td colspan="6" class="muted">Keine Fahrzeuge gemeldet</td></tr>';
      return;
    }

    this.tableBodyEl.innerHTML = this.vehicles
      .map(
        (v) => `
          <tr>
            <td>${v.vehicleId ?? "-"}</td>
            <td>${v.status ?? "-"}</td>
            <td>${v.battery ?? "-"}</td>
            <td>${v.orderId ?? "-"}</td>
            <td>${this.formatPosition(v)}</td>
            <td>${this.formatDateTime(v.lastSeen)}</td>
          </tr>
        `
      )
      .join("");
  }

  private refreshVehicleSelect(): void {
    if (!this.vehicleSelect) return;
    const current = this.vehicleSelect.value;
    const idleVehicles = this.vehicles.filter((v) => v.status === "Idle");

    this.vehicleSelect.innerHTML =
      '<option value="">-- Fahrzeug wählen --</option>' +
      idleVehicles
        .map((v) => `<option value="${v.vehicleId}">${v.vehicleId}</option>`)
        .join("");

    if (idleVehicles.some((v) => v.vehicleId === current)) {
      this.vehicleSelect.value = current;
    }
  }

  private refreshNodeSelect(): void {
    if (!this.nodeSelect) return;
    const current = this.nodeSelect.value;

    this.nodeSelect.innerHTML =
      '<option value="">-- Ziel wählen --</option>' +
      this.nodes
        .map((n) => `<option value="${n.nodeId}">${n.nodeId}</option>`)
        .join("");

    if (this.nodes.some((n) => n.nodeId === current)) {
      this.nodeSelect.value = current;
    }
  }

  private async sendReposition(): Promise<void> {
    const vehicleId = this.vehicleSelect?.value;
    const destNodeId = this.nodeSelect?.value;

    if (!vehicleId || !destNodeId) {
      this.setStatus("Bitte Fahrzeug und Zielknoten wählen.", "error");
      return;
    }

    if (this.sendBtn) this.sendBtn.disabled = true;
    this.setStatus("Wird gesendet …", "");

    try {
      const res = await fetch("/fleet/vehicles/move", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ vehicleId, destNodeId }),
      });

      if (res.ok) {
        this.setStatus(`${vehicleId} fährt zu ${destNodeId}.`, "ok");
      } else {
        const text = await res.text();
        this.setStatus(`Fehler: ${text}`, "error");
      }
    } catch (err) {
      this.setStatus(`Netzwerkfehler: ${String(err)}`, "error");
    } finally {
      if (this.sendBtn) this.sendBtn.disabled = false;
    }
  }

  private setStatus(msg: string, cls: string): void {
    if (!this.statusEl) return;
    this.statusEl.textContent = msg;
    this.statusEl.className = `repo-status ${cls}`.trim();
  }

  private formatPosition(vehicle: VehicleSummary): string {
    if (!vehicle.position) return "-";
    const { x, y, mapId } = vehicle.position;
    return `${x.toFixed(2)}, ${y.toFixed(2)} (${mapId ?? "-"})`;
  }

  private formatDateTime(dateTime: string): string {
    if (!dateTime) return "-";
    return new Date(dateTime).toISOString();
  }
}

customElements.define("fleet-vehicle-table", FleetVehicleTable);
