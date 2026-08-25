using Astronomy.DataIngestion;
using Astronomy.SharedKernel.Stars;

namespace Astronomy.UnitTests;

public class JobsParsingTests
{
    [Fact]
    public void SplitCsv_HandlesQuotedCommas()
    {
        var fields = Jobs.SplitCsv("a,\"b,c\",d");
        Assert.Equal(new[] { "a", "b,c", "d" }, fields);
    }

    [Fact]
    public void SplitCsv_HandlesEscapedQuotes()
    {
        var fields = Jobs.SplitCsv("a,\"he said \"\"hi\"\"\",b");
        Assert.Equal(new[] { "a", "he said \"hi\"", "b" }, fields);
    }

    [Fact]
    public void SplitCsv_HandlesEmptyFields()
    {
        var fields = Jobs.SplitCsv("a,,c,");
        Assert.Equal(new[] { "a", "", "c", "" }, fields);
    }

    // HYG v3.8-style row: id,hip,hd,hr,gl,bf,proper,ra(h),dec,dist(pc),pmra,pmdec,
    // rv,mag,absmag,spect,ci,x,y,z,vx,vy,vz,rarad,decrad,pmrarad,pmdecrad,
    // bayer,flam,con,comp,comp_primary,base,lum,var,var_min,var_max
    private const string SiriusLine =
        "0,32349,48915,2491,,9Alp CMa,Sirius,6.7524,-16.7161,2.64,-546.01,-1223.08,-5.55,-1.44,1.43," +
        "A0m...,-0.05,2.4,-2.0,-0.5,-7.6,-1.3,0.2,1.7638,-0.2918,-546.01,-1223.08,Alp,9,CMa,,,,,,";

    [Fact]
    public void ParseHygLine_ParsesRealisticRow()
    {
        var star = Jobs.ParseHygLine(SiriusLine);
        Assert.NotNull(star);
        Assert.Equal("32349", star.Value.Hip);
        Assert.Equal("Sirius", star.Value.ProperName);
        Assert.Equal("9Alp CMa", star.Value.BayerFlamsteed);
        Assert.Equal("Alp", star.Value.Bayer);
        Assert.Equal("9", star.Value.Flamsteed);
        Assert.Equal("CMa", star.Value.Constellation);
        Assert.Equal(6.7524 * 15.0, star.Value.RaDeg, 6);
        Assert.Equal(-16.7161, star.Value.DecDeg, 6);
        Assert.Equal(-546.01, star.Value.PmRaMasYr, 3);
        Assert.Equal(-1223.08, star.Value.PmDecMasYr, 3);
        Assert.Equal(2.64 * 3.262, star.Value.DistLightYears, 3);
        Assert.Equal(-1.44, star.Value.Vmag, 2);
        Assert.Equal("A0m...", star.Value.SpectralType);
    }

    [Fact]
    public void ParseHygLine_DropsSol()
    {
        // dist 0 marks the Sun; Sol must not enter the catalog.
        var solLine = "0,,,,,Sol,0,0,0,0,0,0,0,-26.8,-4.85,G2V,,,,,,,,,,,,,,,,";
        Assert.Null(Jobs.ParseHygLine(solLine));
    }

    [Fact]
    public void ParseHygLine_DropsShortOrMalformedRows()
    {
        Assert.Null(Jobs.ParseHygLine("1,2,3"));
        Assert.Null(Jobs.ParseHygLine(""));
    }

    [Fact]
    public void ParseHygLine_HandlesQuotedProperName()
    {
        var line = "0,32349,48915,2491,,9Alp CMa,\"Sirius, the Dog Star\",6.7524,-16.7161,2.64,-546.01,-1223.08,-5.55,-1.44,1.43," +
                   "A0m...,-0.05,2.4,-2.0,-0.5,-7.6,-1.3,0.2,1.7638,-0.2918,-546.01,-1223.08,Alp,9,CMa,,,,,,";
        var star = Jobs.ParseHygLine(line);
        Assert.NotNull(star);
        Assert.Equal("Sirius, the Dog Star", star.Value.ProperName);
    }

    private static long NtpSeconds(DateTimeOffset utc) =>
        (long)(utc - new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;

    [Fact]
    public void ParseLeapSecondsFile_ParsesModernIntegerEntries()
    {
        var text = string.Join('\n',
            "# Updated 2026-01-01",
            "#@ 4029379200",
            "#$ leap-seconds.list",
            $"{NtpSeconds(new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero))}\t37\t# 1 Jan 2017",
            $"{NtpSeconds(new DateTimeOffset(2015, 7, 1, 0, 0, 0, TimeSpan.Zero))}\t36\t# 1 Jul 2015",
            $"{NtpSeconds(new DateTimeOffset(2012, 7, 1, 0, 0, 0, TimeSpan.Zero))}\t35\t# 1 Jul 2012",
            $"{NtpSeconds(new DateTimeOffset(1972, 1, 1, 0, 0, 0, TimeSpan.Zero))}\t10\t# 1 Jan 1972");
        var entries = Jobs.ParseLeapSecondsFile(text);
        Assert.Equal(4, entries.Count);
        // Parse preserves file order; the job sorts by effective date before use.
        Assert.Equal(new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero), entries[0].EffectiveUtc);
        Assert.Equal(37, entries[0].TaiMinusUtc);
        Assert.Equal(new DateTimeOffset(1972, 1, 1, 0, 0, 0, TimeSpan.Zero), entries[^1].EffectiveUtc);
        Assert.Equal(10, entries[^1].TaiMinusUtc);
    }

