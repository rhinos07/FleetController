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
  SEQ_URL              Seq ingestion URL               (default: unset = console only)
"""

import json
import logging
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

# ── Logging setup ─────────────────────────────────────────────────────────────

class _AppLabel(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        record.Application = "agv-simulator"
        return True


def _setup_logging() -> None:
    seq_url = os.getenv("SEQ_URL")
    if seq_url:
        import seqlog
        seqlog.log_to_seq(
            server_url=seq_url,
            level=logging.DEBUG,
            batch_size=10,
            auto_flush_timeout=2,
            override_root_logger=True,
        )
    else:
        logging.basicConfig(
            level=logging.INFO,
            format="[%(asctime)s %(levelname)s] %(name)s: %(message)s",
            datefmt="%H:%M:%S",
        )
    logging.getLogger().addFilter(_AppLabel())

_setup_logging()
log = logging.getLogger("agv_simulator")

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
            log.info("Topology loaded: %d nodes, %d edges", len(nodes), len(raw_edges))
            return nodes, raw_edges

        except Exception as e:
            log.warning("Topology fetch attempt %d/%d failed: %s", attempt, TOPOLOGY_RETRIES, e)
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
        self._log = logging.getLogger(f"agv.{serial}")
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
            self._log.error("MQTT connect failed rc=%d", rc)
            return

        self._log.info("Connected to %s:%d", MQTT_HOST, MQTT_PORT)
        client.subscribe(_topic(self.state.serial, "order"),          qos=1)
        client.subscribe(_topic(self.state.serial, "instantActions"), qos=1)

        with self._lock:
            self._publish("connection", self.state.to_connection_msg("ONLINE"), qos=1)
            self._publish("state",      self.state.to_state_msg())

    def _on_message(self, _client, _userdata, msg):
        try:
            payload = json.loads(msg.payload.decode())
        except Exception as e:
            self._log.warning("Bad message on %s: %s", msg.topic, e)
            return

        if msg.topic.endswith("/order"):
            oid   = payload.get("orderId", "")
            nodes = payload.get("nodes", [])
            src   = nodes[0]["nodeId"]  if nodes else "?"
            dst   = nodes[-1]["nodeId"] if nodes else "?"
            kind  = "dodge" if oid.startswith("DODGE-") else "transport"
            self._log.info("Order received [%s] %s: %s → %s", kind, oid, src, dst)
            with self._lock:
                self._pending_order = payload
            self._order_event.set()

        elif msg.topic.endswith("/instantActions"):
            for action in payload.get("instantActions", []):
                self._log.info("InstantAction: %s", action.get("actionType"))
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
            self._log.debug("Node %s occupied — waiting", node_id)
            lock.acquire()
        with self._lock:
            self._held_node = node_id

    def _release_node(self, node_id: str) -> None:
        """Give up exclusive hold on node_id."""
        _get_node_lock(node_id).release()
        with self._lock:
            if self._held_node == node_id:
                self._held_node = None

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
                self._log.error(
                    "Order %s rejected: node %s has no known position — cannot execute",
                    oid, node_id,
                )
                return

            target_x     = node_pos["x"]
            target_y     = node_pos["y"]
            target_theta = node_pos.get("theta", 0.0)

            with self._lock:
                cur_x, cur_y = self.state.x, self.state.y

            dx   = target_x - cur_x
            dy   = target_y - cur_y
            dist = math.sqrt(dx * dx + dy * dy)

            # Acquire exclusive access to the target node before moving there.
            # If another vehicle is already there, publish a stopped state so the fleet
            # controller can send a dodge order, then block until the node is free.
            with self._lock:
                prev_held = self._held_node
            if node_id != prev_held:
                node_lock = _get_node_lock(node_id)
                if not node_lock.acquire(blocking=False):
                    # Node is occupied — inform fleet controller we are stopped and waiting,
                    # then block until the node is free.  After node_lock.acquire() returns
                    # we hold the lock in both branches, so _held_node is always set while
                    # the lock is owned.
                    with self._lock:
                        self.state.driving = False
                        self.state.vx = 0.0
                        self.state.vy = 0.0
                    self._publish_state()
                    self._log.debug("Node %s occupied — waiting", node_id)
                    node_lock.acquire()
                # Lock is now held (either acquired non-blocking above, or after waiting).
                with self._lock:
                    self._held_node = node_id

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
        kind = "dodge" if oid.startswith("DODGE-") else "transport"
        src  = nodes[0]["nodeId"]  if nodes else "?"
        dst  = nodes[-1]["nodeId"] if nodes else "?"
        self._log.info("Order completed [%s] %s: %s → %s", kind, oid, src, dst)

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
        self._log.info("Action %s (%s) RUNNING", action_type, action_id)

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
        self._log.info("Action %s (%s) FINISHED", action_type, action_id)

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
                    try:
                        self._simulate_order(order)
                    except Exception as exc:
                        oid  = order.get("orderId", "?")
                        kind = "dodge" if oid.startswith("DODGE-") else "transport"
                        self._log.error(
                            "Order failed [%s] %s: %s", kind, oid, exc, exc_info=True,
                        )
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
    log.info("VDA5050 AGV Simulator starting — fleet=%s broker=%s:%d vehicles=%s",
             FLEET_URL, MQTT_HOST, MQTT_PORT, ", ".join(AGV_SERIALS))

    nodes, _edges = fetch_topology()
    start_nodes   = pick_start_nodes(nodes, len(AGV_SERIALS))

    threads = []
    for i, serial in enumerate(AGV_SERIALS):
        start_node = start_nodes[i]
        if start_node not in nodes:
            log.warning("Start node %r not in topology, skipping %s", start_node, serial)
            continue
        sim = AgvSimulator(serial, start_node, nodes)
        t   = threading.Thread(target=sim.run, name=f"agv-{serial}", daemon=True)
        threads.append(t)
        t.start()
        time.sleep(0.5)  # stagger connections

    log.info("All %d simulators running", len(threads))
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        log.info("Simulator stopped")


if __name__ == "__main__":
    main()
