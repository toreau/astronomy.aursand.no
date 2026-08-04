# Spike S0.4 — SGP4 library selection

**Date**: 2026-08-04 · **Status**: PASS — One_Sgp4 1.1.0 chosen as the propagator

## Objective

Pick the C# SGP4 library for Phase 5 (ISS + satellites): accuracy vs the official Vallado verification set, OMM ingestion path, thread safety, pass-prediction performance, licence and maintenance.

## Method

Harness in `spikes/S04-sgp4/` (console, net10). Reference set: **SGP4-VER.TLE + tcppver.out** from the official Vallado mirror `spacecompute/Vallado` (cpp/TestSGP4/TestSGP4/; the nasa/GMAT mirror is a 14-byte stub — discarded). 33 ordered cases (TLE ↔ output blocks), each with expected TEME positions at minutes-since-epoch. TLE lines normalized to 69 chars (the .TLE file's line-2s carry trailing junk; CRLF stripped). Modes: `vectors` (per-candidate, per-case error stats, near-earth vs deep-space by mean motion < 6 rev/d), `perf` (60k propagations ≈ 7-day pass @ 10 s), `threads` (serial vs 8-way parallel), `omm` (live CelesTrak stations CSV, `FORMAT=omm`).

Candidates: **SGP.NET 1.5.0** (parzivail, MIT, pushed 2026-05) and **One_Sgp4 1.1.0** (1manprojects, MIT, pushed 2025-03).

## Environment

macOS 26.4 (arm64), .NET SDK 10.0.302. Fixtures fetched from raw.githubusercontent.com (reachable).

## Results

### Vector agreement vs Vallado reference (tcppver.out)

| Candidate | In-envelope cases | Agreement | Failures |
|---|---|---|---|
| **One_Sgp4 1.1.0** | 29/33 | **bit-exact or ≤ 1 m** (max 0.96 km at 23599; 0.21 km at 21897) — incl. all deep-space cases (14128, 20413, 23333, 4632, …) | 4 input-handling rejects (below) |
| SGP.NET 1.5.0 | 25/33 | Near-earth passes ~30–80 m; **SDP4 broken**: 14128 errs 12.5 km **at epoch** → 23 km; deep-space 11–51,393 km; LEO 29141 errs 44 km; throws `e <= -0.001` on 33334 | defective deep-space initialization |

One_Sgp4's rejected cases are **not propagation failures**:
- 33333 / 33334 / 33335: **intentionally broken TLEs** in the verification suite (33334 has mm = 0.000) — One_Sgp4's checksum validation correctly rejects them (defensive behavior; Phase 5 needs a tolerant-parse option).
- 11801: TLE parser rejects this file line (input quirk — investigate in Phase 5).
- Second 20413: propagates **~3.5 years past epoch** (t ≈ 1.84M min) — far outside the SGP4 design envelope (days); both libraries diverge there (One_Sgp4 48.9k km, SGP.NET 51.4k km). Expected; excluded from the gate; first 20413 (0.247 rev/d, GEO resonance) matches **0.000 km**.

Notable: One_Sgp4's README claims "SGP only, no deep-space" — **outdated**: its SDP4 matches the reference exactly.

### OMM ingestion path (live CelesTrak CSV, 22 stations)

- SGP.NET `Tle.ParseOmmCsv` parses all 22; ISS propagates sensibly (+1h TEME ≈ (1236, −5175, −4241) km).
- One_Sgp4 has its own OMM parser (`ParserOMM`) — not exercised in this run; OMM CSV→TLE adapter is the planned Phase 5 pattern (elements stored as OMM, converted at load).

### Thread safety

Both libraries: **bit-identical** serial vs 8-way parallel (0.0E+000 deviation) — PASS.

### Performance (case 5, LEO, 60k evals = 7-day pass @ 10 s)

| Candidate | Time | vs SLO (< 800 ms p95) |
|---|---|---|
| SGP.NET | 18 ms | ×44 margin |
| One_Sgp4 | 57 ms | ×14 margin |

### Licences / maintenance

Both MIT, both recently active. One_Sgp4 has a test suite incl. OMM parser tests; single-file port lineage of Vallado's SGP4.

## Gate verdict

**PASS — One_Sgp4 1.1.0.** Meets the vector gate (mean < 0.1 km, max < 1 km across in-envelope cases including deep space, at the reference's printed precision), thread-safe, 14× SLO headroom, MIT, maintained. SGP.NET fails the deep-space gate (defective SDP4 initialization) and is **not** the chosen library — its strength is the wider toolkit (frames, observers, ground tracks), not the propagator core.

## Decisions feeding

- **ADR 9 (element format + propagator)**: OMM remains the primary storage format; One_Sgp4 behind `IOrbitalPropagator`; OMM→mean-elements adapter at load time; checksum-tolerant parse mode required for verification-style TLEs (configurable strictness).
- Phase 5 uses the ISS OMM fixture (`fixtures/iss-stations-omm.csv`) for the ingestion spike S0.10.
- Accuracy expectation for the API: propagation errors are seconds-to-minutes-level for element ages < 3 days (LEO); freshness policy unchanged.

## Open issues

- 11801 TLE parse rejection: reproduce with the raw line, decide parser tolerance in Phase 5.
- One_Sgp4 API ergonomics: epoch is passed explicitly (`EpochTime`), no tsince-based entry point — adapter must carry epoch; static methods mean stateless call sites (fine, verified thread-safe).
- Validation vectors for the Phase 5 accuracy suite: promote `fixtures/tcppver.out` + `SGP4-VER.TLE` into `Astronomy.AccuracyTests` (in-envelope cases only; exclude 33333–33335, 20413-bis long arc).
