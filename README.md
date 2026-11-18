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
`meshexe`, `meshreinstall`. `meshexe` and `meshreinstall` only answer 400 "MeshCentral is not used"; `syncmesh` just stores the node id.

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
- Agents are always installed **without** the MeshCentral agent (`--nomesh`, per the Tactical RMM docs; "Take Control" does not exist here, use VNC instead). MeshCentral is not used by this project at all. An agent that already has the Mesh agent keeps it until you remove the "Mesh Agent" program on that computer.
- The server URL defaults to the address you are browsing; override it in the form if agents reach the server under another name. Use https in production.
- Verification: command building and input validation are unit-tested; the page was checked against a mock API. The generated commands follow upstream's manual install sequence but have not been run on a real Windows machine or against a real server.

## Notifications (email / webhook)
`#/notifications` (permission `can_manage_notifications`, superusers have it) manages channels. When an alert opens or resolves (check, task or offline agent whose email-alert flag is on), every enabled matching channel gets a message.
- **Email**: SMTP with STARTTLS (implicit TLS/465 is not supported). **Webhook**: JSON or Slack format; JSON bodies carry an HMAC-SHA256 `X-NetTactical-Signature`.
- Secrets (SMTP password, webhook secret) are stored AES-GCM encrypted and never returned by the API. Webhook URLs to loopback/link-local/private-metadata addresses are refused, redirects are not followed.
- Each channel has a Test button. Sending is queued in the background and never blocks agent check-in.
- Verified with unit tests and a mock API; not yet against a real SMTP server or webhook receiver.

## HTTPS without a reverse proxy
The API container can serve HTTPS itself (Kestrel). Put `fullchain.pem` and `privkey.pem` in `./certs` (`chmod 644 certs/privkey.pem`, the container user must read it), set `PUBLIC_URL=https://your.host.name` in `.env`, and start with the extra file:
`docker compose -f docker-compose.yml -f docker-compose.https.yml up -d --build`
The dashboard and `/api/v3` are then on `https://your.host.name` (host port `HTTPS_PORT`, default 443) and the plain HTTP port is no longer published. Use that address as Server URL for installing agents. The certificate name must match the host name and agents must trust the CA. Renew: replace the files and `docker compose restart api`. NATS has its own TLS (section above).

## Push install (domain administrator)
The **Install agent** page (`#/installer`) has a second card, *Push install with a domain administrator*: the server installs the agent on up to 20 computers at once over PowerShell remoting (WinRM), using the client/site/options chosen at the top. Needs the permission `can_push_install` (superusers have it); every push is audited (who, which computers, which account; never the password).
- **Credential**: `DOMAIN\user` or `user@domain.tld`, typed for each push. It is sent to the server over your HTTPS session, handed to PowerShell on standard input, and never stored, logged or returned. Use a dedicated deployment account and always put the server behind HTTPS.
- **Safety**: if any computer rejects the credential the rest of the job is skipped, so a wrong password cannot lock the domain account. Targets must be hostnames or private IPv4 addresses (loopback, link-local and multicast are refused). Each computer gets its own 1-hour installer token.
- **Force reinstall** runs the existing uninstaller first, then installs again. Without it an already installed computer is reported as *Already installed*.
- **Requirements on the target**: WinRM enabled (`winrm quickconfig` or the GPO *Allow remote server management through WinRM*), firewall open on 5985 (or 5986 with `Tactical:Push:UseSsl`), the account is a local administrator (Domain Admins normally is), and the computer can download the agent from GitHub.
- **Requirements on the server**: PowerShell. Windows: the built-in `powershell.exe`. When using hostnames the domain account authenticates with Kerberos; for IP addresses add them to the WinRM client's TrustedHosts (`Set-Item WSMan:\localhost\Client\TrustedHosts -Value 10.0.0.*`). Docker/Linux: `docker compose up -d --build` installs PowerShell 7, the PSWSMan WSMan client (PowerShell on Linux has none of its own; the build needs internet access to GitHub and powershellgallery.com) and `gss-ntlmssp` by default (`WITH_POWERSHELL=false` in `.env` leaves them out); NTLM then works for domain accounts, Kerberos does not. Inside the container short computer names (`LAPTOP-01`) usually do not resolve; use the FQDN (`laptop-01.corp.local`) or the IP address.
- Settings: `Tactical:Push:PowerShellPath`, `UseSsl`, `MaxParallel` (4), `TargetTimeoutSeconds` (600). Jobs are kept in memory for 6 hours and lost when the server restarts.
- Verified: validation, script generation, error classification and secret scrubbing (unit tests), and the page against a mock API. **Not verified**: the PowerShell/WinRM execution itself has never run against a real Windows computer or domain, and the Docker PowerShell option has not been built.

