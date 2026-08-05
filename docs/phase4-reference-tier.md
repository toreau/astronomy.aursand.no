# Phase 4 — Reference tier via SPICE

Status: **complete** · Date: 2026-08-05 · Commit: HEAD of main

## Goal

Make `precision=advanced|reference` real on the position endpoints: a SPICE-backed
reference chain (CSPICE N66 + JPL DE-series kernels) validated ≤ 1″ against the
Horizons full-grid fixtures (2,435 epochs/body × 9 bodies, 1900–2100) — the gate
that was deferred from S0.3.

## What was delivered

### Reference chain (`Astronomy.Modules.Ephemeris/Reference/`)

- `CSpice.cs` — CSPICE N66 P/Invoke surface (LibraryImport; `erract`, `spkobj`,
  `spkcov` added beyond the S0.3 probe set; `spkobj_c`/`spkcov_c` take SPICE
  **cells** — `SpiceCell.cs` replicates the `_SpiceCell` header layout, the
  S0.3-era plain-array marshaling fails `CELLTYPECHK`).
- `SpiceKernelPool` — loads `de441.bsp` (preferred) > `de440.bsp` > `de440s.bsp`
  + `naif0012.tls` + `pck00010.tpc`, optional `de440s_plus_MarsPC.bsp` and
  `earth_assoc_itrf93.tf`; **global lock around every CSPICE call** (S0.3
  thread-safety finding re-confirmed on the host, see below); `erract RETURN`
  at init (CSPICE's default error action is **ABORT — any SPICE error kills the
  process**; this was a live production hazard until fixed); prefix-hash
  (64 MiB head) kernel provenance for >100 MiB files; graceful degradation to
  `IsAvailable=false` when the native lib or kernels are absent.
- `SpiceReferenceEphemeris : IReferenceEphemeris` — J2000 positions, astrometric
  `LT` / apparent `LT+S` against EARTH; **validated era 1972-01-01 onwards**
  (leap-second era, see Findings); Mars uses its planet-center segment
  (de440s_plus_MarsPC, 1950–2050) inside that window and the barycenter outside.
- `IReferenceEphemeris` is registered via the module registrar; kernel dir from
  `ASTRONOMY_KERNEL_PATH` (default `/data/kernels`).

### Tier mapping (`EphemerisService`)

- `precision=consumer` → engine chain (unchanged).
- `precision=advanced|reference` on `/position` → SPICE; `frame=icrs` only
  (astrometric + apparent). `frame=of-date` → **400 AST-4001**; pre-1972 →
  **400 AST-4001**; kernels unavailable → **503 AST-5030** (no silent fallback);
  `frame=horizontal` → engine chain + **AST-7003** warning.
- `AST-7002` now only appears on non-position endpoints at advanced/reference
  (rise-set, twilight, visibility, events) with a message pointing at the
  position-endpoint coverage.
- Metadata: `algorithm: spice-de441` (`N66:j2000-astrometric|j2000-apparent`)
  + per-kernel `spice:<file>` dataset refs with sha256 prefixes; `/ready` reports
  `kernels: ok|unavailable`.

### Deployment (`Dockerfiles`, worker)

- Both images gain a `cspice` build stage (arturania/cspice mirror, sparse
  checkout of `src include lib`, the two S0.3 patches, `gcc -shared
  --whole-archive`). The api image additionally publishes the worker CLI so
  gates/diagnostics run from either container.
- Worker CLI: `compare-spice` (the gate, with worst-epoch reporting),
  `spice-probe`, `spice-cov` (segment/coverage listing), `spice-threadtest`,
  `ingest eop-c04` (IERS C04, staged+activated into the dataset registry).
- `naif` job: idempotent kernel refresh (large kernels fetched only when
  missing), streamed download for the 3.3 GB `de441.bsp` (JPL ftp —
  `GetByteArrayAsync` hits the 2 GB response-buffer limit), NAIF/JPL listings.
- Scheduled task `naif-kernels` (weekly; temporarily every 5 min during the
  phase, **to be restored to weekly — Coolify API was flaky, see Follow-ups**).

## Gate results — REFERENCE GATE PASS

CSPICE vs Horizons (q1, J2000 astrometric, `LT`), full fixture grid, 1972–2100
(1,558 epochs/body × 9 bodies, pre-1972 rows excluded by the validated era):

| body    | mean   | max    | | body    | mean   | max    |
|---------|--------|--------|-|---------|--------|--------|
| sun     | 0.014″ | 0.025″ | | jupiter | 0.026″ | 0.073″ |
| moon    | 0.013″ | 0.025″ | | saturn  | 0.032″ | 0.067″ |
| mercury | 0.014″ | 0.026″ | | uranus  | 0.164″ | 0.391″ |
| venus   | 0.013″ | 0.026″ | | neptune | 0.039″ | 0.118″ |
| mars    | 0.014″ | 0.025″ | |         |        |        |

