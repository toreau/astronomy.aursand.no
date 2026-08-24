# Changelog

All notable changes, grouped by date. Format inspired by Keep a Changelog
(keepachangelog.com); sections: Core · Bugs fixed · Performance ·
Infrastructure & ops · Tests · Documentation · Dependencies.

## 2026-08-24

### Core
- Production storage switched SQLite → PostgreSQL (dev/tests stay SQLite). `AstronomyDbConfig`
  (`ASTRONOMY_DB_PROVIDER` `sqlite`|`postgres`, default sqlite; `ASTRONOMY_DB_CONNECTION` for
  Postgres) routes every DB access; provider-aware `EnsureSchema` (Postgres `Migrate()`,
  SQLite idempotent model script); health probe now provider-neutral via EF `CanConnect`.
- Npgsql migrations replace the SQLite sets for both contexts (registry + satellite elements);
  design-time factories pinned to Npgsql so `dotnet ef` always targets the production provider.
- Worker heartbeat + `jobs`/`host-gates` run against the configured provider; `backup` CLI is
  now SQLite-only (prod backups delegated to Coolify's Postgres resource — none configured).

### Infrastructure & ops
- Coolify: `astronomy-db` PostgreSQL created (internal, `postgres:16-alpine`, uuid hostname on
  the coolify network, `SSL Mode=Disable`); api + worker deployed with `ASTRONOMY_DB_PROVIDER`
  + `ASTRONOMY_DB_CONNECTION`; `/health/ready` → `db: ok` in production.
- Old `/data/astronomy.db` left in place but unused; registry/elements data is re-fetchable by
  the scheduled `naif` / `omm-refresh` jobs (no migration of existing data needed).

### Dependencies
- Microsoft.* 10.0.10 → 10.0.11 (OpenApi, Data.Sqlite, EF Core Design/Sqlite, DI.Abstractions,
  Mvc.Testing); Microsoft.OpenApi 2.11.0 → 2.12.2 (2.x — AspNetCore.OpenApi pins `< 3.0.0`).
- Added Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3 (Infrastructure, Satellites) + Npgsql 10.0.3
  (DataIngestion). Pins unchanged: SQLitePCLRaw 3.0.5, accuracy engines, SGP.NET, NodaTime.
  Test-tooling majors (Test.Sdk 18 / runner 4 / coverlet 10) deferred.

### Tests
- Suite 1,366 → 1,379; `AstronomyDbConfig` parsing covered; store/registry/integration tests
  drive the config-based (SQLite) path.

## 2026-08-06

### Core
- Live endpoint verification harness: `spikes/S12-live-verification/` — 408 independent checks vs JPL Horizons, sunrise-sunset.org, IERS, VizieR, USNO, python sgp4, skyfield; report in `docs/live-verification.md`
- `Program.cs` decomposed into per-domain endpoint modules (`Endpoints/`), OpenAPI domain tags
- Almanac `year=` parameter (12 monthly sections in one compressed response); bulk `calendars/range` (≤ 366 days)
- Standardized health endpoints `/health/live` + `/health/ready` (deep probe: db/schema, kernels, `kernelHashes`, datasets, satellite elements)

### Bugs fixed
- Moon horizontal altitude +0.87° — missing topocentric parallax in both tiers (`EphemerisCalculator.Horizontal` + `SpiceReferenceEphemeris.HorizontalPosition`); post-fix ≤ 0.0008° vs Horizons
- Moon-phase wax/waning naming inversion (named from the continuous phase in 1/16-width bands)
- Satellite passes in progress at window start or end now reported (rise/set clamped to the window)
- Twilight begin/end now belong to the requested local day at every longitude (date-line wrap at UTC+8 fixed via local-solar-noon pairing)
- Leap-seconds dataset now fetched live from the IERS list (was a copy of the compiled-in table)
- 503 `problem+json` details path-redacted; validation failures (bad dates etc.) return 400 instead of 500
- JSON enums serialize camelCase (`"type": "nautical"`, not `0`); CI coverage summary step glob fixed

### Performance
- Registry `ActiveVersion` 30s in-memory cache (parallelized almanac previously opened thousands of SQLite connections per request)
- Almanac parallelism bounded; satellite pass prediction parses the TLE once per request
- Output caching on stable endpoints; brotli/gzip response compression; satellite elements cached (version-keyed, 60s)

### Infrastructure & ops
- Fully automated dependency updates: Dependabot (weekly, accuracy-critical engines isolated), `global.json` SDK pin, weekly native-watcher workflow (erfa/cspice)
- Scheduled data refresh with gate-before-activate: `naif` weekly (kernels + all datasets incl. previously-orphaned eop-ut1) and `omm-refresh` daily; UT1 continuity guard; payload floors
- Kernel integrity markers (`.size-sha`) and kernel-change api restart contract (`COOLIFY_API_URL`/`TOKEN`/`APP_UUID`)
- Dockerfiles pinned (cspice fork `53bce32`, erfa `v2.0.1`); `docs/ops-schedule.md` documents the cron matrix + update inventory

### Tests
- Suite 1,310 → 1,369; CI collects + uploads per-project coverage
- Per-assembly line rates: Ephemeris 40.6 → 48.2%, Api 67.2 → 73.5%, Satellites 85.5 → 87.3% (weighted 56.7 → 59.8%)

### Documentation
- `docs/API.md`, `docs/ops-schedule.md`, `docs/live-verification.md`; README endpoint/tier/errors tables

### Dependencies
- See Dependabot/native pins under Infrastructure & ops

## 2026-08-05

### Core
- Phases 2–7 completed: Sun & Moon (positions, rise-set, twilight, moon phases, almanac), planets (visibility, events, monthly almanac), reference tier (SPICE DE441 + ERFA IAU2000A, historical ΔT pre-1972), bright stars (HYG v3.8 catalog, cone search/name/position/rise-set/brightest), satellites (SGP4 positions, pass prediction, OMM ingestion)
- Reference tier prefers `de441.bsp` (Horizons-identical, 3.3 GB streamed); Mars planet-center via `de440s_plus_MarsPC.bsp`; outer planets use barycenter targets
- API hardening: `/health/live`+`/health/ready` (deep readiness), request logging with `X-Request-Id`, 400 mapping for parse errors, satellite store dedupe (unique index + STJ round-trip), observer-required-when-horizontal

### Bugs fixed
- Engine `Equatorial.ra` is in HOURS — horizontal positions corrected (×15 conversion); `SiderealTime` hours conversion; star transit offset; horizontal UT1 computation (JD-epoch vs J2000 seconds); moon magnitude from illumination (was phase fraction)

### Infrastructure & ops
- Satellite elements scheduled refresh (`omm refresh`, daily task) + weekly `naif` job: kernels gap-fill, EOP C04, star-catalog gap-fill, reference gate (24 h throttle)

### Tests
- Accuracy suite grew to 1,122 tests (planets 49-epoch grids, USNO event vectors, Vallado SGP4 bit-exact); API/unit/architecture/integration suites

### Documentation
- README.md + docs/API.md (full request/response reference); phase reports 2–7 with validation evidence

### Dependencies
- One_Sgp4 1.1.0 (bit-exact vs Vallado; SGP.NET rejected); NodaTime 3.3.3 / TZDB 2026c; CSPICE N66 containerized

## 2026-08-04

### Core
- Phase 0: workspace, Astronomy-Engine + time-scale spikes (S0.2, S0.5)
- Spikes S0.3–S0.11: CSPICE containerized (lock-serialized, validated 9/9), SGP4 engine selection, SQLite/EF recipe, star cone-search tile index, tzdata pinning, OMM ingestion, deployable skeleton
- Phase 1: solution scaffold — SharedKernel (time scales, units, coordinates, datasets), Infrastructure (versioned dataset registry/catalog/loaders), six module contracts (Calendars + Time real, others 501), API composition root (error envelope, request-id, bounds, cancellation, OpenAPI, health), worker CLI (heartbeat/migrate/probe), CI workflow

### Bugs fixed
- Engine `Equatorial.ra` in hours (latent S0.2 finding); compare-subcommand body switch (mercury/uranus/neptune mapped to Sun)

### Tests
- First unit/architecture/integration/api suites green; S0.2 position gate closed (Sun/planets ≤ 23″)

### Documentation
- Spike reports S02–S11; Phase 1 foundation report

### Dependencies
- SQLitePCLRaw 3.0.5 pinned; sha256-pinned SPICE kernels (binaries gitignored)