## Windows Update approval and schedule
- **Per computer**: device page, tab **Patch**. Select updates and *Approve* / *Ignore* / *Reset*; *Scan now* asks the agent to rescan; *Install approved now* installs the approved ones immediately (the agent must answer a ping). *Settings* edits that computer's own rules.
- **Per automation policy**: **Policies** page, button **Updates**. Applies to every agent using the policy (unless inheritance is blocked or the agent overrides a field).
- **Rules** (agent over policy, field by field, `inherit` falls through): approve/ignore by severity (critical, important, moderate, low, other); install schedule `never / daily / weekly (weekdays) / monthly (day)` at an hour in the **agent's time zone**; reboot after install (`never / only if required / always`). New updates reported by the agent get the automatic decision at once; saving settings applies them to updates that nobody decided on yet. A manual Approve/Ignore is never overwritten. With no rule set, updates wait for a manual decision.
- **Scheduler**: a background job checks once a minute, and during the scheduled hour sends the approved updates to every agent that answers a ping (an offline agent is retried each minute until the hour ends, then waits for the next run). Needs permission `can_manage_winupdates` to change anything (superusers have it); changes are audited.
- Existing databases get the new columns automatically at start-up (`SchemaUpgrade`).
- Verified: rule logic (unit tests) and the pages against a mock API. **Not verified**: the NATS commands `getwinupdates` and `installwinupdates` (with `guids`) are taken from the upstream agent protocol but have not been run against a real agent; if the agent's time zone name cannot be resolved on the server the schedule falls back to UTC.

## Security notes
- **MessagePack**: upgraded `2.5.172` -> `2.5.303` (fixes the 2.x advisories CVE-2024-48924, CVE-2026-48510, CVE-2026-48512 and CVE-2026-92707; the 3.x line is fixed in 3.1.9). Replies from endpoints are decoded through `AgentReplyDecoder`: 32 MB size cap, `MessagePackSecurity.UntrustedData`, object-graph depth limit 32, and malformed input returns `"malformed"` instead of throwing. The stack uses plain NATS, so use a TLS-protected link where possible.
- `Directory.Build.props` turns on NuGet audit (direct and transitive packages); watch the restore warnings.
- The new decoder has only been compiled against stubs; `dotnet build` and a real agent reply still need to be run.

## Installed software
Like Tactical RMM, each asset has a **Software** tab (asset page) with the applications it reported: name, version, publisher, install date. Search by name, publisher or version; **Refresh from agent** asks the agent for a fresh list right now (NATS command `softwarelist`, 15 second wait) and stores it. If the agent is offline the tab says so and keeps the last list. The list also arrives when the agent posts it to `/api/v3/software`; either way only the known fields are kept (at most 10,000 items per asset).
- The **Installed software** report (Reports page) has one row per asset and application for a client or site, with a search box to find who has an application or a version, and a CSV download.
- **Uninstall** (Windows assets): each application whose agent reported an uninstall command has an *Uninstall* button. The server finds the application in the stored list (name and version must match exactly), builds the command itself and runs it on the asset (waits up to 10 minutes); the browser never sends a command. Windows Installer entries (`MsiExec.exe /I{GUID}` or `/X{GUID}`) run silently as `msiexec.exe /x {GUID} /qn /norestart`; every other uninstaller runs exactly as Windows registered it and **may show a window on the computer** (the page says so afterwards). The list is refreshed when it finishes. Needs the permission `can_send_cmd` (run commands) and access to the asset; each uninstall is audited with the command. **Not verified**: the agent's `softwarelist` reply is assumed to carry an `uninstall` field per application (as the upstream agent does); if it does not, no button appears. The run itself has not been tried on a real agent.
- API: `POST /agents/{agent_id}/software/uninstall` (body `name`, `version`), `GET /agents/{agent_id}/software`, `POST /agents/{agent_id}/software/refresh` (permission `can_list_agents` and access to the asset), `GET /reports/software?q=`.
- Not tested against a real agent: the `softwarelist` request and the shape of its reply follow Tactical RMM and have only been tested with a fake agent.
- Not included: uninstalling software from the dashboard, and a "last refreshed" automatic schedule (a list is refreshed when you press the button or when the agent posts one).

