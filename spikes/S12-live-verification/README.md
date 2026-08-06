# S12 — Live endpoint verification harness

One-off Python comparison of every live endpoint on `astronomy.aursand.no`
against independent online sources. Findings: `docs/live-verification.md`.

## Setup

```bash
python3 -m venv venv
./venv/bin/pip install requests sgp4 skyfield
```

## Run

```bash
./venv/bin/python contract.py    # contract sweep (all endpoints, 200/400/503)
./venv/bin/python time_cal.py    # time scales + calendars vs Python/IERS
./venv/bin/python ephemeris.py   # positions/horizontal/rise-set vs JPL Horizons,
                                 # sunrise-sunset.org, skyfield
./venv/bin/python stars.py       # stars vs VizieR HIP + skyfield
./venv/bin/python satellites.py  # ISS vs python sgp4 (independent frame math)
./venv/bin/python misc.py        # moon phases vs USNO, events vs skyfield, almanac consistency
```

Live-API responses are cached in `.cache/` (created at runtime, not committed).
Third-party calls are rate-limited with sleeps; a full run takes ~15 minutes.

## Result of the 2026-08-06 run

408 checks, 8 deviations:
- **Confirmed bug:** Moon horizontal altitude +0.87° in both precision tiers
  (missing topocentric parallax) — see `docs/live-verification.md` item 1.
- **Expected model difference:** sun rise/set vs sunrise-sunset.org 103–220 s
  (their NOAA approximation; API is USNO-validated).
- Minor observation: `events?types=opposition` may return conjunction events.
