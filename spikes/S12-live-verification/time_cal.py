import datetime, json, sys, zoneinfo

sys.path.insert(0, ".")
from liveverify import api_get, GRID

results = []


def rec(name, ok, delta, note=""):
    results.append({"check": name, "pass": ok, "delta": delta, "note": note})
    print(f"{'PASS' if ok else 'FAIL'}  {name:<58} delta={delta} {note}")


# ---- JD / MJD ----
for t in [
    "2000-01-01T12:00:00Z",
    "2026-08-15T12:00:00Z",
    "1970-01-01T00:00:00Z",
    "2027-01-01T00:00:00Z",
]:
    d = api_get("/api/v1/time/julian-date", {"time": t})
    dt = datetime.datetime.fromisoformat(t.replace("Z", "+00:00"))
    jd_ref = (
        2440587.5
        + (
            dt - datetime.datetime(1970, 1, 1, tzinfo=datetime.timezone.utc)
        ).total_seconds()
        / 86400.0
    )
    rec(
        f"julian-date {t}",
        abs(d["julianDate"] - jd_ref) < 1e-9,
        f"{d['julianDate'] - jd_ref:+.3e}",
    )

# ---- time scales ----
d = api_get("/api/v1/time/time-scales", {"time": "2026-08-15T12:00:00Z"})
rec("TAI-UTC = 37", d["taiMinusUtcSeconds"] == 37.0, str(d["taiMinusUtcSeconds"]))
rec(
    "TT-UTC = 69.184",
    abs(d["ttMinusUtcSeconds"] - 69.184) < 0.001,
    f"{d['ttMinusUtcSeconds']:.4f}",
)
rec(
    "TDB-TT band < 1.7ms",
    abs(d["tdbMinusTtSeconds"]) < 0.0017,
    f"{d['tdbMinusTtSeconds'] * 1000:.3f} ms",
)
# UT1-UTC vs the live IERS Bulletin A (ser7) — same data family, validates dataset freshness + conversion
import requests

ser7 = requests.get("https://maia.usno.navy.mil/ser7/ser7.dat", timeout=30).text
rows = []
for line in ser7.splitlines():
    p = line.split()
    if len(p) >= 2:
        try:
            rows.append((float(p[0]), float(p[1])))
        except ValueError:
            pass
mjd_target = (
    2440587.5
    + (
        datetime.datetime(2026, 8, 15, 12, 0, tzinfo=datetime.timezone.utc)
        - datetime.datetime(1970, 1, 1, tzinfo=datetime.timezone.utc)
    ).total_seconds()
    / 86400.0
    - 2400000.5
)
ut1_ref = rows[-1][1]
rec(
    "UT1-UTC vs IERS ser7 (latest)",
    abs(d["ut1MinusUtcSeconds"] - ut1_ref) < 0.05,
    f"api={d['ut1MinusUtcSeconds']:.6f} iers={ut1_ref:.6f}",
)

# ---- calendars ----
cases = [
    ("2026-08-15", "Europe/Oslo"),
    ("2026-08-15", None),
    ("2027-01-01", "Europe/Oslo"),
    ("2026-08-15", "Asia/Singapore"),
    ("2026-08-15", "Australia/Sydney"),
    ("2026-08-15", "America/New_York"),
    ("2026-12-31", "Europe/Oslo"),
    ("2028-02-29", "Europe/Oslo"),
]
for date, tz in cases:
    c = api_get(
        "/api/v1/calendars/convert",
        {"date": date, "timezone": tz} if tz else {"date": date},
    )
    y, m, dnum = map(int, date.split("-"))
    iso = datetime.date(y, m, dnum).isocalendar()
    iso_ref = f"{iso.year}-W{iso.week:02d}-{iso.weekday}"
    ok_iso = c["isoWeekDate"] == iso_ref
    dt = datetime.datetime(y, m, dnum, tzinfo=datetime.timezone.utc)
    jd_ref = (
        2440587.5
        + (
            dt - datetime.datetime(1970, 1, 1, tzinfo=datetime.timezone.utc)
        ).total_seconds()
        / 86400.0
    )
    ok_jd = abs(c["julianDate"] - jd_ref) < 1e-6
    ok_dow = c["dayOfWeek"] == datetime.date(y, m, dnum).strftime("%A")
    ok_tz = True
    tznote = ""
    if tz:
        off = (
            datetime.datetime(y, m, dnum, 12, 0, tzinfo=zoneinfo.ZoneInfo(tz))
            .utcoffset()
            .total_seconds()
        )
        ok_tz = c["utcOffsetSeconds"] == int(off)
        tznote = f"offset={c['utcOffsetSeconds']} ref={int(off)}"
    rec(
        f"calendars/convert {date} {tz or '-'}",
        ok_iso and ok_jd and ok_dow and ok_tz,
        f"iso={c['isoWeekDate']} ref={iso_ref}",
        tznote,
    )

# range self-consistency vs convert
r = api_get("/api/v1/calendars/range", {"from": "2026-08-01", "to": "2026-08-31"})
c1 = api_get("/api/v1/calendars/convert", {"date": "2026-08-15"})
entry15 = [e for e in r["entries"] if e["gregorianDate"] == "2026-08-15"][0]
rec(
    "calendars/range consistent with convert",
    entry15["julianDate"] == c1["julianDate"],
    f"{entry15['julianDate']} vs {c1['julianDate']}",
)

fails = [r for r in results if not r["pass"]]
json.dump(results, open("results_time_cal.json", "w"), indent=1)
print(f"\n{len(results) - len(fails)}/{len(results)} passed")
sys.exit(1 if fails else 0)
