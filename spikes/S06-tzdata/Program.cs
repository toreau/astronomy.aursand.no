using NodaTime;
using NodaTime.TimeZones;

Console.WriteLine("=== S0.6 tzdata pinning validation ===");
var checks = 0;
var failures = 0;

void Check(string label, bool ok, string detail)
{
    checks++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}: {detail}");
    if (!ok) failures++;
}

var source = TzdbDateTimeZoneSource.Default;
Console.WriteLine($"INFO  tzdb version: {source.VersionId} (from NodaTime {typeof(DateTimeZoneProviders).Assembly.GetName().Version})");
Console.WriteLine($"INFO  zones in tzdb: {source.GetIds().Count()}");

var sw = System.Diagnostics.Stopwatch.StartNew();
var oslo = DateTimeZoneProviders.Tzdb["Europe/Oslo"];
sw.Stop();
Console.WriteLine($"INFO  first zone lookup: {sw.ElapsedMilliseconds} ms");

var memBefore = GC.GetTotalMemory(true);
foreach (var id in source.GetIds().Take(100)) { var _ = DateTimeZoneProviders.Tzdb[id]; }
var memAfter = GC.GetTotalMemory(true);
Console.WriteLine($"INFO  memory delta after 100 zone lookups: {(memAfter - memBefore) / 1024.0 / 1024.0:F2} MB");

void CheckTransition(string zoneId, Instant before, Instant after, int beforeOffsetH, int afterOffsetH, string beforeName, string afterName)
{
    var tz = DateTimeZoneProviders.Tzdb[zoneId];
    var i1 = tz.GetZoneInterval(before);
    var i2 = tz.GetZoneInterval(after);
    var ok = i1.WallOffset.Seconds / 3600 == beforeOffsetH && i2.WallOffset.Seconds / 3600 == afterOffsetH &&
             i1.Name == beforeName && i2.Name == afterName;
    Check($"transition {zoneId} {i1.Name}->{i2.Name} @{before.ToDateTimeUtc():yyyy-MM-dd HH:mm}Z",
        ok, $"before {i1.WallOffset.Seconds / 3600}h/{i1.Name}, after {i2.WallOffset.Seconds / 3600}h/{i2.Name} (expect {beforeOffsetH}h/{beforeName} -> {afterOffsetH}h/{afterName})");
}

CheckTransition("Europe/Oslo",
    Instant.FromUtc(2026, 3, 29, 0, 59, 59), Instant.FromUtc(2026, 3, 29, 1, 0, 1), 1, 2, "CET", "CEST");
CheckTransition("Europe/Oslo",
    Instant.FromUtc(2026, 10, 25, 0, 59, 59), Instant.FromUtc(2026, 10, 25, 1, 0, 1), 2, 1, "CEST", "CET");
CheckTransition("America/New_York",
    Instant.FromUtc(2026, 3, 8, 6, 59, 59), Instant.FromUtc(2026, 3, 8, 7, 0, 1), -5, -4, "EST", "EDT");
CheckTransition("America/New_York",
    Instant.FromUtc(2026, 11, 1, 5, 59, 59), Instant.FromUtc(2026, 11, 1, 6, 0, 1), -4, -5, "EDT", "EST");
CheckTransition("Pacific/Auckland",
    Instant.FromUtc(2026, 4, 4, 13, 59, 59), Instant.FromUtc(2026, 4, 4, 14, 0, 1), 13, 12, "NZDT", "NZST");
CheckTransition("Pacific/Auckland",
    Instant.FromUtc(2026, 9, 26, 13, 59, 59), Instant.FromUtc(2026, 9, 26, 14, 0, 1), 12, 13, "NZST", "NZDT");

