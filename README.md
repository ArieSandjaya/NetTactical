# NetTactical — Tactical RMM backend in ASP.NET Core (.NET 8, C#)

A port of the **core scope** of the Tactical RMM backend (Django) to a C# N-Tier architecture. The original Go agent and `nats-api` are **left unchanged**:
the REST paths `/api/v3/*`, JSON field names, NATS subjects, and the `agents_agent` table schema are all preserved.

## Structure
| Project | Contents | Django equivalent |
|---|---|---|
| `Domain` | Entities + constants | `*/models.py`, `constants.py` |
| `Application` | Interfaces, DTOs, pure logic (CheckEvaluator, PermissionEvaluator, etc.) | `checks/models.py::handle_check`, `permissions.py` |
| `Infrastructure` | EF Core + Npgsql, NATS.Net + MessagePack, services, background jobs | ORM, `nats_utils.py`, Celery |
| `Api` | Controllers, token authentication, rate limiting | `views.py`, `urls.py`, Knox/DRF |
| `tests` | xunit tests for the pure logic | `tests.py` |

## Covered so far
Login (+TOTP, throttle 10/minute & 300/day), Knox-style tokens, API key, RBAC + client/site scope, Client/Site/Agent,
agent registration + `nats-rmm.conf`, `apiv3` (checkin, config, checkrunner GET/PATCH, runchecks, checkinterval, taskrunner GET/PATCH, newagent),
Check, AutomatedTask, Alert (dashboard), Script, run script/cmd/ping/reboot via NATS, Audit log, agent outage job (replaces celery beat).

**All of the agent's `/api/v3` endpoints now exist** (apart from those listed below): `checkin`, `config`, `checkrunner`, `runchecks`, `checkinterval`,
`taskrunner`, `newagent`, `syncmesh`, `choco`, `software`, `winupdates` (PUT/PATCH/POST), `superseded`, `histresult`, `installer` (GET/POST),
`meshexe`, `meshreinstall`. The MeshCentral integration (AES-GCM token, device group listing via WebSocket, installer download) is configured through `Tactical:Mesh`.

## Dashboard CRUD (new)
- **Check** — `GET agents/{agentId}/checks`, `GET automation/policies/{id}/checks`, `GET/PUT/DELETE checks/{id}`, `POST checks` (per-type validation: disk, cpu, memory, ping, winsvc, script, eventlog; the ping host is checked against injection).
- **Scheduled task** — `GET agents/{agentId}/tasks`, `GET/PUT/DELETE tasks/{id}`, `POST tasks`, `POST tasks/{id}/run`. Changes are pushed to the agent's Task Scheduler (`schedtask`/`delschedtask`) by `TaskSyncJob` (every 2 minutes); a deletion is only final once the agent confirms it.
- **Accounts** — `accounts/users` (CRUD, password reset, TOTP reset; own 2FA via `accounts/totp/status|setup|enable|disable` and the **Security** page (`#/security`, QR code)) and `accounts/roles` (CRUD) + `accounts/permissions`. Escalation protection: a non-superuser cannot touch a superuser or grant permissions they do not have; the last active superuser cannot be deleted/deactivated; a password reset/deactivation revokes all tokens.
- **Organisation** — `PUT/DELETE clients/{id}`, `POST clients/sites`, `PUT/DELETE sites/{id}`, `PUT agents/{agentId}` (requires `can_edit_agent` + scope on both the source and destination site).
- **Policy** — `automation/policies` CRUD (deletion is rejected while the policy still has tasks, so that agents are cleaned up first).

## Asset dashboard (new)
- **Historical metrics** — every CPU/memory/disk check result is stored as a point in the `check_metric` table (minimum gap `Tactical:MetricMinIntervalSeconds`, default 240 s). `MetricRetentionJob` deletes data older than `Tactical:MetricRetentionDays` (default 90).
- **Health score 0-100** — `HealthScorer` (pure logic): deductions for offline, disk, CPU, memory, open alerts, pending updates, reboot required, long uptime, outdated agent. Weights and level thresholds can be changed via `Tactical:Health`. Every score carries a list of factors + stable codes for translation in the UI.
- **Endpoints** (`can_list_agents` permission, client/site scope applies):
  - `GET /fleet/summary?client_id=&site_id=&days=30&risk_limit=10` — totals, alerts, patches, score per client, at-risk devices, latest alerts, OS distribution, agent versions, daily trends.
  - `GET /fleet/assets?client_id=&site_id=` — every asset with score, level, online state, main issue, pending updates, open alerts, last seen.
  - `GET /fleet/patches?client_id=&site_id=` — outstanding Windows updates grouped by KB (failed first, then by severity) and a per-asset view.
  - `GET /alerts?resolved=false` (existing; now also returns `agent_id`, `client`, `site`) and `PUT /alerts/{id}/resolve`.
  - `GET /agents/{agentId}/overview` — device facts, score + factors, current metrics, disks, **issue list** (failed checks/tasks with stdout/stderr/exit code, offline, patches, outdated agent), checks, tasks, patches, stopped automatic services.
  - `GET /agents/{agentId}/metrics?metric=cpu|memory|disk&label=C:&days=30` — time series (downsampled to at most 1000 points).