## Reports
`#/reports`: look at a report on screen (first 200 rows) or download it as a CSV file that opens in Excel. Narrow it to a client or site; the role's client/site scope always applies.
- **Asset inventory** (`can_list_agents`): client, site, hostname, OS, agent version, online status, last seen, health score, pending updates, open alerts, reboot needed, uptime.
- **Outstanding patches** (`can_list_agents`): assets with pending or failed Windows updates, or that need a reboot.
- **Installed software** (`can_list_agents`): see the Installed software section.
- **Alert history** (`can_list_alerts`): alerts of a period (dates inclusive, default last 30 days, at most one year, times in UTC), open and resolved, up to 5000.
- API: `GET /reports`, `GET /reports/{name}` (JSON table), `GET /reports/{name}/csv`; filters `client_id`, `site_id`, `from`, `to` (`yyyy-MM-dd`).
- CSV safety: text that starts with `=`, `+`, `-` or `@` gets a leading apostrophe, so a hostname or alert message sent by an agent cannot run as a spreadsheet formula. The file is UTF-8 with a byte order mark.
- Not included: scheduled or emailed reports, PDF, and a report of check or task results.

## Script snippets
Reusable pieces of script (`#/snippets`; listing needs `can_list_scripts`, changes need `can_manage_scripts`). Write `{{name}}` in a script and the snippet's code is put there each time the script runs on an agent: a script run from the dashboard, a script action of an automated task, and a script check.
- Names are letters, digits, `-` and `_`. Editing a snippet changes every script that uses it. A snippet cannot be renamed, and cannot be deleted while a script still uses it (the page shows how many do).
- Insertion happens once: a snippet that contains `{{other}}` does not pull in another snippet (so nothing can loop). Unknown names are left as written. The result is limited to 1,000,000 characters.
- API: `GET/POST /script-snippets`, `GET/PUT/DELETE /script-snippets/{id}`.
- Not included: snippets inside script arguments or environment variables, and `{{client.Field}}` custom-field placeholders.

