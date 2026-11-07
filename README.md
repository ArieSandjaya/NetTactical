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

## CRUD dashboard (baru)
- **Check** — `GET agents/{agentId}/checks`, `GET automation/policies/{id}/checks`, `GET/PUT/DELETE checks/{id}`, `POST checks` (validasi per tipe: disk, cpu, memory, ping, winsvc, script, eventlog; host ping dicek anti-injeksi).
- **Task terjadwal** — `GET agents/{agentId}/tasks`, `GET/PUT/DELETE tasks/{id}`, `POST tasks`, `POST tasks/{id}/run`. Perubahan didorong ke Task Scheduler agent (`schedtask`/`delschedtask`) oleh `TaskSyncJob` (tiap 2 menit); penghapusan baru final setelah agent mengonfirmasi.
- **Akun** — `accounts/users` (CRUD, reset password, reset TOTP; 2FA sendiri via `accounts/totp/setup|enable`) dan `accounts/roles` (CRUD) + `accounts/permissions`. Perlindungan eskalasi: non-superuser tak bisa menyentuh superuser atau memberi izin yang tidak dimilikinya; superuser aktif terakhir tak bisa dihapus/dinonaktifkan; reset password/nonaktif mencabut semua token.
- **Organisasi** — `PUT/DELETE clients/{id}`, `POST clients/sites`, `PUT/DELETE sites/{id}`, `PUT agents/{agentId}` (butuh `can_edit_agent` + scope site asal & tujuan).
- **Policy** — `automation/policies` CRUD (hapus ditolak bila masih punya task, supaya agent dibersihkan lebih dulu).

## Dashboard armada (baru)
- **Metrik historis** — tiap hasil check CPU/memori/disk disimpan sebagai titik di tabel `check_metric` (jeda minimum `Tactical:MetricMinIntervalSeconds`, default 240 dtk). `MetricRetentionJob` menghapus data lebih tua dari `Tactical:MetricRetentionDays` (default 90).
- **Skor kesehatan 0-100** — `HealthScorer` (logika murni): pengurang untuk offline, disk, CPU, memori, alert terbuka, update tertunda, perlu reboot, uptime panjang, agent usang. Bobot dan ambang level bisa diubah lewat `Tactical:Health`. Setiap skor membawa daftar faktor + kode stabil untuk diterjemahkan di UI.
- **Endpoint** (izin `can_list_agents`, scope client/site berlaku):
  - `GET /fleet/summary?client_id=&site_id=&days=30&risk_limit=10` — total, alert, patch, skor per client, perangkat berisiko, alert terbaru, sebaran OS, versi agent, tren harian.
  - `GET /agents/{agentId}/overview` — fakta perangkat, skor + faktor, metrik terkini, disk, **daftar masalah** (check/task gagal beserta stdout/stderr/kode keluar, offline, patch, agent usang), check, task, patch, layanan otomatis yang berhenti.
  - `GET /agents/{agentId}/metrics?metric=cpu|memory|disk&label=C:&days=30` — deret waktu (di-downsample ke maks 1000 titik).
- Belum ada: usia perangkat/hardware (agent tidak mengirimnya), prediksi, dan riwayat kejadian error setelah alert selesai (hanya masalah yang sedang terbuka).

## Belum dicakup (sengaja, "inti dulu")
Policy inheritance penuh (client/site level), penjadwalan & persetujuan winupdate otomatis, instalasi chocolatey, file transfer, hasil 'collector' ke custom field (histresult), sinkronisasi izin Mesh (`run_sync_task`), terminal & WebSocket (SignalR),
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
- `Domain` + `Application` dibangun dan diuji: **59 + 123 asersi logika lulus, ditambah 12 test untuk skor kesehatan, aturan metrik, dan parser data agent** (evaluasi check, izin, token Mesh AES-GCM, URL Mesh, versi installer, update superseded, keputusan reboot; validator Check/Task, payload Task Scheduler, aturan akun). Service CRUD & controller baru hanya terkompilasi terhadap stub — belum dijalankan terhadap Postgres.
- Seluruh solusi **dikompilasi lolos** terhadap stub sementara untuk EF Core / NATS / MessagePack (bagian ASP.NET dikompilasi dengan SDK asli). Ini hanya membuktikan kode konsisten, **bukan** bahwa pemanggilan API paket-paket itu sudah benar.
- Belum pernah `dotnet build` dengan paket NuGet asli, dan belum diuji terhadap agent Go, NATS, MeshCentral, atau Postgres sungguhan.
- Tidak ada `NuGet.config` di zip ini; build memakai nuget.org seperti biasa.

## Lisensi & asal-usul
Proyek ini adalah port berbasis perilaku dari [Tactical RMM](https://github.com/amidaware/tacticalrmm) (Amidaware), yang berlisensi **Tactical RMM License v1.0** — *bukan* open source.
Pemakaian untuk jaringan internal/pelanggan sendiri diizinkan; menjadikannya layanan SaaS/komersial berbayar memerlukan izin tertulis dari Amidaware.
Folder `ee/` (SSO, Reporting) milik Amidaware memerlukan token sponsor dan **tidak** diport di sini. Pastikan Anda mematuhi lisensi aslinya sebelum membuka repo ini ke publik atau mendistribusikannya.
