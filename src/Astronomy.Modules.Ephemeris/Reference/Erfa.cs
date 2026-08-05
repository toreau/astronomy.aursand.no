using System.Runtime.InteropServices;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// Minimal ERFA (liberfa.so, IAU SOFA) P/Invoke surface for the reference-tier
/// correction chain: IAU 2006/2000A precession-nutation (of-date frames) and
/// the full celestial-to-terrestrial rotation (horizontal frames, fed by EOP
/// C04 UT1 + polar motion). Two-part Julian dates are passed as (2451545.0,
/// jd - 2451545.0) per SOFA practice to preserve precision.
/// </summary>
internal static partial class Erfa
{
    private const string Lib = "liberfa";

    [LibraryImport(Lib, EntryPoint = "eraPnm06a")]
    public static partial void Pnm06a(double date1, double date2, [Out] double[] rnpb);

    [LibraryImport(Lib, EntryPoint = "eraC2t06a")]
    public static partial void C2t06a(
        double tta, double ttb, double uta, double utb, double xp, double yp,
        [Out] double[] rc2t);

    [LibraryImport(Lib, EntryPoint = "eraDtdb")]
    public static partial double Dtdb(double date1, double date2, double ut, double elong, double u, double v);
}
