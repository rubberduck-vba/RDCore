using RDCore.CLI.Host.Symbols;
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

    [TestMethod]
    public void BuiltIns_ReflectBitness()
    {
        var x64 = Provide(Env(true), new RDCoreProject());
        Assert.AreEqual((short)-1, ((VBIntegerValue)x64["Win64"].Value).Value);
        Assert.AreEqual((short)0, ((VBIntegerValue)x64["Win32"].Value).Value);
        Assert.AreEqual((short)-1, ((VBIntegerValue)x64["VBA7"].Value).Value);

        var x86 = Provide(Env(false), new RDCoreProject());
        Assert.AreEqual((short)0, ((VBIntegerValue)x86["Win64"].Value).Value);
        Assert.AreEqual((short)-1, ((VBIntegerValue)x86["Win32"].Value).Value);
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
