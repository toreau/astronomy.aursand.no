#!/usr/bin/env python3
"""Generate independent DE440 reference RA/Dec/range via skyfield+jplephem
(read-only evaluation of the local de440s.bsp kernel). Emits CSV consumed by
the C# probe's `crosscheck` (CSPICE vs skyfield) and `erfa` (ERFA vs CSPICE)
modes. Columns: body, utc, tt_jd, tdb_minus_tt_s, ra_deg, dec_deg, dist_km."""

import csv
import sys
from datetime import datetime, timedelta

from skyfield.api import load

KERNEL = sys.argv[1] if len(sys.argv) > 1 else "/spice/kernels/de440s.bsp"
OUT = sys.argv[2] if len(sys.argv) > 2 else "/probe/out/de440s_ref.csv"

SKY_NAMES = {"sun": "sun", "moon": "moon", "venus": "venus", "mars": "mars barycenter",
             "jupiter": "jupiter barycenter", "saturn": "saturn barycenter"}
BODIES = list(SKY_NAMES)

epochs = []
d = datetime(2020, 1, 1, 12, 0, 0)
while d <= datetime(2030, 12, 31):
    epochs.append(d)
    d += timedelta(days=90)
epochs += [
    datetime(2000, 1, 1, 12, 0, 0),
    datetime(2016, 12, 31, 23, 59, 59),
    datetime(2026, 8, 4, 12, 0, 0),
]

ts = load.timescale()
eph = load(KERNEL)

with open(OUT, "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(
        ["body", "utc", "tt_jd", "tdb_minus_tt_s", "ra_deg", "dec_deg", "dist_km"]
    )
    for utc in epochs:
        t = ts.utc(
            utc.year,
            utc.month,
            utc.day,
            utc.hour,
            utc.minute,
            utc.second + utc.microsecond / 1e6,
        )
        for body in BODIES:
            astrometric = eph["earth"].at(t).observe(eph[SKY_NAMES[body]])
            ra, dec, dist = astrometric.radec()
            w.writerow(
                [
                    body,
                    utc.strftime("%Y-%m-%dT%H:%M:%S"),
                    f"{t.tt:.10f}",
                    f"{(t.tdb - t.tt) * 86400.0:.9f}",
                    f"{ra.degrees:.9f}",
                    f"{dec.degrees:.9f}",
                    f"{dist.km:.6f}",
                ]
            )

print(f"wrote {len(epochs) * len(BODIES)} rows -> {OUT}")
