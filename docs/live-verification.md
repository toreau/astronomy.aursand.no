# Live endpoint verification — 2026-08-06 (updated after parallax fix)

Independent comparison of every live endpoint on `https://astronomy.aursand.no`
against external sources (JPL Horizons API, sunrise-sunset.org, IERS, VizieR
HIP, USNO tables, python `sgp4`, `skyfield`). Harness: `spikes/S12-live-verification/`.

> **Update (post-fix):** the moon horizontal parallax bug (item 1 below) was
> fixed in both tiers — the topocentric observer offset is now subtracted in
> `EphemerisCalculator.Horizontal` and `SpiceReferenceEphemeris.HorizontalPosition`
> (commit after this report). Post-fix consumer verification against Horizons:
> worst altitude/azimuth deviation **0.0008°** over 4 points (Oslo/Sydney ×
> 2026-08-15/2027-01-01), previously 0.87°. Reference tier re-verified on
> production after deploy.

## Environment snapshot

| Component | State |
|---|---|
| `/health/ready` | `ready`, db ok, kernels ok, star catalog ok |
| Reference kernels (metadata) | `de441.bsp` + `de440s_plus_MarsPC.bsp` + control files |
| Datasets | leap-seconds `iers-2026a` · eop-ut1 `20260804` · eop-c04 `20260805` · star-catalog-hyg `v38` · satellite-elements `20260805` (22 elements, 22×warn 24–72 h old) |
| Contract sweep | 33/33 (all endpoints 200 on valid input; 400 + `AST-4001` on invalid; unknown HIP 999999 → 400) |

## Results by domain

| Domain | Checks | Result |
|---|---|---|
| Time scales & JD | 17/17 | **clean** — JD exact to 1e-9; TAI−UTC=37, TT−UTC=69.184, TDB−TT −1.06 ms; UT1−UTC matches IERS ser7 |
| Calendars | included above | **clean** — ISO week/day/JD exact vs Python; tz offsets exact vs zoneinfo (Oslo/Singapore/Sydney/NY); range ≡ convert |
| Ephemeris positions | 5 epochs × 9 bodies × 2 precisions × 2 frames | **clean** — consumer ≤ 30″ vs Horizons, **reference ≤ 2″** (de441), of-date and icrs |
| Ephemeris magnitude/elongation | planets × 5 epochs | **clean** — mag ≤ 0.3, elongation ≤ 0.5° |
| Horizontal alt/az | sun/moon/mars × 2 locations × 2 epochs | 4 deviations — **moon altitude bug** (below) |
| Rise/set + twilight | sun × 4 dates vs sunrise-sunset.org; moon/mars/jupiter × 2 vs skyfield | 4 deviations — **model difference** (below); skyfield checks all pass |
| Moon phases | 12 quarters vs USNO table | **clean** — ≤ 2 min |
| Events 2026 | jupiter/saturn oppositions, venus/mercury max elongations vs skyfield; mars (none) | **clean** — all found; no false Mars opposition |
| Stars | 10 stars position/name/vmag vs VizieR HIP; brightest top-5; rise/set vs skyfield incl. circumpolar | 37/37 **clean** — positions ≤ 1.5″, vmag exact, circumpolar agreement |
| Satellites | ISS position × 4 instants + 24 h passes vs python sgp4; search/status vs CelesTrak | 7/7 **clean** — alt/az ≤ 0.5°, range ≤ 20 km, pass counts/times match |
| Almanac | monthly ≡ endpoints, daily ≡ monthly, yearly = 12 months | **clean** |

**Total: 408 checks, 8 deviations** (4 bug + 4 model-difference).

## Deviations

### 1. Moon horizontal altitude error ~0.9° — FIXED

`/api/v1/ephemeris/moon/position?frame=horizontal` returned altitude **~0.87° too
high** (azimuth correct to 0.003°) in **both** `precision=consumer` and
`precision=reference`. Root cause: both horizontal paths computed the Moon's
position geocentrically without the observer's topocentric offset (parallax;
Moon parallax reaches ~0.95°).

**Fix:** `EphemerisCalculator.Horizontal` now subtracts the observer's
geocentric EQD vector (`ObserverVector`) before `Horizon`; 
`SpiceReferenceEphemeris.HorizontalPosition` subtracts the observer's ITRS
position (`GeodeticToItrs`, WGS-84) before normalizing. Regression tests pin
the Horizons-verified value (alt 24.80°/az 154.12° @ Oslo 2026-08-15T12:00Z,
tol 0.1°), the parallax magnitude, sun invariance, and `GeodeticToItrs`.
Post-fix: worst consumer deviation 0.0008° over 4 points.

### 2. Sun rise/set vs sunrise-sunset.org: 103–220 s — EXPECTED MODEL DIFFERENCE

The API matches USNO to ≤ 30 s (accuracy suite); sunrise-sunset.org uses the
NOAA solar-calculator approximation (± 1–2 min class). Deltas are consistent
(~2–3.5 min) across 4 dates. Not a bug; documented.

## Minor observations (not bugs)

- `/api/v1/ephemeris/events?types=opposition` can return events classified
  `conjunction` — the endpoint searches both relative-longitude crossings
  (180°/0°) and reports whichever occurs first with its actual classification.
  Semantically surprising; consider filtering by requested type.
- Satellite elements were 22–72 h old during the run (daily 06:00 UTC refresh);
  tolerances accounted for it.
- HIP 99999 is a real catalog star (Vulpecula, mag 8.08); unknown-HIP 400s
  verified with 999999.

## Verdicts

| Domain | Verdict |
|---|---|
| Time, calendars | Pass |
| Ephemeris positions (icrs/of-date, both tiers) | Pass |
| Horizontal (sun, planets) | Pass |
| Horizontal (moon) | **Fixed** — parallax correction in both tiers; post-fix ≤ 0.0008° vs Horizons |
| Rise/set, twilight, moon phases, events, stars, satellites, almanac | Pass |

One bug found (moon horizontal parallax, both tiers) — **fixed and verified**.
One documentation-grade observation. Everything else agrees with independent
sources within declared tolerances.
