# Spike S0.10 — OMM ingestion pipeline

**Date**: 2026-08-04 · **Status**: PASS

## Objective

Prove the satellite-element lifecycle end-to-end — fetch → validate → stage → activate → rollback with freshness thresholds — on the S0.9 SQLite store, and validate the OMM→propagation path for the chosen propagator (One_Sgp4; S0.4 only exercised SGP.NET's OMM parser).

## Method

`spikes/S10-omm/` (net10, Microsoft.Data.Sqlite 10.0.10 + SQLitePCLRaw.bundle_e_sqlite3 3.0.5 pin per S0.9 finding; One_Sgp4 1.1.0; SGP.NET 1.5.0). Store: `datasets` (staged/active), `satellite_elements` (all versions kept; active set via `active_datasets` pointer JOIN), `audit`. Modes: `init`, `fetch` (live CelesTrak `FORMAT=omm` CSV), `stage-file`, `validate`, `activate` (transactional pointer swap, versions retained for rollback), `status`, `rollback`, `lifecycle` (full drill), `propagate` (OMM→propagation), `cross-tle` (propagator comparison on a real TLE).

## Results

| Gate | Result |
|---|---|
| Live fetch → validate → stage (CelesTrak, 22 stations) | PASS — 22 rows, epoch/mean-motion/ecc/incl/bstar/angles sanity rules |
| Tampered payload rejection (6.6-yr-old epoch, ecc 0.9877) | PASS — rejected with both violations reported; **no activation; state untouched** |
| Activate → status → rollback lifecycle | PASS — ALL GREEN (fresh < 24 h: 21, warn < 72 h: 1 — correct mixed-state reporting) |
| Versioning semantics | PASS — versions retained after activation (rollback can restore); active set = pointer JOIN |
| OMM→propagation (SGP.NET, two paths) | PASS — `OMM→Satellite` and `OMM→Tle→Sgp4` bit-identical at +1 h: (1236.195, −5175.528, −4241.124) km — matches the S0.4 independent baseline |
| Propagator cross-check (One_Sgp4 vs SGP.NET, same real ISS TLE) | PASS — **|Δ| = 0.037 km** at +1 h (gate < 1 km) |

## Findings (important)

1. **`DateTime.Parse` without `RoundtripKind` converts to local time** (classic .NET gotcha — "…Z" input becomes Kind=Local, +02:00 here). Propagation was correct only via consistent wall-clock math. **Phase 5 rule: every stored/read epoch uses `DateTimeStyles.RoundtripKind`.** (Found via a printed epoch — the S0.9 recipe docs now carry this.)
2. **One_Sgp4's OMM support is weak**: `ParserOMM` is **XML-only** (rejects the CelesTrak CSV OMM), and its `Sgp4(Omm, wgs)` ctor produces **garbage results** in testing (uninitialized propagator state, all init-variant attempts). Its reliable path is TLE-line input (`ParserTLE`, validated in S0.4) or the `SatFunctions`/`Tle` path.
3. **ADR 9 decision recorded**: OMM remains the primary *storage* format (rich, structured, versioned); **the ingest-time adapter is: OMM CSV → SGP.NET `OmmCsvParser` → OmmData** (validated), used both for validation and as the propagation input. A separate **OMM→TLE line converter** is required only for legacy TLE compatibility (One_Sgp4); prototype showed standard TLE column-layout pitfalls → Phase 5 task with a **round-trip validation harness** (generate → parse via SGP.NET → assert every field vs OMM source). This satisfies "never present stale data": elements are stored/activated/freshness-checked on OMM values regardless of the propagation path.
4. Validation rules calibrated on real data: bstar bound 0.02 (0.01 too tight), NORAD ids now **5–6 digits** (100057 observed).

## Gate verdict

**PASS** — lifecycle reproducible end-to-end; stale/failure semantics correct (validation gates activation; rollback restores; freshness states fresh/warn/degraded/refuse implemented); propagators agree at 37 m; findings recorded (RoundtripKind, One_Sgp4 OMM limits, adapter decision).

## Decisions feeding

- ADR 9 (element format + adapter): OMM CSV primary; SGP.NET parser as ingest adapter; OMM→TLE converter deferred to Phase 5 with round-trip validation harness.
- Phase 5: worker loop = `fetch → validate → stage → activate` (this spike's modes, promoted), freshness policy thresholds (24/72/168 h), `astronomy-cli` subcommands.
- Fixture: `fixtures/tampered.csv` retained as the rejection test input.
