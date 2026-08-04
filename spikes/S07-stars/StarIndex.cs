namespace S07Stars;

public interface IStarIndex
{
    string Name { get; }
    List<Star> ConeSearch(double raDeg, double decDeg, double radiusDeg, double maxMag);
}

public sealed class BruteForceIndex : IStarIndex
{
    public string Name => "brute-force";
    private readonly Star[] _stars;
    private readonly double[] _ux, _uy, _uz;

    public BruteForceIndex(List<Star> stars)
    {
        _stars = stars.ToArray();
        _ux = new double[_stars.Length];
        _uy = new double[_stars.Length];
        _uz = new double[_stars.Length];
        for (var i = 0; i < _stars.Length; i++)
        {
            _ux[i] = _stars[i].X;
            _uy[i] = _stars[i].Y;
            _uz[i] = _stars[i].Z;
        }
    }

    public List<Star> ConeSearch(double raDeg, double decDeg, double radiusDeg, double maxMag)
    {
        var cosR = Math.Cos(radiusDeg * Math.PI / 180);
        var (cx, cy, cz) = UnitVector(raDeg, decDeg);
        var result = new List<Star>(16);
        for (var i = 0; i < _stars.Length; i++)
        {
            if (_stars[i].Vmag > maxMag) continue;
            if (_ux[i] * cx + _uy[i] * cy + _uz[i] * cz >= cosR)
                result.Add(_stars[i]);
        }
        return result;
    }

    public static (double X, double Y, double Z) UnitVector(double raDeg, double decDeg)
    {
        var ra = raDeg * Math.PI / 180;
        var dec = decDeg * Math.PI / 180;
        return (Math.Cos(ra) * Math.Cos(dec), Math.Sin(ra) * Math.Cos(dec), Math.Sin(dec));
    }
}

public sealed class TileIndex : IStarIndex
{
    public string Name => "tile-5deg";
    private const double TileSizeDeg = 5.0;
    private readonly Star[] _stars;
    private readonly int[] _tileOf;
    private readonly List<int>[] _tiles;

    public TileIndex(List<Star> stars)
    {
        _stars = stars.ToArray();
        _tileOf = new int[_stars.Length];
        _tiles = new List<int>[72 * 37];
        for (var i = 0; i < _tiles.Length; i++) _tiles[i] = new List<int>(4);
        for (var i = 0; i < _stars.Length; i++)
        {
            var tile = TileFor(_stars[i].RaDeg, _stars[i].DecDeg);
            _tileOf[i] = tile;
            _tiles[tile].Add(i);
        }
    }

    private static int TileFor(double raDeg, double decDeg)
    {
        var ra = (int)(raDeg / TileSizeDeg) % 72;
        var dec = (int)((decDeg + 90) / TileSizeDeg);
        if (dec < 0) dec = 0;
        if (dec > 36) dec = 36;
        return dec * 72 + ra;
    }

    public List<Star> ConeSearch(double raDeg, double decDeg, double radiusDeg, double maxMag)
    {
        var cosR = Math.Cos(radiusDeg * Math.PI / 180);
        var (cx, cy, cz) = BruteForceIndex.UnitVector(raDeg, decDeg);
        var pad = (int)Math.Ceiling(radiusDeg / TileSizeDeg);
        var cosDec = Math.Max(Math.Cos(decDeg * Math.PI / 180), 0.15);
        var raPad = (int)Math.Ceiling(radiusDeg / (TileSizeDeg * cosDec));
        var raTile = (int)(raDeg / TileSizeDeg);
        var decTile = (int)((decDeg + 90) / TileSizeDeg);
        var result = new List<Star>(16);
        for (var dt = decTile - pad; dt <= decTile + pad; dt++)
        {
            if (dt < 0 || dt > 36) continue;
            for (var rt = raTile - raPad; rt <= raTile + raPad; rt++)
            {
                var tile = dt * 72 + ((rt % 72) + 72) % 72;
                foreach (var idx in _tiles[tile])
                {
                    if (_stars[idx].Vmag > maxMag) continue;
                    if (_stars[idx].X * cx + _stars[idx].Y * cy + _stars[idx].Z * cz >= cosR)
                        result.Add(_stars[idx]);
                }
            }
        }
        return result;
    }
}

public sealed class RaSortedIndex : IStarIndex
{
    public string Name => "ra-sorted+dec-band";
    private readonly Star[] _stars;
    private readonly double[] _ux, _uy, _uz;

    public RaSortedIndex(List<Star> stars)
    {
        _stars = stars.OrderBy(s => s.RaDeg).ToArray();
        _ux = new double[_stars.Length];
        _uy = new double[_stars.Length];
        _uz = new double[_stars.Length];
        for (var i = 0; i < _stars.Length; i++)
        {
            _ux[i] = _stars[i].X;
            _uy[i] = _stars[i].Y;
            _uz[i] = _stars[i].Z;
        }
    }

    public List<Star> ConeSearch(double raDeg, double decDeg, double radiusDeg, double maxMag)
    {
        var cosR = Math.Cos(radiusDeg * Math.PI / 180);
        var (cx, cy, cz) = BruteForceIndex.UnitVector(raDeg, decDeg);
        var cosLo = Math.Max(Math.Cos((decDeg - radiusDeg) * Math.PI / 180), 0.087);
        var cosHi = Math.Max(Math.Cos((decDeg + radiusDeg) * Math.PI / 180), 0.087);
        var raHalf = radiusDeg / Math.Min(cosLo, cosHi);
        var result = new List<Star>(16);
        SearchRange(raDeg - raHalf, raDeg + raHalf, decDeg, radiusDeg, maxMag, cosR, cx, cy, cz, result);
        if (raDeg + raHalf > 360)
            SearchRange(0, raDeg + raHalf - 360, decDeg, radiusDeg, maxMag, cosR, cx, cy, cz, result);
        else if (raDeg - raHalf < 0)
            SearchRange(360 + raDeg - raHalf, 360, decDeg, radiusDeg, maxMag, cosR, cx, cy, cz, result);
        return result;
    }

    private void SearchRange(double loRa, double hiRa, double decDeg, double radiusDeg, double maxMag,
        double cosR, double cx, double cy, double cz, List<Star> result)
    {
        var lo = LowerBound(loRa);
        var hi = LowerBound(hiRa);
        for (var i = lo; i < hi; i++)
        {
            if (_stars[i].Vmag > maxMag) continue;
            if (Math.Abs(_stars[i].DecDeg - decDeg) > radiusDeg) continue;
            if (_ux[i] * cx + _uy[i] * cy + _uz[i] * cz >= cosR)
                result.Add(_stars[i]);
        }
    }

    private int LowerBound(double ra)
    {
        var lo = 0;
        var hi = _stars.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_stars[mid].RaDeg < ra) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
