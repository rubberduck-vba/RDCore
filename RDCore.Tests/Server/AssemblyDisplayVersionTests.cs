using RDCore.SDK;
using System.Reflection;

namespace RDCore.Tests.Server;

[TestClass]
public sealed class AssemblyDisplayVersionTests
{
    private const string Sha = "2062f9857560619aec056b37ac0181c84c0c6e6a";

    [TestMethod]
    public void InformationalVersion_IsReturnedWithItsBuildMetadataIntact()
        => Assert.AreEqual($"0.1.0+{Sha}", AssemblyDisplayVersion.Get($"0.1.0+{Sha}", new Version(0, 1, 0, 0)));

    [TestMethod]
    public void InformationalVersion_KeepsThePrereleaseSuffixAndBuildMetadata()
        => Assert.AreEqual($"0.1.0-rc.1+{Sha}", AssemblyDisplayVersion.Get($"0.1.0-rc.1+{Sha}", new Version(0, 1, 0, 0)));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public void NoInformationalVersion_FallsBackToTheNumericVersion(string? informationalVersion)
        => Assert.AreEqual("0.1.0", AssemblyDisplayVersion.Get(informationalVersion, new Version(0, 1, 0, 0)));

    [TestMethod]
    public void NoInformationalVersion_PadsATwoPartNumericVersion()
        => Assert.AreEqual("1.0.0", AssemblyDisplayVersion.Get(null, new Version(1, 0)));

    [TestMethod]
    public void NoVersionAtAll_IsUnknown()
        => Assert.AreEqual("0.0.0", AssemblyDisplayVersion.Get(null, null));

    [TestMethod]
    public void NoAssembly_IsUnknown()
        => Assert.AreEqual(AssemblyDisplayVersion.Unknown, AssemblyDisplayVersion.Get((Assembly?)null));

    [TestMethod]
    public void Assembly_ReportsItsInformationalVersion()
    {
        // arrange
        var assembly = typeof(AssemblyDisplayVersion).Assembly;
        var expected = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        // act
        var version = AssemblyDisplayVersion.Get(assembly);

        // assert
        Assert.AreEqual(expected, version);
    }

    [TestMethod]
    [DataRow("RDCore.SDK")]
    [DataRow("RDCore.Runtime")]
    [DataRow("RDCore.LanguageServer")]
    [DataRow("RDCore.ParseServer")]
    [DataRow("RDCore.Diagnostics")]
    [DataRow("rdc")]
    public void ProductAssembly_FollowsTheSingleVersionSource(string assemblyName)
    {
        // Directory.Build.props' VersionPrefix feeds both versions of every product assembly: a csproj that sets its own
        // <Version> or <AssemblyVersion> breaks one of these. No "+<sha>" check: a build from a source tarball has none.

        // arrange
        var expected = typeof(AssemblyDisplayVersion).Assembly.GetName().Version;
        var assembly = Assembly.Load(assemblyName);

        // act
        var numeric = assembly.GetName().Version!;
        var display = AssemblyDisplayVersion.Get(assembly);

        // assert
        Assert.AreEqual(expected, numeric);
        StringAssert.StartsWith(display, numeric.ToString(3));
    }
}
