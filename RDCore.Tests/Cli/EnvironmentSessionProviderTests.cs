using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using RDCore.CLI.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Workspace;

namespace RDCore.Tests.Cli;

[TestClass]
public sealed class EnvironmentSessionProviderTests
{
    private static readonly Uri WorkspaceRoot = new("file:///c:/ws/");
    private static readonly Symbol GlobalScope = GlobalSymbols.UnresolvedSymbol;

    private static EnvironmentSessionProvider NewProvider(MockFileSystem? fileSystem = null)
        => new(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false),
            fileSystem ?? new MockFileSystem(),
            NullLogger<EnvironmentSessionProvider>.Instance);

    [TestMethod]
    public void Session_BeforeCompose_Throws()
    {
        var sut = NewProvider();

        Assert.IsFalse(sut.IsComposed);
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = sut.Session);
    }

    [TestMethod]
    public void Compose_MakesModuleAndConstantSymbolsResolvable()
    {
        var project = new RDCoreProject
        {
            Modules = [new RDCoreModule { RelativeUri = "src/MyModule.bas" }],
            PrecompilerConstants = { ["RDDEBUG"] = "1" },
        };
        var sut = NewProvider();

        var session = sut.Compose(project, WorkspaceRoot);

        Assert.IsTrue(sut.IsComposed);
        Assert.AreSame(session, sut.Session);

        Assert.IsTrue(session.Symbols.TryResolve("MyModule", GlobalScope, out var module));
        Assert.IsInstanceOfType<VBStandardModuleSymbol>(module);

        Assert.IsTrue(session.Symbols.TryResolve("RDDEBUG", GlobalScope, out var rdDebug));
        Assert.AreEqual((short)1, ((VBIntegerValue)((PrecompilerConstantSymbol)rdDebug!).Value).Value);

        Assert.IsTrue(session.Symbols.TryResolve("Win64", GlobalScope, out var win64));
        Assert.AreEqual((short)-1, ((VBIntegerValue)((PrecompilerConstantSymbol)win64!).Value).Value);
    }

    [TestMethod]
    public void Compose_ResolvesModuleName_FromVBNameAttribute_NotFileName()
    {
        // an absolute root the MockFileSystem accepts on both Windows and the Linux CI runner.
        var root = Path.Combine(Path.GetTempPath(), "rdcore-esp-vbname");
        var project = new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = "src/oldName.bas" }] };
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "src", "oldName.bas")] = new("Attribute VB_Name = \"Renamed\"\r\nOption Explicit\r\n"),
        });

        var session = NewProvider(fs).Compose(project, new Uri(root));

        Assert.IsTrue(session.Symbols.TryResolve("Renamed", GlobalScope, out var module), "resolves under VB_Name");
        Assert.IsInstanceOfType<VBStandardModuleSymbol>(module);
        Assert.IsFalse(session.Symbols.TryResolve("oldName", GlobalScope, out _), "not under the file name");
    }

    [TestMethod]
    public void Compose_FallsBackToFileName_WhenSourceHasNoVBNameAttribute()
    {
        var root = Path.Combine(Path.GetTempPath(), "rdcore-esp-fallback");
        var project = new RDCoreProject { Modules = [new RDCoreModule { RelativeUri = "src/Helpers.bas" }] };
        var fs = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(root, "src", "Helpers.bas")] = new("Option Explicit\r\nPublic X As Long\r\n"),
        });

        var session = NewProvider(fs).Compose(project, new Uri(root));

        Assert.IsTrue(session.Symbols.TryResolve("Helpers", GlobalScope, out _));
    }

    [TestMethod]
    public void Compose_Twice_ReplacesTheSession()
    {
        var sut = NewProvider();

        var first = sut.Compose(new RDCoreProject(), WorkspaceRoot);
        var second = sut.Compose(new RDCoreProject(), WorkspaceRoot);

        Assert.AreNotSame(first, second);
        Assert.AreSame(second, sut.Session);
    }
}
