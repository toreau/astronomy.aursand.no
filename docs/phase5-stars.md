# Phase 5 — Bright stars (star catalogue queries)

Status: **complete** · Date: 2026-08-05 · Commit: HEAD of main

## Goal

Implement the master plan's star catalogue feature (the module existed as a stub
since Phase 1, throwing `FeatureNotImplementedInPhaseException`): cone search over
a star catalogue plus the natural companions (name lookup, star position with
proper motion, analytic rise/set/transit, brightest list) — self-contained,
dataset-driven, following the platform's data discipline.

## What was delivered

### Dataset: `star-catalog-hyg` (v38)

- Source: HYG v3.8 (`hyg_v38.csv.gz`, 13.6 MB, 119,626 rows, from the archived
  astronexus/HYG-Database repo — the project moved to Codeberg after v4; the
  GitHub archive is stable). Columns used: HIP, IAU proper name, Bayer/Flamsteed
  (combined + split), J2000 RA/Dec, pmra/pmdec (mas/yr, Hipparcos cos(dec)-scaled
  convention), distance (pc → ly), Vmag, spectral type, constellation abbrev.
- Ingest job `ingest star-catalog` (worker): gzip fetch → RFC-4180 CSV parse →
  normalize to 13 columns → validate (≥100k rows, Sirius spot check, magnitude
  range) → stage+activate version `v38`. 119,625 stars kept (Sol dropped).
- Consumption: `StarCatalog` (SharedKernel) + `StarCatalogLoader` (Infrastructure)
  load the active version into an immutable array at first use; absent dataset →
  **503 AST-5031** (no silent fallback). Added to `DatasetNames` + worker
  `dataset status`.
- The weekly `naif-kernels` refresh job gained a **one-time gap-fill** for the
  catalog (runs before the 24 h throttle — it only acts when the dataset is
  missing; the HYG catalog is static, so no weekly re-fetch).

### StarService (Astronomy.Modules.Stars)

- **Cone search** (`/stars/search`): angular-separation filter (radius ≤ 180°,
  max magnitude, limit ≤ 100), results sorted by Vmag.
- **Name lookup** (`/stars/name`): IAU proper name (case-insensitive substring),
  Bayer/Flamsteed designation, or HIP number.
- **Star position** (`/stars/{hip}/position`): J2000 catalog coordinates
  propagated by proper motion (RA term uses the cos(dec)-scaled pmra; verified
  against the catalog's radians/yr fields); `frame=of-date` precesses via the
  engine's EQJ→EQD rotation; `frame=horizontal` uses the engine's Horizon.
- **Rise/set/transit** (`/stars/{hip}/rise-set`): analytic hour-angle solution
  (cos H = (sin alt − sin lat sin dec)/(cos lat cos dec)) with the standard
  −0.5667° refraction threshold; circumpolar detection; date-shift handling.
- **Brightest** (`/stars/brightest`): top-N by Vmag with optional constellation
  filter.
- Constellation abbreviations → full IAU names via a static 88-entry table.

### API

`/api/v1/stars/search|name|brightest`, `/api/v1/stars/{hip}/position|rise-set`
with the platform conventions (AST-4001 validation, AST-5031 for a missing
catalog, cache headers, metadata with `star-catalog-hyg` dataset refs and a
`proper-motion` algorithm ref).

## Gate results — STAR GATE PASS (worker `star-gate`)

- **Spot checks**: Sirius, Canopus, Arcturus, Vega, Capella, Rigel, Betelgeuse,
  Antares vs canonical Hipparcos/BSC values — all ≤ 0.51″ (Antares 0.02″), mags
  within 0.2 (Antares is a variable; Vmag 1.06 in HYG).
- **BSC cross-validation**: Yale Bright Star Catalog (CDS V/50, `catalog.gz`,
  byte-exact offsets from the ReadMe) — 50 bright stars (Vmag < 3) matched to
  HYG by nearest neighbour: **50/50 matched, median 0.45″, p95 1.66″, max
  15.2″ (1 double-star outlier), gate median ≤ 1″ + ≤ 5 over 5″ → PASS**.

## Live verification (astronomy.aursand.no)

- Cone search around Sirius: Sirius first (RA 101.283 — proper-motion-corrected
  from J2000 101.287 by −546 mas/yr × 26.6 yr ✓); brightest top-5 = Sirius,
  Canopus, Arcturus, Rigil Kentaurus, Vega (correct order).
- Rise/set self-consistency: **altitude at the computed rise = −0.568°**
  (threshold −0.5667°), at transit = 13.347° (theory 13.35°) — the analytic
  events are exact. Vega circumpolar from Oslo (dec 38.8 > 90 − 59.9) ✓.

## Findings

1. **The engine's `Horizon` takes RA in HOURS, not degrees.** Its Equatorial
   convention is hours; `EphemerisCalculator.Horizontal` passed degrees — so
   **all horizontal alt/az outputs (position endpoints, almanac, visibility)
   have been wrong since Phase 2**. Fixed (`ra / 15.0`) in both the ephemeris
   calculator and the new star path; pinned by a zenith test (star at
   LST=RA, dec=lat → alt = 90°). Verified live: the engine-validated sun
   transit (11:23 UTC) yields az = 180.00° exactly. The Phase 2/3 accuracy
   tests never caught this because they assert RA/Dec, not alt/az.
2. **The engine's `SiderealTime` returns GMST in HOURS (0–24)**, not degrees
   (found during star rise/set debugging; the initial star transit times were
   off until the ×15 conversion).
3. **Transit offset bug** during star rise/set development: the formula must be
   time *until* the hour angle wraps to 0 (`(360 − HA) % 360 / degPerDay`), not
   HA/degPerDay.
4. **HYG v3.8 details**: `dist` is in parsecs (not ly); `pmra` is the
   cos(dec)-scaled Hipparcos convention (matches `pmrarad` exactly); `con` is
   the constellation abbreviation; Sol is present (dropped by the dist>0
   filter); Antares is a variable (Vmag 1.06); HIP 71683 is α Cen, not Antares.
5. **CDS layout**: V/50's data file is `catalog.gz` (not `catalog.dat`) under
   `/ftp/cats/V/50/`; the older paths 404.
6. **Coolify worker-app scheduled-task endpoint** was down for the entire phase
   (UI "Run" button used to trigger the ingest; the api-app endpoint + name-based
   `diagnose_app` work).

## Tests

- 18 star unit tests (catalog parse/load, cone search incl. radius/mag/limit,
  name search, proper-motion math, rise/set ordering + circumpolar, brightest,
  constellation table, **zenith horizontal pin**).
- 4 new API tests (AST-5031 × 3, AST-4001).
- All suites: 1,169 tests green. Host gates provide the live evidence
  (established pattern); the full catalog is not committed to the repo.

## Out of scope (Phase 6 candidates)

- of-date reference positions via ERFA nutation/precession (stars already
  precess via the engine; the SPICE-era ERFA chain would unify).
- Pre-1972 reference positions (historical ΔT).
- Variable-star light curves, star charts, and the full 120k-star queries
  beyond the bright default (maxMagnitude 6.5).
