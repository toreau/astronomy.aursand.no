# Operations schedule & update inventory

How the astronomy API keeps its third-party data and dependencies current with
no manual intervention, and what remains manual by design.

## Scheduled tasks (Coolify, worker container)

| Task | Cadence | Command | What it does |
|---|---|---|---|
| `naif-kernels` | weekly, Sun 03:00 UTC | `dotnet Astronomy.DataIngestion.dll naif /data/kernels >> /data/naif.log 2>&1` | Kernel gap-fill + integrity markers, EOP UT1 (ser7), EOP C04, satellite elements (gate-before-activate), leap seconds (IERS), star-catalog gap-fill (gate-before-activate), 24 h-throttled reference gate, kernel-change api restart |
| `omm-refresh` | daily, 06:00 UTC | `dotnet Astronomy.DataIngestion.dll omm refresh >> /data/omm.log 2>&1` | CelesTrak stations elements → stage → sat-gate → activate |

All dataset jobs validate before activating (count floors, plausibility bounds,
UT1 continuity vs the active dataset, accuracy gates for stars/satellites) —
a failed refresh leaves the previous version active.

## Dataset update cadence

| Dataset | Source | Cadence | Gate |
|---|---|---|---|
| `leap-seconds` | IERS `leap-seconds.list` | weekly (naif) | count/monotonic/plausibility |
| `eop-ut1` | USNO ser7 | weekly (naif) | count/plausibility/continuity |
| `eop-c04` | IERS C04 | weekly (naif) | count/plausibility/continuity |
| `star-catalog-hyg` | HYG v3.8 (static) | gap-fill only | star-gate before activate |
| `satellite-elements` | CelesTrak stations | daily (omm-refresh) + weekly (naif) | sat-gate before activate |
| SPICE kernels | JPL/NAIF | gap-fill + control-kernel refresh weekly | integrity markers + reference gate + api restart on change |

## Kernel reload contract

After `naif` refreshes kernels, the worker compares the volume hashes against
the hashes the running API loaded (exposed as `kernelHashes` in
`/health/ready`). On mismatch it restarts the api container via the Coolify
API. Opt-in via worker env:

- `COOLIFY_API_URL` — Coolify instance URL
- `COOLIFY_API_TOKEN` — Coolify API token (Bearer)
- `COOLIFY_API_APP_UUID` — the api application UUID
- `ASTRONOMY_API_URL` — default `https://astronomy.aursand.no`

Without these env vars the check is skipped and kernel changes take effect on
the next manual deploy/restart (de441 is stable; the leap-second `naif0012.tls`
is the case that matters).

## Code dependency updates

| Layer | Mechanism | Manual? |
|---|---|---|
| NuGet packages | Dependabot, weekly grouped PRs gated by the CI suite (1,304 tests) | majors reviewed; patch/minor auto-merge policy per repo rules |
| .NET SDK | `global.json` pin (10.0.302, `latestFeature` roll-forward) | deliberate bump commit |
| cspice fork / erfa | pinned in Dockerfiles; weekly `native-watcher` workflow opens an issue when upstreams move | deliberate bump + sed-patch verification |
| tzdata | flows through NodaTime bumps (Dependabot) | as above |

## Verification

- CI: full suite on every PR (and on every Dependabot PR) — the gate for all
  code dependency updates.
- Host: weekly reference gate (`compare-spice`) + star-gate + sat-gate via
  `naif`, throttled to once per 24 h.
- Manual/monthly: `spikes/S12-live-verification/` harness vs JPL Horizons and
  other online sources; results in `docs/live-verification.md`.
