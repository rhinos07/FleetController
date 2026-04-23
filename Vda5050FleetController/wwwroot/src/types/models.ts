/**
 * Domain model type definitions for Fleet Manager
 * These match the C# models from the backend
 */

export interface Position {
  x: number;
  y: number;
  mapId: string;
}

export interface VehicleSummary {
  vehicleId: string;
  status: VehicleStatus;
  position?: Position;
  battery?: number;
  orderId?: string;
  lastSeen: string; // ISO 8601 datetime string
}

export interface TopologyNode {
  nodeId: string;
  x: number;
  y: number;
  theta: number;
  mapId: string;
}

export interface TopologyEdge {
  edgeId: string;
  from: string;
  to: string;
}

export interface OrderSummary {
  orderId: string;
  sourceId: string;
  destId: string;
  loadId?: string;
  status: TransportStatus;
  vehicleId?: string;
}

export interface OrderHistoryDto {
  id: number;
  orderId: string;
  sourceId: string;
  destId: string;
  loadId?: string;
  finalStatus: string;
  assignedVehicleId?: string;
  createdAt: string;   // ISO 8601 datetime string
  startedAt?: string;  // ISO 8601 datetime string, nullable
  completedAt: string; // ISO 8601 datetime string
}

export interface FleetStatus {
  vehicles: VehicleSummary[];
  activeOrders: number;
  pendingOrders: number;
  nodes: TopologyNode[];
  edges: TopologyEdge[];
  orders: OrderSummary[];
  lowBatteryThreshold: number;
}

// Enum types matching backend
export type VehicleStatus =
  | "Idle"
  | "Driving"
  | "Busy"
  | "Error"
  | "Charging";

export type TransportStatus =
  | "Pending"
  | "Assigned"
  | "InProgress"
  | "Completed"
  | "Failed";

// Constants
export const ACTIVE_ORDER_STATUSES: TransportStatus[] = ["Pending", "Assigned", "InProgress"];

export const VEHICLE_STATUS_COLORS: Record<VehicleStatus, string> = {
  Idle: "#10b981",      // green
  Driving: "#f59e0b",   // orange
  Busy: "#f59e0b",      // orange
  Error: "#ef4444",     // red
  Charging: "#8b5cf6"   // purple
};
