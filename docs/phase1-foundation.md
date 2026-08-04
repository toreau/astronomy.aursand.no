# Phase 1 — Foundation (completed)

**Date**: 2026-08-04 · **Status**: COMPLETE — deployed and verified live

## Scope delivered

**Solution structure** (`Astronomy.slnx`, net10, `Directory.Build.props`: nullable, warnings-as-errors):
- `src/Astronomy.Api` (composition root — evolved from the S0.11 skeleton), `Astronomy.SharedKernel`, `Astronomy.Infrastructure`, `Astronomy.DataIngestion` (renamed from `Astronomy.Worker`), 6 modules (`Calendars`, `Time`, `Ephemeris`, `Stars`, `Satellites`, `Almanac`), 4 test projects (`Unit`, `Architecture`, `Integration`, `Api`).

**SharedKernel** (zero deps, no `DateTime` — architecture-tested): `Angle`/`Distance`/`Velocity`, `JulianDate`/`ModifiedJulianDate`, `TimeScale` + `AstronomicalTime`, `LeapSecondTable` (IERS, dataset-backed, S0.5-validated), `TimeScaleConverter` (leap-chain ΔT policy; TDB≈TT; UT1 from EOP samples), `ObserverLocation`, frames/position/refraction/precision enums, `DatasetRef`/`AlgorithmRef`/`CalculationWarning`/`CalculationMetadata`, `IDatasetCatalog` + `IDatasetRegistry`, `FeatureNotImplementedInPhaseException`.

**Infrastructure**: EF Core dataset registry (`datasets`/`active_datasets`/`audit`; S0.9 recipe: WAL, Cache=Private, busy_timeout, SQLitePCLRaw 3.0.5 pin, RoundtripKind; **idempotent staging**), `DatasetCatalog`, versioned time-dataset loaders (`leap-seconds`, `eop-ut1` from `/data/datasets/{name}/{version}/`), JSON console logging, backup (`BackupDatabase`).

**Modules**: real `Calendars` (gregorian/ISO-week/JD + tz-aware local time via NodaTime tzdb 2026c) and `Time` (julian-date + time-scales); `Ephemeris`/`Stars`/`Almanac` contracts → 501; `Satellites` with the S0.10 OMM ingestion pipeline ported (fetch/validate/stage/activate/status, freshness states) behind `ISatelliteElementIngestionService` + its own EF store (migrated, not EnsureCreated — finding).

**API conventions** (all verified live): `/api/v1` + single OpenAPI doc; RFC 9457 problem+json with stable codes (`AST-4001` validation, `AST-5010` not-implemented-in-phase, `AST-5000`); `X-Request-Id` echo; cancellation tokens; query bounds (date parse + days-range checks; limit/offset helpers ready for domain endpoints); CORS allowlist (config); `/healthz` + `/ready` (SQLite read + registry); anonymous only.

**DataIngestion CLI** (worker container): `migrate`, `backup`, `dataset status|activate|rollback`, `ingest eop|leap-seconds`, `omm fetch|stage-file|activate|rollback|status`, retained `probe|fixtures|compare|naif`; heartbeat default with registry migration at startup.

**Tests** (all green): Unit 9 (S0.5 vectors incl. corrected J2000/TT semantics), Architecture 7 (dependency rules, module surface whitelist, no-DateTime — xunit v3 + reflection; NetArchTest dropped as unnecessary), Integration 5 (registry lifecycle incl. rollback + corruption-restore; converter driven by activated datasets), Api 9 (anonymous access, JD/TimeScales values, 400/501 envelopes, request-id, OpenAPI).

**CI**: `.github/workflows/ci.yml` — restore/build/test on push + PR.

## Live verification (astronomy.aursand.no)

- `/healthz`, `/ready` 200
- `/api/v1/time/julian-date?time=2000-01-01T12:00:00Z` → JD 2451545.0, provenance `leap-seconds/iers-2026a` + `eop-ut1/20260804`
- `/api/v1/time/time-scales` → TAI−UTC 37 s, TT−UTC 69.184 s, **UT1−UTC 0.366 s from the live EOP dataset**, TDB−TT −0.8 ms
- `/api/v1/calendars/convert?date=2026-08-04&timezone=Europe/Oslo` → 2026-W32-2, +02:00, tzdb 2026c provenance
- `/api/v1/ephemeris/sun/position` → 501 `AST-5010` "planned for Phase 2"
- invalid time → 400 `AST-4001` problem+json; OpenAPI doc 200

## Datasets live (via the worker, all on the shared volume)

| Dataset | Version | Rows | Notes |
|---|---|---|---|
| leap-seconds | iers-2026a | 28 | staged+activated |
| eop-ut1 | 20260804 | 380 | live ser7 fetch; latest UT1−UTC 0.3660 s |
| satellite-elements | 20260804 | 22 | live CelesTrak OMM; freshness 19 fresh / 3 warn |

## Findings

1. `EnsureCreated` is a no-op on a db with existing tables → **all stores must use EF `Migrate()`** (Satellites store fixed mid-deploy).
2. Dataset staging must be **idempotent** (re-run of a failed ingestion updates the staged record — caught in production on the first OMM retry).
3. `UseSetting` in WebApplicationFactory feeds *configuration*, not env vars → the API reads paths from configuration (env vars surface there in production).
4. xunit v3's collection/string `Assert` overloads differ (analyzer-as-error) → string-join/LINQ assertions used in architecture tests.
5. Dockerfiles must copy the **full `src/` tree** (project references) and restore/publish from repo-root-relative paths.
6. DataIngestion rename required a Coolify `dockerfile_location` update (dockerfile apps point at repo paths).

## Deferred (unchanged from plan)

- S0.8 rate limiting (indefinite); metrics (none); uptime-kuma monitor (optional, needs UI action); SPICE ≤ 1″ host gate (recorded); volume-chown strategy for a rootless worker (worker remains root — internal-only).
- Domain phases 2–6 begin with the Sun/Moon Phase 2 (ephemeris 501s become real).
