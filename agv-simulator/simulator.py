#!/usr/bin/env python3
"""
VDA5050 v2 AGV Simulator — Demo container for FleetController.

Each simulated vehicle:
  1. Fetches the live topology from the FleetController REST API
  2. Publishes ONLINE connection message on startup
  3. Publishes periodic idle state heartbeats
  4. Subscribes to its order topic and simulates driving node-by-node
  5. Executes pick/drop actions with configurable duration
  6. Publishes completion state (empty nodeStates + edgeStates)

Environment variables:
  FLEET_CONTROLLER_URL URL of the fleet controller     (default: http://fleet-controller:8080)
  MQTT_HOST            MQTT broker hostname            (default: mosquitto)
  MQTT_PORT            MQTT broker port                (default: 1883)
  MQTT_INTERFACE_NAME  VDA5050 interface name          (default: uagv)
  MQTT_MAJOR_VERSION   VDA5050 major version           (default: v2)
  AGV_MANUFACTURER     Manufacturer name               (default: acme)
  AGV_SERIALS          Comma-separated serials         (default: agv-01,agv-02,agv-03)
  DRIVE_SPEED          Units per second                (default: 2.0)
  ACTION_DURATION      Seconds per pick/drop           (default: 3.0)
  STATE_HZ             State publishes/s while driving (default: 1.0)
  IDLE_INTERVAL        Seconds between heartbeats      (default: 5.0)
  INITIAL_CHARGE       Initial battery %               (default: 80.0)
  TOPOLOGY_RETRY_S     Seconds between topology retries(default: 3.0)
  TOPOLOGY_MAX_RETRIES Max retries for topology fetch  (default: 30)
"""

import json
import math
import os
import threading
import time
import urllib.request
import urllib.error
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Optional

import paho.mqtt.client as mqtt

# ── Configuration ──────────────────────────────────────────────────────────────

FLEET_URL        = os.getenv("FLEET_CONTROLLER_URL",  "http://fleet-controller:8080")
MQTT_HOST        = os.getenv("MQTT_HOST",              "mosquitto")
MQTT_PORT        = int(os.getenv("MQTT_PORT",          "1883"))
INTERFACE_NAME   = os.getenv("MQTT_INTERFACE_NAME",   "uagv")
MAJOR_VERSION    = os.getenv("MQTT_MAJOR_VERSION",    "v2")
MANUFACTURER     = os.getenv("AGV_MANUFACTURER",      "acme")
AGV_SERIALS      = [s.strip() for s in os.getenv("AGV_SERIALS", "agv-01,agv-02,agv-03").split(",")]
DRIVE_SPEED      = float(os.getenv("DRIVE_SPEED",      "2.0"))
ACTION_DURATION  = float(os.getenv("ACTION_DURATION",  "3.0"))
STATE_HZ         = float(os.getenv("STATE_HZ",         "1.0"))
IDLE_INTERVAL    = float(os.getenv("IDLE_INTERVAL",    "5.0"))
INITIAL_CHARGE   = float(os.getenv("INITIAL_CHARGE",  "80.0"))
TOPOLOGY_RETRY_S = float(os.getenv("TOPOLOGY_RETRY_S", "3.0"))
TOPOLOGY_RETRIES = int(os.getenv("TOPOLOGY_MAX_RETRIES", "30"))
# Distance (in map units) below which a vehicle is considered "already at" a node
NODE_ARRIVAL_TOLERANCE = 0.05

# ── Topology fetch ─────────────────────────────────────────────────────────────

def fetch_topology() -> tuple[dict, list]:
    """
    Fetches nodes and edges from the FleetController REST API.
    Returns (nodes_by_id, edges_list) where nodes_by_id maps nodeId → {x, y, theta, mapId}.
    Retries until the controller is reachable or max retries exceeded.
    """
    nodes_url = f"{FLEET_URL}/fleet/topology/nodes"
    edges_url = f"{FLEET_URL}/fleet/topology/edges"

    for attempt in range(1, TOPOLOGY_RETRIES + 1):
        try:
            with urllib.request.urlopen(nodes_url, timeout=5) as r:
                raw_nodes = json.loads(r.read())
            with urllib.request.urlopen(edges_url, timeout=5) as r:
                raw_edges = json.loads(r.read())

            nodes = {
                n["nodeId"]: {
                    "x":     n["x"],
                    "y":     n["y"],
                    "theta": n["theta"],
                    "mapId": n["mapId"],
                }
                for n in raw_nodes
            }
            print(f"Topology loaded: {len(nodes)} nodes, {len(raw_edges)} edges")
            return nodes, raw_edges

        except Exception as e:
            print(f"Topology fetch attempt {attempt}/{TOPOLOGY_RETRIES} failed: {e}")
            if attempt < TOPOLOGY_RETRIES:
                time.sleep(TOPOLOGY_RETRY_S)

    raise RuntimeError(
        f"Could not fetch topology from {FLEET_URL} after {TOPOLOGY_RETRIES} attempts"
    )


