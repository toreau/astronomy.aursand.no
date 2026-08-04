# Spike S0.5 — Time-scale conversions

**Date**: 2026-08-04 · **Status**: PASS (all validation vectors green)

## Objective

Prove the UTC/TAI/TT/UT1/TDB + JD/MJD converter design with quantified error bands, and choose data sources for leap seconds and UT1−UTC.

## Method

Disposable implementation in `spikes/S05-time-scales` (no packages; engine package referenced only for the `DeltaT_EspenakMeeus` cross-check):

- `Julian`: JD/MJD from UTC `DateTime` (Unix epoch 2440587.5 base).
- `LeapSeconds`: full IERS leap-second table 1972-01-01 (10 s) → 2017-01-01 (37 s), applied by MJD.
- `TimeScales`: UTC→TAI (table), TAI→TT (+32.184 s), TT JD⇄UTC round trip, UT1 = UTC + ΔUT1 (live from USNO `maia.usno.navy.mil/ser7/ser7.dat`, daily), TDB−TT simplified periodic model (Fairhead–Bretagnon 2-term with sin(g+0.01671 sin g) phase term + 1-term sin(L)).
- Validation vectors: Unix epoch JD/MJD, J2000.0 (TT JD 2451545.0), leap-second boundary 2016-12-31→2017-01-01, TT−UTC today (69.184 s), TDB−TT band over 10 years, TT round trip, ΔT cross-check vs Espenak–Meeus.

## Environment

macOS 26.4 (arm64), .NET SDK 10.0.302. Data sources used: `maia.usno.navy.mil` (reachable), embedded leap table (IERS Bulletin C).

## Results

| Vector | Expected | Measured | Verdict |
|---|---|---|---|
| Unix epoch → JD | 2440587.5 | exact | PASS |
| Unix epoch → MJD | 40587.0 | exact | PASS |
| J2000 TT→UTC | 2000-01-01T11:58:55.816Z (TT = UTC + 64.184 s at 2000) | −64.184 s | PASS |
| J2000 UTC→TT | JD 2451545.0000000 | exact | PASS |
| TAI−UTC @2016-12-31 | 36 s | 36 | PASS |
| TAI−UTC @2017-01-01 | 37 s | 37 | PASS |
| TT−UTC @2016-12-31 | 68.184 s | 68.184 | PASS |
| TT−UTC @2026-08-04 | 69.184 s | 69.184 | PASS |
| TDB−TT band (10 y) | ≤ ±1.7 ms | −1.603…+1.583 ms | PASS |
| TDB−TT @J2000 | ≈ 0 | −0.093 ms | PASS |
| TT JD round trip | < 1 ms | 19.7 µs | PASS |
| UT1−UTC live | ser7.dat | +0.3660 s (2026-08-04) | n/a (INFO) |
| ΔT = TT−UT1 (leap chain) | — | 68.82 s vs E-M 64.86 s (divergence 3.95 s) | finding |

## Findings

1. **Double-JD precision**: JD as `double` round-trips to ~20 µs at current epochs (measured 19.7 µs). Adequate for consumer/advanced tiers; reference tier should use TT/JD math with explicit error budget or higher-precision representation if sub-µs needed.
2. **ΔT policy divergence (3.95 s in 2026)**: the leap-chain ΔT (actual leap seconds + UT1) and Espenak–Meeus (tidal smoothing model) disagree. **The shared kernel must pick one policy**: leap-chain for dates ≥ 1972 (our datasets are versioned and deterministic); E-M only as a fallback model for pre-1972 reconstruction. Never mix.
3. **UT1−UTC source**: `ser7.dat` (USNO, daily, plain text) works and is reachable; parse → versioned JSON dataset in the ingestion design. IERS Bulletin A is the authoritative alternative; both are candidates for the ingestion dataset registry.
4. **Leap-second source**: embedded IERS table verified; Noda Time tzdb leap seconds to be evaluated in S0.6 as the production carrier (single-source, versioned with tzdb).
5. **TDB−TT**: simplified 2+1-term model stays within the ±1.7 ms theoretical band; consumer tier may approximate TDB≈TT with an explicit warning (max error 1.7 ms, negligible at consumer angular scales); advanced tier applies the model; reference tier should use the full Fairhead–Bretagnon/SOFA series (SPICE spike S0.3 provides ERFA `tdbtcb` path as fallback).

## Gate verdict

**PASS** — all published vectors met (≤ 1 ms tolerance); UT1 chain live-tested; error bands quantified and documented.

## Decisions feeding

- ADR 5/6: shared-kernel `TimeScaleConverter` contract (leap-chain ΔT policy; versioned EOP + leap tables; TDB model per tier).
- Dataset registry entries: `eop-ut1` (ser7/Bulletin A daily), `leap-seconds` (IERS/Bulletin C, tzdb carrier pending S0.6).
- Accuracy tier table: timing budgets consistent with measured precision.

## Open issues

- Production leap-second carrier decision (embedded table vs Noda Time tzdb) — S0.6.
- Full Fairhead–Bretagnon series vs simplified model for reference tier — S0.3.
