import datetime, json, math, sys

sys.path.insert(0, ".")
from liveverify import api_get

results = []


def rec(name, ok, delta, note=""):
    results.append({"check": name, "pass": ok, "delta": delta, "note": note})
    print(f"{'PASS' if ok else 'FAIL'}  {name:<58} delta={delta} {note}")


# ---- moon phases vs USNO reference table (committed accuracy fixtures) ----
USNO = [
    ("2026-10-10T15:50:00Z", "New Moon"),
    ("2026-10-18T16:13:00Z", "First Quarter"),
    ("2026-10-26T04:12:00Z", "Full Moon"),
    ("2026-11-01T20:28:00Z", "Last Quarter"),
    ("2026-11-09T07:02:00Z", "New Moon"),
    ("2026-11-17T11:48:00Z", "First Quarter"),
    ("2026-11-24T14:54:00Z", "Full Moon"),
    ("2026-12-01T06:09:00Z", "Last Quarter"),
    ("2026-12-09T00:52:00Z", "New Moon"),
    ("2026-12-17T05:43:00Z", "First Quarter"),
    ("2026-12-24T01:28:00Z", "Full Moon"),
    ("2026-12-30T19:00:00Z", "Last Quarter"),
]
ph = api_get(
    "/api/v1/ephemeris/moon/phases",
    {"from": "2026-10-01T00:00:00Z", "to": "2027-01-01T00:00:00Z"},
)
for exp_utc, exp_phase in USNO:
    match = min(
        ph["events"],
        key=lambda e: abs(
            (
                datetime.datetime.fromisoformat(e["utc"].replace("Z", "+00:00"))
                - datetime.datetime.fromisoformat(exp_utc.replace("Z", "+00:00"))
            ).total_seconds()
        ),
    )
    dm = (
        abs(
            (
                datetime.datetime.fromisoformat(match["utc"].replace("Z", "+00:00"))
                - datetime.datetime.fromisoformat(exp_utc.replace("Z", "+00:00"))
            ).total_seconds()
        )
        / 60.0
    )
    rec(
        f"moon phase {exp_phase} {exp_utc[:10]} vs USNO",
        dm < 2.0 and match["phase"] == exp_phase,
        f"Δ={dm:.1f}min api={match['utc'][:16]}",
    )

# ---- events vs skyfield (oppositions/conjunctions/max elongations over 2026) ----
from skyfield.api import load as _load, wgs84 as _wgs84
from skyfield import almanac as _almanac
from skyfield.framelib import ecliptic_frame

_ts = _load.timescale()
_eph = _load("de421.bsp")

t0 = _ts.utc(2026, 1, 1)
t1 = _ts.utc(2027, 1, 1)
times = _ts.linspace(t0, t1, 366 * 4)


def elongation_series(body):
    earth = _eph["earth"]
    out = []
    for t in times:
        e = earth.at(t)
        s = e.observe(_eph["sun"]).apparent()
        b = e.observe(_eph[body]).apparent()
        out.append((t, s.separation_from(b).degrees))
    return out


def extrema(series, kind):
    """kind='max' or 'min': local extrema."""
    found = []
    for i in range(1, len(series) - 1):
        a, b, c = series[i - 1][1], series[i][1], series[i + 1][1]
        if kind == "max" and b > a and b > c:
            found.append(series[i])
        if kind == "min" and b < a and b < c:
            found.append(series[i])
    return found


def nearest(events, target, tol_days=1.5):
    best = None
    for e in events:
        d = (
            abs(
                (
                    datetime.datetime.fromisoformat(e["utc"].replace("Z", "+00:00"))
                    - target
                ).total_seconds()
            )
            / 86400.0
        )
        if d < tol_days:
            if best is None or d < best[1]:
                best = (e, d)
    return best


def utc_of(sf_time):
    return sf_time.utc_datetime()


