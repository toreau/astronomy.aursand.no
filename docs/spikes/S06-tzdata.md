# Spike S0.6 — tzdata pinning via Noda Time

**Date**: 2026-08-04 · **Status**: PASS

## Objective

Civil time-zone mechanism with pinned, reproducible tzdata for the Calendars module; resolve ADR 5/6 details and the S0.5 open item (leap-second carrier).

## Method

Console spike `spikes/S06-tzdata/` (net10, NodaTime 3.3.3). Verified: DST transitions (Europe/Oslo, America/New_York, Pacific/Auckland incl. reversed-season), historical dates, gap/fold semantics, tzdb version discovery, leap-second modeling, load/memory cost, and both pinning strategies (embedded vs custom `TzdbProvider` from downloaded `.nzd`).

## Results

| Check | Result |
|---|---|
| tzdb version | **TZDB 2026c** (mapping 48.2), embedded in NodaTime 3.3.3 — current release |
| Zone inventory | 597 zones; first lookup 5 ms; ~2 MB for 100 zone lookups |
| Oslo 2026-03-29 / 2026-10-25 | CET→CEST / CEST→CET at exact instants — PASS |
| New York 2026-03-08 / 2026-11-01 | EST→EDT / EDT→EST — PASS |
| Auckland 2026-04-04 / 2026-09-26 | NZDT→NZST / NZST→NZDT (reversed season) — PASS |
| Oslo 1900 (pre-1970) | +1:00 CET — PASS |
| London 1969 | +1:00 year-round (British Standard Time) — PASS |
| EU rule since 1996 (Oslo) | CET→CEST — PASS |
| DST gap 2026-03-29 02:30 | `SkippedTimeException` (strict) — PASS |
| DST fold 2026-10-25 02:30 | `AmbiguousTimeException` (strict); lenient resolves to first occurrence (+02) — PASS |
| Leap second 23:59:60 | Rejected (`ArgumentOutOfRangeException`) — **Noda Time does NOT model leap seconds** |
| Pinning (a): package version = tzdb | Embedded 2026c == `nodatime.org/tzdb/latest.txt` (tzdb2026c.nzd) — confirmed current |
| Pinning (b): custom provider | Feed resolves; direct `.nzd` fetch returned 400 (URL mechanics only) — viable escape hatch, not needed |

## Decisions

1. **Pinning strategy (a)**: NodaTime package version = tzdb version. Upgrade procedure = bump NodaTime (review its release notes for tzdb bumps); documented in the Calendars module design. Strategy (b) (custom `TzdbProvider` from a downloaded, versioned tzdata file) reserved as escape hatch if a tzdb update must ship without a NodaTime release — mechanics to be completed only if needed.
2. **Leap-second carrier**: confirmed Noda Time does not model leap seconds → our versioned IERS table (S0.5 result) remains the leap-second carrier; tzdb is used for civil zones only. Explicit boundary (civil time ≠ astronomical time scales) recorded.
3. **OS independence confirmed**: no `TimeZoneInfo` dependency; tzdb fully embedded — reproducible across hosts/containers.

## Gate verdict

**PASS** — transition table exact; pinning strategy (a) chosen; leap-second carrier decision recorded; startup cost negligible (5 ms, ~2 MB).

## Decisions feeding

- ADR 5 (time/date representation): `DateTimeOffset` + Noda Time civil layer; no `TimeZoneInfo`.
- Calendars module design: tzdb-pinned zones via `DateTimeZoneProviders.Tzdb`; strict/lenient resolver semantics for API timezone requests.
- S0.5 follow-up closed: leap-second source = embedded IERS table (not tzdb).
