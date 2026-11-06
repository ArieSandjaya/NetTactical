# NetTactical — Tactical RMM backend dalam ASP.NET Core (.NET 8, C#)

Port **cakupan inti** backend Tactical RMM (Django) ke C# N-Tier. Agent Go dan `nats-api` asli **tidak diubah**:
path REST `/api/v3/*`, nama field JSON, subject NATS, dan skema tabel `agents_agent` dipertahankan.

## Struktur
| Proyek | Isi | Padanan Django |
|---|---|---|
| `Domain` | Entitas + konstanta | `*/models.py`, `constants.py` |
| `Application` | Interface, DTO, logika murni (CheckEvaluator, PermissionEvaluator, dsb.) | `checks/models.py::handle_check`, `permissions.py` |
| `Infrastructure` | EF Core + Npgsql, NATS.Net + MessagePack, service, background job | ORM, `nats_utils.py`, Celery |
| `Api` | Controller, autentikasi token, rate limit | `views.py`, `urls.py`, Knox/DRF |
| `tests` | xunit untuk logika murni | `tests.py` |

## Sudah dicakup
Login (+TOTP, throttle 10/menit & 300/hari), token Knox-style, API key, RBAC + scope client/site, Client/Site/Agent,
registrasi agent + `nats-rmm.conf`, `apiv3` (checkin, config, checkrunner GET/PATCH, runchecks, checkinterval, taskrunner GET/PATCH, newagent),
Check, AutomatedTask, Alert (dashboard), Script, run script/cmd/ping/reboot via NATS, Audit log, job outage agent (pengganti celery beat).

**Seluruh endpoint `/api/v3` milik agent kini ada** (selain yang tertulis di bawah): `checkin`, `config`, `checkrunner`, `runchecks`, `checkinterval`,
`taskrunner`, `newagent`, `syncmesh`, `choco`, `software`, `winupdates` (PUT/PATCH/POST), `superseded`, `histresult`, `installer` (GET/POST),
`meshexe`, `meshreinstall`. Integrasi MeshCentral (token AES-GCM, daftar device group via WebSocket, unduh installer) diisi lewat `Tactical:Mesh`.

## Belum dicakup (sengaja, "inti dulu")
Policy inheritance penuh (client/site level), penjadwalan & persetujuan winupdate otomatis, instalasi chocolatey, file transfer, hasil 'collector' ke custom field (histresult), sinkronisasi izin Mesh (`run_sync_task`), CRUD Check/Task/User/Role di dashboard, terminal & WebSocket (SignalR),
notifikasi email/SMS/webhook, AlertTemplate, custom field, snippet script, reporting & SSO (`ee/`, lisensi sponsor terpisah), Mesh.

## Menjalankan
```bash
export Tactical__SecretKey="<>=32 karakter acak>"
export Tactical__BootstrapAdmin__Password="<>=12 karakter>"
export ConnectionStrings__Default="Host=...;Database=tacticalrmm;Username=...;Password=..."
dotnet run --project src/TacticalRmm.Api
```
Dev: skema dibuat via `EnsureCreated`. Produksi: `dotnet ef migrations add Initial -p src/TacticalRmm.Infrastructure -s src/TacticalRmm.Api`.
Token installer tampil sekali di log saat pertama jalan.

## Status verifikasi (jujur)
- `Domain` + `Application` dibangun dan diuji: **59 asersi logika lulus** (evaluasi check, izin, token Mesh AES-GCM, URL Mesh, versi installer, update superseded, keputusan reboot).
- Seluruh solusi **dikompilasi lolos** terhadap stub sementara untuk EF Core / NATS / MessagePack (bagian ASP.NET dikompilasi dengan SDK asli). Ini hanya membuktikan kode konsisten, **bukan** bahwa pemanggilan API paket-paket itu sudah benar.
- Belum pernah `dotnet build` dengan paket NuGet asli, dan belum diuji terhadap agent Go, NATS, MeshCentral, atau Postgres sungguhan.
- Tidak ada `NuGet.config` di zip ini; build memakai nuget.org seperti biasa.

## Lisensi & asal-usul
Proyek ini adalah port berbasis perilaku dari [Tactical RMM](https://github.com/amidaware/tacticalrmm) (Amidaware), yang berlisensi **Tactical RMM License v1.0** — *bukan* open source.
Pemakaian untuk jaringan internal/pelanggan sendiri diizinkan; menjadikannya layanan SaaS/komersial berbayar memerlukan izin tertulis dari Amidaware.
Folder `ee/` (SSO, Reporting) milik Amidaware memerlukan token sponsor dan **tidak** diport di sini. Pastikan Anda mematuhi lisensi aslinya sebelum membuka repo ini ke publik atau mendistribusikannya.
