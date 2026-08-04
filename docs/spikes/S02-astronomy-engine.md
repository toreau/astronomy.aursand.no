# Spike S0.2 — Astronomy Engine precision and semantics (partial)

**Date**: 2026-08-04 · **Status**: PARTIAL — event validation passed; position (RA/Dec) validation blocked by network

## Objective

Quantify consumer/advanced-tier viability of `CosineKitty.AstronomyEngine` 2.1.19: Sun/Moon/planet positions vs JPL Horizons, rise/set/transit/twilight/moon-phase times vs USNO, thread safety, .NET 10 runtime behavior.

## Method

Two comparison harnesses in `spikes/S02-astronomy-engine`:

1. **Event harness (`rts`, `phases`)**: fetches USNO API (`aa.usno.navy.mil/api/rstt/oneday`, `.../moon/phases/year`) at `tz=0` for Oslo (59.9139N, 10.7522E, 25 m) and compares against engine `SearchRiseSet`, `SearchHourAngle(...).time`, `SearchAltitude(-6°)`, `SearchMoonQuarter`.
2. **Position harness (`generate`, `compare`)**: batch-fetches JPL Horizons OBSERVER ephemerides (`CENTER=500@399`, quantities 1/2/9 = astrometric J2000 RA/Dec, apparent-of-date RA/Dec, range AU) into CSV fixtures; compares engine `GeoVector(Aberration.None)` → `EquatorFromVector` (J2000 astrometric) and `GeoVector(Aberration.Corrected)` + `Rotation_EQJ_EQD` → `EquatorFromVector` (of-date apparent), with great-circle angular separation and distance error.

## Environment

- macOS 26.4 (arm64), .NET SDK 10.0.302, Docker 29.5.3
- Network: **ssd.jpl.nasa.gov and naif.jpl.nasa.gov unreachable** (TCP timeout, HTTP and HTTPS, IPv4) from the dev network; aa.usno.navy.mil, celestrak.org, iers.org, datacenter.iers.org, github.com, cdsarc.cds.unistra.fr reachable.

## Results

### API-surface verification (done)

- Package: `CosineKitty.AstronomyEngine` 2.1.19 (owner don_cross, MPL-2.0). CLR namespace is **`CosineKitty`** (class `Astronomy`) — XML docs misleadingly use `CosineKitty.Astronomy.*` member ids.
- Verified signatures: `GeoVector(Body, AstroTime, Aberration)`, `EquatorFromVector(AstroVector)`, `Rotation_EQJ_EQD(AstroTime)`, `SearchRiseSet(Body, Observer, Direction, AstroTime, limitDays, altitudeDeg)`, `SearchAltitude(Body, Observer, Direction, AstroTime, limitDays, altitudeDeg)`, `SearchHourAngle(Body, Observer, hourAngleDeg, AstroTime, direction) → HourAngleInfo.time`, `SearchMoonQuarter(AstroTime) → MoonQuarterInfo.quarter` (**int**: 0=new,1=first,2=full,3=third), `MoonPhase`, `Horizon`, `DeltaT_EspenakMeeus(decimalYear)` — note: takes a decimal **year**, not JD.
- `AstroTime`: ctor from `DateTime` (interpreted as UTC); fields `tt` (TT JD), `ut`; `ToUtcDateTime()`.
- Builds clean on .NET 10, netstandard2.0 asset.

### Event validation vs USNO (passed) — Oslo, 2026-08-01..08-08

| Event set | N | mean |Δ| | max |Δ| | verdict |
|---|---|---|---|---|---|---|
| Sun rise/set/transit/civil-twilight | 20 | 0.2 min | 0.5 min | PASS (≤ ±1 min consumer) |
| Moon rise/set/transit | 15 | 0.3 min | 0.5 min | PASS |
| Moon phase times (all 50 phases of 2026) | 50 | 0.8 min | 1.3 min | PASS (≤ ±2 min consumer) |

Engine's rise/set default chain (altitude 0°, standard refraction) matches USNO to the minute; no systematic offset observed.

### Position validation vs Horizons (blocked)

The `generate`/`compare` harness is built and compiles, but no fixtures could be fetched: `ssd.jpl.nasa.gov` times out from the dev network (both IPv4/IPv6, HTTP/HTTPS). **Mitigation**: run `generate` from the Coolify host network during S0.11 (verify reachability there); fixtures commit into `spikes/S02-astronomy-engine/fixtures/` for reuse by `Astronomy.AccuracyTests`.

### Thread safety

Not yet exercised (position harness blocked); retest in the S0.11 run or with synthetic fixtures. Engine is stateless/pure — low risk, verify anyway.

## Gate verdict

**PARTIAL** — event-level validation PASSes at consumer tier (Sun/Moon ±1 min, phases ±2 min). Position-level precision gate (≤ 60″ consumer, ≤ 10″ advanced) **unverified** pending JPL-reachable network. Advanced tier decision (engine vs SPICE) stays open until the position run completes.

## Decisions feeding

- ADR 3 (library strategy): engine viable for consumer tier events; keep `IEphemerisCalculator` abstraction.
- Accuracy tier table: event accuracies now have measured evidence (Sun/Moon events ~0.5 min).
- Shared kernel: reuse `AstroTime`-equivalent mapping — engine treats `DateTime` as UTC; shared-kernel `AstronomicalTime` must convert explicitly.

## Open issues

- JPL unreachable from dev network — verify from Coolify host (S0.11); if also blocked, evaluate alternate networks/mirrors (GitHub-hosted DE kernels are candidates — integrity via checksums to be established).
- Full-grid fixture generation (1900–2100 @ 30d) deferred to the first reachable network.
- Moon phases: USNO rounds to minutes; residual ≤ 1.3 min includes rounding.
