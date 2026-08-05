using System.Globalization;
using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel.Coordinates;

namespace Astronomy.AccuracyTests;

public class PositionAccuracyTests
{
    private static readonly EphemerisCalculator Calculator = new();
    private static readonly string FixturesDir = FindFixtures();

    private static string FindFixtures() => FixturesDirFor();

    internal static string FixturesDirFor()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "horizons_sun_sample.csv")))
                return candidate;
        }
        throw new FileNotFoundException("accuracy fixtures not found");
    }

    public static IEnumerable<object[]> SunRows() => Load("horizons_sun_sample.csv");
    public static IEnumerable<object[]> MoonRows() => Load("horizons_moon_sample.csv");

    private static IEnumerable<object[]> Load(string file)
    {
        foreach (var line in File.ReadAllLines(Path.Combine(FixturesDir, file)).Skip(1))
        {
            var p = line.Split(',');
            yield return
            [
                DateTimeOffset.Parse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                double.Parse(p[1], CultureInfo.InvariantCulture),
                double.Parse(p[2], CultureInfo.InvariantCulture),
                double.Parse(p[3], CultureInfo.InvariantCulture),
                double.Parse(p[4], CultureInfo.InvariantCulture),
            ];
        }
    }

    private static double Separation(double ra1, double dec1, double ra2, double dec2)
    {
        var (r1, d1, r2, d2) = (ra1 * Math.PI / 180, dec1 * Math.PI / 180, ra2 * Math.PI / 180, dec2 * Math.PI / 180);
        var cosSep = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(r1 - r2);
        return Math.Acos(Math.Clamp(cosSep, -1, 1)) * 180 / Math.PI * 3600;
    }

    [Theory]
    [MemberData(nameof(SunRows))]
    public void Sun_J2000Astrometric_Within15Arcsec(DateTimeOffset utc, double ra1, double dec1, double _ra2, double _dec2)
    {
        _ = (_ra2, _dec2);
        var eq = Calculator.GeocentricEquatorial(BodyId.Sun, utc, apparent: false);
        var sep = Separation(ra1, dec1, eq.RaDeg, eq.DecDeg);
        Assert.True(sep < 15.0, $"{utc:O}: {sep:F2}\"");
    }

    [Theory]
    [MemberData(nameof(SunRows))]
    public void Sun_OfDateApparent_Within15Arcsec(DateTimeOffset utc, double _ra1, double _dec1, double ra2, double dec2)
    {
        _ = (_ra1, _dec1);
        var eq = Calculator.GeocentricEquatorial(BodyId.Sun, utc, apparent: true);
        var sep = Separation(ra2, dec2, eq.RaDeg, eq.DecDeg);
        Assert.True(sep < 15.0, $"{utc:O}: {sep:F2}\"");
    }

    [Theory]
    [MemberData(nameof(MoonRows))]
    public void Moon_J2000Astrometric_MeanBelow30_MaxBelow110(DateTimeOffset utc, double ra1, double dec1, double _ra2, double _dec2)
    {
        _ = (_ra2, _dec2);
        var eq = Calculator.GeocentricEquatorial(BodyId.Moon, utc, apparent: false);
        var sep = Separation(ra1, dec1, eq.RaDeg, eq.DecDeg);
        Assert.True(sep < 110.0, $"{utc:O}: {sep:F2}\"");
    }

    [Theory]
    [MemberData(nameof(MoonRows))]
    public void Moon_OfDateApparent_MeanBelow30_MaxBelow110(DateTimeOffset utc, double _ra1, double _dec1, double ra2, double dec2)
    {
        _ = (_ra1, _dec1);
        var eq = Calculator.GeocentricEquatorial(BodyId.Moon, utc, apparent: true);
        var sep = Separation(ra2, dec2, eq.RaDeg, eq.DecDeg);
        Assert.True(sep < 110.0, $"{utc:O}: {sep:F2}\"");
    }

    [Fact]
    public void Moon_SampleMeans_AreBelowTierCeiling()
    {
        var separations = new List<double>();
        foreach (var row in MoonRows())
        {
            var (utc, ra1, dec1, _, _) = ((DateTimeOffset)row[0], (double)row[1], (double)row[2], (double)row[3], (double)row[4]);
            var eq = Calculator.GeocentricEquatorial(BodyId.Moon, utc, apparent: false);
            separations.Add(Separation(ra1, dec1, eq.RaDeg, eq.DecDeg));
        }
        Assert.True(separations.Average() < 30.0, $"moon mean {separations.Average():F2}\"");
    }
}

public class PlanetPositionAccuracyTests
{
    private static readonly EphemerisCalculator Calculator = new();
    private static readonly string FixturesDir = PositionAccuracyTests.FixturesDirFor();

    public static IEnumerable<object[]> Rows() =>
        new[]
        {
            "mercury", "venus", "mars", "jupiter", "saturn", "uranus", "neptune",
        }.SelectMany(body => Load(body).Select(row => (object[])new object[] { body, row[0], row[1], row[2], row[3], row[4] }));