def pick_start_nodes(nodes: dict, count: int) -> list[str]:
    """
    Distributes AGV start positions across charging and IN stations, preferring
    unique nodes so that vehicles do not block each other at startup.
    Falls back to any available node if none match those prefixes.
    """
    preferred = [nid for nid in nodes if nid.upper().startswith(("CHG", "IN-", "IN_"))]
    if not preferred:
        preferred = list(nodes.keys())
    all_node_ids = list(nodes.keys())

    result: list[str] = []
    used: set[str] = set()
    for _ in range(count):
        # Prefer unique preferred nodes, then unique any node, then cycle preferred
        candidates = (
            [n for n in preferred if n not in used]
            or [n for n in all_node_ids if n not in used]
            or preferred
        )
        node = candidates[0]
        used.add(node)
        result.append(node)
    return result


# ── Helpers ────────────────────────────────────────────────────────────────────

def _ts() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"

def _topic(serial: str, msg_type: str) -> str:
    return f"{INTERFACE_NAME}/{MAJOR_VERSION}/{MANUFACTURER}/{serial}/{msg_type}"

def _is_charging_node(node_id: str) -> bool:
    return node_id.upper().startswith("CHG")


# ── Node-occupation registry (shared across all simulators) ───────────────────

_node_locks: dict[str, threading.Lock] = {}
_node_locks_guard = threading.Lock()


def _get_node_lock(node_id: str) -> threading.Lock:
    """Returns (and lazily creates) the per-node mutex."""
    with _node_locks_guard:
        if node_id not in _node_locks:
            _node_locks[node_id] = threading.Lock()
        return _node_locks[node_id]


# ── AGV state ─────────────────────────────────────────────────────────────────

@dataclass
class AgvState:
    serial: str
    x: float
    y: float
    theta: float
    map_id: str = "UNKNOWN"
    driving: bool = False
    battery_charge: float = INITIAL_CHARGE
    charging: bool = False
    order_id: str = ""
    order_update_id: int = 0
    last_node_id: str = ""
    last_node_seq_id: int = 0
    node_states: list = field(default_factory=list)
    edge_states: list = field(default_factory=list)
    action_states: list = field(default_factory=list)
    errors: list = field(default_factory=list)
    _header_id: int = field(default=0, repr=False)
    vx: float = 0.0
    vy: float = 0.0

    def next_header(self) -> int:
        self._header_id += 1
        return self._header_id

    def to_state_msg(self) -> dict:
        return {
            "headerId":           self.next_header(),
            "timestamp":          _ts(),
            "version":            "2.0.0",
            "manufacturer":       MANUFACTURER,
            "serialNumber":       self.serial,
            "orderId":            self.order_id,
            "orderUpdateId":      self.order_update_id,
            "lastNodeId":         self.last_node_id,
            "lastNodeSequenceId": self.last_node_seq_id,
            "driving":            self.driving,
            "operatingMode":      "AUTOMATIC",
            "agvPosition": {
                "x":                   round(self.x, 3),
                "y":                   round(self.y, 3),
                "theta":               round(self.theta, 4),
                "mapId":               self.map_id,
                "positionInitialized": True,
                "localizationScore":   0.95,
            },
            "velocity": {
                "vx":    round(self.vx, 3),
                "vy":    round(self.vy, 3),
                "omega": 0.0,
            },
            "batteryState": {
                "batteryCharge": round(self.battery_charge, 1),
                "charging":      self.charging,
            },
            "actionStates": self.action_states,
            "nodeStates":   self.node_states,
            "edgeStates":   self.edge_states,
            "errors":       self.errors,
        }

    def to_connection_msg(self, connection_state: str = "ONLINE") -> dict:
        return {
            "headerId":        self.next_header(),
            "timestamp":       _ts(),
            "version":         "2.0.0",
            "manufacturer":    MANUFACTURER,
            "serialNumber":    self.serial,
            "connectionState": connection_state,
        }


# ── Per-vehicle simulator ──────────────────────────────────────────────────────

