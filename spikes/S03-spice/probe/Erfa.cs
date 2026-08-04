using System.Runtime.InteropServices;

namespace SpiceProbe;

public static unsafe partial class Erfa
{
    private const string Lib = "liberfa";

    [LibraryImport(Lib, EntryPoint = "eraEpv00")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int Epv00(double date1, double date2, [In, Out] double[] pvh, [In, Out] double[] pvb);

    [LibraryImport(Lib, EntryPoint = "eraDtdb")]
    public static partial double Dtdb(double date1, double date2, double ut, double elong, double u, double v);
}
