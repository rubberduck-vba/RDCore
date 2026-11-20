using RDCore.Runtime.Support.Host.Symbols;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Runtime;

[TestClass]
[TestCategory("MS-VBAL 3.4.1 Conditional Compilation Const Directive")]
public sealed class ConfigurationSymbolProviderTests
{
    private static IRuntimeEnvironmentProfile Env(bool is64) => new RuntimeEnvironmentProfile(is64, 0, 1252, false);

    private static Dictionary<string, PrecompilerConstantSymbol> Provide(
        IRuntimeEnvironmentProfile env, RDCoreProject project, IReadOnlyDictionary<string, string>? overrides = null)
        => new ConfigurationSymbolProvider(env, project, overrides)
            .ProvideSymbols()
            .Cast<PrecompilerConstantSymbol>()
            .ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);

    // each operating system's constant is 1 on it alone, as MS-VBA's are (Win32, Win64, VBA6 and VBA7 are 1 in 64-bit Excel), and defined 0 on
    // every other; Win32 says the Win32 API is there, as MS-VBA's does in 64-bit Office too. Win64 is 1 on a 64-bit Mac too, as in Excel for Mac, unless
    // the platform sets it on Windows only; Is64BitEnv is 1 in any 64-bit environment, and Linux64 is not predefined.
    [TestMethod]
    [DataRow(VBOperatingSystem.Windows, true, false, "Win32 Win64 Is64BitEnv", DisplayName = "64-bit Windows")]
    [DataRow(VBOperatingSystem.Windows, true, true, "Win32 Win64 Is64BitEnv", DisplayName = "64-bit Windows, Win64 on Windows only")]
    [DataRow(VBOperatingSystem.Windows, false, false, "Win32", DisplayName = "32-bit Windows")]
    [DataRow(VBOperatingSystem.Linux, true, false, "Linux Is64BitEnv", DisplayName = "64-bit Linux")]
    [DataRow(VBOperatingSystem.Linux, false, false, "Linux", DisplayName = "32-bit Linux")]
    [DataRow(VBOperatingSystem.MacOS, true, false, "Mac Win64 Is64BitEnv", DisplayName = "64-bit macOS")]
    [DataRow(VBOperatingSystem.MacOS, true, true, "Mac Is64BitEnv", DisplayName = "64-bit macOS, Win64 on Windows only")]
    [DataRow(VBOperatingSystem.Other, true, false, "Is64BitEnv", DisplayName = "another operating system")]
    public void BuiltIns_ReflectTheOperatingSystemAndBitness(VBOperatingSystem os, bool is64, bool win64OnlySetOnWindows, string trueOnes)
    {
        var environment = new RuntimeEnvironmentProfile(is64, 0, 1252, false, OperatingSystem: os, Win64OnlySetOnWindows: win64OnlySetOnWindows);
        var symbols = Provide(environment, new RDCoreProject());
        var expectedTrue = trueOnes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append("VBA6").Append("VBA7").Append("DEBUG").ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var name in new[] { "Win16", "Win32", "Win64", "Mac", "Linux", "Is64BitEnv", "VBA6", "VBA7", "DEBUG" })
        {
            Assert.AreEqual(expectedTrue.Contains(name) ? (short)1 : (short)0, ((VBIntegerValue)symbols[name].Value).Value, name);
        }

        Assert.DoesNotContain("Linux64", symbols.Keys, "a retired constant is not predefined");
    }

    [TestMethod]
    public void TheHostsProfile_IsOfTheOperatingSystemItRunsOn()
    {
        var expected = OperatingSystem.IsWindows() ? VBOperatingSystem.Windows
            : OperatingSystem.IsMacOS() ? VBOperatingSystem.MacOS
            : OperatingSystem.IsLinux() ? VBOperatingSystem.Linux
            : VBOperatingSystem.Other;

        Assert.AreEqual(expected, RuntimeEnvironmentProfile.From(new RDCore.SDK.Server.Configuration.SdkEnvironmentOptions()).OperatingSystem);
        Assert.AreEqual(VBOperatingSystem.Windows, RuntimeEnvironmentProfile.Default.OperatingSystem, "design time and tests are Windows, wherever they run");
    }

    [TestMethod]
    public void ARdprojConstant_OverridesAnOperatingSystemsConstant()
    {
        var project = new RDCoreProject { PrecompilerConstants = { ["Linux"] = "-1", ["Win32"] = "0" } };

        var symbols = Provide(Env(true), project);

        Assert.AreEqual((short)-1, ((VBIntegerValue)symbols["Linux"].Value).Value);
        Assert.AreEqual((short)0, ((VBIntegerValue)symbols["Win32"].Value).Value);
    }

    [TestMethod]
    public void RdprojConstants_AreProvided_AndShadowBuiltIns()
    {
        var project = new RDCoreProject
        {
            PrecompilerConstants = { ["RDDEBUG"] = "1", ["MODE"] = "\"debug\"", ["Win64"] = "0" },
        };

        var symbols = Provide(Env(true), project);

        Assert.AreEqual((short)1, ((VBIntegerValue)symbols["RDDEBUG"].Value).Value);
        Assert.AreEqual("debug", ((VBStringValue)symbols["MODE"].Value).Value);
        Assert.AreEqual((short)0, ((VBIntegerValue)symbols["Win64"].Value).Value, "the .rdproj entry shadows the built-in");
    }

    [TestMethod]
    public void CliOverrides_ShadowRdprojAndBuiltIns()
    {
        var project = new RDCoreProject { PrecompilerConstants = { ["RDDEBUG"] = "0" } };
        var overrides = new Dictionary<string, string> { ["RDDEBUG"] = "1", ["VBA7"] = "0" };

        var symbols = Provide(Env(true), project, overrides);

        Assert.AreEqual((short)1, ((VBIntegerValue)symbols["RDDEBUG"].Value).Value);
        Assert.AreEqual((short)0, ((VBIntegerValue)symbols["VBA7"].Value).Value);
    }

    [TestMethod]
    public void UnparsableConstant_IsSkipped()
    {
        var project = new RDCoreProject { PrecompilerConstants = { ["BAD"] = "1 + 1", ["GOOD"] = "1" } };

        var symbols = Provide(Env(true), project);

        Assert.IsFalse(symbols.ContainsKey("BAD"));
        Assert.IsTrue(symbols.ContainsKey("GOOD"));
    }

    [TestMethod]
    public void EveryConstant_IsVariantDeclared()
    {
        foreach (var symbol in Provide(Env(true), new RDCoreProject { PrecompilerConstants = { ["X"] = "1" } }).Values)
        {
            Assert.AreEqual("Variant", symbol.ResolvedType.Name);
        }
    }
}
