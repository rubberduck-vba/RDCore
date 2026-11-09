using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.6.10</strong> a simple name written with a <em>type-declaration character</em> (<c>S$</c>,
/// <c>I%</c>). The character is not part of the name: the tiers are searched for "the name value of &lt;name&gt;", so a
/// typed name binds what its bare name does, wherever a simple name is read, called or assigned.
/// </summary>
/// <remarks>
/// The library is the exception, and only where the bare name binds one of its members (or nothing): it declares its
/// <c>$</c> functions as members of their own, so there the typed name binds the member named with the character.
/// A declaration of the enclosing procedure or module still outranks the library, typed name or not.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
public sealed class TypedNameTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static (RuntimeExecutionOutcome Outcome, IReadOnlyList<string> Output, IRuntimeSession Session) Run(
        Symbol[] symbols, bool standardLibrary, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (session, outcome) = RuntimeSourceHarness.Run(fileSystem: null, symbols, output, standardLibrary, body);
        return (outcome, output.Lines, session);
    }

    private static object? ValueOf(IRuntimeSession session, Symbol symbol)
        => session.Symbols.Resolver.GetValue(symbol).Value.BoxedValue;

    [TestMethod]
    public void ATypedVariable_IsAssignedAndReadByItsName()
    {
        var s = Variable("S", VBStringType.TypeInfo);

        var (outcome, output, session) = Run([s], standardLibrary: false, "S$ = \"x\"", "Debug.Print S$");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("x", output.Single().Trim());
        Assert.AreEqual("x", ValueOf(session, s));
    }

    [TestMethod]
    public void ATypedForCounter_BindsItsVariable()
    {
        var i = Variable("I", VBIntegerType.TypeInfo);

        var (outcome, output, _) = Run([i], standardLibrary: false, "For I% = 1 To 2", "Debug.Print I%", "Next");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        CollectionAssert.AreEqual(new[] { "1", "2" }, output.Select(line => line.Trim()).ToArray());
    }

    [TestMethod]
    public void ATypedLSetTarget_BindsItsVariable()
    {
        // LSet resolves its target the way Let, Input # and Get do, so this is all of them.
        var s = Variable("S", VBStringType.TypeInfo);

        var (outcome, _, session) = Run([s], standardLibrary: false, "S = \"xxxx\"", "LSet S$ = \"ab\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("ab  ", ValueOf(session, s));
    }

    [TestMethod]
    public void ATypedNameSharingAFieldsName_ResolvesTheField()
    {
        // the library declares Left and Left$, but a field of the enclosing module named Left is what Left means here,
        // and so it is what Left$ means: the module's tier is searched before the library's.
        var left = Variable("Left", VBStringType.TypeInfo);

        var (outcome, output, session) = Run([left], standardLibrary: true, "Left$ = \"x\"", "Debug.Print Left$");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("x", output.Single().Trim());
        Assert.AreEqual("x", ValueOf(session, left));
    }

    [TestMethod]
    public void ATypedNameSharingAProceduresName_CallsTheProcedure()
    {
        // a Function of the module named Trim, called as Trim$ and assigning its result as Trim$: both are the Function,
        // not the library's Trim$.
        var stub = new VBFunctionMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "Trim", ScopeKind.Module,
            SymbolKindExt.Function, VBStringType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var text = new VBParameterSymbol(Workspace, stub.Uri, "Text", SourceRange.Empty, SourceRange.Empty, ParameterKind.ExplicitByVal, VBStringType.TypeInfo);
        var trim = stub with { Parameters = [text] };
        var output = new RuntimeOutputBuffer();

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [], output, standardLibrary: true, arrange: null,
            new RuntimeSourceHarness.Surroundings { Procedures = [(trim, ["Trim$ = \"[\" & Text & \"]\""])] },
            "Debug.Print Trim$(\"a\")");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("[a]", output.Lines.Single().Trim());
    }
}
