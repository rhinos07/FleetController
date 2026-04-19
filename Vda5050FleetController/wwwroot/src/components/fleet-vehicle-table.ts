import { VehicleSummary } from "../types/models";

export class FleetVehicleTable extends HTMLElement {
  private tableBodyEl: HTMLElement | null = null;

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.tableBodyEl = this.querySelector("#vehicleRows");
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
    `;
  }

  public updateVehicles(vehicles: VehicleSummary[]): void {
    if (!this.tableBodyEl) return;

    if (vehicles.length === 0) {
      this.tableBodyEl.innerHTML = '<tr><td colspan="6" class="muted">Keine Fahrzeuge gemeldet</td></tr>';
      return;
    }

    this.tableBodyEl.innerHTML = vehicles
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
