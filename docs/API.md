# API Reference

Base URL: `https://astronomy.aursand.no`

All endpoints are `GET`. Responses are JSON; errors follow RFC 7807
(`application/problem+json`) with an `AST-*` code (see the table at the end).

Every successful response includes a `metadata` object:

```json
"metadata": {
  "datasets": [{ "name": "spice:de441.bsp", "version": "f67f876c(head3154MiB)" }, ...],
  "algorithms": [{ "name": "spice-de441", "version": "N66:j2000-astrometric" }],
  "warnings": [{ "code": "AST-7004", "message": "TLE age 41h exceeds 72h; SGP4 accuracy degrades with element age" }]
}
```

Common query parameters:

| Parameter | Values | Notes |
|---|---|---|
| `time` | ISO-8601 UTC, e.g. `2026-08-05T12:00:00Z` | Defaults to now where optional |
| `precision` | `consumer` \| `advanced` \| `reference` | See README tier table |
| `frame` | `icrs` \| `of-date` \| `horizontal` | `icrs` default |
| `positionType` | `astrometric` \| `apparent` | `icrs`+`astrometric`, `of-date`+`apparent` |
| `refraction` | `none` \| `simple` | Alt/az only |
| `latitude`, `longitude`, `elevationMeters` | numbers | Required for horizontal/observer endpoints |

---

## System

### `GET /health/live`

```bash
curl https://astronomy.aursand.no/health/live
```

```json
{ "status": "ok" }
```

Liveness probe: 200 whenever the process is up.

### `GET /health/ready`

```bash
curl https://astronomy.aursand.no/health/ready
```

```json
{
  "status": "ready",
  "db": "ok",
  "kernels": "ok",
  "starCatalog": "ok",
  "datasets": {
    "leap-seconds": "20260805",
    "eop-ut1": "20260805",
    "eop-c04": "20260805",
    "star-catalog-hyg": "v38",
    "satellite-elements": "20260805"
  },
  "satelliteElements": "ok (20260805, 22 elements)"
}
```

Readiness probe: verifies the database is reachable and the registry schema exists
(provider-neutral — SQLite in dev/tests, PostgreSQL in production), then reports each
component (reference kernels, star catalog, active dataset versions, satellite
elements). Returns 200 whenever the database is healthy —
components report `unavailable (...)` without failing the probe — and
503 with `"status": "not-ready"` when the database or schema is broken.

### Position endpoints and observers

`latitude`/`longitude` are **required** when `frame=horizontal` (they define
the observer). For `icrs`/`of-date` they are optional and unused.

---

## Time

### `GET /api/v1/time/julian-date`

`?time=2000-01-01T12:00:00Z`

```bash
curl "https://astronomy.aursand.no/api/v1/time/julian-date?time=2000-01-01T12:00:00Z"
```

```json
{
  "julianDate": 2451545,
  "modifiedJulianDate": 51544.5,
  "utc": "2000-01-01T12:00:00.0000000+00:00",
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "time-scale-converter", "version": "leap-chain-1.0" } ], "warnings": [] }
}
```

### `GET /api/v1/time/time-scales`

`?time=2026-08-05T12:00:00Z`

```bash
curl "https://astronomy.aursand.no/api/v1/time/time-scales?time=2026-08-05T12:00:00Z"
```

```json
{
  "utc": "2026-08-05T12:00:00.0000000+00:00",
  "taiJd": 2461258.0004282407,
  "ttJd": 2461258.000800741,
  "ut1Jd": 2461258.000004236,
  "tdbJd": 2461258.000800731,
  "taiMinusUtcSeconds": 37,
  "ttMinusUtcSeconds": 69.184,
  "ut1MinusUtcSeconds": 0.366,
  "tdbMinusTtSeconds": -0.000828,
  "leapSecondDatasetVersion": "iers-2026a",
  "eopDatasetVersion": "20260804",
  "algorithmVersion": "leap-chain-1.0"
}
```

---

## Calendars

### `GET /api/v1/calendars/convert`

`?date=2026-08-05&timezone=Europe/Oslo`

```bash
curl "https://astronomy.aursand.no/api/v1/calendars/convert?date=2026-08-05&timezone=Europe/Oslo"
```

