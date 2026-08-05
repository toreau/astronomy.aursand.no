using System.Globalization;
using One_Sgp4;
using One_Sgp4.omm;

namespace Astronomy.Modules.Satellites.Application;

/// <summary>
/// One_Sgp4 1.1.0 adapter (S0.4 gate PASS): builds TLE lines 1/2 from the
/// stored OMM mean elements (exactly the TLE mean elements), parses with the
/// checksum-tolerant mode, and propagates to TEME km (WGS-72).
/// </summary>
public sealed class OneSgp4Propagator : IOrbitalPropagator
{
    public TemeVector Propagate(OrbitalElementRow elements, DateTimeOffset utc)
    {
        var line1 = BuildLine1(elements);
        var line2 = BuildLine2(elements);
        return PropagateTle(line1, line2, elements.EpochUtc, utc);
    }

    /// <summary>Raw TLE entry point (accuracy-suite/verification use).</summary>
    internal TemeVector PropagateTle(string line1, string line2, DateTimeOffset epochUtc, DateTimeOffset utc)
    {
        var tle = ParserTLE.parseTle(line1, line2, "v");
        var (year, dayOfYear) = EpochFields(epochUtc);
        var t = new EpochTime(year, dayOfYear);
        t.addMinutes((utc - epochUtc).TotalMinutes);
        var p = SatFunctions.getSatPositionAtTime(tle, t, Sgp4.wgsConstant.WGS_72);
        return new TemeVector(p.getX(), p.getY(), p.getZ());
    }

    internal static string BuildLine1(OrbitalElementRow e)
    {
        var c = new char[69];
        var s = new Span<char>(c);
        s.Fill(' ');
        c[0] = '1';
        Write(c, 2, e.NoradId.PadLeft(5));
        c[7] = 'U';
        c[9] = '0'; c[10] = '0'; c[11] = '0'; c[12] = '0'; c[13] = '0'; // intl designator placeholder
        Write(c, 18, EpochField(e.EpochUtc));                                  // 19-32
        Write(c, 33, SignedDecimal(e.MmDot, 10));                              // 34-43
        Write(c, 44, ExponentField(e.MmDdot));                                 // 45-52
        Write(c, 53, ExponentField(e.Bstar));                                  // 54-61
        c[62] = '0';
        Write(c, 64, "1".PadLeft(4));                                          // 65-68 el number
        c[68] = Checksum(c);
        return new string(c);
    }

    internal static string BuildLine2(OrbitalElementRow e)
    {
        var c = new char[69];
        var s = new Span<char>(c);
        s.Fill(' ');
        c[0] = '2';
        Write(c, 2, e.NoradId.PadLeft(5));
        Write(c, 8, e.Inclination.ToString("F4", CultureInfo.InvariantCulture).PadLeft(8));    // 9-16
        Write(c, 17, e.RaOfAscNode.ToString("F4", CultureInfo.InvariantCulture).PadLeft(8));   // 18-25
        Write(c, 26, ((int)Math.Round(e.Eccentricity * 1e7)).ToString("0000000"));             // 27-33
        Write(c, 34, e.ArgOfPericenter.ToString("F4", CultureInfo.InvariantCulture).PadLeft(8)); // 35-42
        Write(c, 43, e.MeanAnomaly.ToString("F4", CultureInfo.InvariantCulture).PadLeft(8));   // 44-51
        Write(c, 52, e.MeanMotion.ToString("F8", CultureInfo.InvariantCulture).PadLeft(11));   // 53-63
        Write(c, 64, e.RevAtEpoch.ToString(CultureInfo.InvariantCulture).PadLeft(4));          // 65-68
        c[68] = Checksum(c);
        return new string(c);
    }

    private static void Write(char[] buffer, int index, string value)
    {
        foreach (var ch in value)
        {
            if (index < 69) buffer[index] = ch;
            index++;
        }
    }

    /// <summary>"±.xxxxxxxx" (no leading zero) right-padded to width; decimal count = width - 2.</summary>
    private static string SignedDecimal(double value, int width)
    {
        var decimals = width - 2;
        var s = (value < 0 ? "-" : "+") + Math.Abs(value)
            .ToString("0." + new string('0', decimals), CultureInfo.InvariantCulture).TrimStart('0');
        return s.PadRight(width);
    }

    /// <summary>
    /// 8-char exponent-style field ("±mmmmm±ee", implied decimal after the
    /// sign, e.g. -11606-4 = -0.11606e-4); zero as " 00000-0".
    /// </summary>
    private static string ExponentField(double value)
    {
        if (value == 0) return " 00000-0".PadRight(8);
        var sign = value < 0 ? "-" : " ";
        var av = Math.Abs(value);
        var exp = (int)Math.Floor(Math.Log10(av)) + 1;
        var mantissa = (int)Math.Round(av / Math.Pow(10, exp - 1) * 1e4);
        return $"{sign}{mantissa:00000}{(exp >= 0 ? "+" : "-")}{Math.Abs(exp)}".PadRight(8);
    }

    internal static (int Year, double DayOfYear) EpochFields(DateTimeOffset epochUtc)
    {
        var utc = epochUtc.UtcDateTime;
        var year = utc.Year;
        var dayOfYear = utc.DayOfYear + utc.TimeOfDay.TotalSeconds / 86400.0; // 1-based day + fraction
        return (year, dayOfYear);
    }

    private static string EpochField(DateTimeOffset epochUtc)
    {
        var (year, day) = EpochFields(epochUtc);
        var year2 = year >= 2000 ? year - 2000 : year - 1900;
        return $"{year2:00}{day:000.00000000}";
    }

    private static char Checksum(char[] c)
    {
        var sum = 0;
        for (var i = 0; i < 68; i++)
        {
            var ch = c[i];
            if (ch == ' ') continue;
            if (ch == '-') { sum++; continue; }
            if (char.IsDigit(ch)) sum += ch - '0';
        }
        return (char)('0' + sum % 10);
    }
}
