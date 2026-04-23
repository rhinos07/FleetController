export class FleetBatterySettings extends HTMLElement {
  private thresholdInput: HTMLInputElement | null = null;
  private saveBtn: HTMLButtonElement | null = null;
  private statusEl: HTMLElement | null = null;

  private currentThreshold: number = 30;

  constructor() {
    super();
  }

  connectedCallback(): void {
    this.render();
    this.thresholdInput = this.querySelector("#batteryThresholdInput");
    this.saveBtn = this.querySelector("#batteryThresholdSave");
    this.statusEl = this.querySelector("#batteryThresholdStatus");

    this.saveBtn?.addEventListener("click", () => this.saveThreshold());
  }

  private render(): void {
    this.innerHTML = `
      <div class="battery-settings-panel">
        <h3>Battery Charging Settings</h3>
        <p class="battery-settings-desc">
          Vehicles with battery below the threshold are automatically dispatched to a free
          charging station. If all charging stations are occupied, a fully-charged vehicle
          is evicted to make room.
        </p>
        <div class="battery-settings-controls">
          <label>
            Low Battery Threshold (%)
            <input
              type="number"
              id="batteryThresholdInput"
              min="1"
              max="99"
              step="1"
              value="${this.currentThreshold}"
            >
          </label>
          <button id="batteryThresholdSave" type="button" class="btn-primary">Save</button>
        </div>
        <p id="batteryThresholdStatus" class="battery-settings-status"></p>
      </div>
    `;
  }

  public updateThreshold(threshold: number): void {
    this.currentThreshold = threshold;
    if (this.thresholdInput) {
      this.thresholdInput.value = String(threshold);
    }
  }

  private async saveThreshold(): Promise<void> {
    const raw = this.thresholdInput?.value ?? "";
    const thresholdValue = parseFloat(raw);

    if (isNaN(thresholdValue) || thresholdValue < 1 || thresholdValue > 99) {
      this.setStatus("Please enter a value between 1 and 99.", "error");
      return;
    }

    if (this.saveBtn) this.saveBtn.disabled = true;
    this.setStatus("Saving…", "");

    try {
      const res = await fetch("/fleet/settings/battery-threshold", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ threshold: thresholdValue }),
      });

      if (res.ok) {
        this.currentThreshold = thresholdValue;
        this.setStatus(`Threshold saved: ${thresholdValue}%`, "ok");
      } else {
        const text = await res.text();
        this.setStatus(`Error: ${text}`, "error");
      }
    } catch (err) {
      this.setStatus(`Network error: ${String(err)}`, "error");
    } finally {
      if (this.saveBtn) this.saveBtn.disabled = false;
    }
  }

  private setStatus(msg: string, cls: string): void {
    if (!this.statusEl) return;
    this.statusEl.textContent = msg;
    this.statusEl.className = `battery-settings-status ${cls}`.trim();
  }
}

customElements.define("fleet-battery-settings", FleetBatterySettings);
