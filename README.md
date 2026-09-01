# Astronomy API

A public astronomy API — positions, events, and pass predictions for the Sun,
Moon, planets, bright stars, and satellites, with validated accuracy tiers.
Live at **https://astronomy.aursand.no**.

Built as a .NET 10 / C# / ASP.NET Core modular monolith. Consumer-tier positions
come from the Astronomy Engine (VSOP87); the advanced/reference tier uses the
JPL SPICE chain (DE441) with an ERFA IAU2000A correction chain, validated
against JPL Horizons across 1900–2150.

## Endpoints

| Domain | Endpoint | Description |
|---|---|---|
| System | `GET /health/live`, `GET /health/ready` | Liveness and deep readiness (db, schema, datasets, kernels) |
| Time | `GET /api/v1/time/julian-date` | JD/TT/TAI/UT1 conversions |
| Time | `GET /api/v1/time/time-scales` | UTC, TAI, TT, UT1 for an instant |
| Calendars | `GET /api/v1/calendars/convert` | Gregorian date → ISO week date, Julian day number, timezone |
| Calendars | `GET /api/v1/calendars/date-arithmetic` | Date ± days with calendar-aware results |
| Calendars | `GET /api/v1/calendars/range` | Bulk conversion for a date range (≤ 366 days) |
| Ephemeris | `GET /api/v1/ephemeris/{body}/position` | RA/Dec (icrs, of-date, horizontal) + distance |
| Ephemeris | `GET /api/v1/ephemeris/{body}/rise-set` | Rise/set/transit for a date and observer |
| Ephemeris | `GET /api/v1/ephemeris/twilight` | Civil/nautical/astronomical twilight |
| Ephemeris | `GET /api/v1/ephemeris/moon/phases` | Moon quarters in a range |
| Ephemeris | `GET /api/v1/ephemeris/{body}/visibility` | Magnitude, elongation, visibility |
| Ephemeris | `GET /api/v1/ephemeris/events` | Oppositions, conjunctions, max elongations |
| Stars | `GET /api/v1/stars/search` | Cone search over the HYG catalog (bright default) |
| Stars | `GET /api/v1/stars/name` | Lookup by proper name, Bayer/Flamsteed, or HIP |
| Stars | `GET /api/v1/stars/brightest` | Top-N by magnitude, optional constellation |
| Stars | `GET /api/v1/stars/{hip}/position` | Proper-motion-corrected position |
| Stars | `GET /api/v1/stars/{hip}/rise-set` | Analytic rise/set/transit |
| Satellites | `GET /api/v1/satellites/{norad}/position` | SGP4 alt/az/range + subpoint |
| Satellites | `GET /api/v1/satellites/{norad}/passes` | Pass prediction over a window |
| Satellites | `GET /api/v1/satellites/search` | Element lookup by name/NORAD |
| Satellites | `GET /api/v1/satellites/status` | Element dataset freshness |
| Almanac | `GET /api/v1/almanac/daily` | Sun/moon/planet day sheet for an observer |
| Almanac | `GET /api/v1/almanac/monthly` | Full month — or full year via `year=` — of daily sections |

Full request/response examples: **[docs/API.md](docs/API.md)**.

## Quick start

```bash
# Sun position (consumer tier, ICRS astrometric)
curl "https://astronomy.aursand.no/api/v1/ephemeris/sun/position?time=2026-08-05T12:00:00Z&frame=icrs&positionType=astrometric"

# Jupiter at reference precision (SPICE/DE441, <= 1")
curl "https://astronomy.aursand.no/api/v1/ephemeris/jupiter/position?time=2026-08-05T12:00:00Z&frame=icrs&positionType=astrometric&precision=reference"

# Bright stars near a point
curl "https://astronomy.aursand.no/api/v1/stars/search?ra=101.287&dec=-16.716&radius=3&maxMagnitude=6.5&limit=5"

# ISS passes over Oslo tonight
curl "https://astronomy.aursand.no/api/v1/satellites/25544/passes?date=2026-08-05&latitude=59.9&longitude=10.7&minElevation=10"
```

