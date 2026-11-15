# NetTactical: handoff for another AI session

Written 2026-11-15. Repo: https://github.com/ArieSandjaya/NetTactical (branch `main`, everything pushed; local clone was `/home/claude/nettactical`, working tree clean at commit `c2b2c01`).

## 1. Project and owner
- Owner: Arie (developer, Indonesia; writes in Indonesian, replies should be in Indonesian). Project name in Claude: "NET Tactical".
- Goal: port **Tactical RMM v1.6.1** (Django) to **ASP.NET Core 8 + PostgreSQL**, keeping the original **Go agent** working unchanged through the same `/api/v3` HTTP protocol and **NATS** command channel.
- MeshCentral is NOT used. Agents always install with `--nomesh`; `MeshExe`/`MeshReinstall` endpoints return BadRequest.

## 2. Architecture
- `src/TacticalRmm.Domain` (entities, `Constants.cs` incl. `Perm` string permissions), `.Application` (DTOs, interfaces, pure logic in `Logic/`), `.Infrastructure` (EF Core/Npgsql, services, NATS, notifications, push), `.Api` (controllers, `wwwroot` UI), `tests/TacticalRmm.Tests` (xunit).
- EF Core with a snake_case naming loop in `AppDbContext` (table `agents_agent` must match Django). **No migrations**: `EnsureCreatedAsync` + `Persistence/SchemaUpgrade.cs` (idempotent `ADD COLUMN IF NOT EXISTS` / `CREATE TABLE IF NOT EXISTS` run at startup). Any new table or column must be added there too.
- Results use `Outcome<T>` mapped by `this.ToResult`; permissions via `[RequirePerm(Perm.X)]`; JSON is snake_case; dashboard auth header `Authorization: Token x`; installer tokens belong to the installer user.
- UI: vanilla JS ES modules in `src/TacticalRmm.Api/wwwroot/js` (hash routing, `h()` DOM helper, `forms.js`, `ui.js`), strict CSP (no inline script/style, no external fonts or CDNs; fonts are self-hosted in `wwwroot/fonts`), `api.js` has a 30 s timeout. **Glass theme** based on the TemplateMo 607 "Glass Admin" look: light by default plus a dark mode switch in the sidebar; colours are CSS variables in `css/app.css` (`:root` and `:root[data-theme="dark"]`), the saved choice is applied by `js/theme.js` in `<head>` (no flash). Do not use colours directly in views; use the variables. Form state is plain objects mutated in place; build payloads at click time. Tables go inside a `scroll-x` wrapper (mobile).
- NATS is the command channel (`NatsCommandBus`, fails fast with "natsdown"); `nats-api` (upstream Linux binary) writes agent data to Postgres. **Without NATS agents cannot connect.**
- Deploy: Docker Compose (`docker-compose.yml`), Windows helper `scripts/windows/nettactical.ps1`/`.cmd` (Docker Desktop), Visual Studio dev mode via `docker-compose.dev.yml` (Postgres + NATS in Docker, API from VS). Details in README.

## 3. Features done (all on main)
Dashboard/assets/clients/sites/users/roles; scripts, checks (with edit), tasks (with edit), policies, audit log pages; alerts; VNC remote control (TightVNC on endpoints); 2FA/TOTP setup UI (`security.js`); installer generator (`#/installer`); glass theme (light + dark); Mesh removed. This session added:
1. **Notifications**: email (SMTP STARTTLS only) and webhook (JSON or Slack, HMAC `X-NetTactical-Signature`) channels, page `#/notifications`, perm `can_manage_notifications`, queued background sender, SSRF protections, secrets AES-GCM encrypted.
2. **Push install** (installer page, second card): server installs the agent on up to 20 computers over WinRM using a domain-admin credential typed per push (never stored/logged/returned; stdin to PowerShell). Stops the whole job when one computer rejects the credential (prevents account lockout). Perm `can_push_install`. Optional PowerShell in Docker: `--build-arg WITH_POWERSHELL=true`. Files: `Application/Logic/PushInstall.cs`, `Infrastructure/Push/*`, `PushInstallController`.
3. **Windows Update approval and schedule**: per-severity auto-approve, per-update Approve/Ignore, Scan now, Install approved now, schedule never/daily/weekly/monthly at an hour in the agent's time zone, reboot policy, agent- and policy-level settings with inheritance (agent over policy, per field). Background `WinUpdateScheduler` ticks each minute. Perm `can_manage_winupdates`. UI: device page Patch tab (`patchpanel.js`) and Policies page "Updates" button (`patchpolicy.js`). Pure rules in `Logic/WinUpdateRules.cs`.

