using System.Reflection;
using XAssert = Xunit.Assert;

namespace Astronomy.ArchitectureTests;

public class DependencyRules
{
    private static readonly Assembly SharedKernel = typeof(Astronomy.SharedKernel.Time.JulianDate).Assembly;
    private static readonly Assembly Infrastructure = typeof(Astronomy.Infrastructure.InfrastructureRegistrar).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static Assembly Module(string name) => Assembly.Load($"Astronomy.Modules.{name}");

    private static string Refs(Assembly a) => string.Join(",", a.GetReferencedAssemblies().Select(r => r.Name ?? ""));

    [Fact]
    public void SharedKernel_HasNoExternalReferences()
    {
        XAssert.Equal("", string.Join(",", SharedKernel.GetReferencedAssemblies()
            .Select(r => r.Name ?? "").Where(r => r.StartsWith("Astronomy.") && r != "Astronomy.SharedKernel")));
        XAssert.Equal("", string.Join(",", SharedKernel.GetReferencedAssemblies()
            .Select(r => r.Name ?? "").Where(r => r is "Microsoft.EntityFrameworkCore" or "Microsoft.Extensions.DependencyInjection")));
    }

    [Fact]
    public void Modules_OnlyReferenceSharedKernel()
    {
        foreach (var name in new[] { "Calendars", "Time", "Ephemeris", "Stars", "Satellites" })
        {
            var forbidden = Module(name).GetReferencedAssemblies()
                .Select(r => r.Name ?? "").Where(r => r.StartsWith("Astronomy.") && r != "Astronomy.SharedKernel");
            XAssert.Equal("", string.Join(",", forbidden));
        }
    }

    [Fact]
    public void Almanac_OnlyReferencesModuleInterfaces()
    {
        XAssert.Equal("", string.Join(",", Module("Almanac").GetReferencedAssemblies()
            .Select(r => r.Name ?? "").Where(r => r is "Astronomy.Infrastructure" or "Astronomy.Api")));
    }

    [Fact]
    public void Api_ReferencesModules_InfrastructureReferencesNothingDomainSpecific()
    {
        XAssert.Equal(1, Refs(Api).Split(',').Count(r => r == "Astronomy.Modules.Calendars"));
        XAssert.Equal(1, Refs(Api).Split(',').Count(r => r == "Astronomy.Modules.Almanac"));
        XAssert.Equal(1, Refs(Api).Split(',').Count(r => r == "Astronomy.Infrastructure"));
        XAssert.Equal("", string.Join(",", Infrastructure.GetReferencedAssemblies()
            .Select(r => r.Name ?? "").Where(r => r.StartsWith("Astronomy.Modules."))));
    }
}

public class SurfaceRules
{
    [Fact]
    public void ModulePublicSurface_IsLimitedToContracts()
    {
        foreach (var name in new[] { "Calendars", "Time", "Ephemeris", "Stars", "Satellites", "Almanac" })
        {
            var assembly = Assembly.Load($"Astronomy.Modules.{name}");
            var publicTypes = assembly.GetExportedTypes();
            var bad = publicTypes.Where(t =>
                t.Name.Contains("Service") && !t.Name.StartsWith("I") && !t.Name.EndsWith("Registrar") && !t.Name.EndsWith("Store") && !t.Name.Contains("DbContext"));
            XAssert.Empty(bad.Select(t => $"{name}:{t.Name}"));
        }
    }
}

public class CodeConventionRules
{
    [Fact]
    public void NoDateTime_InSharedKernel()
    {
        var types = typeof(Astronomy.SharedKernel.Time.JulianDate).Assembly.GetExportedTypes();
        foreach (var t in types)
            foreach (var prop in t.GetProperties())
                XAssert.NotEqual(typeof(DateTime), Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType);
    }
}
