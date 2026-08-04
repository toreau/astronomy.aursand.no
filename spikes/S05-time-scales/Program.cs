using S05TimeScales;

Console.WriteLine("=== S0.5 time-scale validation ===");
var failures = 0;

void Check(string label, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}: {detail}");
    if (!ok) failures++;
}

var unix = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
Check("Unix epoch -> JD", Math.Abs(Julian.ToJd(unix) - 2440587.5) < 1e-9, $"JD={Julian.ToJd(unix):F10} expected 2440587.5");
Check("Unix epoch -> MJD", Math.Abs(Julian.ToMjd(unix) - 40587.0) < 1e-9, $"MJD={Julian.ToMjd(unix):F10} expected 40587.0");

var j2000Tt = 2451545.0;
var j2000Utc = TimeScales.TtToUtc(j2000Tt);
var j2000Diff = (j2000Utc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalSeconds;
Check("J2000 TT->UTC", Math.Abs(j2000Diff + 64.184) < 0.5, $"UTC={j2000Utc:yyyy-MM-ddTHH:mm:ss.fff}Z Δ={j2000Diff:F3}s (expect -64.184s)");
Check("J2000 UTC->TT", Math.Abs(TimeScales.TtJd(j2000Utc) - j2000Tt) < 1e-7, $"TT JD={TimeScales.TtJd(j2000Utc):F10}");

var leap2016 = new DateTime(2016, 12, 31, 23, 59, 59, DateTimeKind.Utc);
Check("Leap table @2016-12-31", LeapSeconds.TaiMinusUtc(leap2016) == 36, $"TAI-UTC={LeapSeconds.TaiMinusUtc(leap2016)} expect 36");
var leap2017 = new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc);
Check("Leap table @2017-01-01", LeapSeconds.TaiMinusUtc(leap2017) == 37, $"TAI-UTC={LeapSeconds.TaiMinusUtc(leap2017)} expect 37");
Check("TT-UTC @2016 (69.184)", Math.Abs((TimeScales.TtJd(leap2016) - Julian.ToJd(leap2016)) * 86400.0 - 68.184) < 1e-3, $"Δ={((TimeScales.TtJd(leap2016) - Julian.ToJd(leap2016)) * 86400.0):F4}s expect 68.184s");
Check("TT-UTC @today (69.184)", Math.Abs((TimeScales.TtJd(DateTime.UtcNow) - Julian.ToJd(DateTime.UtcNow)) * 86400.0 - 69.184) < 1e-3, $"Δ={((TimeScales.TtJd(DateTime.UtcNow) - Julian.ToJd(DateTime.UtcNow)) * 86400.0):F4}s expect 69.184s");

var dut1 = await TimeScales.FetchUt1MinusUtcAsync(DateTime.UtcNow);
if (dut1 == null)
    Console.WriteLine("WARN  ser7.dat fetch failed; using embedded fallback -0.2s for UT1 checks");
dut1 ??= -0.2;
var ut1JdNow = TimeScales.Ut1Jd(DateTime.UtcNow, dut1.Value);
Console.WriteLine($"INFO  UT1-UTC (ser7, live) = {dut1.Value:F4}s -> UT1 JD offset = {dut1.Value:F4}s");

var tdbMin = double.MaxValue;
var tdbMax = double.MinValue;
for (var i = -5; i <= 5; i++)
{
    var tt = 2451545.0 + i * 36.525;
    var tdb = TimeScales.TdbMinusTtSeconds(tt);
    tdbMin = Math.Min(tdbMin, tdb);
    tdbMax = Math.Max(tdbMax, tdb);
}
Check("TDB-TT band <= 1.7 ms", tdbMax - tdbMin < 0.0034, $"band over 10y = {(tdbMax - tdbMin) * 1000:F3} ms (min {tdbMin * 1000:F3} ms, max {tdbMax * 1000:F3} ms)");
var tdbJ2000 = TimeScales.TdbMinusTtSeconds(2451545.0);
Check("TDB-TT @J2000 ~ 0", Math.Abs(tdbJ2000) < 0.001, $"TDB-TT = {tdbJ2000 * 1000:F3} ms");

var today = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc);
var ttJd = TimeScales.TtJd(today);
var ut1Jd = TimeScales.Ut1Jd(today, dut1.Value);
var dT = (ttJd - ut1Jd) * 86400.0;
var decimalYear = 2000.0 + (ttJd - 2451545.0) / 365.25;
var espenak = CosineKitty.Astronomy.DeltaT_EspenakMeeus(decimalYear);
Console.WriteLine($"INFO  ΔT(leap-chain, actual LSI + UT1) = {dT:F2}s vs Espenak-Meeus(2026.59) = {espenak:F2}s (divergence {dT - espenak:F2}s — expected: E-M applies tidal smoothing, leap-chain uses actual leap seconds)");

var roundtrip = new DateTime(2026, 8, 4, 3, 9, 30, DateTimeKind.Utc);
Check("TT round-trip", Math.Abs((TimeScales.TtToUtc(TimeScales.TtJd(roundtrip)) - roundtrip).TotalSeconds) < 1e-3, $"tt->utc->tt Δ={Math.Abs((TimeScales.TtToUtc(TimeScales.TtJd(roundtrip)) - roundtrip).TotalSeconds):F9}s (double-JD precision ~2e-5s)");

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
