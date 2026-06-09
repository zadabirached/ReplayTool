# ReplayTool

A developer tool for reproducing captured fleet scenarios locally. Has no database — state lives on the filesystem.

## Prerequisites

- .NET 10 SDK
- RabbitMQ (for replay publishing, added in later tasks)
- JobService running with its DB migrated (for type-1 inserts, added in later tasks)

## Run

```bash
cd ReplayTool.API
dotnet run
```

Service starts on `http://localhost:5000` by default.

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `STORAGE_ROOT` | `./cases` | Root folder where case directories are stored |

Override via environment variable or `appsettings.json`.

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/health` | Liveness check |
| POST | `/cases` | Create a new case |
