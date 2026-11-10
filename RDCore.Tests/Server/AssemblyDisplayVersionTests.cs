using RDCore.SDK;
using System.Reflection;

namespace RDCore.Tests.Server;

[TestClass]
public sealed class AssemblyDisplayVersionTests
{
    private const string Sha = "2062f9857560619aec056b37ac0181c84c0c6e6a";

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
    [DataRow("RDCore.Runtime")]
    [DataRow("RDCore.LanguageServer")]
    [DataRow("RDCore.ParseServer")]
    [DataRow("RDCore.Diagnostics")]
    [DataRow("rdc")]
    public void ProductAssembly_FollowsTheSingleVersionSource(string assemblyName)
    {
        // Directory.Build.props' VersionPrefix feeds both versions of every product assembly, and of this test assembly: a csproj
        // that sets its own <Version> or <AssemblyVersion> breaks one of these. RDCore.SDK is a package, versioned on its own.
        // No "+<sha>" check: a build from a source tarball has none.

        // arrange
        var expected = typeof(AssemblyDisplayVersionTests).Assembly.GetName().Version;
        var assembly = Assembly.Load(assemblyName);

        // act
        var numeric = assembly.GetName().Version!;
        var display = AssemblyDisplayVersion.Get(assembly);

        // assert
        Assert.AreEqual(expected, numeric);
        StringAssert.StartsWith(display, numeric.ToString(3));
    }
}