    [Fact]
    public void ParseLeapSecondsFile_DropsPre1972AndFractionalRows()
    {
        var text = string.Join('\n',
            $"{NtpSeconds(new DateTimeOffset(1965, 1, 1, 0, 0, 0, TimeSpan.Zero))}\t3\t# pre-1972",
            "1893456000\t1.4228180\t# 1961 rubber-seconds era (fractional)",
            "not-a-number\t37\t# garbage",
            $"{NtpSeconds(new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero))}\t37\t# 1 Jan 2017");
        var entries = Jobs.ParseLeapSecondsFile(text);
        Assert.Single(entries);
        Assert.Equal(37, entries[0].TaiMinusUtc);
    }

    [Fact]
    public void ParseSer7Line_ValidLine_ReturnsSample()
    {
        var sample = Jobs.ParseSer7Line("60814.0    0.3660000   0.0000000");
        Assert.NotNull(sample);
        Assert.Equal(60814.0, sample.Value.Mjd, 3);
        Assert.Equal(0.366, sample.Value.Ut1MinusUtc, 6);
    }

    [Fact]
    public void ParseSer7Line_MalformedOrShortLines_ReturnNull()
    {
        Assert.Null(Jobs.ParseSer7Line(""));
        Assert.Null(Jobs.ParseSer7Line("60814.0"));                       // one field
        Assert.Null(Jobs.ParseSer7Line("not-a-number 0.366"));            // bad mjd
        Assert.Null(Jobs.ParseSer7Line("60814.0 not-a-number"));          // bad dut1
        Assert.Null(Jobs.ParseSer7Line("# comment line"));
    }

    private static string FinalsLine(string mjd, string ut1) =>
        new string(' ', 68)
            .Remove(7, 8).Insert(7, mjd.PadRight(8))
            .Remove(58, 10).Insert(58, ut1.PadRight(10));

    [Fact]
    public void ParseFinalsLine_ValidLine_ReturnsSample()
    {
        var sample = Jobs.ParseFinalsLine(FinalsLine("41685.00", " 0.8056163"));
        Assert.NotNull(sample);
        Assert.Equal(41685.00, sample.Value.Mjd, 2);
        Assert.Equal(0.8056163, sample.Value.Ut1MinusUtc, 7);
    }

    [Fact]
    public void ParseFinalsLine_RealFinalRow_Parses()
    {
        var line = "73 1 3 41685.00 I  0.118980 0.011039  0.135656 0.013616  I 0.8056163 0.0002710  3.5563 0.1916  P    -0.751    0.199    -0.701    0.300   .141000   .134000   .8044000   -18.636    -3.571  ";
        var sample = Jobs.ParseFinalsLine(line);
        Assert.NotNull(sample);
        Assert.Equal(41685.00, sample.Value.Mjd, 2);
        Assert.Equal(0.8056163, sample.Value.Ut1MinusUtc, 7);
    }

    [Fact]
    public void ParseFinalsLine_TrailerWithBlankUt1_ReturnsNull()
    {
        Assert.Null(Jobs.ParseFinalsLine("271017 61695.00".PadRight(187)));
    }

    [Fact]
    public void ParseFinalsLine_MalformedOrOutOfRange_ReturnNull()
    {
        Assert.Null(Jobs.ParseFinalsLine("too short"));
        Assert.Null(Jobs.ParseFinalsLine(FinalsLine("not-mjd", " 0.8056163")));
        Assert.Null(Jobs.ParseFinalsLine(FinalsLine("99999.00", " 0.8056163"))); // mjd > 80000
        Assert.Null(Jobs.ParseFinalsLine(FinalsLine("41685.00", "      bad ")));  // bad ut1
    }

    [Fact]
    public void ParseEopC04Line_ValidLine_ReturnsSampleWithPolarMotion()
    {
        // IERS C04 text format: year month day MJD x y UT1-UTC ...
        var sample = Jobs.ParseEopC04Line("2026 08 06 60814.000 0.123456 0.456789 0.3660000 0.1 0.2");
        Assert.NotNull(sample);
        Assert.Equal(60814.0, sample.Value.Mjd, 3);
        Assert.Equal(0.366, sample.Value.Ut1MinusUtc, 6);
        Assert.Equal(0.123456, sample.Value.X, 6);
        Assert.Equal(0.456789, sample.Value.Y, 6);
    }

    [Fact]
    public void ParseEopC04Line_ShortLine_PolarMotionDefaultsToZero()
    {
        // Fewer than 8 fields: x/y are not present and default to zero.
        var sample = Jobs.ParseEopC04Line("2026 08 06 60814.000 0.123456 0.456789 0.3660000");
        Assert.NotNull(sample);
        Assert.Equal(0.366, sample.Value.Ut1MinusUtc, 6);
        Assert.Equal(0.0, sample.Value.X, 6);
        Assert.Equal(0.0, sample.Value.Y, 6);
    }

    [Fact]
    public void ParseEopC04Line_OutOfEraOrMalformed_ReturnNull()
    {
        Assert.Null(Jobs.ParseEopC04Line("1969 08 06 60814.000 0.123456 0.456789 0.3660000")); // year < 1970
        Assert.Null(Jobs.ParseEopC04Line("2101 08 06 60814.000 0.123456 0.456789 0.3660000")); // year > 2100
        Assert.Null(Jobs.ParseEopC04Line("2026 08 06 39999.000 0.123456 0.456789 0.3660000")); // mjd < 40000
        Assert.Null(Jobs.ParseEopC04Line("2026 08 06 80001.000 0.123456 0.456789 0.3660000")); // mjd > 80000
        Assert.Null(Jobs.ParseEopC04Line("2026 08 06"));                                        // too short
        Assert.Null(Jobs.ParseEopC04Line("2026 08 06 60814.000 0.123456 0.456789 x"));          // bad dut1
    }
}
