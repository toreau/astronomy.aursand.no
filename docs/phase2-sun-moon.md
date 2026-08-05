# Phase 2 — Sun & Moon (completed)

**Date**: 2026-08-05 · **Status**: COMPLETE — deployed and verified live

## Scope delivered

**Ephemeris calculator adapter** (`EphemerisCalculator`, engine `CosineKitty.AstronomyEngine` 2.1.19 wrapped internally; ADR 3 abstraction):
- Semantic mapping (validated in S0.11 on 2,435 epochs/body): J2000-astrometric = `GeoVector(Aberration.None)` → `EquatorFromVector`; of-date-apparent = `GeoVector(Corrected)` + `Rotation_EQJ_EQD` → `EquatorFromVector`; horizontal = `Horizon` (refraction mapped)
- **RA boundary: `ra × 15` hours→degrees at the adapter** (S0.11 finding applied); degrees are the only angle unit crossing module boundaries (unit-tested)
- `positionType=geometric` rejected with `AST-4001` + documented caveat (engine has no pure-geometric path; its no-aberration path equals astrometric within arcseconds)

**Services** (all consumer-tier, `precision ≠ consumer` → `AST-7002` warning + honest tier labeling):
- Positions (sun/moon, 3 semantic pairs), rise/set/transit (`SearchRiseSet`/`SearchHourAngle`), twilight (−6/−12/−18° via `SearchAltitude`), moon phases (`SearchMoonQuarter` loop, ≤ 366-day cap), moon illumination (`Illumination` + phase-name buckets)
- Every result carries `CalculationMetadata` (engine version + dataset provenance: leap-seconds, eop-ut1)

**Almanac composition** (real): daily sun section (rise/set/noon + all three twilight periods), moon section (rise/set/transit + phase + illumination); UTC-only; `composedFrom: [calendars, ephemeris]`; per-section metadata.

**API** (live): `GET /api/v1/ephemeris/{body}/position|rise-set`, `GET /api/v1/ephemeris/twilight`, `GET /api/v1/ephemeris/moon/phases`, `GET /api/v1/almanac/daily`. Caching conventions: phases `public, max-age=3600`, rise-set/twilight `public, max-age=900`, position `no-cache`.

**Accuracy suite** (`Astronomy.AccuracyTests`, 408 tests): committed Horizons sampled fixtures (sun+moon, 98 epochs each, 1900–2099, captured from the host grid via the worker `sample` subcommand); gates: **sun < 15″** both pairs (measured max 7.3″), **moon per-epoch < 110″ + sample mean < 30″** (measured 20.7″ — the tier ceiling, encoded honestly); embedded USNO event vectors (Oslo sunrise/sunset/civil-twilight ± 30–60 s; 12 moon-phase events ± 2 min).

## Live verification (astronomy.aursand.no, 2026-08-04 Oslo)

| Endpoint | Result | vs reference |
|---|---|---|
| sun/rise-set | rise 03:05:12.56Z, set 19:39:19.0Z, transit 11:23:06.7Z | USNO 03:05/19:39 — exact (S0.2: 03:05:12.563Z) |
| sun/position (of-date) | RA 134.60°, Dec 17.15°, 1.0145 AU | physically correct |
| twilight civil | 02:07:09Z–20:36:38Z | USNO 02:07/20:37 ✓ |
| almanac/daily | full sun section; **astronomical twilight null** (Oslo never reaches −18° in August — correct) | physical ✓ |
| moon/phases | `Cache-Control: public, max-age=3600` | ✓ |
| precision=advanced | `AST-7002` warning | ✓ |
| positionType=geometric | 400 `AST-4001` | ✓ |

## Tests (all green, 445 total)

Unit 14 · Accuracy 408 · Architecture 7 · Integration 5 · Api 11.

## Findings

1. Engine `Equatorial.ra` in hours — now a tested invariant at the adapter boundary (the ×15 conversion is enforced by unit + accuracy tests).
2. xunit v3 `xUnit1026` analyzer rejects unused theory parameters even with `_`-prefix names — `_ = (param1, param2);` satisfies it.
3. `astronomicalTwilight: null` is a legitimate result for high-latitude summer (the event genuinely doesn't occur) — the API must keep nullable event fields and document the semantics (done in the contract).
4. The sampled-fixture capture pattern (worker `sample` subcommand → run_once logs → committed files) works well; the full-grid gate remains the worker `compare`.

## Deferred (unchanged)

Planets (Phase 3), advanced/reference tiers via SPICE (Phase 4), stars/satellites sections in the almanac, moonrise/moonset in the almanac already delivered; HTTP caching beyond headers (CDN/Redis) later.
