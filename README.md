# ServerMonitor

Self-hosted monitoring for a small fleet of machines. A lightweight agent runs on every server
you want to watch and pushes CPU, memory, disk and uptime readings to a central API; a Blazor
dashboard shows the fleet, and a Telegram bot reports when a threshold is crossed.

Built as a learning project, then taken far enough to actually run on my own server.

![Server dashboard](docs/images/dashboard.png)

---

## What it does

- **Watches many machines.** Each one runs `ServerMonitor.Agent`, registers itself once with a
  shared enrollment token, and afterwards authenticates with its own API key.
- **Survives network outages.** The agent buffers readings in memory while the server is
  unreachable and sends the backlog once it returns, retrying with an exponential backoff
  capped at five minutes.
- **Shows the fleet at a glance.** Machines that have gone quiet are dimmed and marked, so a
  frozen reading is never mistaken for a live one.
- **Alerts on thresholds.** CPU, memory and disk have configurable limits; alerting fires once
  on the way up and once on the way back down, and needs several consecutive breaches so a
  single spike stays quiet.
- **Notices when a machine goes quiet.** A configurable silence threshold turns into an alert
  when an agent stops reporting, and another when it comes back. Rule checking is independent
  of delivery, so events are recorded even with no Telegram configured.
- **Runs on Linux and Windows.** Readings come from `/proc` on Linux and from Win32 API calls
  through P/Invoke on Windows.

### Fleet

![Fleet view](docs/images/fleet.png)

### Alerts

![Alert log](docs/images/alerts.png)

---

## Architecture

```mermaid
flowchart LR
    subgraph "Machine A"
        HWA["/proc · WinAPI"] --> AGA["ServerMonitor.Agent"]
    end
    subgraph "Machine B"
        HWB["/proc · WinAPI"] --> AGB["ServerMonitor.Agent"]
    end

    AGA -- "POST /api/ingest" --> API["ServerMonitor.Api"]
    AGB -- "POST /api/ingest" --> API
    API --> DB[("PostgreSQL")]
    DB --> WEB["ServerMonitor.Web<br/>Blazor Server"]
    DB --> TG["Telegram bot"]
```

Collection is deliberately a separate assembly from persistence: the agent references
`ServerMonitor.Collection` only, so it ships without EF Core, the Postgres driver or the
Telegram client.

| Project | Role |
|---------|------|
| `ServerMonitor.Domain` | Entities and domain rules. No dependencies at all. |
| `ServerMonitor.Collection` | Reading metrics off a machine. |
| `ServerMonitor.Agent` | The program installed on each watched machine. |
| `ServerMonitor.Infrastructure` | EF Core, migrations, API keys, Telegram. |
| `ServerMonitor.Api` | HTTP API: ingest, registration, reads. |
| `ServerMonitor.Web` | Blazor Server UI. Talks to the API over HTTP only. |
| `ServerMonitor.Tests` | xUnit tests. |

**Stack:** .NET 10 · ASP.NET Core · Blazor Server · EF Core 10 · PostgreSQL 17 · xUnit ·
Telegram.Bot

---

## Running it

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Docker (for PostgreSQL)

### 1. Database

```bash
cp .env.example .env
```

Put a password in `.env`, then:

```bash
docker compose up -d
```

### 2. Secrets

The API needs a connection string and an enrollment token. Neither belongs in a tracked file,
so both go into [User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=servermonitor;Username=monitor;Password=<your password>" --project ServerMonitor.Api
```

Generate an enrollment token and give it to the API:

```bash
openssl rand -hex 32 | tr -d '\r\n'
```

```bash
dotnet user-secrets set "Agents:EnrollmentToken" "<the token>" --project ServerMonitor.Api
```

Telegram is optional. Without it the bot stays disabled:

```bash
dotnet user-secrets set "Telegram:BotToken" "<token from @BotFather>" --project ServerMonitor.Api
dotnet user-secrets set "Telegram:ChatId" "<your chat id>" --project ServerMonitor.Api
```

### 3. Schema

```bash
dotnet ef database update --project ServerMonitor.Infrastructure --startup-project ServerMonitor.Api
```

### 4. Start

Three processes, in this order:

```bash
dotnet run --project ServerMonitor.Api
```

```bash
dotnet run --project ServerMonitor.Web
```

```bash
dotnet run --project ServerMonitor.Agent
```

The UI is at `http://localhost:5298`, the API at `https://localhost:7212`, and its OpenAPI
reference at `https://localhost:7212/scalar/v1`.

---

## Adding a machine

Publish the agent and copy it to the machine you want to watch:

```bash
dotnet publish ServerMonitor.Agent -c Release -r linux-x64 --self-contained false -o ./agent-publish
```

Point it at your API and give it the same enrollment token — environment variables work
anywhere, which matters for systemd units and containers:

```bash
Agent__ServerUrl=https://monitor.example.com Agent__EnrollmentToken=<the token> ./ServerMonitor.Agent
```

On first run the agent exchanges the enrollment token for its own API key and writes it to
`agent-state.json` next to the binary (mode `0600` on Unix). That file is what keeps a restart
from registering the machine twice, so keep it.

Other settings, all optional:

| Setting | Default | Meaning |
|---------|---------|---------|
| `Agent__CollectIntervalSeconds` | `5` | How often to take a reading |
| `Agent__BufferCapacity` | `720` | Readings kept while the server is unreachable (~1 hour) |
| `Agent__MaxRetryDelaySeconds` | `300` | Ceiling for the retry backoff |

---

## Tests

```bash
dotnet test
```

Two collector tests read real `/proc` and take a second of wall time, so they are opt-in:

```bash
SERVERMONITOR_RUN_COLLECTOR_TESTS=1 dotnet test
```

---

## Documentation

`docs/guide/` is a chapter-by-chapter walkthrough of this codebase — what every layer does and
why it was built that way, down to language details. It is written in Russian, as a study
companion rather than product documentation, and writing it is what surfaced most of the bugs
that have since been fixed.

`docs/superpowers/` holds the design specs and implementation plans each stage was built from.

---

## Status and limits

It runs, it keeps history, and it has been watching a real machine for weeks. It is not
production software yet, and the gaps are deliberate rather than unknown:

- **No authentication on read endpoints.** Anyone who can reach the API can read metrics.
  Ingest and registration are the only protected routes.
- **The enrollment token never expires** and agent keys cannot be rotated — removing a machine
  is the only way to revoke access.
- **Data is kept forever.** A reading every five seconds adds up; there is no retention policy.
- **The agent's buffer is in memory**, so a restart during an outage loses what it held.
- **Nothing watches the monitor itself.** If the central API dies, no alert goes out — a
  system cannot report its own death. That needs an external check against `/healthz`, which
  is part of the packaging stage.
- **One offline threshold for the whole fleet**, and alerts have no severity levels.

Roadmap, in order: authentication → packaging (Docker images, install script, `/healthz`) →
retention.

---

## A note on the name

The repository started out as `SeverMonitor` — a typo, missing an `r`. It has been corrected
in stages, because renaming a project folder touches paths in every `.csproj` that references
it as well as the solution file, and that deserved its own commit rather than riding along
with a feature.

A detail worth keeping from it: a project's file name and its folder name are not required to
match, so the build ran happily with the mismatch for a month. A compiler will never point out
this kind of mistake.
