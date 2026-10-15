using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.6.10</strong> a simple name that resolves to a constant. A constant has no storage: its value is
/// substituted at each of its use sites (<strong>MS-VBAL §5.4.3.2</strong>), so the symbol carries it and the evaluator
/// folds it.
/// </summary>
/// <remarks>
/// A constant whose symbol carries no value - a workspace <c>Enum</c> member, which nothing gives one yet, or a
/// <c>Const</c> with no expression - has nothing to read either, and neither has an <c>Enum</c>'s own name evaluated as a
/// value. Reading any of them is an interpreter gap, so it ends in an internal error rather than an exception out of the
/// interpreter.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.6.10 Simple Name Expressions")]
public sealed class ConstantReadTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBConstantMemberSymbol Constant(string name, ExpressionNode? value)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, VBLongType.TypeInfo,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Public, value);

    private static LiteralExpressionNode Literal(int value)
        => new(new SyntaxNodeId(RuntimeSourceHarness.ModuleUri.AbsolutePath, []),
            new SourceLocation(RuntimeSourceHarness.ModuleUri, SourceRange.Empty), new VBLongValue(value));

    // Enum Colour: Red - with no value on its member, as a workspace Enum's members have.
    private static Symbol[] Colour()
    {
        var symbol = new VBEnumMemberSymbol(Workspace, RuntimeSourceHarness.ModuleUri, "Colour", ScopeKind.Module, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        var red = new VBEnumConstMemberSymbol(
            Workspace, symbol.Uri, "Red", ScopeKind.Module, SymbolKindExt.EnumMember, SourceRange.Empty, SourceRange.Empty);
        return [symbol with { ResolvedType = new VBEnumType(symbol, [red]) }, red];
    }

    private static (RuntimeExecutionOutcome Outcome, IReadOnlyList<string> Output) Run(
        IEnumerable<Symbol> symbols, bool standardLibrary, params string[] body)
    {
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, symbols, output, standardLibrary, body);
        return (outcome, output.Lines);
    }

    [TestMethod]
    public void AValuedModuleConst_FoldsToItsValue()
    {
        var (outcome, output) = Run([Constant("Answer", Literal(42))], standardLibrary: false, "Debug.Print Answer & \"!\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("42!", output.Single().Trim());
    }

    [TestMethod]
    public void ALocalConst_FoldsToItsValue()
    {
        // the declaration pass puts the Const on the procedure with its expression, which is all a use site needs.
        var (outcome, output) = Run([], standardLibrary: false, "Const K As Long = 42", "Debug.Print K");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual("42", output.Single().Trim());
    }

    [TestMethod]
    public void AnUnvaluedModuleConst_IsAnInternalErrorNotACrash()
    {
        var (outcome, _) = Run([Constant("Nope", value: null)], standardLibrary: false, "Debug.Print Nope");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
    }

    [TestMethod]
    public void AnUnvaluedWorkspaceEnumMember_IsAnInternalErrorNotACrash()
    {
        var (outcome, _) = Run(Colour(), standardLibrary: false, "Debug.Print Red");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
    }

    [TestMethod]
    public void AnEnumNameAsAValue_IsAnInternalErrorNotACrash()
    {
        // the Enum's own name is a type, with neither a value nor storage. A constant qualified by it is resolved as a
        // member of it instead (PredefinedEnumValueTests).
        var (outcome, _) = Run([], standardLibrary: true, "Debug.Print VbCompareMethod");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.InternalError, outcome.Kind);
    }
}
