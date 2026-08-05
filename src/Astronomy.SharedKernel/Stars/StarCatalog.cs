using System.Globalization;

namespace Astronomy.SharedKernel.Stars;

/// <summary>
/// A single catalog entry from the ingested HYG-derived star catalog.
/// Positions are J2000 (epoch and equinox 2000.0); PmRaMasYr follows the
/// Hipparcos convention (mas/yr, cos(dec)-scaled).
/// </summary>
public readonly record struct StarRecord(
    string Hip,
    string ProperName,
    string BayerFlamsteed,
    string Bayer,
    string Flamsteed,
    string Constellation,
    double RaDeg,
    double DecDeg,
    double PmRaMasYr,
    double PmDecMasYr,
    double DistLightYears,
    double Vmag,
    string SpectralType)
{
    public static StarRecord Parse(string line, char delimiter = ',')
    {
        var p = line.Split(delimiter);
        return new StarRecord(
            p[0],
            p[1],
            p[2],
            p[3],
            p[4],
            p[5],
            double.Parse(p[6], CultureInfo.InvariantCulture),
            double.Parse(p[7], CultureInfo.InvariantCulture),
            double.Parse(p[8], CultureInfo.InvariantCulture),
            double.Parse(p[9], CultureInfo.InvariantCulture),
            double.Parse(p[10], CultureInfo.InvariantCulture),
            double.Parse(p[11], CultureInfo.InvariantCulture),
            p[12]);
    }

    public string ToCsvLine() =>
        string.Join(',',
            Hip, ProperName, BayerFlamsteed, Bayer, Flamsteed, Constellation,
            RaDeg.ToString("F6", CultureInfo.InvariantCulture),
            DecDeg.ToString("F6", CultureInfo.InvariantCulture),
            PmRaMasYr.ToString("F3", CultureInfo.InvariantCulture),
            PmDecMasYr.ToString("F3", CultureInfo.InvariantCulture),
            DistLightYears.ToString("F3", CultureInfo.InvariantCulture),
            Vmag.ToString("F2", CultureInfo.InvariantCulture),
            SpectralType);
}

/// <summary>
/// Immutable in-memory star catalog loaded from the active star-catalog-hyg
/// dataset version. Absent dataset degrades to IsAvailable=false with a reason
/// (stars endpoints then return 503 AST-5031 - no silent fallback).
/// </summary>
public sealed class StarCatalog
{
    public static readonly StarCatalog Unavailable = new([], "unavailable", "star catalog dataset not ingested");

    private readonly StarRecord[] _stars;
    private readonly Dictionary<string, int> _byHip;

    public IReadOnlyList<StarRecord> Stars => _stars;

    public string Version { get; }

    public bool IsAvailable { get; }

    public string Reason { get; }

    public StarCatalog(StarRecord[] stars, string version, string reason)
    {
        _stars = stars;
        Version = version;
        IsAvailable = stars.Length > 0;
        Reason = reason;
        _byHip = new Dictionary<string, int>(stars.Length / 2, StringComparer.Ordinal);
        for (var i = 0; i < stars.Length; i++)
        {
            var hip = stars[i].Hip;
            if (hip.Length > 0 && !_byHip.ContainsKey(hip))
                _byHip[hip] = i;
        }
    }

    public bool TryGetByHip(string hip, out StarRecord star)
    {
        if (_byHip.TryGetValue(hip, out var index))
        {
            star = _stars[index];
            return true;
        }
        star = default;
        return false;
    }

    public IEnumerable<StarRecord> SearchByName(string query)
    {
        var q = query.Trim().ToLowerInvariant();
        if (q.Length == 0) yield break;
        var seen = 0;
        foreach (var star in _stars)
        {
            if (Matches(star, q))
            {
                yield return star;
                if (++seen >= 20) yield break;
            }
        }
    }

    private static bool Matches(StarRecord star, string q) =>
        (star.ProperName.Length > 0 && star.ProperName.ToLowerInvariant().Contains(q)) ||
        (star.BayerFlamsteed.Length > 0 && star.BayerFlamsteed.ToLowerInvariant().Contains(q)) ||
        star.Hip.Equals(q, StringComparison.OrdinalIgnoreCase);
}