- Not available yet: device age/hardware (the agent does not send it), predictions, and an error event history after an alert is resolved (only currently open issues are shown).

## Dashboard UI (new)
Served directly by the API from `src/TacticalRmm.Api/wwwroot` (open `/` in a browser once the API is running; no Node, no build, no CDN).
- **Login** (+2FA code if enabled), the token is stored in `sessionStorage`, and an expired session automatically returns to login.
- **Asset summary** (`#/`): health score, online agents, alerts, pending updates, CPU/memory/disk trends (7/30/90 days), score per client (click to filter), at-risk devices, latest alerts, patch status, operating systems, agent versions. Refreshes itself every 60 seconds.
- **Device detail** (`#/device/<agentId>`): score + the factors lowering it, current metrics, metric history, then the Issues (output/stderr/exit code), Check, Task, Patch, Disk & services tabs.
- Security: the entire DOM is built with `textContent` (no `innerHTML`) because device names and check output come from agents; CSP `default-src 'self'` with no inline scripts/styles; supports dark mode and phone widths.
- Tested with a mock API shaped like the DTOs + Playwright (login, filter, tabs, 401/404, dark mode, phone, XSS payload not executed). Not yet tested against a real backend.
- **Assets** (`#/assets`): every asset with score, status, main issue, updates and alerts. Search, health filter (at risk / attention / healthy / offline), client chips and sorting are applied in the browser.
- **Alerts** (`#/alerts`): open/resolved, severity filter, and a Resolve button (needs `can_manage_alerts`; a 403 is shown as a notice).
- **Patches** (`#/patches`): totals, outstanding updates by KB (filter failed/pending), and a per-asset view.
- **VNC passwords** (`#/vnc`) and the Connect/Test buttons on the asset page: see "VNC remote control".
- **Monitoring** section: **Checks** (`#/checks`) and **Tasks** (`#/tasks`) are managed per agent or per policy (pick it in the selector): list with status, add (all seven check types; manual/daily/weekly/run-once tasks running a command or a script), delete, enable/disable and run-now for tasks. Items inherited from a policy show "From policy" and cannot be deleted on the agent. **Policies** (`#/policies`: create, edit, delete, jump to a policy's checks/tasks), **Scripts** (`#/scripts`: add with body, shell, timeout, platforms; delete) and **Audit log** (`#/audit`: paged, filter by action, search the page). Editing an existing check or task is not in the UI yet (delete and re-add); monthly schedules and check-failure tasks are API-only.
- **Security** (`#/security`): turn your own 2FA on or off (QR code).
- **Manage** section: **Clients & Sites** (`#/clients`: add/rename/delete clients and sites, asset counts), **Users** (`#/users`: add, edit, role, superuser/active/dashboard-block flags, reset password, reset 2FA, delete) and **Roles** (`#/roles`: permission checkboxes and optional client/site limits). Each action needs the matching permission; a 403 or a validation error from the API is shown as a message.
- **Asset actions** on the asset page (`Actions…`): run a command (cmd/PowerShell/bash), run a script from the script list (waits for output) and reboot with confirmation. Output is always rendered as plain text.
- Not yet in the UI: checks, scheduled tasks, automation policies, scripts, audit log (the API for them already exists).

## Installing agents (Install agent page)

`#/installer` (**Install agent**) generates what you need to put the unchanged Tactical RMM Go agent on a Windows computer: pick client, site, type (workstation/server), 32/64-bit and how long the token stays valid (1 hour to 30 days). You get the download link (official `amidaware/rmmagent` release matching `Tactical:LatestAgentVer`), a PowerShell one-liner (downloads and installs) and a Command Prompt version, plus the token.

- The token belongs to the installer account and has a fixed expiry; unlike dashboard sessions it is never auto-extended.
- Anyone holding the token can enroll an agent in any client/site until it expires. Treat it like a password. The page says so and the response is sent with `Cache-Control: no-store`.
- Requires the new permission `can_install_agents` (superusers have it). Every generation is written to the audit log.
- The agent is installed **without** the MeshCentral agent by default (`--nomesh`, per the Tactical RMM docs; "Take Control" then does not work, use VNC instead). Tick *Install MeshCentral agent* on the page only if you run MeshCentral. Agents that already have the Mesh agent keep it until you remove the "Mesh Agent" service/program on that computer.
- The server URL defaults to the address you are browsing; override it in the form if agents reach the server under another name. Use https in production.
- Verification: command building and input validation are unit-tested; the page was checked against a mock API. The generated commands follow upstream's manual install sequence but have not been run on a real Windows machine or against a real server.

## Security notes
- **MessagePack**: upgraded `2.5.172` -> `2.5.303` (fixes the 2.x advisories CVE-2024-48924, CVE-2026-48510, CVE-2026-48512 and CVE-2026-92707; the 3.x line is fixed in 3.1.9). Replies from endpoints are decoded through `AgentReplyDecoder`: 32 MB size cap, `MessagePackSecurity.UntrustedData`, object-graph depth limit 32, and malformed input returns `"malformed"` instead of throwing. The stack uses plain NATS, so use a TLS-protected link where possible.
- `Directory.Build.props` turns on NuGet audit (direct and transitive packages); watch the restore warnings.
- The new decoder has only been compiled against stubs; `dotnet build` and a real agent reply still need to be run.

## Not covered (deliberately, "core first")
Full policy inheritance (client/site level), automatic winupdate scheduling & approval, chocolatey installation, file transfer, the 'collector' result to custom field (histresult), Mesh permission sync (`run_sync_task`), terminal & WebSocket (SignalR),
email/SMS/webhook notifications, AlertTemplate, custom fields, script snippets, reporting & SSO (`ee/`, separate sponsor licence), Mesh.

## VNC remote control (TightVNC already on the endpoints)
Instead of MeshAgent, the dashboard can open the TightVNC that is already installed on an asset (useful when the antivirus blocks MeshAgent). Nothing is installed by us.
- **Passwords with fallback** — `#/vnc` (permission `can_manage_vnc`): the lowest priority number is the default, the others are fallbacks. The server tries them in order only if the asset rejects the default, remembers which one worked per asset (in memory) and tries that first next time. At most `Tactical:Vnc:MaxAttempts` (default 4) attempts per connect, because TightVNC temporarily blocks a client after repeated failures. Passwords are max 8 characters (a VNC limit), stored AES-256-GCM encrypted (key derived from `Tactical:SecretKey`), and never returned by the API.
- **Connect / Test** — buttons on the asset page (permission `can_use_vnc`, asset scope applies). The API itself performs the RFB authentication handshake (no session is opened) to find a working password, then **Connect** downloads a `.vnc` file for TightVNC Viewer; **Test** only reports the result. Both are written to the audit log.
  - `POST /agents/{agentId}/vnc/test`, `POST /agents/{agentId}/vnc/file`
  - `GET/POST /vnc/credentials`, `PUT/DELETE /vnc/credentials/{id}`
- **Target address** — taken only from the asset's own record (local IPs from the agent's hardware inventory, `wmi_detail`), never from the request. Only private LAN addresses (10/8, 172.16/12, 192.168/16) are contacted unless `Tactical:Vnc:AllowPublicTargets` is set. Port: `Tactical:Vnc:Port` (5900).
- The API server must be able to reach port 5900 of the assets (same LAN, as here) and the viewer machine as well, since the viewer connects directly. VNC is not encrypted: restrict port 5900 with the Windows firewall to the admin/RMM subnets.
- Fix included: the Go `nats-api` writes a `wmi_detail` column that the schema was missing, so the agent hardware inventory was being lost. Existing databases created earlier need `ALTER TABLE agents_agent ADD COLUMN wmi_detail jsonb;`.
- Verified: DES/obfuscation against OpenSSL, the handshake against a fake VNC server (wrong password, protocol 3.3, refusal, unsupported, timeout). Not verified against a real TightVNC server or viewer.

