# Vehicle Communication

This document describes how the Fleet Controller communicates with AGVs (Automated Guided Vehicles) using the **VDA5050 v2** protocol over **MQTT**.

---

## Overview

All communication between the Fleet Controller and vehicles follows the [VDA5050 v2 specification](https://www.vda.de/en/topics/innovation-and-technology/automated-guided-vehicle-systems/vda-5050). Messages are exchanged as JSON payloads over an MQTT broker (default: `localhost:1883`).

```
┌────────────────────────┐          MQTT Broker          ┌──────────────────┐
│    Fleet Controller    │ ◄───────────────────────────► │     Vehicle      │
│  (Vda5050MqttService)  │                               │   (AGV / AMR)    │
└────────────────────────┘                               └──────────────────┘
          │  publishes: order, instantActions                     │
          │  subscribes: state, connection                        │
          └───────────────────────────────────────────────────────┘
```

---

## MQTT Topic Structure

Topics follow the VDA5050 convention:

```
{interfaceName}/{majorVersion}/{manufacturer}/{serialNumber}/{messageType}
```

| Placeholder      | Default value        | Description                              |
|------------------|----------------------|------------------------------------------|
| `interfaceName`  | `uagv`               | Fixed interface identifier               |
| `majorVersion`   | `v2`                 | Protocol major version                   |
| `manufacturer`   | vehicle-specific     | Manufacturer name reported by the AGV    |
| `serialNumber`   | vehicle-specific     | Serial number reported by the AGV        |
| `messageType`    | see table below      | Type of VDA5050 message                  |

### Topic Examples

| Direction                       | Topic                                      | Message Type     |
|---------------------------------|--------------------------------------------|------------------|
| Fleet Controller → Vehicle      | `uagv/v2/acme/agv-01/order`               | `order`          |
| Fleet Controller → Vehicle      | `uagv/v2/acme/agv-01/instantActions`      | `instantActions` |
| Vehicle → Fleet Controller      | `uagv/v2/acme/agv-01/state`               | `state`          |
| Vehicle → Fleet Controller      | `uagv/v2/acme/agv-01/connection`          | `connection`     |

The Fleet Controller subscribes to wildcard topics to receive messages from **all** vehicles simultaneously:

```
uagv/v2/+/+/state
uagv/v2/+/+/connection
```

---

## Message Types

### Common Header

Every VDA5050 message includes the following header fields:

| Field          | Type     | Description                                           |
|----------------|----------|-------------------------------------------------------|
| `headerId`     | `int`    | Monotonically incrementing counter per vehicle        |
| `timestamp`    | `string` | ISO 8601 UTC timestamp (e.g. `2024-01-15T10:30:00Z`) |
| `version`      | `string` | VDA5050 version (`2.0.0`)                             |
| `manufacturer` | `string` | Manufacturer identifier                               |
| `serialNumber` | `string` | Vehicle serial number                                 |

---

### Fleet Controller → Vehicle

#### 1. `order`

Instructs the vehicle to execute a transport task. Contains the full path as a sequence of **nodes** and **edges**, with **actions** (e.g. `pick`, `drop`) attached to nodes.

**Topic:** `uagv/v2/{manufacturer}/{serialNumber}/order`  
**QoS:** At Least Once (1)

| Field           | Type           | Description                                             |
|-----------------|----------------|---------------------------------------------------------|
| `orderId`       | `string`       | Unique order identifier (e.g. `TO-a1b2c3d4e5f6g7h8`)  |
| `orderUpdateId` | `int`          | Increments on each order update (starts at `0`)         |
| `nodes`         | `Node[]`       | Ordered list of waypoints the vehicle must reach        |
| `edges`         | `Edge[]`       | Connections between consecutive nodes                   |

**Node fields:**

| Field          | Type           | Description                                          |
|----------------|----------------|------------------------------------------------------|
| `nodeId`       | `string`       | Unique node identifier matching the topology map     |
| `sequenceId`   | `int`          | Position in the path (0, 2, 4, …; edges are odd)    |
| `released`     | `bool`         | Whether the vehicle may traverse this node now       |
| `nodePosition` | `NodePosition` | Coordinates `x`, `y`, `theta`, `mapId`              |
| `actions`      | `VdaAction[]`  | Actions to execute at this node (e.g. `pick`, `drop`)|

**Edge fields:**

| Field         | Type          | Description                                     |
|---------------|---------------|-------------------------------------------------|
| `edgeId`      | `string`      | Unique edge identifier                          |
| `sequenceId`  | `int`         | Position in the path (1, 3, 5, …; nodes are even)|
| `released`    | `bool`        | Whether the vehicle may traverse this edge now  |
| `startNodeId` | `string`      | Origin node                                     |
| `endNodeId`   | `string`      | Destination node                                |
| `maxSpeed`    | `double?`     | Optional speed limit in m/s (default: `1.5`)    |
| `actions`     | `VdaAction[]` | Actions to execute while traversing the edge    |

**VdaAction fields:**

| Field              | Type                | Description                                            |
|--------------------|---------------------|--------------------------------------------------------|
| `actionId`         | `string`            | Unique action identifier                               |
| `actionType`       | `string`            | Action name (e.g. `pick`, `drop`, `stopPause`)        |
| `blockingType`     | `string`            | `HARD` = vehicle waits for action to finish            |
| `actionParameters` | `ActionParameter[]` | Key/value pairs (e.g. `loadId: PAL-42`)               |

**Example payload:**

```json
{
  "headerId": 1,
  "timestamp": "2024-01-15T10:30:00.000Z",
  "version": "2.0.0",
  "manufacturer": "acme",
  "serialNumber": "agv-01",
  "orderId": "TO-a1b2c3d4e5f6",
  "orderUpdateId": 0,
  "nodes": [
    {
      "nodeId": "STATION-IN-01",
      "sequenceId": 0,
      "released": true,
      "nodePosition": { "x": 5.0, "y": 3.0, "theta": 0.0, "mapId": "FLOOR-1" },
      "actions": [
        {
          "actionId": "pick-TO-a1b2c3d4e5f6",
          "actionType": "pick",
          "blockingType": "HARD",
          "actionParameters": [{ "key": "loadId", "value": "PAL-42" }]
        }
      ]
    },
    {
      "nodeId": "STATION-OUT-01",
      "sequenceId": 2,
      "released": true,
      "nodePosition": { "x": 40.0, "y": 3.0, "theta": 3.1415, "mapId": "FLOOR-1" },
      "actions": [
        {
          "actionId": "drop-TO-a1b2c3d4e5f6",
          "actionType": "drop",
          "blockingType": "HARD",
          "actionParameters": []
        }
      ]
    }
  ],
  "edges": [
    {
      "edgeId": "E-STATION-IN-01-STATION-OUT-01",
      "sequenceId": 1,
      "released": true,
      "startNodeId": "STATION-IN-01",
      "endNodeId": "STATION-OUT-01",
      "maxSpeed": 1.5,
      "actions": []
    }
  ]
}
```

---

#### 2. `instantActions`

Sends one or more immediate commands to the vehicle. These are executed out-of-band and are not part of the current order path.

**Topic:** `uagv/v2/{manufacturer}/{serialNumber}/instantActions`  
**QoS:** At Least Once (1)

| Field            | Type          | Description                             |
|------------------|---------------|-----------------------------------------|
| `instantActions` | `VdaAction[]` | List of actions to execute immediately  |

**Supported instant action types:**

| `actionType`     | Triggered by                                 | Description                   |
|------------------|----------------------------------------------|-------------------------------|
| `stopPause`      | `POST /fleet/vehicles/{vehicleId}/pause`     | Pause the vehicle immediately |
| `startPause`     | `POST /fleet/vehicles/{vehicleId}/resume`    | Resume the vehicle            |
| `startCharging`  | `POST /fleet/vehicles/{vehicleId}/charge`    | Initiate battery charging     |

**Example payload:**

```json
{
  "headerId": 2,
  "timestamp": "2024-01-15T10:35:00.000Z",
  "version": "2.0.0",
  "manufacturer": "acme",
  "serialNumber": "agv-01",
  "instantActions": [
    {
      "actionId": "IA-9f3e1a2b",
      "actionType": "stopPause",
      "blockingType": "HARD",
      "actionParameters": []
    }
  ]
}
```

---

### Vehicle → Fleet Controller

#### 3. `state`

Periodically published by the vehicle to report its current status, position, battery, active order progress, and any errors.

**Topic:** `uagv/v2/{manufacturer}/{serialNumber}/state`  
**QoS:** At Most Once (0)  
**Subscribed via wildcard:** `uagv/v2/+/+/state`

| Field                 | Type              | Description                                                  |
|-----------------------|-------------------|--------------------------------------------------------------|
| `orderId`             | `string`          | ID of the order currently being executed (empty if idle)     |
| `orderUpdateId`       | `int`             | Update counter matching the dispatched order                 |
| `lastNodeId`          | `string`          | ID of the last node the vehicle passed                       |
| `lastNodeSequenceId`  | `int`             | Sequence ID of that node                                     |
| `driving`             | `bool`            | `true` while the vehicle is in motion                        |
| `operatingMode`       | `string`          | Operating mode (e.g. `AUTOMATIC`, `MANUAL`)                  |
| `agvPosition`         | `AgvPosition`     | Current position: `x`, `y`, `theta`, `mapId`, `localizationScore` |
| `velocity`            | `Velocity`        | Current velocity: `vx`, `vy`, `omega`                        |
| `batteryState`        | `BatteryState`    | `batteryCharge` (0–100 %) and `charging` flag                |
| `actionStates`        | `ActionState[]`   | Status of each active or recently completed action           |
| `errors`              | `VdaError[]`      | Active errors: `errorType`, `errorLevel`, `errorDescription` |
| `nodeStates`          | `NodeState[]`     | Remaining nodes in the current order horizon                 |
| `edgeStates`          | `EdgeState[]`     | Remaining edges in the current order horizon                 |

**Order completion detection:** The Fleet Controller marks an order as completed when a `state` message arrives with the matching `orderId` and **all** of `nodeStates`, `edgeStates` are empty and `driving` is `false`.

**Vehicle status derivation (internal):**

| Condition                              | `VehicleStatus` |
|----------------------------------------|-----------------|
| `errors` contains a `FATAL` error      | `Error`         |
| `driving == true`                      | `Driving`       |
| `orderId` is non-empty                 | `Busy`          |
| otherwise                              | `Idle`          |

**Example payload:**

```json
{
  "headerId": 42,
  "timestamp": "2024-01-15T10:31:05.000Z",
  "version": "2.0.0",
  "manufacturer": "acme",
  "serialNumber": "agv-01",
  "orderId": "TO-a1b2c3d4e5f6",
  "orderUpdateId": 0,
  "lastNodeId": "STATION-IN-01",
  "lastNodeSequenceId": 0,
  "driving": true,
  "operatingMode": "AUTOMATIC",
  "agvPosition": {
    "x": 12.3,
    "y": 3.0,
    "theta": 0.0,
    "mapId": "FLOOR-1",
    "positionInitialized": true,
    "localizationScore": 0.98
  },
  "velocity": { "vx": 1.2, "vy": 0.0, "omega": 0.0 },
  "batteryState": { "batteryCharge": 78.5, "charging": false },
  "actionStates": [
    { "actionId": "pick-TO-a1b2c3d4e5f6", "actionStatus": "FINISHED", "resultDescription": "" }
  ],
  "errors": [],
  "nodeStates": [
    { "nodeId": "STATION-OUT-01", "sequenceId": 2, "released": true }
  ],
  "edgeStates": [
    { "edgeId": "E-STATION-IN-01-STATION-OUT-01", "sequenceId": 1, "released": true }
  ]
}
```

---

#### 4. `connection`

Published by the vehicle (or MQTT broker via a Last Will message) to report connectivity changes.

**Topic:** `uagv/v2/{manufacturer}/{serialNumber}/connection`  
**QoS:** At Least Once (1)  
**Subscribed via wildcard:** `uagv/v2/+/+/connection`

| Field             | Type     | Description                                 |
|-------------------|----------|---------------------------------------------|
| `connectionState` | `string` | `ONLINE`, `OFFLINE`, or `CONNECTIONBROKEN`  |

When a vehicle comes online (`ONLINE`) it is registered in the `VehicleRegistry` and its status is set to `Idle`. When it goes offline (`OFFLINE` or `CONNECTIONBROKEN`), its status is set to `Offline`.

**Example payload:**

```json
{
  "headerId": 1,
  "timestamp": "2024-01-15T10:29:58.000Z",
  "version": "2.0.0",
  "manufacturer": "acme",
  "serialNumber": "agv-01",
  "connectionState": "ONLINE"
}
```

---

## Message Flow

### Transport Order Lifecycle

```
WMS/MFR           Fleet Controller            MQTT Broker              Vehicle
   │                     │                         │                      │
   │  POST /fleet/orders  │                         │                      │
   │────────────────────►│                         │                      │
   │  202 Accepted        │                         │                      │
   │◄────────────────────│                         │                      │
   │                     │── publish order ────────►│                      │
   │                     │   (QoS 1)               │── order ────────────►│
   │                     │                         │                      │ (pick at source)
   │                     │◄────────────────────────│◄── state (driving) ──│
   │                     │◄────────────────────────│◄── state (driving) ──│
   │                     │                         │                      │ (drop at dest)
   │                     │◄────────────────────────│◄── state (idle) ─────│
   │                     │  (order completed)      │                      │
```

### Instant Action (Pause / Resume)

```
Operator          Fleet Controller            MQTT Broker              Vehicle
   │                     │                         │                      │
   │  POST /pause         │                         │                      │
   │────────────────────►│                         │                      │
   │                     │── publish instantActions►│                      │
   │                     │   (QoS 1)               │── instantActions ───►│
   │                     │                         │                      │ (stops)
   │  POST /resume        │                         │                      │
   │────────────────────►│                         │                      │
   │                     │── publish instantActions►│                      │
   │                     │   (QoS 1)               │── instantActions ───►│
   │                     │                         │                      │ (resumes)
```

### Vehicle Registration

```
MQTT Broker         Fleet Controller
     │                    │
     │── connection ──────►│  GetOrCreate(manufacturer, serial)
     │   connectionState:  │  → VehicleStatus = Idle
     │   "ONLINE"          │
     │                    │
     │── state ───────────►│  ApplyState(vehicleState)
     │   (first report)    │  → position, battery updated
```

---

## Vehicle Availability

A vehicle is eligible to receive a new transport order only when **all** of the following conditions are met:

1. `VehicleStatus` is `Idle`
2. `batteryCharge > 20 %`
3. No active `FATAL` errors

---

## Configuration

MQTT connection parameters are configured in `appsettings.json`:

```json
{
  "Mqtt": {
    "Host":          "localhost",
    "Port":          1883,
    "ClientId":      "fleet-controller-01",
    "InterfaceName": "uagv",
    "MajorVersion":  "v2"
  }
}
```

| Setting         | Description                                                  |
|-----------------|--------------------------------------------------------------|
| `Host`          | MQTT broker hostname or IP address                           |
| `Port`          | MQTT broker port (default: 1883)                             |
| `ClientId`      | MQTT client identifier for the Fleet Controller              |
| `InterfaceName` | First segment of all VDA5050 topics (default: `uagv`)        |
| `MajorVersion`  | Second segment of all VDA5050 topics (default: `v2`)         |
