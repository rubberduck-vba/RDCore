using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime;

/// <summary>
/// The surroundings <see cref="RuntimeSourceHarness"/> composes a session of, beyond the symbols a body refers to:
/// what a suite arranges there has to be what the body actually runs against, or a test passes for the wrong reason.
/// </summary>
[TestClass]
public sealed class RuntimeSourceHarnessTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    [TestMethod]
    public void AnExtraProcedure_IsCallableFromTheBody()
    {
        var stub = new VBFunctionMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "Twice", ScopeKind.Module,
            SymbolKindExt.Function, VBLongType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var n = new VBParameterSymbol(Workspace, stub.Uri, "n", SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal, VBLongType.TypeInfo);
        var twice = stub with { Parameters = [n] };
        var output = new RuntimeOutputBuffer();

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [], output, standardLibrary: false, arrange: null,
            new RuntimeSourceHarness.Surroundings { Procedures = [(twice, ["Twice = n * 2"])] },
            "Debug.Print Twice(21)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("42", output.Lines.Single().Trim());
    }

    [TestMethod]
    public void TheDirectives_AreTheOnesAnExtraProcedureRunsUnderToo()
    {
        // a callee's frame takes the directives of the module declaring it, not the caller's: the harness's module has
        // to carry them, or the body runs under Option Compare Text and a procedure of its own module under the default.
        var stub = new VBFunctionMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "SameText", ScopeKind.Module,
            SymbolKindExt.Function, VBBooleanType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var a = new VBParameterSymbol(Workspace, stub.Uri, "a", SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal, VBStringType.TypeInfo);
        var b = new VBParameterSymbol(Workspace, stub.Uri, "b", SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal, VBStringType.TypeInfo);
        var sameText = stub with { Parameters = [a, b] };
        var output = new RuntimeOutputBuffer();

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [], output, standardLibrary: false, arrange: null,
            new RuntimeSourceHarness.Surroundings
            {
                Directives = new ModuleDirectives(Compare: OptionCompare.Text),
                Procedures = [(sameText, ["SameText = a = b"])],
            },
            "Debug.Print SameText(\"a\", \"A\")");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("True", output.Lines.Single().Trim());
    }

    [TestMethod]
    public void TheEnvironment_IsTheOneTheSessionRunsUnder()
    {
        var lcid = 0;

        RuntimeSourceHarness.Run(
            fileSystem: null, [], output: null, standardLibrary: false,
            session => lcid = session.Environment.Lcid,
            new RuntimeSourceHarness.Surroundings { Environment = new RuntimeEnvironmentProfile(Is64Bit: true, 1036, 1252, false) },
            "Debug.Print 1");

        Assert.AreEqual(1036, lcid);
    }

    [TestMethod]
    public void ADispatcherSpy_SeesEachExternalCallWithItsArguments_AndTheCallStillRuns()
    {
        RuntimeSourceHarness.RecordingDispatcher? spy = null;
        var output = new RuntimeOutputBuffer();

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [], output, standardLibrary: true, arrange: null,
            new RuntimeSourceHarness.Surroundings { Dispatcher = inner => spy = new RuntimeSourceHarness.RecordingDispatcher(inner) },
            "Debug.Print Len(\"abc\")");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsNotNull(spy);
        var request = spy.Requests.Single();
        Assert.AreEqual("Len", request.Member.Name);
        Assert.AreEqual("abc", request.Arguments[0].BoxedValue);
        Assert.AreEqual("3", output.Lines.Single().Trim());
    }
}