## Agents need NATS (read this if agents stay offline)

Agents connect to a **NATS server** (port 4222) and the API sends commands through it. Running only the API (for example from Visual Studio) is not enough: the log shows `can not connect uris: nats://127.0.0.1:4222`, agents cannot connect, and every command to an agent fails.

- Easiest: install Docker Desktop and run the whole stack: `docker compose up -d --build` (see Docker Compose below). It starts PostgreSQL, NATS (with the original `nats-api`) and the API. Dashboard: `http://localhost:8080`.
- Check NATS from PowerShell: `Test-NetConnection 127.0.0.1 -Port 4222` must say `TcpTestSucceeded : True`.
- Endpoints must reach this computer on the API port and on 4222 (open both in Windows Firewall). Generate the installer with the server's LAN address (for example `http://192.168.1.10:8080`), not `localhost`.
- When NATS is down the API now answers a command within about 3 seconds with "NATS down" instead of waiting up to a minute (which made the dashboard look frozen).
- Running the API alone on Windows also prints `nats-server reload failed`; that is harmless and is now a single informational line.

## Windows (Docker Desktop)

Everything runs in Docker, so Windows works the same as a Linux server. Requirements: Windows 10/11 (64-bit) with Docker Desktop (WSL2 backend) installed and running.

```
git clone https://github.com/ArieSandjaya/NetTactical.git
cd NetTactical
scripts\windows\nettactical.cmd setup      # creates .env with generated secrets and prints the first admin login
scripts\windows\nettactical.cmd up         # builds and starts PostgreSQL + NATS + API
scripts\windows\nettactical.cmd firewall   # as Administrator: opens the API and NATS ports (Private/Domain networks only)
scripts\windows\nettactical.cmd status     # containers and port checks
```