```json
{
  "gregorianDate": "2026-08-05",
  "isoWeekDate": "2026-W32-3",
  "julianDate": 2461257.5,
  "dayOfWeek": "Wednesday",
  "timeZone": "Europe/Oslo",
  "localTime": "2026-08-05T12:00:00+02:00",
  "utcOffsetSeconds": 7200,
  "metadata": { "datasets": [ { "name": "tzdb", "version": "TZDB: 2026c (mapping: 48.2)" } ], "algorithms": [ { "name": "gregorian-conversion", "version": "1.0" } ], "warnings": [] }
}
```

### `GET /api/v1/calendars/date-arithmetic`

`?date=2026-08-05&days=7`

```bash
curl "https://astronomy.aursand.no/api/v1/calendars/date-arithmetic?date=2026-08-05&days=7"
```

```json
{
  "startDate": "2026-08-05",
  "daysAdded": 7,
  "resultDate": "2026-08-12",
  "timeZone": null,
  "metadata": { "datasets": [ { "name": "tzdb", "version": "TZDB: 2026c (mapping: 48.2)" } ], "algorithms": [ { "name": "gregorian-conversion", "version": "1.0" } ], "warnings": [] }
}
```

### `GET /api/v1/calendars/range`

`?from=2026-01-01&to=2026-12-31&timezone=Europe/Oslo` (inclusive; max span 366 days)

```bash
curl "https://astronomy.aursand.no/api/v1/calendars/range?from=2026-01-01&to=2026-12-31&timezone=Europe/Oslo"
```

```json
{
  "from": "2026-01-01",
  "to": "2026-12-31",
  "timeZone": "Europe/Oslo",
  "entries": [
    {
      "gregorianDate": "2026-01-01",
      "isoWeekDate": "2026-W01-4",
      "julianDate": 2461041.5,
      "dayOfWeek": "Thursday",
      "timeZone": "Europe/Oslo",
      "localTime": "2026-01-01T12:00:00+01:00",
      "utcOffsetSeconds": 3600,
      "metadata": { "datasets": [ { "name": "tzdb", "version": "TZDB: 2026c (mapping: 48.2)" } ], "algorithms": [ { "name": "gregorian-conversion", "version": "1.0" } ], "warnings": [] }
    }
  ],
  "metadata": { "datasets": [ { "name": "tzdb", "version": "TZDB: 2026c (mapping: 48.2)" } ], "algorithms": [ { "name": "gregorian-conversion", "version": "1.0" } ], "warnings": [] }
}
```

Each day is a `DateConversionResult` (same shape as `/calendars/convert`); an
unknown timezone yields the per-entry `AST-6001` warning. `from` after `to`, or
a span over 366 days, returns `400 AST-4001`. Cached 3600 s.

---

## Ephemeris

### `GET /api/v1/ephemeris/{body}/position`

Bodies: `sun`, `moon`, `mercury`, `venus`, `mars`, `jupiter`, `saturn`,
`uranus`, `neptune`.

#### Consumer, ICRS astrometric

```bash
curl "https://astronomy.aursand.no/api/v1/ephemeris/sun/position?time=2026-08-05T12:00:00Z&frame=icrs&positionType=astrometric&refraction=none&precision=consumer"
```

```json
{
  "body": "sun",
  "rightAscensionDeg": 134.228885,
  "declinationDeg": 17.255834,
  "altitudeDeg": null,
  "azimuthDeg": null,
  "distanceKm": 151771210.9,
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:j2000-astrometric" } ], "warnings": [] }
}
```

#### Reference precision (SPICE DE441 + ERFA), of-date apparent

```bash
curl "https://astronomy.aursand.no/api/v1/ephemeris/moon/position?time=2026-08-05T22:00:00Z&frame=of-date&positionType=apparent&refraction=none&precision=reference"
```

