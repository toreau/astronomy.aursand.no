using System.Globalization;
using CosineKitty;

namespace S02AstronomyEngine;

public static class Comparison
{
    public sealed record Stats(
        int N,
        double MeanJ2000Arcsec, double P95J2000Arcsec, double MaxJ2000Arcsec,
        double MeanOfDateArcsec, double P95OfDateArcsec, double MaxOfDateArcsec,
        double MeanDistPct, double MaxDistPct);

    public static Stats Compare(string bodyName, IReadOnlyList<FixtureRow> rows)
    {
        var body = HorizonsApi.Bodies[bodyName].EngineBody;
        var separationsJ2000 = new List<double>(rows.Count);
        var separationsOfDate = new List<double>(rows.Count);
        var distPct = new List<double>(rows.Count);

        using var detail = File.CreateText(Path.Combine("output", $"{bodyName}_detail.csv"));
        detail.WriteLine("utc,hz_ra_j2000,hz_dec_j2000,ae_ra_j2000,ae_dec_j2000,sep_j2000_arcsec,hz_ra_ofdate,hz_dec_ofdate,ae_ra_ofdate,ae_dec_ofdate,sep_ofdate_arcsec,hz_dist_au,ae_dist_au,dist_err_pct");

        foreach (var row in rows)
        {
            var t = new AstroTime(row.Utc);

            var vJ = Astronomy.GeoVector(body, t, Aberration.None);
            var eqJ = Astronomy.EquatorFromVector(vJ);

            var vD = Astronomy.GeoVector(body, t, Aberration.Corrected);
            var rot = Astronomy.Rotation_EQJ_EQD(t);
            var vDofd = Astronomy.RotateVector(rot, vD);
            var eqD = Astronomy.EquatorFromVector(vDofd);

            var sepJ = SeparationArcsec(row.RaJ2000Deg, row.DecJ2000Deg, eqJ.ra, eqJ.dec);
            var sepD = SeparationArcsec(row.RaOfDateDeg, row.DecOfDateDeg, eqD.ra, eqD.dec);
            var dPct = 100.0 * Math.Abs(eqD.dist - row.DistAu) / row.DistAu;

            separationsJ2000.Add(sepJ);
            separationsOfDate.Add(sepD);
            distPct.Add(dPct);

            detail.WriteLine(string.Join(',',
                row.Utc.ToString("O", CultureInfo.InvariantCulture),
                row.RaJ2000Deg.ToString("F6", CultureInfo.InvariantCulture), row.DecJ2000Deg.ToString("F6", CultureInfo.InvariantCulture),
                eqJ.ra.ToString("F6", CultureInfo.InvariantCulture), eqJ.dec.ToString("F6", CultureInfo.InvariantCulture),
                sepJ.ToString("F3", CultureInfo.InvariantCulture),
                row.RaOfDateDeg.ToString("F6", CultureInfo.InvariantCulture), row.DecOfDateDeg.ToString("F6", CultureInfo.InvariantCulture),
                eqD.ra.ToString("F6", CultureInfo.InvariantCulture), eqD.dec.ToString("F6", CultureInfo.InvariantCulture),
                sepD.ToString("F3", CultureInfo.InvariantCulture),
                row.DistAu.ToString("F8", CultureInfo.InvariantCulture), eqD.dist.ToString("F8", CultureInfo.InvariantCulture),
                dPct.ToString("F4", CultureInfo.InvariantCulture)));
        }

        return new Stats(
            rows.Count,
            separationsJ2000.Average(),
            P95(separationsJ2000),
            separationsJ2000.Max(),
            separationsOfDate.Average(),
            P95(separationsOfDate),
            separationsOfDate.Max(),
            distPct.Average(),
            distPct.Max());
    }

    public static double SeparationArcsec(double ra1, double dec1, double ra2, double dec2)
    {
        var (r1, d1, r2, d2) = (ToRad(ra1), ToRad(dec1), ToRad(ra2), ToRad(dec2));
        var cosSep = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(r1 - r2);
        return ToDeg(Math.Acos(Math.Clamp(cosSep, -1.0, 1.0))) * 3600.0;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
    private static double ToDeg(double rad) => rad * 180.0 / Math.PI;

    private static double P95(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1];
    }
}