Other commands: `logs [api|nats|db]`, `down`, `update` (git pull + rebuild), `backup` (database dump into `backups\`). `setup` picks this computer's LAN address for `PUBLIC_URL` (agents use it); change it in `.env` if endpoints reach it under another name or behind HTTPS, then run `up` again.

Notes
- Use the **Docker** route for agents: the Visual Studio profile starts only the API, so NATS is missing and agents stay offline (see "Agents need NATS").
- `.gitattributes` forces LF endings for files used inside the Linux containers; without it Git for Windows can turn `entrypoint.sh` into CRLF and the NATS container fails to start.
- These scripts were written without access to a Windows machine and have not been run; if one fails, send the message.
- A fully native route (API, PostgreSQL and NATS installed directly on Windows, no Docker) is not provided: the upstream `nats-api` is published only as a Linux binary.

## Docker Compose
`docker compose up -d --build` starts three containers: **db** (PostgreSQL 16), **nats** (nats-server + the unchanged upstream `nats-api`, downloaded at image build time from the Tactical RMM `v1.6.1` tag, not stored in this repo) and **api** (this project, serving the dashboard on `http://localhost:8080/`).
```bash
cp .env.example .env      # set POSTGRES_PASSWORD, TACTICAL_SECRET_KEY (openssl rand -hex 32) and ADMIN_PASSWORD
docker compose up -d --build
docker compose logs api | grep "Installer token"   # shown once on first start
```
- The API writes `nats-rmm.conf` to a volume shared with the nats container, which watches the file and reloads nats-server itself (a separate container cannot be signalled from the API).
- Agents reach NATS on port `4222` (`NATS_PORT`). The stack is plain HTTP/NATS: put a TLS reverse proxy in front for anything beyond local testing, and check what TLS settings the Go agent expects.
- `.dockerignore` excludes any local `NuGet.config`, `.env`, and build output.
- Not yet run: the compose file passes `docker compose config` and the entrypoint script was exercised with fake binaries, but no daemon was available here, so the images have never been built or started.

## Running
```bash
export Tactical__SecretKey="<>=32 random characters>"
export Tactical__BootstrapAdmin__Password="<>=12 characters>"
export ConnectionStrings__Default="Host=...;Database=tacticalrmm;Username=...;Password=..."
dotnet run --project src/TacticalRmm.Api
```
Dev: the schema is created via `EnsureCreated`. Production: `dotnet ef migrations add Initial -p src/TacticalRmm.Infrastructure -s src/TacticalRmm.Api`.
The installer token is shown once in the log on first run.

## Verification status (honest)
- `Domain` + `Application` were built and tested: **59 + 123 logic assertions pass, plus 12 tests for the health score, metric rules, and agent data parser** (check evaluation, permissions, Mesh AES-GCM token, Mesh URLs, installer version, superseded updates, reboot decisions; Check/Task validators, Task Scheduler payload, account rules). The new CRUD services & controllers were only compiled against stubs — they have not been run against Postgres.
- The whole solution **compiled successfully** against temporary stubs for EF Core / NATS / MessagePack (the ASP.NET parts were compiled with the real SDK). This only proves the code is self-consistent, **not** that the calls into those packages' APIs are correct.
- `dotnet build` has never been run with the real NuGet packages, and nothing has been tested against a real Go agent, NATS, MeshCentral, or Postgres.
- There is no `NuGet.config` in this zip; the build uses nuget.org as usual.

## Licence & origin
This project is a behaviour-based port of [Tactical RMM](https://github.com/amidaware/tacticalrmm) (Amidaware), which is licensed under the **Tactical RMM License v1.0** — it is *not* open source.
Use on your own internal/customer networks is permitted; turning it into a paid SaaS/commercial service requires written permission from Amidaware.
Amidaware's `ee/` folder (SSO, Reporting) requires a sponsor token and is **not** ported here. Make sure you comply with the original licence before making this repo public or distributing it.
