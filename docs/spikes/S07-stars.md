# Spike S0.7 — In-memory star cone search

**Date**: 2026-08-04 · **Status**: PASS

## Objective

Validate in-memory cone search at catalogue sizes 1k–100k against the §21 SLO (10k < 20 ms p95, 100k < 100 ms), choose the index shape and catalogue load format; feeds ADR 8 (in-memory confirmed for MVP).

## Method

Harness `spikes/S07-stars/` (net10, BenchmarkDotNet 0.15.8):
- **Real data**: Yale BSC (CDS V/50, `catalog.gz` — public domain) parsed → 9,092 stars (18 rows skipped: placeholder/malformed lines); committed fixture `fixtures/bsc.csv` (RA/Dec J2000, Vmag, HR id).
- **Indexes**: brute-force unit-vector dot product; 5°-tile grid (RA×dec, RA padding scaled by 1/cos(dec)); RA-sorted with dec-band prefilter (band edge cos-corrected + 0°/360° wrap handling).
- **Correctness gate**: all indexes must return identical result sets vs brute force over 200 random cones (radius 0.5–10°, mag 3–6) — **PASS** after fixing three real edge-case bugs (tile RA-padding near poles; sorted-index RA wrap; band-edge cos correction).

## Results

Per 20 queries (mixed radius 1–10° + magnitude filter), mean:

| Index | BSC 9,092 | 10k synthetic | 100k synthetic | Alloc (10k) |
|---|---|---|---|---|
| Brute force | 312 µs (15.6 µs/q) | 423 µs (21 µs/q) | 6,212 µs (311 µs/q) | 11.6 KB |
| **Tile 5°** | **8.9 µs (0.45 µs/q)** | **17.5 µs (0.88 µs/q)** | **241 µs (12 µs/q)** | 10.0 KB |
| RA-sorted+dec-band | 18.5 µs (0.92 µs/q) | 23.2 µs (1.2 µs/q) | 856 µs (43 µs/q) | 10.0 KB |

SLO: 10k cone < 20 ms p95 → **measured 0.9 µs/query (~20,000× margin)**; 100k < 100 ms → **12 µs/query (~8,000× margin)**. Memory: 9k stars ≈ 1 MB (unit vectors + fields); trivial.

## Decisions

1. **Index: 5°-tile grid + unit-vector dot product + magnitude prefilter** — fastest at all sizes (2–18× over RA-sorted at 100k), simple, exact (verified set-identical to brute force).
2. **Catalogue format: versioned CSV for MVP** — parse of 9,092 rows is ~ms; binary packing deferred until a catalogue > 1M stars (Phase 6 decision with PostGIS/DuckDB).
3. Allocation note: per-query `List<Star>` (~10 KB/20 queries) — acceptable; pooled buffers are a Phase 4 optimization if load demands.

## Gate verdict

**PASS** — SLOs met with orders-of-magnitude headroom; index + format decided with measured numbers; BSC fixture committed (feeds Phase 4: `Astronomy.Modules.Stars` in-memory catalogue + `bsc` catalogue option).

## Decisions feeding

- ADR 8 (star-catalogue storage): in-memory confirmed for MVP (≤ ~1M stars); PostGIS/DuckDB re-evaluated only at Gaia scale (Phase 6).
- Stars module design: `IStarIndex` abstraction (swap tile index for spatial store later), bounded cone params (radius ≤ 30°, limit ≤ 1000) consistent with §18 budgets.
