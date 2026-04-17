# FleetManager – Docker Setup

## Verzeichnisstruktur

Lege diesen `docker/`-Ordner direkt im Repository-Root ab:

```
FleetManager/                    ← Repo-Root
├── Vda5050FleetController/
├── FleetManager.Tests/
├── Vda5050FleetController.sln
└── docker/                      ← dieser Ordner
    ├── docker-compose.yml
    ├── Dockerfile
    ├── .env.example
    └── mosquitto/
        └── config/
            └── mosquitto.conf
```

## Schnellstart

```bash
# 1. In den docker-Ordner wechseln
cd docker

# 2. .env anlegen (einmalig)
cp .env.example .env

# 3. Alles starten
docker compose up --build
```

### Mit MQTT Explorer (Web-UI zum Debuggen)

```bash
docker compose --profile tools up --build
```

Öffne danach http://localhost:4000 und verbinde dich mit Host `mosquitto`, Port `1883`.

## Erreichbare Dienste

| Dienst | URL |
|---|---|
| FleetManager API | http://localhost:8080 |
| Swagger UI | http://localhost:8080/swagger |
| Live Dashboard (SignalR) | http://localhost:8080 |
| MQTT Broker | localhost:1883 |
| MQTT WebSocket | localhost:9001 |
| MQTT Explorer (optional) | http://localhost:4000 |

## Nützliche Befehle

```bash
# Nur Logs des FleetControllers anzeigen
docker compose logs -f fleet-controller

# Container stoppen (Daten bleiben erhalten)
docker compose down

# Alles zurücksetzen inkl. Volumes
docker compose down -v

# Nur neu bauen ohne Cache
docker compose build --no-cache
```

## Testaufruf per curl

```bash
# Fleet-Status abfragen
curl http://localhost:8080/fleet/status

# Transportauftrag erstellen
curl -X POST http://localhost:8080/fleet/orders \
  -H "Content-Type: application/json" \
  -d '{"sourceStationId":"ST01","destStationId":"ST05","loadId":"PAL-42"}'
```
