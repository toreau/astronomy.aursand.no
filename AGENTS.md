# AGENTS.md — astronomy.aursand.no

Public astronomy API: positions, events and pass predictions for the Sun, Moon, planets,
bright stars and satellites, with accuracy tiers validated against JPL Horizons 1900–2150.
.NET 10 ASP.NET Core **modular monolith**. Coolify: api `jk87r6rrgoegw3s6v3hz4ulu`
(rootless, :8080, public) + worker `p47lnt171dhf6kec7dn2jbtj` (root, no FQDN; scheduled
refreshes + kernel-reload contract). Full docs: wiki `Services/astronomy`.
CI builds a single **arm64** image to `ghcr.io/toreau/astronomy-api` via the
trusted builder: image production uses
`toreau/gh-workflows/.github/workflows/container-build-attest.yml@373df7517487bd20ac55a0986926677c0ddcbcf6`,
with build configuration from `.github/container-build.json`. The builder runs
only on `main` after `build-test` succeeds and emits `main-<sha>` with an exact
digest; it does not produce `latest`. The trusted builder generates the
build-provenance and SPDX-SBOM attestations, so the **signer identity is the
trusted builder** (the reusable workflow at its trusted revision), not this
repo's `ci.yml`. The repository is public.
CI is a **thin caller** into the reusable workflow library `toreau/gh-workflows`:
`dotnet-ci` (`build-test`), `container-build-attest` (trusted builder, pinned to
the trusted revision), `dispatch`; `native-watcher` calls `native-pin-watcher`.
The k8s-research GitOps consumer keeps this app **damped** (`attestation: false`
in `apps/astronomy/meta.yaml`): promotion authorization is the trusted expected
image to exact digest binding. Provenance is produced by the producer but is
**not** currently required for this app's promotion. There is no signer-specific
in-cluster admission policy for this app in current k8s-research policy
(`policy.images` covers only the separate reference app; admission hardening is
S11-owned).

## Commands

- Build/test: `dotnet restore Astronomy.slnx` / `dotnet build Astronomy.slnx -c Release` /
  `dotnet test Astronomy.slnx -c Release` (CI adds `--no-restore --no-build` + XPlat coverage
  artifact). `TreatWarningsAsErrors` + `AnalysisLevel=latest`; SDK pinned `10.0.302`.
- Run dev: `dotnet run --project src/Astronomy.Api` (localhost:5219).
- Data ingestion CLI: `Astronomy.DataIngestion` subcommands
  (ingest/naif/omm/compare-spice/star-gate/sat-gate/fixtures).
- Live verification harness: `spikes/S12-live-verification/` (408 checks vs Horizons/USNO/
  VizieR/skyfield/sgp4; `ASTRONOMY_API_BASE` for pre-deploy checks).

## Architecture (facts not obvious from filenames)

- 10 src projects: `Astronomy.Api` (minimal APIs, per-domain `Endpoints/` modules), 6 domain
  modules (Ephemeris, Stars, Satellites, Time, Calendars, Almanac), `Astronomy.Infrastructure`
  (versioned dataset registry, storage providers, loaders), `Astronomy.SharedKernel`, worker CLI
  `Astronomy.DataIngestion`. 5 test projects (Accuracy/Unit/API/Architecture/Integration).
- Accuracy tiers: `consumer` = Astronomy Engine 2.1.19 (VSOP87, ≤ 22.5″); `advanced` =
  SPICE DE441 + ERFA (≤ 10″); `reference` = + IAU2000A (≤ 1″).
- Dataset-driven with stage/activate/rollback registry; **every response carries `metadata`**
  (dataset versions, algorithm chain, `AST-7002/7003/7004` warnings: tier fallback, degraded
  horizontal chain, stale TLEs).
- Errors: RFC 7807 `application/problem+json`, codes `AST-4xxx` (400), `AST-5xxx` (500),
  `AST-7xxx` (warnings). Details path-redacted.
- Native CSPICE/ERFA calls are serialized behind a global lock.

## Critical gotchas

- **CSPICE is not thread-safe** (global lock) and its `erract RETURN` default aborts the
  process — guarded; never bypass the lock or change error handling.
