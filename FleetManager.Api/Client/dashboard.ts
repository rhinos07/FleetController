export type DashboardEventName = 'orderUpdated' | 'vehicleUpdated' | 'zoneBlockChanged';

export interface DashboardEvent {
    event: DashboardEventName;
    payload: unknown;
}