```json
{
  "body": "moon",
  "rightAscensionDeg": 37.06911,
  "declinationDeg": 19.90277,
  "altitudeDeg": null,
  "azimuthDeg": null,
  "distanceKm": 379443.0,
  "metadata": {
    "datasets": [
      { "name": "leap-seconds", "version": "iers-2026a" },
      { "name": "eop-ut1", "version": "20260804" },
      { "name": "spice:de441.bsp", "version": "f67f876c(head3154MiB)" },
      { "name": "spice:naif0012.tls", "version": "678e32bd" },
      { "name": "spice:pck00010.tpc", "version": "59468328" }
    ],
    "algorithms": [ { "name": "spice-de441", "version": "N66:of-date-apparent:erfa" } ],
    "warnings": []
  }
}
```

#### Horizontal (reference tier — ERFA C2T + EOP C04)

```bash
curl "https://astronomy.aursand.no/api/v1/ephemeris/sun/position?time=2026-08-05T11:23:13Z&latitude=59.9&longitude=10.7&elevationMeters=0&frame=horizontal&positionType=apparent&refraction=none&precision=reference"
```

```json
{
  "body": "sun",
  "rightAscensionDeg": 0,
  "declinationDeg": 0,
  "altitudeDeg": 46.992,
  "azimuthDeg": 179.998,
  "distanceKm": 0,
  "metadata": {
    "datasets": [
      { "name": "leap-seconds", "version": "iers-2026a" },
      { "name": "eop-ut1", "version": "20260804" },
      { "name": "eop-c04", "version": "20260805" },
      { "name": "spice:de441.bsp", "version": "f67f876c(head3154MiB)" },
      { "name": "spice:naif0012.tls", "version": "678e32bd" },
      { "name": "spice:pck00010.tpc", "version": "59468328" }
    ],
    "algorithms": [ { "name": "spice-de441", "version": "N66:horizontal:erfa-c2t" } ],
    "warnings": []
  }
}
```

#### Errors

- `frame=of-date&positionType=astrometric` → 400 `AST-4001` (of-date requires `apparent`).
- `precision=reference` before 1900-01-01 → 400 `AST-4001` (validated era).
- Reference kernels unavailable → 503 `AST-5030`.

```json
{
  "type": "https://astronomy.aursand.no/errors/AST-4001",
  "title": "ArgumentException",
  "status": 400,
  "detail": "of-date reference positions require positionType=apparent (ERFA IAU2000A chain)",
  "instance": "/api/v1/ephemeris/sun/position",
  "code": "AST-4001"
}
```

### `GET /api/v1/ephemeris/{body}/rise-set`

`?date=2026-08-05&latitude=59.9&longitude=10.7`

```bash
curl "https://astronomy.aursand.no/api/v1/ephemeris/sun/rise-set?date=2026-08-05&latitude=59.9&longitude=10.7"
```

```json
{
  "body": "sun",
  "riseUtc": "2026-08-05T03:07:51.5047723Z",
  "setUtc": "2026-08-05T19:36:53.1330111Z",
  "transitUtc": "2026-08-05T11:23:13.2990061Z",
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:rise-set" } ], "warnings": [] }
}
```

### `GET /api/v1/ephemeris/twilight`

`?date=2026-08-05&latitude=59.9&longitude=10.7&type=nautical`

```bash
curl "https://astronomy.aursand.no/api/v1/ephemeris/twilight?date=2026-08-05&latitude=59.9&longitude=10.7&type=nautical"
```

```json
{
  "type": "nautical",
  "beginUtc": "2026-08-05T00:27:10.0664851Z",
  "endUtc": "2026-08-05T22:12:24.7503286Z",
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:twilight" } ], "warnings": [] }
}
```

`type`: `civil` (0), `nautical` (1), `astronomical` (2).

### `GET /api/v1/ephemeris/moon/phases`

`?from=2026-08-01T00:00:00Z&to=2026-09-01T00:00:00Z`

```json
{
  "from": "2026-08-01T00:00:00Z",
  "to": "2026-09-01T00:00:00Z",
  "events": [
    { "utc": "2026-08-06T02:21:58.6727484Z", "phase": "Last Quarter", "illuminationFraction": 0.5 },
    { "utc": "2026-08-12T17:37:11.3439753Z", "phase": "New Moon", "illuminationFraction": 0 }
  ],
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:moon-phases" } ], "warnings": [] }
}
```

### `GET /api/v1/ephemeris/{body}/visibility`

`?time=2026-08-05T22:00:00Z&latitude=59.9&longitude=10.7` (planets only)