## Custom fields
Fields an administrator defines once for **clients, sites or assets** (`#/custom-fields`, permission `can_manage_custom_fields`). Types: text, number, yes/no, date and time, pick one (with a list of choices). A field can have a default value that shows wherever no value is set.
- Values are edited on the Clients & Sites page (button **Fields** on a client or site; needs `can_manage_clients` / `can_manage_sites`) and on each asset's page (needs `can_edit_agent`). Reading needs the matching list permission, and the role's client/site scope applies.
- API: `GET/POST /custom-fields`, `PUT/DELETE /custom-fields/{id}`, `GET/PUT /custom-fields/values/{model}/{target}` (`target` is the client or site id, or the asset's `agent_id`). A blank value removes the value.
- A field's model and type cannot change after creation. Removing a choice from a pick-one field clears that value where it was used. Deleting a field, client, site or asset deletes its values.
- Not included: using fields inside scripts (`{{client.Name}}`), a "required" flag, multiple-choice, and filling a field from a check result.

## Bulk actions, maintenance, notes, services, agent update (new)
- **Bulk actions** (Assets list: tick assets, panel appears): run a command, run a script, restart, maintenance on/off or **update agent** on up to 100 assets (8 at a time). The whole request is refused if one asset is outside your scope or you lack the permission of the action. Results appear live per asset (waiting, running, done, failed, offline). Jobs live in memory for 6 hours and are lost on restart. API: `POST /bulk-actions`, `GET /bulk-actions/{id}`.
- **Maintenance mode** (asset page button, or bulk): while on, the asset opens no new alerts or notifications; alerts already open stay. Needs `can_edit_agent`.
- **Notes and history** (asset page tabs): free-text notes per asset, and a history of commands, scripts and actions run from the dashboard (who, when). API under `agents/{agent_id}/notes` and `/history`.
- **Services and processes** (asset page tabs, Windows, permission `can_send_cmd`): list Windows services and start/stop/restart them; list processes and end one. Live: the agent is asked each time (NATS `winservices`, `winsvcaction`, `procs`, `killproc`).
- **Update agent** (asset page button for Windows assets, or bulk): the server pings the agent, then sends NATS `agentupdate` with the download URL of `Tactical:LatestAgentVer` (default `2.12.0`, official rmmagent release on GitHub), version and installer name. The agent downloads it and restarts itself. Skipped when already on that version, refused for non-Windows; offline agents get nothing sent. Needs `can_manage_agents`; audited. API: `POST /agents/{agent_id}/update`. Set `Tactical__LatestAgentVer` to the version you want agents to reach.
- **Not verified on a real agent**: the shape of the `agentupdate` payload and of the services/processes replies follow Tactical RMM from memory and are tested only with a fake agent. Try one test PC first.

## Not covered (deliberately, "core first")
Chocolatey installation, file transfer, the 'collector' result to custom field (histresult), terminal & WebSocket (SignalR),
SMS notifications, alert templates beyond the simple "always notify" flags (no custom messages, no per-template email recipients), placeholders such as `{{client.Field}}` in scripts, custom-field results from checks, reporting & SSO (`ee/`, separate sponsor licence).

## VNC remote control (TightVNC already on the endpoints)
Instead of MeshAgent, the dashboard can open the TightVNC that is already installed on an asset (useful when the antivirus blocks MeshAgent). Nothing is installed by us.
- **Passwords with fallback** — `#/vnc` (permission `can_manage_vnc`): the lowest priority number is the default, the others are fallbacks. The server tries them in order only if the asset rejects the default, remembers which one worked per asset (in memory) and tries that first next time. At most `Tactical:Vnc:MaxAttempts` (default 4) attempts per connect, because TightVNC temporarily blocks a client after repeated failures. Passwords are max 8 characters (a VNC limit), stored AES-256-GCM encrypted (key derived from `Tactical:SecretKey`), and never returned by the API.
- **Connect / Test** — buttons on the asset page (permission `can_use_vnc`, asset scope applies). The API itself performs the RFB authentication handshake (no session is opened) to find a working password, then **Connect** downloads a `.vnc` file for TightVNC Viewer; **Test** only reports the result. Both are written to the audit log.
  - `POST /agents/{agentId}/vnc/test`, `POST /agents/{agentId}/vnc/file`
  - `GET/POST /vnc/credentials`, `PUT/DELETE /vnc/credentials/{id}`
- **Target address** — taken only from the asset's own record (local IPs from the agent's hardware inventory, `wmi_detail`), never from the request. Only private LAN addresses (10/8, 172.16/12, 192.168/16) are contacted unless `Tactical:Vnc:AllowPublicTargets` is set. Port: `Tactical:Vnc:Port` (5900).
- The API server must be able to reach port 5900 of the assets (same LAN, as here) and the viewer machine as well, since the viewer connects directly. VNC is not encrypted: restrict port 5900 with the Windows firewall to the admin/RMM subnets.
- Fix included: the Go `nats-api` writes a `wmi_detail` column that the schema was missing, so the agent hardware inventory was being lost. Existing databases are upgraded automatically at startup (`SchemaUpgrade`, idempotent SQL), so no manual `ALTER TABLE` is needed.
- Verified: DES/obfuscation against OpenSSL, the handshake against a fake VNC server (wrong password, protocol 3.3, refusal, unsupported, timeout). Not verified against a real TightVNC server or viewer.