var oslo1900 = DateTimeZoneProviders.Tzdb["Europe/Oslo"].GetZoneInterval(Instant.FromUtc(1900, 1, 1, 12, 0, 0));
Check("pre-1970 history (Oslo 1900)", oslo1900.WallOffset.Seconds / 3600 == 1, $"offset {oslo1900.WallOffset.Seconds / 3600}h ({oslo1900.Name})");
var london1969 = DateTimeZoneProviders.Tzdb["Europe/London"].GetZoneInterval(Instant.FromUtc(1969, 1, 1, 12, 0, 0));
Check("British Standard Time (London 1969, year-round +1)", london1969.WallOffset.Seconds / 3600 == 1, $"offset {london1969.WallOffset.Seconds / 3600}h ({london1969.Name})");
var oslo1996 = DateTimeZoneProviders.Tzdb["Europe/Oslo"].GetZoneInterval(Instant.FromUtc(1996, 3, 31, 0, 59, 59));
var oslo1996b = DateTimeZoneProviders.Tzdb["Europe/Oslo"].GetZoneInterval(Instant.FromUtc(1996, 3, 31, 1, 0, 1));
Check("EU rule since 1996 (Oslo)", oslo1996.WallOffset.Seconds / 3600 == 1 && oslo1996b.WallOffset.Seconds / 3600 == 2, $"{oslo1996.Name}->{oslo1996b.Name}");

try
{
    var _ = oslo.AtStrictly(new LocalDateTime(2026, 3, 29, 2, 30));
    Check("DST gap 02:30 -> 03:30 skipped (strict throws)", false, "no exception - gap not detected");
}
catch (SkippedTimeException)
{
    Check("DST gap 02:30 -> 03:30 skipped (strict throws)", true, "SkippedTimeException");
}
try
{
    var _ = oslo.AtStrictly(new LocalDateTime(2026, 10, 25, 2, 30));
    Check("DST fold 02:30 ambiguous (strict throws)", false, "no exception - fold not detected");
}
catch (AmbiguousTimeException)
{
    Check("DST fold 02:30 ambiguous (strict throws)", true, "AmbiguousTimeException");
}

try
{
    var _ = Instant.FromUtc(2016, 12, 31, 23, 59, 60);
    Check("leap second 23:59:60 rejected", false, "accepted - leap seconds modeled (unexpected)");
}
catch (ArgumentOutOfRangeException)
{
    Check("leap second 23:59:60 rejected", true, "ArgumentOutOfRangeException - Noda Time does NOT model leap seconds");
}

var resolved = oslo.AtLeniently(new LocalDateTime(2026, 10, 25, 2, 30));
Check("lenient fold resolution (earlier occurrence)", resolved.Hour == 2 && resolved.Minute == 30 && resolved.Offset.Seconds / 3600 == 2,
    $"resolved to {resolved.LocalDateTime:yyyy-MM-dd HH:mm} {resolved.Offset} (first occurrence, CEST)");

var customStreamAvailable = false;
try
{
    var nzdUrl = "https://nodatime.org/tzdb/latest.txt";
    using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    var v = (await hc.GetStringAsync(nzdUrl)).Trim();
    Console.WriteLine($"INFO  nodatime.org/tzdb/latest.txt = {v} (compare with embedded {source.VersionId})");
    customStreamAvailable = true;
}
catch (Exception ex)
{
    Console.WriteLine($"INFO  nodatime.org tzdb feed unreachable: {ex.Message.Split('\n')[0]} (option (b) evaluation only)");
}
if (customStreamAvailable)
{
    try
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var data = await hc.GetByteArrayAsync("https://nodatime.org/tzdb/latest.nzd");
        using var ms = new MemoryStream(data);
        var custom = TzdbDateTimeZoneSource.FromStream(ms);
        Check("custom TzdbProvider from downloaded .nzd", custom.VersionId != null, $"loaded {custom.VersionId}, {custom.GetIds().Count()} zones, {data.Length / 1024} KB");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"INFO  custom .nzd download failed: {ex.Message.Split('\n')[0]}");
    }
}

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