```json
{
  "body": "jupiter",
  "magnitude": -2.2,
  "elongationDeg": 78.4,
  "visibilityStatus": "visible",
  "constellation": "Leo",
  "altitudeDeg": 23.1,
  "azimuthDeg": 214.5,
  "visibleTonight": true,
  "nakedEyeVisible": true,
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:visibility" } ], "warnings": [] }
}
```

### `GET /api/v1/ephemeris/events`

`?from=2026-01-01T00:00:00Z&to=2027-01-01T00:00:00Z&bodies=venus&types=opposition,max-elongation`

```json
{
  "from": "2026-01-01T00:00:00Z",
  "to": "2027-01-01T00:00:00Z",
  "events": [
    {
      "body": "venus",
      "type": "max-elongation",
      "utc": "2026-08-15T06:23:02.0043954Z",
      "elongationDeg": 45.891,
      "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:events" } ], "warnings": [] }
    }
  ],
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:events" } ], "warnings": [] }
}
```

Event types: `opposition`, `conjunction`, `max-elongation` (mercury/venus only).

---

## Stars

Catalog: HYG v3.8 (`star-catalog-hyg`, 119,625 stars). Positions are J2000
with proper-motion correction; `frame=of-date` precesses via the engine
rotation, `frame=horizontal` gives topocentric alt/az.

### `GET /api/v1/stars/search`

`?ra=101.287&dec=-16.716&radius=3&maxMagnitude=6.5&limit=5`

```bash
curl "https://astronomy.aursand.no/api/v1/stars/search?ra=101.287&dec=-16.716&radius=3&maxMagnitude=6.5&limit=5"
```

```json
[
  { "catalogue": "hyg", "catalogueId": "32349", "name": "Sirius", "raDeg": 101.283, "decDeg": -16.725, "vmag": -1.44 },
  { "catalogue": "hyg", "catalogueId": "31700", "name": "8Nu 3CMa", "raDeg": 99.473, "decDeg": -18.238, "vmag": 4.42 }
]
```

### `GET /api/v1/stars/name`

`?name=sirius`

```bash
curl "https://astronomy.aursand.no/api/v1/stars/name?name=sirius"
```

```json
[ { "catalogue": "hyg", "catalogueId": "32349", "name": "Sirius", "raDeg": 101.283, "decDeg": -16.725, "vmag": -1.44 } ]
```

### `GET /api/v1/stars/brightest`

`?limit=5`

```json
{
  "stars": [
    { "hip": "32349", "name": "Sirius", "constellation": "Canis Major", "raDeg": 101.283, "decDeg": -16.725, "vmag": -1.44 },
    { "hip": "30438", "name": "Canopus", "constellation": "Carina", "raDeg": 95.988, "decDeg": -52.696, "vmag": -0.62 },
    { "hip": "69673", "name": "Arcturus", "constellation": "Bootes", "raDeg": 213.915, "decDeg": 19.182, "vmag": -0.05 },
    { "hip": "71683", "name": "Rigil Kentaurus", "constellation": "Centaurus", "raDeg": 219.902, "decDeg": -60.834, "vmag": -0.01 },
    { "hip": "91262", "name": "Vega", "constellation": "Lyra", "raDeg": 279.235, "decDeg": 38.784, "vmag": 0.03 }
  ],
  "metadata": { "datasets": [ { "name": "star-catalog-hyg", "version": "v38" } ], "algorithms": [ { "name": "hyg-star-catalog", "version": "v38:proper-motion:brightest" } ], "warnings": [] }
}
```

### `GET /api/v1/stars/{hip}/position`

`?time=2026-08-05T22:00:00Z&frame=icrs&positionType=astrometric`

```bash
curl "https://astronomy.aursand.no/api/v1/stars/32349/position?time=2026-08-05T22:00:00Z&frame=icrs&positionType=astrometric"
```

```json
{
  "hip": "32349",
  "name": "Sirius",
  "bayerFlamsteed": "9Alp CMa",
  "constellation": "Canis Major",
  "vmag": -1.44,
  "spectralType": "A0m...",
  "distLightYears": 8.6,
  "position": { "raDeg": 101.283, "decDeg": -16.725, "altDeg": null, "azDeg": null },
  "metadata": { "datasets": [ { "name": "star-catalog-hyg", "version": "v38" } ], "algorithms": [ { "name": "hyg-star-catalog", "version": "v38:proper-motion:j2000-astrometric" } ], "warnings": [] }
}
```

