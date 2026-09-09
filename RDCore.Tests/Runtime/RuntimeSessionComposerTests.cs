using System.IO.Abstractions.TestingHelpers;
using RDCore.CLI.Host.Symbols;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Runtime;

[TestClass]
public sealed class RuntimeSessionComposerTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");
    private static readonly Symbol GlobalScope = GlobalSymbols.UnresolvedSymbol;

    private static IRuntimeSession Compose(bool is64Bit, RDCoreProject project, IReadOnlyDictionary<string, string>? defines = null)
    {
        var environment = new RuntimeEnvironmentProfile(is64Bit, 0, 1252, false);
        return RuntimeSessionComposer.Compose(
            environment,
            new ConfigurationSymbolProvider(environment, project, defines),
            new ProjectSymbolProvider(WorkspaceRoot, project, new MockFileSystem()));
    }

    [TestMethod]
    public void ComposedSession_CarriesTheEnvironment()
        => Assert.IsTrue(Compose(is64Bit: true, new RDCoreProject()).Environment.Is64Bit);

    [TestMethod]
    public void ConfigurationSymbols_ResolveInTheSession()
    {
        var project = new RDCoreProject { PrecompilerConstants = { ["RDDEBUG"] = "1" } };

        var session = Compose(is64Bit: true, project);

        Assert.IsTrue(session.Symbols.TryResolve("Win64", GlobalScope, out var win64));
        Assert.AreEqual((short)-1, ((VBIntegerValue)((PrecompilerConstantSymbol)win64!).Value).Value);

        Assert.IsTrue(session.Symbols.TryResolve("RDDEBUG", GlobalScope, out var rdDebug));
        Assert.AreEqual((short)1, ((VBIntegerValue)((PrecompilerConstantSymbol)rdDebug!).Value).Value);
    }

    [TestMethod]
    public void CliDefine_OverridesInTheComposedSession()
    {
        var project = new RDCoreProject { PrecompilerConstants = { ["RDDEBUG"] = "0" } };
        var defines = new Dictionary<string, string> { ["RDDEBUG"] = "1" };

        var session = Compose(is64Bit: true, project, defines);

        Assert.IsTrue(session.Symbols.TryResolve("RDDEBUG", GlobalScope, out var rdDebug));
        Assert.AreEqual((short)1, ((VBIntegerValue)((PrecompilerConstantSymbol)rdDebug!).Value).Value);
    }

    [TestMethod]
    public void ProjectModuleSymbols_ResolveInTheSession()
    {
        var project = new RDCoreProject
        {
            Modules = [new RDCoreModule { RelativeUri = "src/MyModule.bas" }],
        };

        var session = Compose(is64Bit: true, project);

        Assert.IsTrue(session.Symbols.TryResolve("MyModule", GlobalScope, out var module));
        Assert.IsInstanceOfType<VBStandardModuleSymbol>(module);
    }

    [TestMethod]
    public void ProjectModuleSymbol_ResolvesUnderItsVBNameNotItsFileName()
    {
        // an absolute root the MockFileSystem accepts on both Windows and the Linux CI runner.
        var root = Path.Combine(Path.GetTempPath(), "rdcore-composer-vbname");
        var environment = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false);
        var project = new RDCoreProject
        {
            Modules = [new RDCoreModule { RelativeUri = "src/File1.bas" }],
        };
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "src", "File1.bas")] = new("Attribute VB_Name = \"RealName\"\r\n"),
        });

        var session = RuntimeSessionComposer.Compose(
            environment, new ProjectSymbolProvider(new Uri(root), project, fs));

        Assert.IsTrue(session.Symbols.TryResolve("RealName", GlobalScope, out _), "should resolve under the VB_Name");
        Assert.IsFalse(session.Symbols.TryResolve("File1", GlobalScope, out _), "should not resolve under the file name");
    }
}