Every response carries `metadata` with the dataset versions, the algorithm
chain, and any warnings used to compute it.

## Accuracy tiers

| Tier | Position source | Claim | Validated era |
|---|---|---|---|
| `consumer` | Astronomy Engine 2.1.19 | measured ≤ 22.5″ vs Horizons | 1900–2100 (full fixture grid) |
| `advanced` | SPICE DE441 + ERFA | ≤ 10″ | 1900–2150 |
| `reference` | SPICE DE441 + ERFA IAU2000A | **≤ 1″** vs JPL Horizons | 1900–2150 |

Validation evidence (host gates against the JPL Horizons fixture grid,
14,022 epochs):

- **Reference q1 (J2000 astrometric)**: all 9 bodies ≤ 0.71″ (moon, pre-1972
  historical-ΔT era; typically ≤ 0.1″).
- **Reference q2 (of-date apparent)**: means ≤ 0.28″, maxima ≤ 1.5″.
- **Reference horizontal**: ERFA C2T chain fed by EOP C04 (UT1 + polar
  motion) — agrees with the engine-validated transit to 0.001°.
- **Stars**: HYG v3.8 catalog vs the Yale Bright Star Catalog — median 0.45″
  over a 50-star bright sample; canonical spot checks ≤ 0.51″.
- **Satellites**: SGP4 (One_Sgp4) vs the official Vallado verification suite —
  bit-exact; cross-propagator agreement 0.14 km over 24 h; pass rise/set
  self-consistency 0.01°.

Frames: `icrs` (J2000), `of-date` (true equator/equinox of date), `horizontal`.
Pre-1900 and post-kernel-coverage epochs are rejected with guidance; the
reference tier is validated from 1900-01-01.

## Data provenance

Everything is dataset-driven through a versioned registry (stage/activate/
rollback), surfaced in every response's `metadata`:

- `leap-seconds`, `eop-ut1` (Bulletin A), `eop-c04` (IERS 14 C04 with polar
  motion) — time scales and the horizontal chain
- `star-catalog-hyg` — HYG v3.8 (119,625 stars, J2000 + proper motion)
- `satellite-elements` — CelesTrak OMM elements (SGP4 propagation)
- SPICE kernels on the deployment volume: `de441.bsp` (3.3 GB, JPL), `de440.bsp`,
  `de440s.bsp`, `de440s_plus_MarsPC.bsp`, `naif0012.tls`, `pck00010.tpc`,
  `earth_assoc_itrf93.tf` — each referenced as `spice:<file>` in metadata

## Errors

Errors follow RFC 7807 (`application/problem+json`):

| Code | Meaning |
|---|---|
| `AST-4001` | Invalid request (validation) |
| `AST-5000` | Internal error |
| `AST-5010` | Feature not implemented in this phase |
| `AST-5030` | Reference tier unavailable (kernels/native lib missing) |
| `AST-5031` | Star catalog dataset not ingested |
| `AST-5032` | Satellite element dataset not ingested |
| `AST-7002` | Tier warning: endpoint uses the consumer chain |
| `AST-7003` | Horizontal reference chain degraded (EOP C04 absent) |
| `AST-7004` | Satellite TLE stale (> 72 h) — accuracy degrades |

## Architecture

```
Astronomy.Api (ASP.NET Core, minimal APIs)
  └── modules: Ephemeris · Stars · Satellites · Time · Calendars · Almanac
  └── Astronomy.Infrastructure (dataset registry, storage providers [SQLite dev / PostgreSQL prod], loaders)
  └── Astronomy.SharedKernel (contracts, coordinates, time scales)
Astronomy.DataIngestion (worker CLI: ingest jobs + host verification gates)
```

- **Native libraries**: `libcspice.so` (CSPICE N66) and `liberfa.so` (SOFA/
  ERFA) are built into the deployment containers; all native calls are
  serialized behind a global lock (the CSPICE build is not thread-safe) and
  guarded by `erract RETURN` (its default error action aborts the process).
