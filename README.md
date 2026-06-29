# ReplayTool

A developer tool for reproducing captured fleet scenarios locally: it inserts a previously
captured set of customer orders into JobService's database, then republishes the captured
routing/assignment events onto the same broker topics JobService consumes — either with the
original timing gaps or sped up.

Has no database of its own — case data lives on the filesystem (or a mounted volume in Docker).

## Prerequisites

- .NET 10 SDK (for running outside Docker)
- Docker + Docker Compose (recommended — brings up Postgres, RabbitMQ, JobService and ReplayTool together)

## Running via Docker Compose (recommended)

The compose stack lives in `JobService/docker-compose.yml` and starts Postgres, RabbitMQ,
JobService, ReplayTool, Prometheus and Grafana on a shared `jobservice-net` network.

```bash
cd JobService
docker compose up --build
```

- JobService API: `http://localhost:8080`
- ReplayTool API: `http://localhost:8081`
- RabbitMQ management UI: `http://localhost:15672` (guest/guest)
- Grafana: `http://localhost:3000`

ReplayTool talks to the `postgres` and `rabbitmq` services by their compose hostnames, not
`localhost`. Because of that, the `replaytool` service sets `REPLAY_ALLOW_REMOTE_DB=true` — see
**Safety guard** below for why that's needed and why it's safe in this context.

## Running standalone (without Docker)

```bash
cd ReplayTool.API
dotnet run
```

Service starts on `http://localhost:5000` by default. Requires JobService and RabbitMQ already
running locally (e.g. via `cd JobService && docker compose up postgres rabbitmq jobservice`).

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `STORAGE_ROOT` | `./cases` | Root folder where case directories are stored |
| `JOBSERVICE_DB` | `Host=localhost;Database=jobservice;Username=postgres;Password=postgres` | Connection string used for the Seed phase inserts |
| `RabbitMQ:Host` / `RabbitMQ__Host` | `localhost` | Broker host used to publish replayed events |
| `RabbitMQ:Port` / `RabbitMQ__Port` | `5672` | Broker port |
| `RabbitMQ:Username` / `RabbitMQ__Username` | `guest` | Broker username |
| `RabbitMQ:Password` / `RabbitMQ__Password` | `guest` | Broker password |
| `REPLAY_ALLOW_REMOTE_DB` | `false` | Overrides the safety guard below for both the DB and the broker target |

Override via environment variable (`__` separates nested keys, e.g. `RabbitMQ__Host`) or
`appsettings.json`.

## Safety guard

ReplayTool writes directly into JobService's database and publishes onto the real broker
exchanges JobService consumes. A captured scenario could have come from a production capture, so
both the DB and broker targets **default to local-only** (`localhost` / `127.0.0.1` / `::1`) and
the tool refuses to start (DB) or run a seed/replay (broker) against any other host unless
`REPLAY_ALLOW_REMOTE_DB=true` is explicitly set.

Docker Compose service hostnames (`postgres`, `rabbitmq`) are not literally `localhost`, so the
compose file sets the override explicitly — that's a deliberate declaration that the compose
stack is a known-safe local/dev environment, not a real captured-prod target.

## The capture → replay loop

### 1. Capture a scenario

Capture is a manual step: pull the three event types below from wherever they were originally
produced (e.g. JobService logs, broker traffic, or a test fixture) into three JSON files, one
array of events per type.

| Type key | Filename | Source |
|----------|----------|--------|
| `JobService-CustomerOrder.Topic` | `JobService-CustomerOrder.Topic.json` | Customer order events to seed into the DB |
| `routingResponses-v2` | `routingResponses-v2.json` | Routing response events (`OrdersRoutingEventV2`) |
| `anytask-solution-v2` | `anytask-solution-v2.json` | Assignment solution events (`AssignmentSolutionV2Event`) |

### 2. Create a case

```bash
curl -X POST http://localhost:8081/cases -H "Content-Type: application/json" \
  -d '{"name": "My Scenario", "description": "optional"}'
```

Returns the case `id` used in every subsequent call.

### 3. Upload the typed files

```bash
curl -X PUT "http://localhost:8081/cases/{id}/files/JobService-CustomerOrder.Topic" \
  -H "Content-Type: application/json" --data-binary @customer-orders.json

curl -X PUT "http://localhost:8081/cases/{id}/files/routingResponses-v2" \
  -H "Content-Type: application/json" --data-binary @routing.json

curl -X PUT "http://localhost:8081/cases/{id}/files/anytask-solution-v2" \
  -H "Content-Type: application/json" --data-binary @assignments.json
```

`GET /cases/{id}/files` lists what's uploaded; `DELETE /cases/{id}/files/{type}` removes one.

### 4. (Optional) Inspect the plan before running

```bash
curl http://localhost:8081/cases/{id}/parse      # parsed events + per-event errors
curl http://localhost:8081/cases/{id}/timeline    # merged Seed+Replay timeline
curl -X POST http://localhost:8081/cases/{id}/runs -d '{"dryRun": true}'
```

A dry run returns a `Completed` run with the full step plan and no side effects — nothing is
inserted or published.

### 5. Trigger a real run

Exact original gaps (`speedFactor: 1.0`, the default):

```bash
curl -X POST http://localhost:8081/cases/{id}/runs -H "Content-Type: application/json" \
  -d '{"dryRun": false, "speedFactor": 1.0, "mode": "Normal"}'
```

Sped up 10x:

```bash
curl -X POST http://localhost:8081/cases/{id}/runs -H "Content-Type: application/json" \
  -d '{"dryRun": false, "speedFactor": 10.0, "mode": "Normal"}'
```

Fast mode (`mode: "Fast"`) replays every step back-to-back, ignoring the captured gaps entirely.

This returns `202 Accepted` immediately with a `Pending` run; the run executes on a background
worker (Seed phase: insert customer orders, dedup against the file and the DB; Replay phase:
publish routing/assignment events in timestamp order with the requested timing).

### 6. Check status

```bash
curl http://localhost:8081/cases/{id}/runs/{runId}
curl http://localhost:8081/cases/{id}/runs            # all runs for the case
```

Poll until `status` is `Completed` or `Failed`. Each step records its phase, event id, result
(`Inserted`/`Skipped`/`Published`/`Failed`), and (for Replay steps) its scheduled vs. actual
offset from the run's start.

### 7. Retry failed steps

If a run finishes `Failed`, only the failed steps need re-execution — succeeded/skipped steps are
left untouched and not re-published:

```bash
curl -X POST http://localhost:8081/cases/{id}/runs/{runId}/retry
```

Returns `409 Conflict` if the run is still in progress or has no failed steps.

## Endpoints

| Method | Path | Description |
|--------|------|--------------|
| GET | `/health` | Liveness check |
| POST | `/cases` | Create a new case |
| GET | `/cases` | List cases |
| GET | `/cases/{id}` | Get a case |
| PUT | `/cases/{id}/files/{type}` | Upload/replace a typed file |
| GET | `/cases/{id}/files` | List uploaded file types |
| DELETE | `/cases/{id}/files/{type}` | Delete a typed file |
| POST | `/cases/{id}/seed` | Insert customer orders only (no replay) |
| GET | `/cases/{id}/parse` | Parsed events + per-event errors |
| GET | `/cases/{id}/timeline` | Merged Seed+Replay timeline |
| POST | `/cases/{id}/runs` | Trigger a run (or dry-run plan) |
| GET | `/cases/{id}/runs` | List runs for a case |
| GET | `/cases/{id}/runs/{runId}` | Get a run's status/steps |
| POST | `/cases/{id}/runs/{runId}/retry` | Retry a failed run's failed steps |
