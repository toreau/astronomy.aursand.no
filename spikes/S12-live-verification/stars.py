import datetime, json, math, sys

sys.path.insert(0, ".")
from liveverify import api_get

import requests

results = []


def rec(name, ok, delta, note=""):
    results.append({"check": name, "pass": ok, "delta": delta, "note": note})
    print(f"{'PASS' if ok else 'FAIL'}  {name:<58} delta={delta} {note}")


def vizier(hip):
    r = requests.get(
        "https://vizier.cds.unistra.fr/viz-bin/asu-tsv",
        params={
            "-source": "I/239/hip_main",
            "HIP": hip,
            "-out.add": "RAICRS,DECICRS,_RAJ2000,_DEJ2000,pmRA,pmDE,Vmag",
            "-out.max": "1",
        },
        timeout=60,
    )
    for line in r.text.splitlines():
        if line.startswith("#") or line.startswith("-") or "\t" not in line:
            continue
        f = line.split("\t")
        if len(f) >= 8 and f[0].strip().replace(".", "", 1).replace("-", "").isdigit():
            return {
                "ra2000": float(f[2]),
                "dec2000": float(f[3]),
                "pmra": float(f[4]),
                "pmde": float(f[5]),
                "vmag": float(f[6]),
            }
    return None


def sep_arcsec(ra1, dec1, ra2, dec2):
    r1, d1, r2, d2 = map(math.radians, (ra1, dec1, ra2, dec2))
    c = math.sin(d1) * math.sin(d2) + math.cos(d1) * math.cos(d2) * math.cos(r1 - r2)
    return math.degrees(math.acos(max(-1, min(1, c)))) * 3600


J2000 = datetime.datetime(2000, 1, 1, 12, 0, tzinfo=datetime.timezone.utc)
EPOCH = "2026-08-15T12:00:00Z"
t = datetime.datetime.fromisoformat(EPOCH.replace("Z", "+00:00"))
years = (t - J2000).total_seconds() / 86400.0 / 365.25

STARS = [
    ("32349", "Sirius"),
    ("91262", "Vega"),
    ("69673", "Arcturus"),
    ("80763", "Antares"),
    ("11767", "Polaris"),
    ("24436", "Rigel"),
    ("24608", "Capella"),
    ("27989", "Betelgeuse"),
    ("97649", "Altair"),
    ("102098", "Deneb"),
]

for hip, name in STARS:
    v = vizier(hip)
    if v is None:
        rec(f"vizier {hip}", False, "no data")
        continue
    api = api_get(
        f"/api/v1/stars/{hip}/position",
        {"time": EPOCH, "frame": "icrs", "positionType": "astrometric"},
    )
    # API convention: PmRaMasYr applied as mu_alpha (divided by cos(dec) at use).
    dec_rad = math.radians(v["dec2000"])
    ra_expected = v["ra2000"] + v["pmra"] / math.cos(dec_rad) * years / 3_600_000.0
    dec_expected = v["dec2000"] + v["pmde"] * years / 3_600_000.0
    d = sep_arcsec(
        ra_expected, dec_expected, api["position"]["raDeg"], api["position"]["decDeg"]
    )
    ok = d < 1.5
    rec(f"star position {name} ({hip})", ok, f'{d:.3f}"')
    if not ok:
        # try the mu_alpha* convention (no cos division)
        ra_alt = v["ra2000"] + v["pmra"] * years / 3_600_000.0
        d2 = sep_arcsec(
            ra_alt, dec_expected, api["position"]["raDeg"], api["position"]["decDeg"]
        )
        rec(f"star position {name} alt-convention", d2 < 1.5, f'{d2:.3f}"')

    n = api_get("/api/v1/stars/name", {"name": name.lower()})
    ok_name = any(r["catalogueId"] == hip for r in n)
    rec(f"star name {name}", ok_name, f"{len(n)} results")
    dm = abs(api["vmag"] - v["vmag"])
    rec(f"star vmag {name}", dm < 0.05, f"{dm:.3f}")

# brightest top-10 vs VizieR magnitudes
bright = api_get("/api/v1/stars/brightest", {"limit": 10})
ref = [
    ("Sirius", -1.44),
    ("Canopus", -0.62),
    ("Rigil Kentaurus", -0.01),
    ("Arcturus", -0.05),
    ("Vega", 0.03),
    ("Capella", 0.08),
    ("Rigel", 0.18),
    ("Procyon", 0.4),
    ("Betelgeuse", 0.42),
    ("Achernar", 0.45),
]
names_api = [b["name"] for b in bright["stars"]]
ok = all(rn in names_api for rn, _ in ref[:5])
rec("stars/brightest top-5 canonical", ok, str(names_api[:5]))

# star rise/set vs skyfield (independent analytic)
from skyfield.api import load as _load, wgs84 as _wgs84
from skyfield import almanac as _almanac

_ts = _load.timescale()
_eph = _load("de421.bsp")
_oslo = _wgs84.latlon(59.9, 10.7, elevation_m=0)
for hip, name, ra, dec in [
    ("32349", "Sirius", 101.287155, -16.716116),
    ("91262", "Vega", 279.234735, 38.783689),
    ("69673", "Arcturus", 213.915300, 19.182409),
]:
    for date in ["2026-08-15", "2026-12-15"]:
        y, m, d = map(int, date.split("-"))
        f = (
            _almanac.risings_and_settings(
                _eph, _eph["star" + hip], _oslo, horizon_degrees=-0.5667
            )
            if False
            else None
        )
        # skyfield stars: use positional star from ICRS RA/Dec via load_star
        from skyfield.starlib import Star as _Star

        star = _Star(ra_hours=ra / 15.0, dec_degrees=dec)
        f = _almanac.risings_and_settings(_eph, star, _oslo, horizon_degrees=-0.5667)
        times, events = _almanac.find_discrete(
            _ts.utc(y, m, d - 1), _ts.utc(y, m, d + 2), f
        )
        sf = [(t.utc_datetime(), e) for t, e in zip(times, events)]
        api = api_get(
            f"/api/v1/stars/{hip}/rise-set",
            {"date": date, "latitude": 59.9, "longitude": 10.7},
        )
        if api["riseUtc"] is None and not sf:
            # Both agree: circumpolar (no rise/set events).
            rec(f"star rise/set {name} {date} vs skyfield", True, "circumpolar (both)")
            continue
        deltas = []
        for at, label in ((api["riseUtc"], 0), (api["setUtc"], 1)):
            if at is None:
                deltas.append(None)
                continue
            atd = datetime.datetime.fromisoformat(at.replace("Z", "+00:00"))
            best = min(
                sf, key=lambda s: abs((s[0] - atd).total_seconds()), default=None
            )
            ds = abs((best[0] - atd).total_seconds()) if best else None
            deltas.append(ds if ds is not None and ds < 3600 else None)
        ok = all(x is not None and x < 300 for x in deltas)
        rec(
            f"star rise/set {name} {date} vs skyfield",
            ok,
            f"riseΔ={deltas[0]}s setΔ={deltas[1]}s",
        )

json.dump(results, open("results_stars.json", "w"), indent=1)
fails = [r for r in results if not r["pass"]]
print(f"\n{len(results) - len(fails)}/{len(results)} passed, {len(fails)} deviations")
for r in fails:
    print("  DEVIATION:", r["check"], "|", r["delta"])
sys.exit(1 if fails else 0)