- **Worker tools** (run via scheduled tasks or ad hoc): `compare-spice`
  (reference gate), `star-gate`, `sat-gate`, `ingest <dataset>`, `naif`
  (kernel refresh), `fixtures` (Horizons grid), `omm`.
- **Weekly maintenance job**: kernel refresh → reference gate → EOP C04 →
  star-catalog gap-fill, throttled to 24 h for safety.
- **Satellite elements**: refreshed daily by a scheduled task
  (`omm refresh` — CelesTrak stations, auto-versioned by UTC date) and weekly
  as part of `naif`; TLEs older than 72 h surface an `AST-7004` warning.
  Activation hot-reloads in the API within ~60 s (no restart needed).

## Development

Prerequisites: .NET 10 SDK.

```bash
dotnet restore Astronomy.slnx
dotnet build Astronomy.slnx -c Release
dotnet test Astronomy.slnx -c Release
```

- The full suite (1,366 tests: 1,122 accuracy, 168 unit, 64 API, 6
  architecture, 6 integration) runs in CI with no network access — the
  accuracy fixtures (Horizons samples, the Vallado SGP4 verification set) are
  committed.
- The SPICE/ERFA code paths require the native libraries and kernels and are
  exercised by the deployed host gates, not by CI.
- The API migrates its own schema at startup (registry + satellite elements),
  so it no longer depends on the worker having run first.

## Deployment

Production runs on Coolify (`astronomy.aursand.no`) as two apps from the same
repo (dockerfile build pack), sharing a `/data` volume for kernels and datasets:

- `api` (rootless, uid **150** — `useradd --uid 150`) — the HTTP service
- `worker` (root) — ingestion jobs and gates

The dataset **registry** lives in PostgreSQL (`astronomy-db`, Coolify) in
production and SQLite in dev/tests — see "Storage" below.

On `main`, CI invokes the trusted central builder (`container-build-attest.yml`),
which builds a single **arm64** image (`ghcr.io/toreau/astronomy-api`, `main-<sha>`),
generates SLSA provenance and an SPDX SBOM, and returns the digest. CI then dispatches
`app-image-pushed` `{app, sha, digest}` to k8s-research. Astronomy is **damped** there
(`attestation: false`): promotion authorization uses trusted metadata, the expected image,
and exact digest existence/binding. Provenance is produced by the trusted builder but is
not used in Astronomy's promotion authorization. The k8s-research copy is not
admission-enforced; current admission scope covers the separate reference application,
frosta-historielag. Production itself builds from git via Coolify; k8s-research receives
the trusted-builder image/digest for the separate local GitOps deployment. The repository
is public.

Environment: `ASTRONOMY_DB_PROVIDER` (`sqlite`|`postgres`, default `sqlite`),
`ASTRONOMY_DB_PATH` (default `/data/astronomy.db`, SQLite),
`ASTRONOMY_DB_CONNECTION` (Postgres, Npgsql keyword form),
`ASTRONOMY_DATA_ROOT` (default `/data`), `ASTRONOMY_KERNEL_PATH` (default
`/data/kernels`). The Dockerfiles build CSPICE + ERFA in a dedicated stage
(one-time apt cost, layer-cached). See `docs/spikes/S11-deploy.md` for depth.

## Documentation

- `CHANGELOG.md` — dated change history (Core · Bugs fixed · Performance ·
  Infrastructure & ops · Tests · Documentation · Dependencies)
- `docs/API.md` — full request/response reference
- `docs/phase1-foundation.md` … `docs/phase7-satellites.md` — phase reports
  with validation evidence
- `docs/spikes/S02–S11` — technology spikes (engine selection, SPICE, SGP4,
  time scales, tzdata, stars, SQLite, OMM, deployment)
- `docs/adr/` — architecture decision records

## Status

All master-plan phases are complete (time/calendars, sun/moon, planets,
reference tier, bright stars, satellites). Documented deferrals: satellite
ICRS (RA/Dec) positions, the 1849–1900 reference era, star of-date unification
onto the ERFA chain, and API rate limiting.
