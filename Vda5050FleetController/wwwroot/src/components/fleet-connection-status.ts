import { ConnectionState } from "../services/fleet-status.service";

export class FleetConnectionStatus extends HTMLElement {
  private statusEl: HTMLElement | null = null;

  private readonly stateMessages: Record<ConnectionState, string> = {
    connecting: "Verbinde mit Live-Stream …",
    connected: "Verbunden (SignalR)",
    reconnecting: "Verbindung unterbrochen, versuche erneut …",
    disconnected: "Verbindung geschlossen"
  };

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.statusEl = this.querySelector("#connectionState");
  }

  private render(): void {
    this.innerHTML = `
      <p class="muted" id="connectionState">Verbinde mit Live-Stream …</p>
    `;
  }

  public updateState(state: ConnectionState): void {
    if (this.statusEl) {
      this.statusEl.textContent = this.stateMessages[state] || "Unbekannter Status";
    }
  }

  public showError(error: string): void {
    if (this.statusEl) {
      this.statusEl.textContent = `SignalR-Fehler: ${error}`;
    }
  }
}

customElements.define("fleet-connection-status", FleetConnectionStatus);
