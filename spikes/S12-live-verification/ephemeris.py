import json, math, sys, datetime

sys.path.insert(0, ".")
from liveverify import api_get, horizons, GRID

results = []


def sep_arcsec(ra1, dec1, ra2, dec2):
    r1, d1, r2, d2 = map(math.radians, (ra1, dec1, ra2, dec2))
    c = math.sin(d1) * math.sin(d2) + math.cos(d1) * math.cos(d2) * math.cos(r1 - r2)
    return math.degrees(math.acos(max(-1, min(1, c)))) * 3600


def rec(name, ok, delta, note=""):
    results.append({"check": name, "pass": ok, "delta": delta, "note": note})
    print(f"{'PASS' if ok else 'FAIL'}  {name:<64} delta={delta} {note}")


HID = GRID["horizons_ids"]


def stop_of(epoch):
    return (
        datetime.datetime.fromisoformat(epoch.replace("Z", "+00:00"))
        + datetime.timedelta(minutes=1)
    ).strftime("%Y-%m-%d %H:%M")


def fetch_row(params):
    text = horizons(params)
    for line in text.splitlines():
        s = line.strip()
        if s.startswith("$$SOE"):
            continue
        if s.startswith("$$EOE"):
            break
        if s and not s.startswith("!") and s[0].isdigit():
            return s
    return None


# ---------------- positions grid: QUANTITIES='1,2,9,23' ----------------
for body in GRID["bodies"]:
    for epoch in GRID["epochs"]:
        date_str = epoch[:10] + " " + epoch[11:16]
        row = fetch_row(
            {
                "format": "text",
                "COMMAND": f"'{HID[body]}'",
                "OBJ_DATA": "'NO'",
                "MAKE_EPHEM": "'YES'",
                "EPHEM_TYPE": "'OBSERVER'",
                "CENTER": "'500@399'",
                "START_TIME": f"'{date_str}'",
                "STOP_TIME": f"'{stop_of(epoch)}'",
                "STEP_SIZE": "'1d'",
                "QUANTITIES": "'1,2,9,23'",
                "CSV_FORMAT": "'YES'",
                "ANG_FORMAT": "'DEG'",
                "CAL_FORMAT": "'CAL'",
                "EXTRA_PREC": "'NO'",
            }
        )
        if row is None:
            rec(f"pos {body} {epoch}", False, "no row")
            continue
        f = [x.strip() for x in row.split(",")]
        try:
            ra_icrf, dec_icrf, ra_app, dec_app = (
                float(f[3]),
                float(f[4]),
                float(f[5]),
                float(f[6]),
            )
            mag_h, elong_h = float(f[7]), float(f[9])
        except (ValueError, IndexError):
            rec(f"pos {body} {epoch}", False, f"parse: {f}")
            continue

        for prec, tol in (("consumer", 30.0), ("reference", 2.0)):
            c = api_get(
                f"/api/v1/ephemeris/{body}/position",
                {
                    "time": epoch,
                    "frame": "icrs",
                    "positionType": "astrometric",
                    "precision": prec,
                },
            )
            d = sep_arcsec(
                ra_icrf, dec_icrf, c["rightAscensionDeg"], c["declinationDeg"]
            )
            rec(f"{prec} icrs {body} {epoch[:10]}", d < tol, f'{d:.3f}"')
            c = api_get(
                f"/api/v1/ephemeris/{body}/position",
                {
                    "time": epoch,
                    "frame": "of-date",
                    "positionType": "apparent",
                    "precision": prec,
                },
            )
            d = sep_arcsec(ra_app, dec_app, c["rightAscensionDeg"], c["declinationDeg"])
            tol2 = 30.0 if prec == "consumer" else 3.0
            rec(f"{prec} of-date {body} {epoch[:10]}", d < tol2, f'{d:.3f}"')

        if body not in ("sun", "moon"):
            v = api_get(
                f"/api/v1/ephemeris/{body}/visibility",
                {"time": epoch, "latitude": 59.9, "longitude": 10.7},
            )
            dm = abs(v["magnitude"] - mag_h)
            rec(
                f"mag {body} {epoch[:10]}",
                dm < 0.3,
                f"{dm:.3f} (api={v['magnitude']:.2f} h={mag_h:.2f})",
            )
            de = abs(v["elongationDeg"] - elong_h)
            rec(f"elongation {body} {epoch[:10]}", de < 0.5, f"{de:.3f}°")