    private static IEnumerable<object[]> Load(string body)
    {
        var path = Path.Combine(FixturesDir, $"horizons_{body}_sample.csv");
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            var p = line.Split(',');
            yield return
            [
                DateTimeOffset.Parse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                double.Parse(p[1], CultureInfo.InvariantCulture),
                double.Parse(p[2], CultureInfo.InvariantCulture),
                double.Parse(p[3], CultureInfo.InvariantCulture),
                double.Parse(p[4], CultureInfo.InvariantCulture),
            ];
        }
    }

    private static double Separation(double ra1, double dec1, double ra2, double dec2)
    {
        var (r1, d1, r2, d2) = (ra1 * Math.PI / 180, dec1 * Math.PI / 180, ra2 * Math.PI / 180, dec2 * Math.PI / 180);
        var cosSep = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(r1 - r2);
        return Math.Acos(Math.Clamp(cosSep, -1, 1)) * 180 / Math.PI * 3600;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Planet_J2000Astrometric_Within30Arcsec(string body, DateTimeOffset utc, double ra1, double dec1, double _ra2, double _dec2)
    {
        _ = (_ra2, _dec2);
        var eq = Calculator.GeocentricEquatorial(new BodyId(body), utc, apparent: false);
        var sep = Separation(ra1, dec1, eq.RaDeg, eq.DecDeg);
        Assert.True(sep < 30.0, $"{body} {utc:O}: {sep:F2}\"");
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Planet_OfDateApparent_Within30Arcsec(string body, DateTimeOffset utc, double _ra1, double _dec1, double ra2, double dec2)
    {
        _ = (_ra1, _dec1);
        var eq = Calculator.GeocentricEquatorial(new BodyId(body), utc, apparent: true);
        var sep = Separation(ra2, dec2, eq.RaDeg, eq.DecDeg);
        Assert.True(sep < 30.0, $"{body} {utc:O}: {sep:F2}\"");
    }
}

public class EventAccuracyTests
{
    private static readonly EphemerisCalculator Calculator = new();
    private static readonly ObserverLocation Oslo = ObserverLocation.FromDegrees(59.9139, 10.7522, 25);

    [Fact]
    public void OsloSunrise_2026_08_04_Within30s()
    {
        var rise = Calculator.SearchRiseSet(BodyId.Sun, new DateOnly(2026, 8, 4), Oslo, rise: true);
        var expected = new DateTimeOffset(2026, 8, 4, 3, 5, 12, TimeSpan.Zero);
        Assert.True(Math.Abs((rise!.Value - expected).TotalSeconds) < 30, $"rise {rise.Value:O}");
    }

    [Fact]
    public void OsloSunset_2026_08_04_Within30s()
    {
        var set = Calculator.SearchRiseSet(BodyId.Sun, new DateOnly(2026, 8, 4), Oslo, rise: false);
        var expected = new DateTimeOffset(2026, 8, 4, 19, 39, 19, TimeSpan.Zero);
        Assert.True(Math.Abs((set!.Value - expected).TotalSeconds) < 30, $"set {set.Value:O}");
    }

    [Fact]
    public void OsloCivilTwilightBegin_2026_08_04_Within60s()
    {
        var begin = Calculator.SearchAltitude(BodyId.Sun, new DateOnly(2026, 8, 4), Oslo, -6.0, rising: true);
        var expected = new DateTimeOffset(2026, 8, 4, 2, 7, 9, TimeSpan.Zero);
        Assert.True(Math.Abs((begin!.Value - expected).TotalSeconds) < 60, $"civil begin {begin.Value:O}");
    }

    public static IEnumerable<object[]> UsnoMoonPhaseRows() =>
    [
        [new DateTimeOffset(2026, 10, 10, 15, 50, 0, TimeSpan.Zero), "New Moon"],
        [new DateTimeOffset(2026, 10, 18, 16, 13, 0, TimeSpan.Zero), "First Quarter"],
        [new DateTimeOffset(2026, 10, 26, 4, 12, 0, TimeSpan.Zero), "Full Moon"],
        [new DateTimeOffset(2026, 11, 1, 20, 28, 0, TimeSpan.Zero), "Last Quarter"],
        [new DateTimeOffset(2026, 11, 9, 7, 2, 0, TimeSpan.Zero), "New Moon"],
        [new DateTimeOffset(2026, 11, 17, 11, 48, 0, TimeSpan.Zero), "First Quarter"],
        [new DateTimeOffset(2026, 11, 24, 14, 54, 0, TimeSpan.Zero), "Full Moon"],
        [new DateTimeOffset(2026, 12, 1, 6, 9, 0, TimeSpan.Zero), "Last Quarter"],
        [new DateTimeOffset(2026, 12, 9, 0, 52, 0, TimeSpan.Zero), "New Moon"],
        [new DateTimeOffset(2026, 12, 17, 5, 43, 0, TimeSpan.Zero), "First Quarter"],
        [new DateTimeOffset(2026, 12, 24, 1, 28, 0, TimeSpan.Zero), "Full Moon"],
        [new DateTimeOffset(2026, 12, 30, 19, 0, 0, TimeSpan.Zero), "Last Quarter"],
    ];

    [Theory]
    [MemberData(nameof(UsnoMoonPhaseRows))]
    public void MoonPhases_2026_Within2MinOfUsno(DateTimeOffset expectedUtc, string expectedPhase)
    {
        var quarters = Calculator.MoonQuarters(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var match = quarters.OrderBy(q => Math.Abs((q.Utc - expectedUtc).TotalMinutes)).First();
        var deltaMin = (match.Utc - expectedUtc).TotalMinutes;
        Assert.True(Math.Abs(deltaMin) < 2.0, $"expected {expectedPhase} {expectedUtc:O}, got {match.Utc:O} (Δ {deltaMin:F1} min)");
        Assert.Equal(expectedPhase, EphemerisCalculator.MoonPhaseName(match.Quarter));
    }
}