- Astronomy Engine `Equatorial.ra` is in **HOURS** — ×15 conversion required.
- Accuracy-engine majors (CosineKitty.AstronomyEngine, One_Sgp4) are deliberate
  re-validation events; Dependabot isolates them in their own group, never bundled.
- Dockerfile pins the cspice fork commit `53bce32` + sed patch on `SpiceZpr.h` lines
  3252–3255; erfa `v2.0.1`. `native-watcher` (weekly, `native-pin-watcher` reusable
  workflow) opens an issue when upstreams move.
- Coolify scheduled-task commands are capped at 255 chars; Coolify native health checks stay
  **off** (no curl in the image — apt is very slow on the host).
- Volume `/data/astronomy` is root-owned → the worker runs as root; api runs rootless uid **150** (`useradd --uid 150` in the Dockerfile).
- Storage is dual-provider via `AstronomyDbConfig` (`Astronomy.SharedKernel.Persistence`):
  dev/tests run SQLite (`ASTRONOMY_DB_PATH`), production runs PostgreSQL (Coolify `astronomy-db`,
  `ASTRONOMY_DB_PROVIDER=postgres` + `ASTRONOMY_DB_CONNECTION`). **Npgsql migrations are
  Postgres-owned** (design-time factories are pinned to Npgsql); the SQLite dev schema is an
  idempotent model-script `EnsureSchema` — both EF contexts share one SQLite file, so plain
  `EnsureCreated` would silently skip the second context's tables.
- Postgres connects over the internal coolify network at the DB **uuid** hostname (not the DB
  name), port 5432, `SSL Mode=Disable` (the server runs ssl=off despite `ssl_mode: require`).
  Worker logs a harmless `libgssapi_krb5.so.2` load warning (Npgsql Kerberos probe).
- Kernel reload contract: worker restarts the api via the Coolify API
  (`COOLIFY_API_URL`/`COOLIFY_API_TOKEN`/`COOLIFY_API_APP_UUID`) when kernel hashes change;
  without those env vars, kernel changes apply at the next deploy.

## Push flow / deploy

- Auto-deploy on push works (GitHub webhook); a wedged deploy holds the queue slot —
  cancel-then-deploy to recover. Deploy builds are slow (CSPICE/ERFA compile stage,
  layer-cached).

## Testing

- ~1,366 tests (1,122 accuracy, 168 unit, 64 API, 6 architecture, 6 integration) — counts
  drift slightly between README and CHANGELOG; treat as ~1.37k.
- CI has **no network access** (fixtures committed); SPICE/ERFA paths are exercised by host
  gates, not CI. Coverage (line, per-assembly): Ephemeris 48.2%, Api 73.5%, Satellites 87.3%,
  weighted ~59.8%.

## Operations

- Health: `/health/live` (liveness); `/health/ready` deep probe (db, kernels + `kernelHashes`,
  star catalog, datasets, satellite elements) — uptime-kuma uses ready.
- Env: `ASTRONOMY_DB_PROVIDER` (`sqlite`|`postgres`, default `sqlite`), `ASTRONOMY_DB_PATH`
  (default `/data/astronomy.db`, SQLite), `ASTRONOMY_DB_CONNECTION` (Postgres only),
  `ASTRONOMY_DATA_ROOT` (`/data`), `ASTRONOMY_KERNEL_PATH` (`/data/kernels`); worker-only
  `COOLIFY_API_URL`/`TOKEN`/`APP_UUID`, `ASTRONOMY_API_URL`.
- **No rate limiting** (deferred by design; passes endpoint is the CPU-heavy one), no auth.
- Scheduled: `naif` weekly Sun 03:00 UTC (kernels + datasets + reference gate),
  `omm-refresh` daily 06:00 UTC (TLE elements) — both gate-before-activate; failed refresh
  leaves the previous version active.
- Caching: position endpoints `no-cache`; rise-set/twilight/almanac `max-age=900`; moon
  phases/events/stars `3600`; satellite status `60`. Almanac monthly = 12 months in one
  gzip-compressed response (~264 KB).

## Wiki

- Services/astronomy `019fd7ae-630e-715c-acd0-5b15c6da6aa9` — endpoints, tiers, data refresh.
- Repo `docs/` is authoritative for detail: API.md, ops-schedule.md, live-verification.md,
  spikes/.
