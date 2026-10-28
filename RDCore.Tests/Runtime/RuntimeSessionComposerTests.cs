using System.IO.Abstractions.TestingHelpers;
using RDCore.CLI.Host.Symbols;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;
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
    public void ASessionIsADebugBuild_ByDefault()
    {
        // the built-in DEBUG constant, lowest precedence like Win64 and the rest. A dev tool defaults
        // to a debug build, and a session composed without the constant at all is one too - dropping
        // every Debug.Print because a provider was missing is not a recoverable answer.
        Assert.IsTrue(Compose(is64Bit: true, new RDCoreProject()).IsDebugBuild());
        Assert.IsTrue(RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false)).IsDebugBuild());
    }

    [TestMethod]
    public void AReleaseConfiguration_IsAReleaseBuild()
    {
        var environment = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, Configuration: VBBuildConfiguration.Release);
        var session = RuntimeSessionComposer.Compose(environment, new ConfigurationSymbolProvider(environment, new RDCoreProject()));

        Assert.IsFalse(session.IsDebugBuild());
    }

    [TestMethod]
    public void AProjectThatDefinesDebug_DecidesTheBuild_OverTheConfiguration()
    {
        // the configuration is only the default of the built-in constant: a project's #Const DEBUG still wins.
        var environment = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false, Configuration: VBBuildConfiguration.Release);
        var project = new RDCoreProject { PrecompilerConstants = { ["DEBUG"] = "-1" } };
        var session = RuntimeSessionComposer.Compose(environment, new ConfigurationSymbolProvider(environment, project));

        Assert.IsTrue(session.IsDebugBuild());
    }

    [TestMethod]
    public void TheProfile_CarriesTheEngineAndTheConfiguration_FromTheOptions()
    {
        var profile = RuntimeEnvironmentProfile.From(new SDK.Server.Configuration.SdkEnvironmentOptions
        {
            ExecutionEngine = VBExecutionEngine.Compiled,
            Configuration = VBBuildConfiguration.Release,
        });

        Assert.AreEqual(VBExecutionEngine.Compiled, profile.ExecutionEngine);
        Assert.AreEqual(VBBuildConfiguration.Release, profile.Configuration);
        Assert.AreEqual(VBExecutionEngine.Interpreter, RuntimeEnvironmentProfile.Default.ExecutionEngine);
        Assert.AreEqual(VBBuildConfiguration.Debug, RuntimeEnvironmentProfile.Default.Configuration);
    }

    [TestMethod]
    public void AProjectThatDefinesDebugAsZero_IsAReleaseBuild()
    {
        var project = new RDCoreProject { PrecompilerConstants = { ["DEBUG"] = "0" } };

        Assert.IsFalse(Compose(is64Bit: true, project).IsDebugBuild());
    }

    [TestMethod]
    public void ACliDefine_DecidesTheBuild_OverTheProject()
    {
        // the precedence a project already has over its constants applies to this one unchanged, which
        // is the whole reason the build is a conditional compilation constant rather than a setting.
        var project = new RDCoreProject { PrecompilerConstants = { ["DEBUG"] = "-1" } };
        var defines = new Dictionary<string, string> { ["DEBUG"] = "0" };

        Assert.IsFalse(Compose(is64Bit: true, project, defines).IsDebugBuild());
    }

    [TestMethod]
    public void ADebugConditionalConstant_DoesNotCollideWithTheDebugObject()
    {
        // the collision this fix is for: both are global, both are named Debug, and before conditional
        // compilation constants had a binding context of their own the name resolved to neither of them.
        // MS-VBAL §3.4.1 keeps them apart - the #Const is accessible to cc-expressions, the object is a
        // value - so a project can define DEBUG and still write Debug.Print.
        var project = new RDCoreProject { PrecompilerConstants = { ["DEBUG"] = "-1" } };
        var environment = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false);
        var session = RuntimeSessionComposer.Compose(
            environment,
            new ConfigurationSymbolProvider(environment, project),
            new StdLibSymbolProvider(WorkspaceRoot));

        Assert.IsTrue(session.Symbols.TryResolveValue("Debug", GlobalScope, out var debugObject));
        Assert.IsInstanceOfType<VBPredeclaredInstanceSymbol>(debugObject);

        Assert.IsTrue(session.Symbols.TryResolveConditionalConstant("DEBUG", GlobalScope, out var debugConstant));
        Assert.IsInstanceOfType<PrecompilerConstantSymbol>(debugConstant);
    }

    [TestMethod]
    public void ComposedSession_CarriesTheEnvironment()
        => Assert.IsTrue(Compose(is64Bit: true, new RDCoreProject()).Environment.Is64Bit);

    [TestMethod]
    public void ComposedSession_WithoutReferences_CarriesAnEmptyReferenceList()
        => Assert.IsEmpty(Compose(is64Bit: true, new RDCoreProject()).References);

    [TestMethod]
    public void ComposedSession_PreservesReferenceOrderExactlyAsGiven()
    {
        var environment = new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false);
        // deliberately not in ascending-priority order: the composer must not re-sort.
        ReferencePriorityInfo[] references = [new("Excel", 1), new("VBA", 0)];

        var session = RuntimeSessionComposer.Compose(environment, references, []);

        CollectionAssert.AreEqual(new[] { "Excel", "VBA" }, session.References.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void ConfigurationSymbols_ResolveInTheSession()
    {
        var project = new RDCoreProject { PrecompilerConstants = { ["RDDEBUG"] = "1" } };

        var session = Compose(is64Bit: true, project);

        Assert.IsTrue(session.Symbols.TryResolveConditionalConstant("Win64", GlobalScope, out var win64));
        Assert.AreEqual((short)-1, ((VBIntegerValue)((PrecompilerConstantSymbol)win64!).Value).Value);

        Assert.IsTrue(session.Symbols.TryResolveConditionalConstant("RDDEBUG", GlobalScope, out var rdDebug));
        Assert.AreEqual((short)1, ((VBIntegerValue)((PrecompilerConstantSymbol)rdDebug!).Value).Value);

        // ...and only there. MS-VBAL §3.4.1: a #Const is accessible to cc-expressions, so `x = Win64`
        // in ordinary source binds nothing - which is also what keeps a DEBUG constant from colliding
        // with the Debug object.
        Assert.IsFalse(session.Symbols.TryResolveValue("Win64", GlobalScope, out _));
        Assert.IsFalse(session.Symbols.TryResolveValue("RDDEBUG", GlobalScope, out _));
    }

    [TestMethod]
    public void CliDefine_OverridesInTheComposedSession()
    {
        var project = new RDCoreProject { PrecompilerConstants = { ["RDDEBUG"] = "0" } };
        var defines = new Dictionary<string, string> { ["RDDEBUG"] = "1" };

        var session = Compose(is64Bit: true, project, defines);

        Assert.IsTrue(session.Symbols.TryResolveConditionalConstant("RDDEBUG", GlobalScope, out var rdDebug));
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

        Assert.IsTrue(session.Symbols.TryResolveValue("MyModule", GlobalScope, out var module));
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

        Assert.IsTrue(session.Symbols.TryResolveValue("RealName", GlobalScope, out _), "should resolve under the VB_Name");
        Assert.IsFalse(session.Symbols.TryResolveValue("File1", GlobalScope, out _), "should not resolve under the file name");
    }
}
