# Spike S0.3 — SPICE bindings, Phase A (Linux container)

**Date**: 2026-08-04 · **Status**: PARTIAL — functional gates A1/A3 PASS; precision cross-check (A2) and ERFA fallback (A4) pending next session; amd64 host gate (B) rides S0.11

## Objective

Prove a reference-tier chain — CSPICE + DE440s — in a Debian container targeting Linux x64, and decide the binding strategy (direct P/Invoke; both managed wrappers are dead: spicedotnet repo deleted, NaifSpiceSharp last pushed 2020).

## Method

`spikes/S03-spice/`: `fetch.sh` pulls artifacts from GitHub mirrors (NAIF hosts unreachable from the dev network); Dockerfile (`mcr.microsoft.com/dotnet/sdk:10.0`, amd64) builds CSPICE N66 from the `arturania/cspice` source mirror, links `libcspice.so` from the `-fPIC` archive, and runs a P/Invoke probe (net10, `LibraryImport`). Probe gates: kernel loading, UTC↔ET (leap seconds), `spkpos` Sun/Moon with/without light-time, J2000→ECLIPJ2000 rotation chain, serial + lock-serialized parallel `spkpos`.

## Environment

macOS 26.4 (arm64) host; container `linux/amd64` (Rosetta emulation — matches production arch). Kernel: `de440s.bsp` (32,726,016 B, sha256 `c1c7feea…` verified identical across two independent mirrors: arturania/cspice, takashi-cw/Stella-JS_Astrocalc).

## Results (all inside the amd64 container)

| Gate | Result |
|---|---|
| CSPICE N66 build from mirror source | PASS — ~2400 files compile with `mk_linux.csh` (after two reproducible patches: drop 4 C++-style comment lines in `SpiceZpr.h` for `-ansi`; create `lib/`/`exe/` dirs); `libcspice.so` linked from the `-fPIC` archive |
| `furnsh` de440s + leapseconds + pck | PASS |
| `utc2et` + `deltet` UTC | PASS — TT−UTC = 64.1839 s at 2000-01-01 (expect 64.184, ≤ 0.1 ms) |
| `et2utc` round-trip | PASS |
| `spkpos` Sun @J2000 (J2000 frame, NONE/LT) | PASS — RA 281.288985°, Dec −23.033251°, r = 147 103 726 km (0.9833 AU ✓); LT variant shifts range by 7 km ✓ |
| `spkpos` Moon @J2000 | PASS — RA 222.455915°, Dec −10.902933°, r = 402 450 km (0.00269 AU ✓), lt = 1.342 s ✓ |
| `pxform` J2000→ECLIPJ2000 | PASS — Sun Dec = 0.000238° (on the ecliptic ✓), rotation chain works |
| Serial spkpos (1600 calls) | PASS — moon drift 1119 km over 1600 min (~0.7 km/min ✓) |
| Parallel spkpos, global lock (8 threads × 200) | PASS — no SPICE errors, identical results |

## Findings (important)

1. **P/Invoke direction confirmed viable** — the ~12-function `CSpice.cs` surface works via `LibraryImport`; array/out/string marshalling all correct. This is the seed of the Phase 4 `IReferenceEphemeris`.
2. **Leap-seconds kernel is mandatory** for UTC↔ET (`SPICE(NOLEAPSECONDS)` otherwise); `latest_leapseconds.tls` fetched from the mirror (NAIF original pending host verification).
3. **TOD/TEME/ITRF93 frames require their FK frame kernels** — `pxform` with an unloaded frame raises UNKNOWNFRAME and this build's default error action **aborts the process** (native SIGABRT — no control return). Production design: load FKs (teme.tf, tod.tf, itrf93.tf — NAIF artifacts to fetch via mirror/host in S0.11), and set error action to continue + always check `failed()`.
4. **This mirror build is NOT thread-safe for concurrent `spkpos`** — parallel calls corrupt the CHKIN/CHKOUT tracer (NAMESDONOTMATCH cascade → SIGSEGV). A global lock fully mitigates (validated, exit 0, 9/9 checks). Phase 4 design: global lock for reference tier (low traffic) or per-thread kernel pools; re-test against the official NAIF N66 build on the host (official builds are thread-safe per NAIF docs) during S0.11.
5. **deltet "ET" semantics need doc verification** (returned TT−UTC-scale value at J2000; the UTC branch — the one we need — is exact). Deferred to host access for SPICE docs.
6. NAIF official checksums for de440s.bsp still unverified (dual-mirror match only) — host network check in S0.11.

## Gate verdict

- **A1 (build + load + positions): PASS** — containerized CSPICE works; sanity values physically correct.
- **A3 (thread-safety strategy): PASS with constraint** — global lock validated; official-build re-test pending (S0.11).
- **A2 (≤ 0.5″ cross-check vs libephemeris): NOT RUN** — next session (pip `libephemeris`, generate reference RA/Dec from same DE440 data, compare).
- **A4 (ERFA fallback build): NOT RUN** — next session (liberfa/erfa is active, BSD-3; compile in same container).
- **B (host gate, ≤ 1″ vs Horizons): S0.11** — also tests NAIF reachability from the Coolify network (decides kernel-refresh architecture: mirror vs direct).

## Decisions feeding

- **ADR 4 final direction**: direct P/Invoke of CSPICE behind `IReferenceEphemeris`; DE440s kernels; global lock for reference-tier calls; FKs required for TOD/TEME; host gate completes the ≤ 1″ claim.
- S0.5 follow-up: CSPICE `deltet`/`utc2et` give an independent time-scale validation path in Phase 4.

## Open issues / next session

- A2 libephemeris cross-check (pin PyPI version; commit generated fixtures).
- A4 ERFA compile + minimal `epv00` probe.
- FK kernel acquisition (teme.tf / tod.tf / itrf93.tf) — mirrors or host.
- Official NAIF build + checksum verification on host (S0.11).