class AgvSimulator:
    def __init__(self, serial: str, start_node: str, nodes: dict):
        self._nodes = nodes
        node = nodes[start_node]
        self.state = AgvState(
            serial=serial,
            x=node["x"], y=node["y"], theta=node["theta"],
            map_id=node.get("mapId", "UNKNOWN"),
            last_node_id=start_node,
            charging=_is_charging_node(start_node),
        )
        self._lock = threading.Lock()
        self._order_event = threading.Event()
        self._pending_order: Optional[dict] = None
        self._instant_actions: list = []
        self._held_node: Optional[str] = None
        self._acquire_node(start_node)

        self._client = mqtt.Client(
            client_id=f"agv-sim-{serial}",
            protocol=mqtt.MQTTv311,
        )
        self._client.on_connect = self._on_connect
        self._client.on_message = self._on_message

    # ── MQTT callbacks ─────────────────────────────────────────────────────────

    def _on_connect(self, client, _userdata, _flags, rc):
        if rc != 0:
            print(f"[{self.state.serial}] MQTT connect failed rc={rc}")
            return

        print(f"[{self.state.serial}] Connected to {MQTT_HOST}:{MQTT_PORT}")
        client.subscribe(_topic(self.state.serial, "order"),          qos=1)
        client.subscribe(_topic(self.state.serial, "instantActions"), qos=1)

        with self._lock:
            self._publish("connection", self.state.to_connection_msg("ONLINE"), qos=1)
            self._publish("state",      self.state.to_state_msg())

    def _on_message(self, _client, _userdata, msg):
        try:
            payload = json.loads(msg.payload.decode())
        except Exception as e:
            print(f"[{self.state.serial}] Bad message on {msg.topic}: {e}")
            return

        if msg.topic.endswith("/order"):
            print(f"[{self.state.serial}] Order received: {payload.get('orderId')}")
            with self._lock:
                self._pending_order = payload
            self._order_event.set()

        elif msg.topic.endswith("/instantActions"):
            for action in payload.get("instantActions", []):
                print(f"[{self.state.serial}] InstantAction: {action.get('actionType')}")
                with self._lock:
                    self._instant_actions.append(action)

    # ── Publishing ─────────────────────────────────────────────────────────────

    def _publish(self, msg_type: str, payload: dict, qos: int = 0):
        self._client.publish(
            _topic(self.state.serial, msg_type),
            json.dumps(payload),
            qos=qos,
            retain=False,
        )

    def _publish_state(self):
        with self._lock:
            msg = self.state.to_state_msg()
        self._publish("state", msg)

    # ── Node-occupation helpers ────────────────────────────────────────────────

    def _acquire_node(self, node_id: str) -> None:
        """Block until this vehicle exclusively holds node_id."""
        lock = _get_node_lock(node_id)
        if not lock.acquire(blocking=False):
            print(f"[{self.state.serial}] Node {node_id} occupied — waiting …")
            lock.acquire()
        with self._lock:
            self._held_node = node_id

    def _release_node(self, node_id: str) -> None:
        """Give up exclusive hold on node_id."""
        with self._lock:
            if self._held_node != node_id:
                return
            self._held_node = None
        _get_node_lock(node_id).release()

    # ── Order simulation ───────────────────────────────────────────────────────

    def _simulate_order(self, order: dict):
        nodes  = order.get("nodes", [])
        edges  = order.get("edges", [])
        oid    = order.get("orderId", "")
        oupd   = order.get("orderUpdateId", 0)

        with self._lock:
            self.state.charging        = False
            self.state.order_id        = oid
            self.state.order_update_id = oupd
            self.state.node_states = [
                {"nodeId": n["nodeId"], "sequenceId": n["sequenceId"], "released": n.get("released", True)}
                for n in nodes
            ]
            self.state.edge_states = [
                {"edgeId": e["edgeId"], "sequenceId": e["sequenceId"], "released": e.get("released", True)}
                for e in edges
            ]
        self._publish_state()

        for i, node in enumerate(nodes):
            node_id  = node["nodeId"]
            # Node position comes from the order message; fall back to fetched topology
            node_pos = node.get("nodePosition") or self._nodes.get(node_id)
            if not node_pos:
                print(f"[{self.state.serial}] Unknown node position for {node_id}, skipping")
                continue

            target_x     = node_pos["x"]
            target_y     = node_pos["y"]
            target_theta = node_pos.get("theta", 0.0)

            with self._lock:
                cur_x, cur_y = self.state.x, self.state.y

            dx   = target_x - cur_x
            dy   = target_y - cur_y
            dist = math.sqrt(dx * dx + dy * dy)

            # Acquire exclusive access to the target node before moving there.
            # If another vehicle is already there, we block until it departs.
            prev_held = self._held_node
            if node_id != prev_held:
                self._acquire_node(node_id)

            # Update edge states when actually driving to a new node
            if dist > NODE_ARRIVAL_TOLERANCE and i > 0 and i - 1 < len(edges):
                gone_edge = edges[i - 1]["edgeId"]
                with self._lock:
                    self.state.edge_states = [
                        e for e in self.state.edge_states if e["edgeId"] != gone_edge
                    ]

            # Release the previous node as we depart from it
            if prev_held is not None and prev_held != node_id:
                self._release_node(prev_held)

            if dist > NODE_ARRIVAL_TOLERANCE:
                self._drive(cur_x, cur_y, target_x, target_y, target_theta, dist)

            with self._lock:
                self.state.x                = target_x
                self.state.y                = target_y
                self.state.theta            = target_theta
                self.state.driving          = False
                self.state.vx               = 0.0
                self.state.vy               = 0.0
                self.state.last_node_id     = node_id
                self.state.last_node_seq_id = node["sequenceId"]
                self.state.node_states = [
                    n for n in self.state.node_states if n["nodeId"] != node_id
                ]

            for action in node.get("actions", []):
                self._execute_action(action)

        with self._lock:
            self.state.node_states   = []
            self.state.edge_states   = []
            self.state.action_states = []
            self.state.driving       = False
        self._publish_state()
        print(f"[{self.state.serial}] Order {oid} complete")

    def _drive(self, x0: float, y0: float, x1: float, y1: float, target_theta: float, dist: float):
        dx    = x1 - x0
        dy    = y1 - y0
        speed = DRIVE_SPEED
        dt    = 1.0 / STATE_HZ
        steps = max(1, int(dist / (speed * dt)))
        drive_theta = math.atan2(dy, dx)
        vx = (dx / dist) * speed
        vy = (dy / dist) * speed

        with self._lock:
            self.state.driving = True
            self.state.theta   = drive_theta

        for step in range(1, steps + 1):
            t    = step / steps
            done = (step == steps)
            with self._lock:
                self.state.x       = x0 + dx * t
                self.state.y       = y0 + dy * t
                self.state.vx      = 0.0 if done else vx
                self.state.vy      = 0.0 if done else vy
                self.state.driving = not done
                self.state.theta   = target_theta if done else drive_theta
                msg = self.state.to_state_msg()
            self._publish("state", msg)
            if not done:
                time.sleep(dt)

    def _execute_action(self, action: dict):
        action_id   = action.get("actionId", "")
        action_type = action.get("actionType", "unknown")
        print(f"[{self.state.serial}] Action {action_type} ({action_id}) RUNNING")

        with self._lock:
            self.state.action_states = [
                {"actionId": action_id, "actionStatus": "RUNNING", "resultDescription": ""}
            ]
        self._publish_state()

        time.sleep(ACTION_DURATION)

        with self._lock:
            self.state.action_states = [
                {"actionId": action_id, "actionStatus": "FINISHED", "resultDescription": "OK"}
            ]
        self._publish_state()
        print(f"[{self.state.serial}] Action {action_type} ({action_id}) FINISHED")

        time.sleep(0.3)
        with self._lock:
            self.state.action_states = []

    # ── Main loop ──────────────────────────────────────────────────────────────

    def run(self):
        self._client.connect(MQTT_HOST, MQTT_PORT, keepalive=60)
        self._client.loop_start()

        time.sleep(2.0)  # wait for on_connect to fire

        while True:
            with self._lock:
                instant_actions = list(self._instant_actions)
                self._instant_actions.clear()

            for action in instant_actions:
                action_type = action.get("actionType", "")
                if action_type == "startCharging":
                    with self._lock:
                        self.state.charging = True
                elif action_type in ("stopPause", "startPause"):
                    pass  # extend here for pause logic if needed

            got_order = self._order_event.wait(timeout=IDLE_INTERVAL)

            if got_order:
                self._order_event.clear()
                with self._lock:
                    order = self._pending_order
                    self._pending_order = None
                if order:
                    self._simulate_order(order)
            else:
                with self._lock:
                    if not self.state.charging and _is_charging_node(self.state.last_node_id):
                        self.state.charging = True
                    if self.state.charging:
                        self.state.battery_charge = min(100.0, self.state.battery_charge + 1.0)
                        if self.state.battery_charge >= 100.0:
                            self.state.charging = False
                    else:
                        self.state.battery_charge = max(21.0, self.state.battery_charge - 0.1)
                self._publish_state()


# ── Entry point ────────────────────────────────────────────────────────────────

def main():
    print(f"VDA5050 AGV Simulator starting")
    print(f"  Fleet controller: {FLEET_URL}")
    print(f"  MQTT broker:      {MQTT_HOST}:{MQTT_PORT}")
    print(f"  Vehicles:         {', '.join(AGV_SERIALS)}")

    nodes, _edges = fetch_topology()
    start_nodes   = pick_start_nodes(nodes, len(AGV_SERIALS))

    threads = []
    for i, serial in enumerate(AGV_SERIALS):
        start_node = start_nodes[i]
        if start_node not in nodes:
            print(f"Start node {start_node!r} not in topology, skipping {serial}")
            continue
        sim = AgvSimulator(serial, start_node, nodes)
        t   = threading.Thread(target=sim.run, name=f"agv-{serial}", daemon=True)
        threads.append(t)
        t.start()
        time.sleep(0.5)  # stagger connections

    print(f"All {len(threads)} simulators running")
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        print("Simulator stopped")


if __name__ == "__main__":
    main()
