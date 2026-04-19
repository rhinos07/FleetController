# FleetManager – Docker Setup

## Verzeichnisstruktur

```
FleetController/                 ← Repo-Root
├── Vda5050FleetController/
├── FleetManager.Tests/
├── Vda5050FleetController.sln
├── agv-simulator/               ← VDA5050 AGV-Simulator (Demo)
│   ├── simulator.py
│   ├── Dockerfile
│   └── requirements.txt
└── docker/                      ← dieser Ordner
    ├── docker-compose.yml       ← Produktions-Stack
    ├── docker-compose.demo.yml  ← Demo-Overlay (+ AGV-Simulator)
    ├── Dockerfile
    ├── .env.example
    └── mosquitto/
        └── config/
            └── mosquitto.conf
```

## Schnellstart (Produktion)

```bash
# 1. .env anlegen (einmalig)
cp docker/.env.example docker/.env

# 2. Stack starten
docker compose -f docker/docker-compose.yml up --build
```

## Demo-Modus (simulierte AGVs)

Im Demo-Modus wird ein zusätzlicher Container gestartet, der drei virtuelle
AGVs simuliert. Die Fahrzeuge melden sich per VDA5050 am MQTT-Broker an,
fahren Transportaufträge ab und senden kontinuierlich Positions- und
Statusmeldungen — ohne dass echte Hardware benötigt wird.

```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up --build
```

### Demo-Ablauf

1. Stack startet (PostgreSQL, Mosquitto, FleetController, AGV-Simulator)
2. Die Simulatoren registrieren sich automatisch beim Fleet Controller
3. Live-Dashboard öffnen: **http://localhost:8080**
4. Transportauftrag absenden (siehe curl-Beispiel unten)
5. Im Dashboard den Auftrag verfolgen — Fahrzeugposition, Batterie, Status

### AGV-Simulator konfigurieren

Alle Parameter sind als Umgebungsvariablen in `docker-compose.demo.yml`
gesetzt und können über eine `.env`-Datei oder direkt überschrieben werden:

| Variable | Standard | Beschreibung |
|---|---|---|
| `AGV_SERIALS` | `agv-01,agv-02,agv-03` | Komma-getrennte Seriennummern |
| `AGV_MANUFACTURER` | `acme` | Herstellername (MQTT-Topic-Segment) |
| `DRIVE_SPEED` | `2.0` | Fahrgeschwindigkeit in Topologie-Einheiten/s |
| `ACTION_DURATION` | `3.0` | Dauer eines Pick/Drop-Vorgangs in Sekunden |
| `STATE_HZ` | `1.0` | State-Updates pro Sekunde während der Fahrt |
| `IDLE_INTERVAL` | `5.0` | Sekunden zwischen Heartbeats im Idle-Zustand |
| `INITIAL_CHARGE` | `80.0` | Startladung der Batterie in % |

Beispiel: 5 schnellere Fahrzeuge starten

```bash
AGV_SERIALS=agv-01,agv-02,agv-03,agv-04,agv-05 DRIVE_SPEED=5.0 \
  docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml up --build
```

### MQTT-Topics der simulierten Fahrzeuge

```
uagv/v2/acme/{serial}/state       ← Fahrzeugstatus (Position, Batterie, …)
uagv/v2/acme/{serial}/connection  ← Online/Offline-Meldung
uagv/v2/acme/{serial}/order       → empfängt Fahraufträge vom Controller
uagv/v2/acme/{serial}/instantActions → empfängt Sofortbefehle (Pause, Laden)
```

### Mit MQTT Explorer (MQTT-Nachrichten beobachten)

```bash
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml \
  --profile tools up --build
```

Öffne danach **http://localhost:4000** und verbinde dich mit Host `mosquitto`, Port `1883`.

## Erreichbare Dienste

| Dienst | URL |
|---|---|
| FleetController Live-Dashboard | http://localhost:8080 |
| Swagger UI | http://localhost:8080/swagger |
| MQTT Broker | localhost:1883 |
| MQTT WebSocket | localhost:9001 |
| MQTT Explorer (optional) | http://localhost:4000 |

## Nützliche Befehle

```bash
# Logs aller Dienste
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml logs -f

# Nur Simulator-Logs
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml logs -f agv-simulator

# Container stoppen (Daten bleiben erhalten)
docker compose -f docker/docker-compose.yml down

# Alles zurücksetzen inkl. Volumes
docker compose -f docker/docker-compose.yml down -v

# Neu bauen ohne Cache
docker compose -f docker/docker-compose.yml -f docker/docker-compose.demo.yml build --no-cache
```

## Transportauftrag per curl

```bash
# Fleet-Status abfragen
curl http://localhost:8080/fleet/status

# Transportauftrag erstellen (Stationen aus der Demo-Topologie)
curl -X POST http://localhost:8080/fleet/orders \
  -H "Content-Type: application/json" \
  -d '{"sourceStationId":"IN-A","destStationId":"OUT-B","loadId":"PAL-42"}'
```

Demo-Topologie (`SEED_DEMO_TOPOLOGY=true`):

| Node  | Typ      | Position      |
|-------|----------|---------------|
| IN-A  | Eingang  | x=5,  y=8     |
| IN-B  | Eingang  | x=5,  y=16    |
| IN-C  | Eingang  | x=5,  y=24    |
| OUT-A | Ausgang  | x=54, y=8     |
| OUT-B | Ausgang  | x=54, y=16    |
| OUT-C | Ausgang  | x=54, y=24    |
| CHG-1 | Laden    | x=3,  y=3     |
| CHG-2 | Laden    | x=56, y=3     |