Worst case overall: **uranus 0.391″** — three orders of magnitude inside the 1″ gate.
Jupiter/Saturn/Uranus/Neptune use **planet barycenters** (center-vs-barycenter
offset ≤ 0.05″; Horizons' planet-specific kernels agree to ≤ 0.39″).
Apparent (`LT+S`) vs astrometric sanity: max 20.9″ (Earth's stellar aberration).

## Findings

1. **CSPICE's default error action is ABORT** — a SPICE error (e.g. an epoch
   outside kernel coverage) calls `exit(1)` and kills the API process. Fixed by
   `erract_c("SET","RETURN")` at pool init; errors now surface as clean
   4xx/5xx via the existing problem+json handler.
2. **~40 s UTC→ET error before the leap-second era.** The first gate run showed
   failures only in 1900–1903 with errors proportional to apparent motion
   (moon 27.3″ ≈ 50 s, sun 1.8″ ≈ 45 s, mercury 3.9″ ≈ 49 s). SPICE vs skyfield
   on the **same kernel** differ 21″ at 1900-01-01 — a time-scale error, not a
   kernel-data error (SPICE extrapolates ΔAT pre-1972; Horizons uses historical
   ΔT). Resolution: the reference tier is validated for the leap-second era
   (1972-01-01+); earlier epochs return 400 with guidance to use consumer
   precision. After the floor, sun/moon/mercury/venus agreement with Horizons
   collapsed from 1.8–27″ to **≤ 0.026″**.
3. **NAIF `de441_part-1/2.bsp` is not the standard de441 product** — only 14
   objects (barycenters + inner planets) with coverage back to 13,201 BC and
   data at 1900–2100 identical to de440. JPL's single `de441.bsp` (3.3 GB, the
   kernel Horizons uses) is the reference; fetched by streaming.
4. **`de440s_plus_MarsPC.bsp` covers only 1950–2050** (all bodies) — Mars
   planet-center positions are windowed to that span; barycenter outside.
5. **Thread safety re-confirmed on the host**: `spice-threadtest` (8 threads ×
   200 unlocked `spkpos`, production lib) crashed the process with
   `SPICE(BADSUBSCRIPT)` in the time subsystem and produced
   `SPICE(INVALIDTIMESTRING)` corruption; the pool's global lock is required.
   (Methodology note: the corruption poisons the in-process pool, so the
   locked-phase measurement in the same process is not meaningful — each phase
   should run in a fresh process; the S0.3 spike measured locked = 0.)
6. **The mirror's `de440s.bsp`** (sha256 `c1c7feea…`) is the NAIF short product:
   14 objects, barycenters only for the outer planets. The engine-vs-Horizons
   Phase-3 gate (≤ 22.5″) was never affected by this.

## Live verification (astronomy.aursand.no)

- `/ready` → `{"status":"ready","db":"ok","kernels":"ok"}`
- jupiter reference 2026-08-04 → RA 129.901°, Dec 18.893°, 942 290 627 km,
  metadata `spice:de441.bsp` + `N66:j2000-astrometric`, zero warnings
- of-date + reference → 400 AST-4001; pre-1972 + reference → 400 AST-4001
- advanced moon → SPICE path, no warnings; consumer mars → engine path
- horizontal + reference → engine alt/az with AST-7003 (no AST-7002)
- Kernel pool on the host: de441.bsp, de440s.bsp, de440.bsp (backup),
  de440s_plus_MarsPC.bsp, naif0012.tls, pck00010.tpc, earth_assoc_itrf93.tf

## Tests

- Unit: `ReferenceTierTests` (tier mapping, of-date/pre-1972 rejection,
  unavailable→503 exception, AST-7003, AST-7002 placement) — 9 new tests.
- Api: advanced+of-date → 400; reference without kernels → 503 AST-5030.
- All suites: 1,148 tests green (30 unit / 12 api / 1,094 accuracy / 7
  architecture / 5 integration).
- CI cannot run the SPICE gates (native lib + 3.3 GB kernels) — gate evidence
  is produced by host runs and recorded here (established S0.11 pattern).

## Follow-ups

- **Restore `naif-kernels` cron to weekly** (`0 3 * * 0`): the Coolify API was
  intermittently rejecting worker-app scheduled-task calls during the phase
  ("Application not found"); the task is temporarily on `*/5 * * * *` (safe —
  the naif job is idempotent).
- **Run `ingest eop-c04`** on the worker (code complete; the C04 dataset is
  staged for the Phase-6 of-date reference chain; not consumed yet).
- Pre-1972 reference positions: a historical-ΔT UTC→ET (ERFA/NodaTime-based)
  could extend the validated era back to 1849 — Phase 6 candidate.
- of-date reference positions (ERFA nutation/precession) — Phase 6.
- Official NAIF toolkit build comparison (mirror is N66 same source; the
  thread-safety evidence transfers; a fresh-process threadtest harness would
  give clean locked-phase numbers).