### `GET /api/v1/stars/{hip}/rise-set`

`?date=2026-08-05&latitude=59.9&longitude=10.7`

```bash
curl "https://astronomy.aursand.no/api/v1/stars/32349/rise-set?date=2026-08-05&latitude=59.9&longitude=10.7"
```

```json
{
  "hip": "32349",
  "riseUtc": "2026-08-05T05:08:04.9963343Z",
  "setUtc": "2026-08-05T13:07:28.1319015Z",
  "transitUtc": "2026-08-05T09:07:46.5641179Z",
  "circumpolar": false,
  "metadata": { "datasets": [ { "name": "star-catalog-hyg", "version": "v38" } ], "algorithms": [ { "name": "hyg-star-catalog", "version": "v38:proper-motion:rise-set" } ], "warnings": [] }
}
```

Circumpolar stars (e.g. Vega from Oslo): `circumpolar: true`, events `null`.
Star catalog not ingested → 503 `AST-5031`.

---

## Satellites

Elements: CelesTrak OMM (`satellite-elements`). Propagation: SGP4
(One_Sgp4), TEME → PEF → topocentric. TLE age > 72 h adds an `AST-7004`
warning.

### `GET /api/v1/satellites/{norad}/position`

`?time=2026-08-05T12:00:00Z&latitude=59.9&longitude=10.7`

```bash
curl "https://astronomy.aursand.no/api/v1/satellites/25544/position?time=2026-08-05T12:00:00Z&latitude=59.9&longitude=10.7"
```

```json
{
  "noradId": "25544",
  "name": "ISS (ZARYA)",
  "altitudeDeg": -26.554,
  "azimuthDeg": 93.685,
  "rangeKm": 6559.01,
  "subpointLatDeg": 24.175,
  "subpointLonDeg": 81.295,
  "subpointAltKm": 422.672,
  "tleAgeHours": 40.887,
  "metadata": { "datasets": [ { "name": "satellite-elements", "version": "20260804" } ], "algorithms": [ { "name": "sgp4", "version": "onesgp4-1.1.0:position" } ], "warnings": [] }
}
```

### `GET /api/v1/satellites/{norad}/passes`

`?date=2026-08-05&latitude=59.9&longitude=10.7&minElevation=10&stepSeconds=30`

```bash
curl "https://astronomy.aursand.no/api/v1/satellites/25544/passes?date=2026-08-05&latitude=59.9&longitude=10.7&minElevation=10"
```

```json
{
  "noradId": "25544",
  "name": "ISS (ZARYA)",
  "from": "2026-08-05T00:00:00Z",
  "to": "2026-08-06T00:00:00Z",
  "passes": [
    { "riseUtc": "2026-08-05T10:06:00Z", "maxElevationUtc": "2026-08-05T10:07:30Z", "maxElevationDeg": 12.6, "setUtc": "2026-08-05T10:09:00Z", "direction": "ascending", "minElevationDeg": 10.0 },
    { "riseUtc": "2026-08-05T11:41:00Z", "maxElevationUtc": "2026-08-05T11:43:30Z", "maxElevationDeg": 20.0, "setUtc": "2026-08-05T11:46:00Z", "direction": "ascending", "minElevationDeg": 10.0 }
  ],
  "metadata": { "datasets": [ { "name": "satellite-elements", "version": "20260804" } ], "algorithms": [ { "name": "sgp4", "version": "onesgp4-1.1.0:passes" } ], "warnings": [] }
}
```

Pass windows are capped at 7 days (`from`/`to` alternative to `date`).

### `GET /api/v1/satellites/search`

`?name=iss`

```json
[
  { "noradId": "49044", "name": "ISS (NAUKA)", "epochUtc": "2026-08-03T19:06:47.841984Z", "tleAgeHours": 41.0 },
  { "noradId": "25544", "name": "ISS (ZARYA)", "epochUtc": "2026-08-03T19:06:47.841984Z", "tleAgeHours": 41.0 }
]
```