# ---------------- horizontal sample ----------------
for body in ["sun", "moon", "mars"]:
    for loc_name, lat, lon, elev in [
        ("oslo", 59.9, 10.7, 0),
        ("sydney", -33.87, 151.21, 0),
    ]:
        for epoch in ["2026-08-15T12:00:00Z", "2027-01-01T00:00:00Z"]:
            date_str = epoch[:10] + " " + epoch[11:16]
            row = fetch_row(
                {
                    "format": "text",
                    "COMMAND": f"'{HID[body]}'",
                    "OBJ_DATA": "'NO'",
                    "MAKE_EPHEM": "'YES'",
                    "EPHEM_TYPE": "'OBSERVER'",
                    "CENTER": "'coord@399'",
                    "SITE_COORD": f"'{lon},{lat},{elev}'",
                    "START_TIME": f"'{date_str}'",
                    "STOP_TIME": f"'{stop_of(epoch)}'",
                    "STEP_SIZE": "'1d'",
                    "QUANTITIES": "'4,5'",
                    "CSV_FORMAT": "'YES'",
                    "ANG_FORMAT": "'DEG'",
                    "CAL_FORMAT": "'CAL'",
                    "EXTRA_PREC": "'NO'",
                }
            )
            if row is None:
                rec(f"horiz {body} {loc_name} {epoch[:10]}", False, "no row")
                continue
            f = [x.strip() for x in row.split(",")]
            try:
                az_h, el_h = float(f[3]), float(f[4])
            except (ValueError, IndexError):
                rec(f"horiz {body} {loc_name} {epoch[:10]}", False, f"parse {f}")
                continue
            c = api_get(
                f"/api/v1/ephemeris/{body}/position",
                {
                    "time": epoch,
                    "frame": "horizontal",
                    "positionType": "apparent",
                    "refraction": "none",
                    "latitude": lat,
                    "longitude": lon,
                    "elevationMeters": elev,
                    "precision": "consumer",
                },
            )
            az_a, el_a = c["azimuthDeg"], c["altitudeDeg"]
            daz = min(abs(az_a - az_h), 360 - abs(az_a - az_h))
            del_ = abs(el_a - el_h)
            rec(
                f"horiz consumer {body} {loc_name} {epoch[:10]}",
                daz < 0.3 and del_ < 0.3,
                f"altΔ={del_:.3f}° azΔ={daz:.3f}° (api {el_a:.2f}/{az_a:.2f} h {el_h:.2f}/{az_h:.2f})",
            )

# ---------------- rise/set: sun vs sunrise-sunset.org; moon/planets vs skyfield ----------------
import requests as _req

def tdiff_iso(a, b):
    if not a or not b:
        return None
    ta = datetime.datetime.fromisoformat(a.replace("Z", "+00:00"))
    tb = datetime.datetime.fromisoformat(b.replace("Z", "+00:00"))
    d = (ta - tb).total_seconds()
    return abs(d) if abs(d) < 12 * 3600 else None

for date in ["2026-08-15", "2027-01-01", "2026-06-21", "2026-12-21"]:
    r = _req.get("https://api.sunrise-sunset.org/json",
                 params={"lat": 59.9, "lng": 10.7, "date": date, "formatted": 0}, timeout=30)
    j = r.json()["results"]
    api = api_get("/api/v1/ephemeris/sun/rise-set", {"date": date, "latitude": 59.9, "longitude": 10.7})
    dr = tdiff_iso(api["riseUtc"], j["sunrise"])
    ds = tdiff_iso(api["setUtc"], j["sunset"])
    rec(f"sun rise/set {date} vs sunrise-sunset.org", (dr or 999) < 180 and (ds or 999) < 180,
        f"riseΔ={dr}s setΔ={ds}s")
    tw = api_get("/api/v1/ephemeris/twilight",
                 {"date": date, "latitude": 59.9, "longitude": 10.7, "type": "civil"})
    dc = tdiff_iso(tw["beginUtc"], j["civil_twilight_begin"])
    rec(f"sun civil twilight {date} vs sunrise-sunset.org", (dc or 999) < 240, f"beginΔ={dc}s")

from skyfield.api import load as _load, wgs84 as _wgs84
from skyfield import almanac as _almanac
_ts = _load.timescale()
_eph = _load("de421.bsp")
_oslo = _wgs84.latlon(59.9, 10.7, elevation_m=0)

for body, skykey in [("moon", "moon"), ("mars", "mars"), ("jupiter", "jupiter barycenter")]:
    for date in ["2026-08-15", "2027-01-01"]:
        y, m, d = map(int, date.split("-"))
        t0 = _ts.utc(y, m, d)
        f = _almanac.risings_and_settings(_eph, _eph[skykey], _oslo, horizon_degrees=-0.8333)
        times, events = _almanac.find_discrete(_ts.utc(y, m, d - 1), _ts.utc(y, m, d + 2), f)
        sf_events = []
        for t, e in zip(times, events):
            sf_events.append((t.utc_datetime(), "rise" if e == 0 else "set"))
        api = api_get(f"/api/v1/ephemeris/{body}/rise-set",
                      {"date": date, "latitude": 59.9, "longitude": 10.7})
        api_events = [(datetime.datetime.fromisoformat(api["riseUtc"].replace("Z", "+00:00")), "rise"),
                      (datetime.datetime.fromisoformat(api["setUtc"].replace("Z", "+00:00")), "set")]
        deltas = []
        for at, akind in api_events:
            if at is None:
                continue
            best = min(sf_events, key=lambda s: abs((s[0] - at).total_seconds()), default=None)
            if best is None:
                deltas.append(None)
                continue
            ds = abs((best[0] - at).total_seconds())
            deltas.append(ds if ds < 3600 else None)
        ok = all(x is not None and x < 240 for x in deltas) and len(deltas) == 2
        rec(f"{body} rise/set {date} vs skyfield", ok,
            f"riseΔ={deltas[0] if deltas else None}s setΔ={deltas[1] if len(deltas) > 1 else None}s")

json.dump(results, open("results_ephemeris.json", "w"), indent=1)
fails = [r for r in results if not r["pass"]]
print(f"\n{len(results) - len(fails)}/{len(results)} passed, {len(fails)} deviations")
for r in fails:
    print("  DEVIATION:", r["check"], "|", r["delta"])
sys.exit(1 if fails else 0)
