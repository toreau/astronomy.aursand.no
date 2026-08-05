# Phase 3 — Planets (completed)

**Date**: 2026-08-05 · **Status**: COMPLETE — deployed, live-verified, full-grid validated

## Scope delivered

**Body registry**: `BodyId` now covers all 9 bodies (sun/moon + mercury, venus, mars, jupiter, saturn, uranus, neptune) — position/rise-set endpoints work for all with zero service changes (adapter + registry only).

**Visibility service** (`GET /api/v1/ephemeris/{body}/visibility`): magnitude (`Illumination.mag`), elongation + visibility status (`Elongation`), constellation (`Constellation` — RA in hours, pinned by a Regulus unit test), altitude/azimuth, `visibleTonight` (planet set after sunset OR rise before sunrise — the documented TimeAndDate-style heuristic), `nakedEyeVisible` (mag ≤ 6.5). Sun/Moon rejected.

**Events service** (`GET /api/v1/ephemeris/events`): Sun-relative oppositions/conjunctions (`SearchRelativeLongitude` — **measured-elongation classification** because the engine's target convention is empirically inverted: search(0) yields the opposition) + Mercury/Venus `max-elongation`; ≤ 366-day window; per-event elongation included for self-consistency; `public, max-age=3600`.

**Almanac**: daily gains a **planets section** (all 8, per-planet rise/transit/set + magnitude + elongation + constellation + visibleTonight + nakedEyeVisible, per-planet metadata); **monthly almanac** (`/api/v1/almanac/monthly?month=yyyy-MM`) with **full per-day planet data** (31 days × 8 planets: rise/transit/set + magnitude + elongation + constellation, magnitude reference instant 12:00 UTC documented) + month-level events; `public, max-age=900`.

**Position endpoint**: geocentric when lat/lon omitted (fixtures-compatible); defaults frame=of-date, positionType=apparent, refraction=none, precision=consumer when params omitted.

## Accuracy validation (full grid, 2,435 epochs per body, 1900–2100 — host worker compare)

| Body | J2000-astrometric mean / max | of-date mean / max | consumer gate ≤ 60″ |
|---|---|---|---|
| sun | 1.3″ / 7.3″ | 1.3″ / 7.2″ | PASS |
| moon | 20.7″ / 105.5″ | 12.2″ / 85.6″ | known ceiling (Phase 2) |
| mercury | 3.2″ / 18.5″ | 3.2″ / 18.7″ | PASS |
| venus | 2.8″ / 20.0″ | 2.8″ / 19.8″ | PASS |
| mars | 2.4″ / 16.1″ | 2.4″ / 16.0″ | PASS |
| jupiter | 4.5″ / 14.1″ | 4.5″ / 14.1″ | PASS |
| saturn | 9.7″ / 22.5″ | 9.7″ / 22.5″ | PASS |
| uranus | 6.9″ / 19.7″ | 6.9″ / 19.7″ | PASS |
| neptune | 10.8″ / 20.5″ | 10.8″ / 20.5″ | PASS |

**Committed CI fixtures**: 49-row samples per planet (1900–2099, captured via the worker `sample` subcommand) → **AccuracyTests now 1,094 tests** (686 new planet gates < 30″ + Phase 2 suite).

## Live verification (astronomy.aursand.no, 2026-08-04)

- venus/position geocentric → RA/Dec matching the full-grid fixture within the validated 20″ envelope; distance 0.772 AU (approaching the mid-August inferior conjunction)
- jupiter/visibility → mag 1.0 at elongation 4.4° (superior-conjunction geometry: dark side toward Earth — physically correct), constellation Cancer, visibleTonight per heuristic
- events jupiter opposition → 2027-02-11T00:17Z, elongation 178.95° (correctly classified)
- almanac/daily → all 8 planets with correct magnitudes (venus −4.3, mars 1.3, …) + elongation + visibleTonight
- almanac/monthly → 31 days × 8 planets, 2 events in August 2026
- events cache header `public, max-age=3600` ✓

## Findings

1. **`SearchRelativeLongitude` target convention is empirically inverted** (search(0) → opposition, search(180) → conjunction) — events are classified by *measured elongation* at the found instant (> 150° opposition, < 30° conjunction), making the mapping robust regardless of the engine's internal convention.
2. **`Constellation` RA is in hours** (Regulus RA 10.139h → Leo; degrees → Cancer) — same ×15 convention as positions; pinned by unit test.
3. At superior conjunction a planet's illuminated side faces away → **magnitude reflects the "new" phase** (Jupiter mag 1.0 at elongation 4.4° is correct physics).
4. Worker compare needed its own body switch for the new planets (the first full-grid run silently mapped mercury/uranus/neptune to the Sun — caught by the first-row debug line; the accuracy suite's committed samples would also have caught it in CI).
5. Phase-fraction was once mislabeled as magnitude in the visibility result (caught live; fixed to `Illumination.mag`).
6. Coolify scheduled-task commands are limited to 255 chars — long chains split across runs (pattern recorded).

## Tests

Unit 21 · Accuracy 1,094 · Architecture 7 · Integration 5 · Api 11 — **all green**.

## Deferred (unchanged)

Planet-planet conjunctions and eclipses/occultations (Phase 6); reference tier via SPICE (Phase 4); stars/satellites almanac sections; advanced-precision honesty remains `AST-7002` warnings (consumer-tier engine chain).
