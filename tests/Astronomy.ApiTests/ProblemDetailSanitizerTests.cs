using Astronomy.Api;

namespace Astronomy.ApiTests;

public class ProblemDetailSanitizerTests
{
    [Fact]
    public void StripsAbsolutePath()
    {
        Assert.Equal("kernel directory not found: <path>",
            ProblemDetailSanitizer.SanitizeDetail("kernel directory not found: /data/kernels"));
    }

    [Fact]
    public void StripsPathWithFilename()
    {
        Assert.Equal("kernel missing: <path>",
            ProblemDetailSanitizer.SanitizeDetail("kernel missing: /data/kernels/de441.bsp"));
    }

    [Fact]
    public void PreservesFilenameListAfterPath()
    {
        Assert.Equal("no planetary kernel found in <path> (looked for de440.bsp, de440s.bsp)",
            ProblemDetailSanitizer.SanitizeDetail("no planetary kernel found in /data/kernels (looked for de440.bsp, de440s.bsp)"));
    }

    [Fact]
    public void StripsEmbeddedPathInExceptionText()
    {
        // The quoted path is one non-space run containing '/', so the quotes
        // are consumed with it.
        Assert.Equal("kernel directory unreadable: Access to the path <path> is denied.",
            ProblemDetailSanitizer.SanitizeDetail("kernel directory unreadable: Access to the path '/data/kernels' is denied."));
    }

    [Fact]
    public void SlashFreeMessageIsUnchanged()
    {
        const string message = "libcspice.so not found (reference tier not deployed in this container)";
        Assert.Equal(message, ProblemDetailSanitizer.SanitizeDetail(message));
    }

    [Fact]
    public void StripsMultiplePaths()
    {
        Assert.Equal("kernel dir <path> and spare <path>",
            ProblemDetailSanitizer.SanitizeDetail("kernel dir /a/b and spare /c/d"));
    }
}