## 4. Remaining work (task list)
- **#35 done**: policy inheritance for client and site (agent > site > client, one resolver `PolicyChain` + `EffectivePolicy`, UI pickers on the Clients & Sites page) and **simple AlertTemplate**. Not done: the policy list does not yet show how many clients/sites use a policy.
- **AlertTemplate (simple)**: a template says "always notify for" offline agents / failing checks / failing tasks. It is assigned to a client or site (site beats client; a default template applies where neither has one; disabled templates are skipped) and only ever *adds* the notification flag, so channels set to "only flagged" send those alerts. It does not change severity, dashboard alerts or what a check/task/agent already has. Hook point: `AlertService.NotifyAsync` via `AlertTemplateResolver`. Page `#/alert-templates`, API `alert-templates`, perms `can_list_alerts` / `can_manage_alerts` (also needed to assign one to a client/site). Not built: templates on policies or agents, severity thresholds, resolved/failure actions, SMS.
- Behaviour to know: a policy that is switched off is skipped (the next level up can still apply); "block policy inheritance" on an agent switches off every level. Assigning a policy to a client/site needs `can_manage_automation_policies`.
- Awaiting evidence from the user: (a) app "hangs" after a few clicks: likely NATS down plus the browser's ~6 connection limit; client hardened but unconfirmed on the real server. Ask for DevTools Network pending requests and `docker compose logs api`. (b) Agent "asks for mesh agent": not diagnosed; ask for exact error and `agent.log`.
- Not built (README "Not covered"): SMS notifications, reprocess-failed-updates counter, email-if-fail for patching, custom fields, reporting, etc. See README sections "Covered so far" and "Not covered".
- Optional: `.vs/` was removed from the git index but may remain in history (purge needs force-push; ask first). `appsettings.json` contains real-looking secrets, so rotate/replace them.

## 5. Verification status (be honest about it)
- **CI exists** (`.github/workflows/ci.yml`): GitHub Actions runs `dotnet build`, `dotnet test` and a syntax check of every UI module on each push. The backend has now been built with real NuGet packages (0 errors) and 93 unit tests pass. Check the Actions tab; the sandbox itself still cannot reach NuGet, so CI is the only place the build runs.
- Still **never run for real**: Docker setup, Windows scripts, VS dev mode, PowerShell/WinRM push, the NATS commands `getwinupdates` / `installwinupdates` (`guids`), SMTP/webhook delivery, the EF queries and `SchemaUpgrade` against a real PostgreSQL, and agent + Postgres + NATS together. VNC only against a fake RFB server. UI only checked for syntax in CI, and earlier against a mock API.
- Unit tests cover pure logic only; the services that talk to the database (including `EffectivePolicy` and `AlertTemplateResolver`) have no tests.
- First thing to do with a real server: `docker compose up` and connect one real agent.

## 6. Sandbox test rig (only exists in the original session's scratchpad, not in the repo)
- Stub build `scratchpad/full` (`dotnet build`), xunit shim `scratchpad/tcheck` (no Theory/InlineData), mock API `scratchpad/mock/server.js` (port 5099) with Playwright tests `test5..12.js`. Recreate if needed; with real NuGet access use `dotnet test` instead.
- Sandbox has no PowerShell, no Docker daemon; GitHub raw/API fetches are blocked.

## 7. Working conventions
- Commit messages end with `Co-Authored-By` and `Claude-Session` footer lines (see git log). Feature work goes on a `feat/...` branch, merged to `main` with `--no-ff`, then pushed. Remote URL: `https://github.com/ArieSandjaya/NetTactical.git`.
- Report caveats honestly; don't claim anything is tested that has not run for real. Keep answers short, in Indonesian.
- Security rules followed: no secrets in logs, validate every external input, SSRF blocklists, strict CSP.