## How agents reach NATS (443 or 4222)
By default the Go agent connects to NATS over **WebSocket on port 443**; only an agent installed with `--natsport N` connects straight to NATS on port N (the value is kept in the registry as `NatsStandardPort`). This stack publishes NATS on 4222 and its WebSocket only on `127.0.0.1:9235` without TLS, so port 443 works only when **your reverse proxy** forwards it to 9235 with the WebSocket `Upgrade` headers and a valid certificate. If that proxy, its certificate or its firewall rule fails, every agent installed with the default goes offline ("cannot connect to NATS on 443").
- The **Install agent** page has a choice "How agents reach NATS": the default (443, needs the proxy) or direct on 4222 (adds `--natsport 4222` to the install command, for both the copy-ready commands and push install).
- An agent that is already installed keeps its setting; to change it, reinstall it with the option, or set `NatsStandardPort` in its registry and restart the agent service (check the key location on your agent version first).

### TLS on NATS (agents installed with `--natsport`)
The Go agent connects with `tls://host:port`, so the NATS server must offer TLS itself. A TLS-terminating proxy (nginx `stream` with `ssl`) in front of NATS does not work: NATS speaks first (`INFO`), the proxy waits for a TLS hello, and the agent times out.
1. Copy the certificate chain and key for your agent host name to `./certs/fullchain.pem` and `./certs/privkey.pem` (or point `NATS_CERT_DIR` at another folder).
2. In `.env`: `NATS_TLS_CERT=/certs/fullchain.pem` and `NATS_TLS_KEY=/certs/privkey.pem`.
3. `docker compose up -d --build`. At startup the API rewrites `nats-rmm.conf` with a `tls` block (`allow_non_tls`, so the API and nats-api inside the stack keep using plain `nats://nats:4222`), and the nats container reloads itself.
4. Publish port 4222 (or forward your 4221 as raw TCP, `proxy_pass nats:4222` in an nginx `stream` block **without** `ssl`). The certificate's name must match the host the agent uses. After renewing the certificate, `docker compose restart nats`.

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

## Developing from Visual Studio

Running only the API from Visual Studio leaves NATS and `nats-api` missing, so agents cannot connect. Run PostgreSQL and NATS in Docker and the API from Visual Studio:

1. Create `.env` (Windows: `scripts\windows\nettactical.cmd setup`, otherwise copy `.env.example`). Keep the values: the API settings below must match them.
2. Start the two containers:
   ```
   docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build db nats
   ```
   This publishes PostgreSQL on `127.0.0.1:5432` (set `DB_PORT` in `.env` if that port is taken) and NATS on `4222`, and shares the folder `.dev\nats` with the container.
3. Add `src\TacticalRmm.Api\appsettings.Development.json` (not committed with real values; use your own `.env` values):
   ```json
   {
     "ConnectionStrings": { "Default": "Host=127.0.0.1;Port=5432;Database=tacticalrmm;Username=tactical;Password=<POSTGRES_PASSWORD from .env>" },
     "Tactical": {
       "SecretKey": "<TACTICAL_SECRET_KEY from .env>",
       "NatsUrl": "nats://127.0.0.1:4222",
       "NatsConfPath": "C:\\path\\to\\NetTactical\\.dev\\nats\\nats-rmm.conf",
       "NatsServerBin": ""
     }
   }
   ```
   `NatsServerBin` stays empty because the container reloads nats-server itself. The password and secret key must be exactly those in `.env`, or `nats-api` and the API cannot talk to the database and NATS.
4. Start the API from Visual Studio. Check that `Test-NetConnection 127.0.0.1 -Port 4222` succeeds.
5. For agents on other computers the API must listen on the network: use `http://0.0.0.0:<port>` as `applicationUrl` in `launchSettings.json`, open that port and 4222 in Windows Firewall, and put the LAN address (not `localhost`) in the installer's Server URL.

This mode was prepared without a Windows machine and has not been run.

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
- `dotnet build` has never been run with the real NuGet packages, and nothing has been tested against a real Go agent, NATS, or Postgres.
- There is no `NuGet.config` in this zip; the build uses nuget.org as usual.

## Licence & origin
This project is a behaviour-based port of [Tactical RMM](https://github.com/amidaware/tacticalrmm) (Amidaware), which is licensed under the **Tactical RMM License v1.0** — it is *not* open source.
Use on your own internal/customer networks is permitted; turning it into a paid SaaS/commercial service requires written permission from Amidaware.
Amidaware's `ee/` folder (SSO, Reporting) requires a sponsor token and is **not** ported here. Make sure you comply with the original licence before making this repo public or distributing it.