### `GET /api/v1/satellites/status`

```json
{ "activeVersion": "20260804", "elementCount": 22, "fresh": 0, "warn": 22, "degraded": 0, "refuse": 0 }
```

Element dataset not ingested → 503 `AST-5032`.

---

## Almanac

### `GET /api/v1/almanac/daily`

`?date=2026-08-05&latitude=59.9&longitude=10.7&precision=consumer`

```bash
curl "https://astronomy.aursand.no/api/v1/almanac/daily?date=2026-08-05&latitude=59.9&longitude=10.7"
```

```json
{
  "date": "2026-08-05",
  "sun": {
    "sunriseUtc": "2026-08-05T03:07:51.5047723Z",
    "sunsetUtc": "2026-08-05T19:36:53.1330111Z",
    "solarNoonUtc": "2026-08-05T11:23:13.2990061Z",
    "civilTwilightBeginUtc": "2026-08-05T02:10:34.6136317Z",
    "civilTwilightEndUtc": "2026-08-05T20:33:26.6265096Z",
    "nauticalTwilightBeginUtc": "2026-08-05T00:27:10.0664851Z",
    "nauticalTwilightEndUtc": "2026-08-05T22:12:24.7503286Z",
    "astronomicalTwilightBeginUtc": null,
    "astronomicalTwilightEndUtc": null
  },
  "moon": {
    "moonriseUtc": "2026-08-05T20:14:16.543016Z",
    "moonsetUtc": "2026-08-05T12:42:50.9251711Z",
    "moonTransitUtc": "2026-08-05T04:10:41.133532Z",
    "phaseName": "Waxing Gibbous",
    "illuminationFraction": 0.568
  },
  "planets": [ { "body": "venus", "riseUtc": "...", "setUtc": "...", "transitUtc": "..." } ],
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:daily" } ], "warnings": [] }
}
```

### `GET /api/v1/almanac/monthly`

`?month=2026-08&latitude=59.9&longitude=10.7`

```json
{
  "month": "2026-08",
  "days": [ { "date": "2026-08-01", "sun": { "sunriseUtc": "...", "sunsetUtc": "..." }, "moon": { "moonriseUtc": "...", "moonsetUtc": "..." }, "planets": [ { "body": "mercury", "riseUtc": "...", "setUtc": "..." } ] } ],
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:monthly" } ], "warnings": [] }
}
```

### `GET /api/v1/almanac/monthly?year=2026` (whole year)

`?year=2026&latitude=59.9&longitude=10.7` — `year` and `month` are mutually
exclusive; providing both (or neither) returns `400 AST-4001`. The response
contains 12 `MonthlyAlmanacResult` sections (same shape as above), computed
concurrently:

```json
{
  "year": "2026",
  "months": [ { "month": "2026-01", "days": [ ... ], "events": [ ... ] }, { "month": "2026-02", ... } ],
  "metadata": { "datasets": [ { "name": "leap-seconds", "version": "iers-2026a" }, { "name": "eop-ut1", "version": "20260804" } ], "algorithms": [ { "name": "astronomy-engine", "version": "2.1.19:monthly" } ], "warnings": [] }
}
```

The full-year payload is ~0.5 MB raw; responses are brotli/gzip-compressed
(~60–90 KB over the wire). Cached 900 s.

---

## Error codes

| Code | Status | Meaning |
|---|---|---|
| `AST-4001` | 400 | Invalid request |
| `AST-5000` | 500 | Internal error |
| `AST-5010` | 501 | Feature not implemented in this phase |
| `AST-5030` | 503 | Reference tier unavailable (kernels/native lib missing) |
| `AST-5031` | 503 | Star catalog dataset not ingested |
| `AST-5032` | 503 | Satellite element dataset not ingested |
| `AST-7002` | 200 (warning) | Endpoint uses the consumer chain at advanced/reference precision |
| `AST-7003` | 200 (warning) | Horizontal reference chain degraded (EOP C04 absent) |
| `AST-7004` | 200 (warning) | Satellite TLE stale (> 72 h) |

Service-unavailable `detail` messages are path-redacted (filesystem paths are
replaced with `<path>`); full reasons are available in the server logs.