# outer planets: opposition (elongation max ~180)
for body, skykey in [
    ("jupiter", "jupiter barycenter"),
    ("saturn", "saturn barycenter"),
    ("mars", "mars"),
]:
    ev = api_get(
        "/api/v1/ephemeris/events",
        {
            "from": "2026-01-01T00:00:00Z",
            "to": "2027-01-01T00:00:00Z",
            "bodies": body,
            "types": "opposition",
        },
    )
    ser = elongation_series(skykey)
    maxima = [s for s in extrema(ser, "max") if s[1] > 160]
    for m in maxima:
        t = utc_of(m[0])
        hit = nearest(ev["events"], t)
        if hit:
            rec(
                f"opposition {body} {t.date()} vs skyfield",
                True,
                f"Δ={hit[1] * 24:.1f}h",
            )
        else:
            rec(
                f"opposition {body} {t.date()} vs skyfield",
                False,
                "not found in api events",
            )
    if not maxima:
        api_opp = [e for e in ev["events"] if e["type"] == "opposition"]
        # No opposition in this year for this body: both sides must agree on none.
        rec(f"opposition {body} 2026 (none)", len(api_opp) == 0,
            f"skyfield: no max; api events: {len(api_opp)}")

# venus/mercury: max elongation
for body, skykey in [("venus", "venus"), ("mercury", "mercury")]:
    ev = api_get(
        "/api/v1/ephemeris/events",
        {
            "from": "2026-01-01T00:00:00Z",
            "to": "2027-01-01T00:00:00Z",
            "bodies": body,
            "types": "max-elongation",
        },
    )
    ser = elongation_series(skykey)
    maxima = extrema(ser, "max")
    for m in maxima:
        t = utc_of(m[0])
        hit = nearest(ev["events"], t)
        if hit:
            rec(
                f"max-elongation {body} {t.date()} vs skyfield",
                True,
                f"Δ={hit[1] * 24:.1f}h",
            )
        else:
            rec(
                f"max-elongation {body} {t.date()} vs skyfield",
                False,
                "not found in api events",
            )

# ---- almanac: monthly vs underlying endpoints (self-consistency) ----
m = api_get(
    "/api/v1/almanac/monthly", {"month": "2026-08", "latitude": 59.9, "longitude": 10.7}
)
for day in m["days"][0:2] + [m["days"][14], m["days"][-1]]:
    d = day["date"]
    rs = api_get(
        "/api/v1/ephemeris/sun/rise-set",
        {"date": d, "latitude": 59.9, "longitude": 10.7},
    )
    rs_m = api_get(
        "/api/v1/ephemeris/moon/rise-set",
        {"date": d, "latitude": 59.9, "longitude": 10.7},
    )
    ok = (
        day["sunRiseUtc"] == rs["riseUtc"]
        and day["sunSetUtc"] == rs["setUtc"]
        and day["moonRiseUtc"] == rs_m["riseUtc"]
        and day["moonSetUtc"] == rs_m["setUtc"]
    )
    rec(f"almanac monthly day {d} consistent", ok, "sun/moon times match endpoints")

dl = api_get(
    "/api/v1/almanac/daily", {"date": "2026-08-15", "latitude": 59.9, "longitude": 10.7}
)
day15 = next(x for x in m["days"] if x["date"] == "2026-08-15")
rec(
    "almanac daily vs monthly (sun)",
    day15["sunRiseUtc"] == dl["sun"]["sunriseUtc"],
    f"{day15['sunRiseUtc']} vs {dl['sun']['sunriseUtc']}",
)

y = api_get(
    "/api/v1/almanac/monthly", {"year": 2026, "latitude": 59.9, "longitude": 10.7}
)
rec(
    "almanac yearly = 12 months",
    len(y["months"]) == 12
    and y["months"][0]["month"] == "2026-01"
    and y["months"][-1]["month"] == "2026-12",
    f"{len(y['months'])} months",
)
aug = next(x for x in y["months"] if x["month"] == "2026-08")
rec(
    "almanac yearly Aug == monthly Aug",
    aug["days"][0]["date"] == m["days"][0]["date"]
    and aug["days"][0]["sunRiseUtc"] == m["days"][0]["sunRiseUtc"],
    f"{len(aug['days'])} days",
)

json.dump(results, open("results_misc.json", "w"), indent=1)
fails = [r for r in results if not r["pass"]]
print(f"\n{len(results) - len(fails)}/{len(results)} passed, {len(fails)} deviations")
for r in fails:
    print("  DEVIATION:", r["check"], "|", r["delta"])
sys.exit(1 if fails else 0)
