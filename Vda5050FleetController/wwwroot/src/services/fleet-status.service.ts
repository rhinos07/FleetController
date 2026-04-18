import * as signalR from "@microsoft/signalr";
import { FleetStatus } from "../types/models";

export type ConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

export class FleetStatusService {
  private connection: signalR.HubConnection;
  private statusUpdateHandlers: Set<(status: FleetStatus) => void> = new Set();
  private connectionStateHandlers: Set<(state: ConnectionState) => void> = new Set();

  constructor() {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/fleet-status")
      .withAutomaticReconnect()
      .build();

    this.setupEventHandlers();
  }

  private setupEventHandlers(): void {
    // Handle fleet status updates
    this.connection.on("fleetStatusUpdated", (status: FleetStatus) => {
      this.statusUpdateHandlers.forEach(handler => handler(status));
    });

    // Handle connection state changes
    this.connection.onreconnecting(() => {
      this.notifyConnectionState("reconnecting");
    });

    this.connection.onreconnected(() => {
      this.notifyConnectionState("connected");
    });

    this.connection.onclose(() => {
      this.notifyConnectionState("disconnected");
    });
  }

  private notifyConnectionState(state: ConnectionState): void {
    this.connectionStateHandlers.forEach(handler => handler(state));
  }

  public async start(): Promise<void> {
    try {
      this.notifyConnectionState("connecting");
      await this.connection.start();
      this.notifyConnectionState("connected");
    } catch (error) {
      console.error("SignalR connection error:", error);
      this.notifyConnectionState("disconnected");
      throw error;
    }
  }

  public async stop(): Promise<void> {
    await this.connection.stop();
  }

  public onStatusUpdate(handler: (status: FleetStatus) => void): () => void {
    this.statusUpdateHandlers.add(handler);
    return () => this.statusUpdateHandlers.delete(handler);
  }

  public onConnectionStateChange(handler: (state: ConnectionState) => void): () => void {
    this.connectionStateHandlers.add(handler);
    return () => this.connectionStateHandlers.delete(handler);
  }
}
