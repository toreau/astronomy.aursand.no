# Phase 7 — Satellite propagation (pass prediction)

Status: **complete** · Date: 2026-08-05 · Commit: HEAD of main

## Goal

Implement the satellite propagation feature (the last stub in the codebase —
`FeatureNotImplementedInPhaseException("satellite propagation", ...)`): SGP4
positions, pass prediction, search, and status, per ADR 9 (OMM storage +
One_Sgp4 behind an abstraction).

## What was delivered

### Propagator (S0.4 choice: One_Sgp4 1.1.0)

- `IOrbitalPropagator` + `OneSgp4Propagator`: OMM mean elements → TLE lines 1/2
  (computed checksums, exponent-style nddot/bstar fields, 1-based epoch day —
  two TLE-format bugs fixed during iteration) → TEME km (WGS-72). Raw-TLE entry
  (`PropagateTle`) for the verification suite.

### Frame math (`SatelliteFrames`)

- GMST (IAU-1982 from UT1 via the platform's `TimeScaleConverter`), TEME→PEF,
  WGS-72 geodetic subpoint (Bowring iteration), observer-topocentric alt/az/
  range with Bennett refraction. TEME pole offsets (~9″) below pass-prediction
  needs, documented.

### Pass prediction (`SatellitePassPredictor`)

- Coarse scan (step 10–300 s, default 30 s) with horizon-crossing bisection
  (altitude is smooth), transit = max-altitude sample, direction from the
  altitude slope at rise. Window cap 7 days (SGP4 validity).

### Service + API

- `ISatelliteService`: position, passes, search, status. **AST-5032** when the
  `satellite-elements` dataset is absent; **AST-7004** TLE-staleness warning
  (> 72 h, the S0.4 freshness expectation); metadata
  `sgp4:onesgp4-1.1.0:<variant>` + dataset refs.
- Endpoints: `/api/v1/satellites/{norad}/position|passes`,
  `/api/v1/satellites/search|status`. Observer required (400 without lat/lon).
- The elements access reads the active dataset version from the store.

## Gate results — SAT GATE PASS (`sat-gate`)

- Freshness: dataset 20260804, 22 elements, ISS TLE age 41.2 h (≤ 72 h).
- **Cross-propagator: max deviation 0.14 km over 24 h** (One_Sgp4 vs SGP.NET,
  gate ≤ 5 km).
- **Pass self-consistency: altitude at the computed rise/set = 10.00°**
  (minElevation) to 0.01°; first ISS pass 13:17–13:22, max elevation 16.6°.
- The gate caught two of its own bugs during iteration (see Findings).

## Accuracy suite — Vallado SGP4 verification, bit-exact

- `SGP4-VER.TLE` + `tcppver.out` committed into `Astronomy.AccuracyTests`
  (140 KB fixture).
- 28 in-envelope cases propagated through the production `PropagateTle` path:
  **all within 1.5 km; the deep-space cases match the reference exactly**
  (0.0 km). Excluded per S0.4: 33333–33335 (intentionally broken TLEs), 11801
  (One_Sgp4 parser quirk — known open issue), and the 20413 long-arc block
  (t ~ 1.84 M min, outside the SGP4 envelope).
- Fixed an S0.4-inherited loader bug: the per-case header TLE lines
  (`2 xxxxx … start stop step`) were parsed as data rows.

## Live verification (astronomy.aursand.no)

- ISS position 2026-08-05T12:00Z from Oslo: alt −26.55°, az 93.69°, range
  6,559 km, subpoint (24.17°N, 81.29°E, 422.7 km), TLE age 40.9 h, no
  warnings; local computation matches the live subpoint exactly.
- ISS passes over Oslo Aug 5: 10:06 (max 12.6°), 11:41 (max 20.0°), 13:17
  (max 16.6°) — ~90-min cadence.
- Search: "iss" → ISS (NAUKA) + ISS (ZARYA). Status: 22 elements, warn=22.

## Findings

1. **TLE line building**: the epoch day is 1-based day-of-year + fraction
   (`DayOfYear + fraction`, not −1); nddot/bstar use the exponent notation
   (`±mmmmm±ee`, e.g. `-11606-4`), not plain decimals; `SignedDecimal` widths
   must be derived from the field width (the decimal point survives a zero
   `TrimStart`).
2. **The sat-gate caught two of its own bugs**: SGP.NET's `FindPosition`
   takes minutes since the TLE epoch (not since now), and the `Predict`
   argument order was misused at the gate call site (minElevation vs step).
3. The api container cannot run the `omm` CLI (the ingestion ctor migrates the
   db — write access required); the read-only satellite endpoints and
   `sat-gate` work from it (the API process opens the db read-write fine).
4. The Vallado `tcppver.out` case blocks begin with a TLE header line
   (`2 xxxxx … start stop step`) that must be skipped when parsing rows.

## Tests

- 11 satellite unit tests (TLE building/checksums, propagation sanity, GMST
  J2000 anchor, geodetic round-trip, zenith topocentric, position/passes/
  search service paths, unknown-norad, window cap).
- 28 SGP4 accuracy cases (Vallado, CI-runnable — pure managed).
- 3 new API tests (AST-5032 × 2, AST-4001).
- All suites: 1,223 tests green.

## Out of scope (candidates)

- ICRS RA/Dec for satellites (TEME→J2000 via the Ephemeris module's ERFA
  surface — low consumer value for pass prediction).
- Fresh element ingestion cadence (the `satellite-elements` dataset updates
  via `omm fetch`; a scheduled refresh could join the weekly job).
- 11801 parser tolerance for verification-style TLEs (open S0.4 issue).
