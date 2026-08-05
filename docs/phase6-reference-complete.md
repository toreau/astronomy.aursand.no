# Phase 6 — Complete the reference tier (ERFA correction chain)

Status: **complete** · Date: 2026-08-05 · Commit: HEAD of main

## Goal

Close the three documented reference-tier gaps: of-date reference positions
(previously 400 AST-4001), pre-1972 reference positions (previously 400), and
horizontal at reference (previously the engine chain + AST-7003 warning). All
three are now served by the SPICE + ERFA chain and gated against the Horizons
fixtures.

## What was delivered

### ERFA integration

- `liberfa` built in the `cspice` Docker stage (meson/ninja, the S0.3 recipe)
  and shipped in both images as `/app/liberfa.so`.
- `Erfa.cs` P/Invoke surface: `eraPnm06a` (IAU 2006/2000A bias-precession-
  nutation), `eraC2t06a` (celestial-to-terrestrial), `eraDtdb` (kept from the
  spike). Two-part Julian dates per SOFA practice.

### of-date reference positions

- `IReferenceEphemeris.OfDatePosition`: J2000 `LT+S` vector rotated by
  `eraPnm06a` → true equator/equinox of date (matches Horizons' IAU2000A
  reduction).
- `frame=of-date&positionType=apparent&precision=advanced|reference` → 200,
  algorithm `N66:of-date-apparent:erfa`.

### pre-1972 reference positions (historical ΔT)

- `HistoricalDeltaT`: Espenak–Meeus (2006) piecewise polynomials (1900–1971),
  replacing SPICE's extrapolated pre-1972 UTC→ET (the ~40 s error source).
- `ET = TT + (TDB−TT neglected, ~1.6 ms)` for 1900–1971; era floor now
  **1900-01-01** (fixture start). 1972+ keeps the leap-second path.

### horizontal reference (ERFA C2T + EOP C04)

- `eop-c04` ingest extended with polar motion x/y (4-column CSV); the C04
  refresh moved ahead of the 24 h gate throttle (it is a daily product).
- `IReferenceEphemeris.HorizontalPosition`: J2000 `LT+S` vector → `eraC2t06a`
  (UT1 from C04 interpolation, polar motion x/y) → topocentric alt/az with
  Bennett refraction. `CanDoHorizontal` = C04 loaded.
- `frame=horizontal&precision=advanced|reference` → 200 via the ERFA chain,
  algorithm `N66:horizontal:erfa-c2t`, metadata gains the `eop-c04` dataset
  ref; the **AST-7003 warning is gone** when C04 is present (degrade-with-
  warning retained when it is absent).

## Gate results — REFERENCE GATE PASS (full 1900–2100, q1 + q2)

compare-spice now runs the entire fixture range (no pre-1972 skip) with two
legs. Historical ΔT validation: the moon's 1900-era error collapsed from 27″
to **0.71″**; all pre-1972 q1 maxima ≤ 0.71″.

| body    | q1 mean | q1 max | pre1972 max | q2 mean | q2 max |
|---------|---------|--------|-------------|---------|--------|
| sun     | 0.016″  | 0.066″ | 0.066″      | 0.089″  | 0.230″ |
| moon    | 0.070″  | 0.714″ | 0.714″      | 0.094″  | 0.513″ |
| mercury | 0.017″  | 0.117″ | 0.117″      | 0.091″  | 0.477″ |
| venus   | 0.016″  | 0.076″ | 0.076″      | 0.092″  | 0.516″ |
| mars    | 0.014″  | 0.055″ | 0.055″      | 0.092″  | 0.392″ |
| jupiter | 0.026″  | 0.075″ | 0.075″      | 0.098″  | 1.326″ |
| saturn  | 0.032″  | 0.068″ | 0.068″      | 0.098″  | 1.174″ |
| uranus  | 0.195″  | 0.391″ | 0.371″      | 0.282″  | 1.500″ |
| neptune | 0.032″  | 0.118″ | 0.063″      | 0.079″  | 1.192″ |

- **q1 (J2000 astrometric): ≤ 1″ everywhere 1900–2100** (worst moon 0.71″
  pre-1972, within the documented 3″ historical-ΔT tolerance).
- **q2 (of-date apparent): means ≤ 0.28″, maxima ≤ 1.5″** (outer planets at
  isolated epochs; gate 2″). The maxima reflect small reduction-definition
  differences vs Horizons' CIO-based q2 (both sides use IAU2000A nutation;
  classical-equinox vs CIO frame reductions differ at the ~1″ level for the
  distant bodies) — documented, not a position error (q1 agrees to ≤ 0.4″).

## Live verification (astronomy.aursand.no)

- of-date reference: moon → `N66:of-date-apparent:erfa`, 200.
- 1905 reference position: 200 (was 400).
- horizontal reference: sun at the engine-validated transit (11:23:13 UTC) →
  **alt 46.992°, az 179.998°** — identical to the engine to 0.001°, zero
  warnings, `N66:horizontal:erfa-c2t`, datasets include `eop-c04`.
- /ready: kernels ok.

## Findings

1. **JD-epoch vs J2000-seconds bug** in the first horizontal implementation:
   `ttMinusUtc` used `ttJd×86400 − …` (seconds since the JD epoch, ~2.1e11)
   instead of `et − (utc − J2000).TotalSeconds` — the resulting UT1 was
   garbage (~59° azimuth error, caught by the transit cross-check). Also fixed
   the TT−UT1 sign (`− dut1`, not `+`).
2. **EOP C04's daily cadence shouldn't sit behind the gate throttle** — moved
   the refresh ahead of the 24 h marker so every naif run refreshes it.
3. The api's reference-ephemeris singleton caches the loaded C04 samples at
   first use — a dataset re-ingest requires an api restart (restart queued
   via the Coolify API after the x/y re-ingest).
4. ERFA `eraPnm06a`/`eraC2t06a` return void and fill out-arrays (the spike's
   `eraEpv00` returns int) — the P/Invoke shapes differ per function.

## Tests

- 9 new unit tests (historical ΔT anchors 1900/1920/1950/1972, era bounds,
  C04 interpolator midpoint/nearest/empty, of-date ERFA path, of-date
  requires apparent, horizontal ERFA path + metadata, horizontal degrade
  without C04).
- 3 API tests flipped (of-date/pre-1972/horizontal at reference → 503 in CI
  where kernels are absent).
- All suites: 1,181 tests green.

## Out of scope (candidates for later)

- Star of-date positions still use the engine rotation (difference vs ERFA
  ~0.3″ — invisible for stars; unifying would share the ERFA surface).
- 1849–1900 reference positions (older EM2006 segments + fixture extension).
- Official NAIF toolkit build comparison.
