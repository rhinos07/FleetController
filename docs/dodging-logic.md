# Dodging Logic

This document describes the **AGV dodge / collision-avoidance** mechanism in the VDA5050 Fleet Controller: how the fleet controller detects that one AGV is blocking another AGV's path, how it resolves the situation by issuing a short "dodge" order, and what edge-cases are handled.

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Key Concepts](#2-key-concepts)
3. [When Dodging Is Triggered](#3-when-dodging-is-triggered)
4. [Blocker Detection Rules](#4-blocker-detection-rules)
5. [Dodge-Target Selection](#5-dodge-target-selection)
6. [Proactive Re-Trigger After Order Completion](#6-proactive-re-trigger-after-order-completion)
7. [Simulator — Blocked-State Announcement](#7-simulator--blocked-state-announcement)
8. [Sequence Diagram](#8-sequence-diagram)
9. [Edge Cases and Safeguards](#9-edge-cases-and-safeguards)
10. [Topology Requirements](#10-topology-requirements)

---

## 1. Problem Statement

An AGV (AGV1) completes a transport order and parks at station **OUT-A**.  
A second AGV (AGV2) is later dispatched to pick from or deliver to **OUT-A**.  
Because AGV1 is still physically present, AGV2 cannot enter the node.

**Without dodging the fleet is deadlocked:**

- AGV1 stays at OUT-A indefinitely (no new order was assigned to it).
- AGV2 cannot proceed; it is either stopped mid-path or the simulator thread is blocked on the internal node-lock.
- No automatic recovery occurs.

**With dodging:**

1. The fleet controller detects the blockage.
2. It sends AGV1 a short *dodge order* that moves it to a free adjacent node.
3. AGV1 vacates OUT-A; AGV2 can now enter.

---

## 2. Key Concepts

| Term | Meaning |
|---|---|
| **Blocker** | An AGV that is stationary at a node needed by another AGV. |
| **Assigned vehicle** | The AGV whose path is blocked; the one we are trying to help. |
| **Dodge order** | A minimal VDA5050 order (ID prefix `DODGE-`) with no pick/drop actions that moves the blocker one hop to a free neighbouring node. |
| **Remaining node IDs** | The list of VDA5050 `nodeStates` reported by a stopped AGV; indicates which nodes it still needs to visit. |
| **pendingOccupied** | The set of node IDs that must *not* be used as dodge targets in a single planning cycle (see §5). |

---

## 3. When Dodging Is Triggered

Dodge logic fires in **three situations**:

### 3a. At dispatch time

`FleetController.DispatchToVehicleAsync` builds the full path for the assigned vehicle and immediately calls `TryResolveBlockersAsync(pathNodeIds, assignedVehicle)` before sending the order to the AGV.  
This handles the common case where a parked AGV is already at the source or destination when the order is created.

### 3b. Mid-path blockage (dynamic)

`FleetController.HandleVehicleStateAsync` checks every incoming state message.  
If a vehicle reports **`driving = false`** with **remaining `nodeStates`** (meaning it has stopped before reaching its final node), the fleet controller calls `TryResolveBlockersAsync(remainingNodeIds, stoppedVehicle)`.

This situation arises when a blocker arrives at a node *after* the assigned vehicle was already dispatched, so the issue was not visible at dispatch time.  
The simulator triggers this path by publishing a "waiting" state (see §7) whenever it cannot immediately acquire a node.

### 3c. Proactive re-trigger after order completion

When an AGV completes its order and transitions to **Idle**, it may be parked at a node that another vehicle has been waiting for.  
`HandleVehicleStateAsync` calls `TryUnblockVehiclesBlockedByAsync(idleVehicle)` which:

1. Looks at all other vehicles that have a stored `RemainingNodeIds` list containing the newly-idle vehicle's `LastNodeId`.
2. For each such blocked vehicle, re-runs `TryResolveBlockersAsync(remainingNodeIds, blockedVehicle)`.

This is the critical path that was previously missing: **the dodge that is sent *after* the blocking AGV finishes its own order**.

---

## 4. Blocker Detection Rules

Inside `TryResolveBlockersAsync`, a vehicle `v` is considered a **blocker** at node `nodeId` when *all* of the following hold:

| Condition | Reason |
|---|---|
| `v.VehicleId != assignedVehicle.VehicleId` | Do not try to move the vehicle we are trying to help. |
| `v.LastNodeId == nodeId` | Vehicle is physically at the node. |
| `v.Status` is **not** `Driving`, `Offline`, or `Error` | Only stationary, operational vehicles can be safely moved. |
| `v.CurrentOrderId is null` **OR** the order is **not** in the active queue | Covers two cases: truly-idle vehicles, and vehicles in `Busy` status whose order has already been completed but whose next heartbeat has not cleared the order ID yet. |

---

## 5. Dodge-Target Selection

The dodge target for a blocker at `nodeId` is the **first free adjacent node** according to the topology.

A node `n` is considered **not free** (`pendingOccupied`) if any of these is true:

- `n` is in `idleAtNode.Keys` — another idle/stale-busy vehicle already occupies it (it would just create a new blockage).
- `n` is in `pathNodeIds` — the assigned vehicle needs to visit it (dodge would move the blocker into the path).
- `n == assignedVehicle.LastNodeId` — the assigned vehicle is *currently standing* there; sending the blocker there would deadlock both vehicles.

The set `pendingOccupied` is updated incrementally: after a dodge target `t` is selected for one blocker, `t` is added to `pendingOccupied` so that a subsequent blocker at the same node does not pick the same target.  
After all blockers at a node are handled, the node itself is removed from `pendingOccupied` (it will be vacated).

If **no free neighbour** exists, the fleet controller logs a warning and skips that blocker — a deadlock in the topology cannot be resolved by a single-hop dodge.

---

## 6. Proactive Re-Trigger After Order Completion

### Data flow

```
AGV2 stopped mid-path
        │
        ▼ (state: driving=false, nodeStates=[OUT-A, …])
HandleVehicleStateAsync
        │
        ├─► TryResolveBlockersAsync          (attempt at the time the stop is received)
        │       │
        │       └─ AGV1 still Driving → no blocker found → returns
        │
        └─► vehicle.RemainingNodeIds = ["OUT-A", …]   ← stored on Vehicle object

… time passes …

AGV1 completes its order at OUT-A
        │
        ▼ (state: driving=false, nodeStates=[], orderId=TO-agv1-…)
HandleVehicleStateAsync
        │
        ├─► _queue.Complete("TO-agv1-…")
        │
        ├─► vehicle.IsAvailable → true (wasIdle was false)
        │
        ├─► TryDispatchAsync()               (dispatch any pending orders)
        │
        └─► TryUnblockVehiclesBlockedByAsync(AGV1)
                │
                └─ finds AGV2.RemainingNodeIds contains "OUT-A"
                        │
                        └─► TryResolveBlockersAsync(["OUT-A"], AGV2)
                                │
                                └─ AGV1 is now Idle at OUT-A → blocker found
                                        │
                                        └─► SendDodgeOrderAsync(AGV1, "OUT-A", "SIDE")
```

### `Vehicle.RemainingNodeIds`

A new property on the `Vehicle` domain model.  
Updated in `ApplyState`:

- **Set** to the list of node IDs from `state.NodeStates` when `driving = false`, `nodeStates.Count > 0`, and `orderId` is set.
- **Cleared** (`null`) when the vehicle starts driving again, completes its order, or has no remaining nodes.

---

## 7. Simulator — Blocked-State Announcement

The Python AGV simulator uses per-node threading locks to prevent two simulated vehicles from occupying the same node simultaneously.

**Before the fix**, when a simulator thread tried to acquire a node that was held by another vehicle, it simply called `lock.acquire()` (blocking) with no state update. The fleet controller had no knowledge that the vehicle was waiting.

**After the fix**, the simulator checks `lock.acquire(blocking=False)` first. If the node is occupied:

1. The vehicle publishes a state message with `driving = false` and the current `nodeStates` (remaining nodes unchanged).
2. It then calls `lock.acquire()` to wait until the node is free.

This announcement causes the fleet controller to store `RemainingNodeIds` on the waiting vehicle and triggers `TryResolveBlockersAsync` (§3b). If the blocker is already idle, the dodge is sent immediately; otherwise the proactive re-trigger (§3c) fires when the blocker later becomes idle.

---

## 8. Sequence Diagram

```
AGV1 (blocker)            Fleet Controller              AGV2 (waiting)
     │                           │                           │
     │── state (order done) ────►│                           │
     │   nodeStates=[]           │── _queue.Complete ────────┤
     │   driving=false           │                           │
     │                           │   AGV1 now Idle at OUT-A  │
     │                           │                           │
     │                           │◄── state (blocked) ───────│
     │                           │    driving=false           │
     │                           │    nodeStates=[OUT-A]      │
     │                           │                           │
     │                           │   Store AGV2.RemainingNodeIds=["OUT-A"]
     │                           │   TryResolveBlockersAsync → AGV1 idle? NO (still Driving at this point)
     │                           │                           │
     ▼ (order completes)         │                           │
     │── state (order done) ────►│                           │
     │   nodeStates=[]           │── _queue.Complete ────────┤
     │   driving=false           │                           │
     │   lastNodeId=OUT-A        │   AGV1 now Idle at OUT-A  │
     │                           │                           │
     │                           │   TryUnblockVehiclesBlockedByAsync(AGV1)
     │                           │   → AGV2.RemainingNodeIds contains OUT-A
     │                           │   → TryResolveBlockersAsync(["OUT-A"], AGV2)
     │                           │   → AGV1 is idle at OUT-A → BLOCKER FOUND
     │                           │                           │
     │◄── DODGE order ───────────│                           │
     │    OUT-A → SIDE           │                           │
     │                           │                           │
     │ (acquires SIDE, releases OUT-A)                       │
     │                           │                           │
     │                           │       OUT-A lock released │
     │                           │◄─────────────────── AGV2 unblocks, drives to OUT-A
```

---

## 9. Edge Cases and Safeguards

### Deadlock prevention: assigned vehicle's position excluded

If the waiting vehicle (AGV2) is stopped at node SRC with OUT-A remaining, sending the blocker from OUT-A back to SRC would create a deadlock (AGV2 holds SRC while waiting for OUT-A; blocker tries to enter SRC while leaving OUT-A).  
`pendingOccupied` always includes `assignedVehicle.LastNodeId`, so the dodge target is never the current position of the blocked vehicle.

This complements the existing protection that excludes all nodes on the assigned vehicle's *path* (source node is held during pick; destination is the goal).

### Blocker detection covers "stale-busy" vehicles

After an order is completed and removed from the active queue, the AGV continues reporting the old `orderId` in its heartbeat until the fleet controller strips it on the next state message. During this window, the vehicle's status is `Busy` (not `Idle`).  
The blocker filter explicitly handles this: `v.CurrentOrderId is null || _queue.FindActive(v.CurrentOrderId) is null`.

### Multiple blockers at the same node

If multiple vehicles are parked at the same node (not possible in the simulator but theoretically valid), each is sent a different dodge target using the incrementally-growing `pendingOccupied` set.

### No free neighbour

If all neighbours of a blocked node are themselves occupied or on the path, the fleet controller logs a warning and skips that node. This can occur in very dense or poorly-connected topologies; it is a topology design limitation, not a software bug.

---

## 10. Topology Requirements

For dodging to work, every station node that can be a **destination** must have at least one **additional adjacent node** that is not on the critical path of any pending order.  

The demo topology uses naming conventions:
- `IN-*` — input stations (pick-up points)
- `OUT-*` — output stations (drop-off points)
- `CHG-*` — charging stations
- Waypoint nodes connect stations and provide the side-branches needed for dodge manoeuvres.

A purely linear topology (A — B — C, only three nodes) where B is both source and destination provides no valid dodge target for a blocker at B when A and C are also occupied or on-path.  
**Recommendation:** ensure every terminal node has at least two edges so there is always an escape route for a parked AGV.
