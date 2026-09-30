using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3-4</strong> the <c>ReDim</c> and <c>Erase</c> statements.
/// </summary>
/// <remarks>
/// The two that change an array's shape rather than its elements, and each other's opposite: <c>ReDim</c>
/// gives a resizable array dimensions and <c>Erase</c> takes them away again. A <c>ReDim</c>'s bounds are
/// evaluated where the statement stands, which is why several of these give one a name to resolve rather
/// than a literal.
/// </remarks>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim")]
[TestCategory("MS-VBAL 5.4.3.4 Erase")]
public sealed class ArrayStatementTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static (RuntimeExecutionOutcome Outcome, Func<string, VBArrayValue?> Array) Run(
        (string Name, VBType Type)[] variables, params string[] body)
        => Run(variables, ModuleDirectives.None, arrange: null, body);

    private static (RuntimeExecutionOutcome Outcome, Func<string, VBArrayValue?> Array) Run(
        (string Name, VBType Type)[] variables, ModuleDirectives directives,
        Action<IRuntimeSession, Dictionary<string, Symbol>>? arrange, params string[] body)
    {
        var symbols = variables.Select(variable => Variable(variable.Name, variable.Type)).ToArray();
        var byName = symbols.ToDictionary(symbol => symbol.Name, symbol => (Symbol)symbol);

        var (session, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, symbols, output: null, standardLibrary: false,
            arrange: arrange is null ? null : runtime => arrange(runtime, byName),
            directives, body);

        return (outcome, name =>
        {
            var symbol = symbols.Single(candidate => candidate.Name == name);
            var value = symbol.ResolvedType!.CreateValue(session.Symbols.Resolver.GetValue(symbol));

            // a Variant target reports the array it holds, not the wrapper it holds it in.
            while (value is VBVariantValue { TypedValue: { } wrapped })
            {
                value = wrapped;
            }

            return value as VBArrayValue;
        });
    }

    private static object? ElementAt(VBArrayValue array, params int[] subscripts)
        => array.GetElementHandle(subscripts)?.Value.BoxedValue;

    // an untouched Variant element holds Empty, which boxes as itself rather than as null - so "back at its
    // default" is a type test and not a null check.
    private static void AssertIsDefault(VBArrayValue array, int subscript, string because)
        => Assert.IsInstanceOfType<VBRuntimeEmptyValue>(ElementAt(array, subscript), because);

    [TestMethod]
    public void Redim_GivesAResizableArrayItsDimensions()
    {
        var (outcome, array) = Run([("Buffer", VBResizableArrayType.TypeInfo)], "ReDim Buffer(1 To 3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        var resized = array("Buffer")!;
        Assert.AreEqual(1, resized.Rank);
        Assert.AreEqual(1, resized.Dimensions[0].LowerBound);
        Assert.AreEqual(3, resized.Dimensions[0].UpperBound);
    }

    [TestMethod]
    public void Redim_EvaluatesItsBoundsWhereTheStatementStands()
    {
        // the whole reason a ReDim is a statement and not only a declaration: `n` is not knowable until it
        // runs, and a bound kept as text could never be evaluated at all.
        var (outcome, array) = Run(
            [("Buffer", VBResizableArrayType.TypeInfo), ("N", VBLongType.TypeInfo)],
            "N = 4",
            "ReDim Buffer(1 To N + 1)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(5, array("Buffer")!.Dimensions[0].UpperBound);
    }

    [TestMethod]
    public void Redim_AnOmittedLowerBound_IsZeroByDefault()
    {
        var (outcome, array) = Run([("Buffer", VBResizableArrayType.TypeInfo)], "ReDim Buffer(3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(0, array("Buffer")!.Dimensions[0].LowerBound);
        Assert.AreEqual(4, array("Buffer")!.Length, "0 To 3 is four elements");
    }

    [TestMethod]
    public void Redim_AnOmittedLowerBound_FollowsOptionBase()
    {
        // MS-VBAL §5.2.1.2, a module dial the statement cannot see for itself - it rides on the frame.
        var (outcome, array) = Run(
            [("Buffer", VBResizableArrayType.TypeInfo)], new ModuleDirectives(Base: 1), arrange: null,
            "ReDim Buffer(3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(1, array("Buffer")!.Dimensions[0].LowerBound);
        Assert.AreEqual(3, array("Buffer")!.Length, "1 To 3 is three elements");
    }

    [TestMethod]
    public void Redim_MoreThanOneDimension_GivesTheArrayThatRank()
    {
        var (outcome, array) = Run([("Grid", VBResizableArrayType.TypeInfo)], "ReDim Grid(1 To 2, 1 To 3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(2, array("Grid")!.Rank);
        Assert.AreEqual(6, array("Grid")!.Length);
    }

    [TestMethod]
    public void Redim_WithoutPreserve_ResetsEveryElement()
    {
        // "Each element in the array is reset to the default value for its data type, unless the word
        // preserve is specified."
        var (outcome, array) = Run(
            [("Buffer", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Buffer"], 1, 3),
            "ReDim Buffer(1 To 3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        AssertIsDefault(array("Buffer")!, 1, "the elements are back at a Variant's own default");
    }

    [TestMethod]
    public void Redim_Preserve_KeepsTheElementsThatStillFit()
    {
        var (outcome, array) = Run(
            [("Buffer", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Buffer"], 1, 3),
            "ReDim Preserve Buffer(1 To 5)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(10, Convert.ToInt32(ElementAt(array("Buffer")!, 1)));
        Assert.AreEqual(30, Convert.ToInt32(ElementAt(array("Buffer")!, 3)));
    }

    [TestMethod]
    public void Redim_Preserve_Growing_LeavesTheExtraElementsAtTheirDefault()
    {
        // "If a <redim-statement> containing the keyword Preserve results in more elements in a dimension,
        // each of the extra elements is set to its default data value."
        var (outcome, array) = Run(
            [("Buffer", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Buffer"], 1, 3),
            "ReDim Preserve Buffer(1 To 5)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(5, array("Buffer")!.Length);
        AssertIsDefault(array("Buffer")!, 5, "an element the old array never had");
    }

    [TestMethod]
    public void Redim_Preserve_Shrinking_DiscardsWhatNoLongerFits()
    {
        var (outcome, array) = Run(
            [("Buffer", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Buffer"], 1, 5),
            "ReDim Preserve Buffer(1 To 2)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(2, array("Buffer")!.Length);
        Assert.AreEqual(20, Convert.ToInt32(ElementAt(array("Buffer")!, 2)));
    }

    [TestMethod]
    public void Redim_Preserve_ChangingTheRank_IsSubscriptOutOfRange()
    {
        // "the number of dimensions might not be changed ... will result in Error 9."
        var (outcome, _) = Run(
            [("Grid", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Grid"], 1, 3),
            "ReDim Preserve Grid(1 To 3, 1 To 2)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Redim_Preserve_ChangingALowerBound_IsSubscriptOutOfRange()
    {
        var (outcome, _) = Run(
            [("Buffer", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Buffer"], 1, 3),
            "ReDim Preserve Buffer(2 To 3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Redim_Preserve_ChangingAnUpperBoundThatIsNotTheLast_IsSubscriptOutOfRange()
    {
        // only the last dimension's upper bound may move; a two-dimensional array's first may not.
        var (outcome, _) = Run(
            [("Grid", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Grid"], 1, 2, 1, 2),
            "ReDim Preserve Grid(1 To 3, 1 To 2)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Redim_Preserve_ChangingTheLastUpperBoundOfSeveralDimensions_IsAllowed()
    {
        var (outcome, array) = Run(
            [("Grid", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Grid"], 1, 2, 1, 2),
            "ReDim Preserve Grid(1 To 2, 1 To 4)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(8, array("Grid")!.Length);
    }

    [TestMethod]
    public void Redim_AnUpperBoundBelowTheLower_IsSubscriptOutOfRange()
    {
        var (outcome, _) = Run([("Buffer", VBResizableArrayType.TypeInfo)], "ReDim Buffer(5 To 1)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.SubscriptOutOfRange, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Redim_AVariantThatHoldsNothingYet_BecomesAnArray()
    {
        // the static rule admits a Variant target precisely so it can become an array, so the Empty one
        // starts as cannot be the "value type is not an array" error 13 names.
        var (outcome, array) = Run([("V", VBVariantType.TypeInfo)], "ReDim V(1 To 3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(3, array("V")!.Length);
    }

    [TestMethod]
    public void Redim_ATargetHoldingSomethingThatIsNotAnArray_IsTypeMismatch()
    {
        // "Runtime Error 13 is raised if the declared type of a redimensioned variable is Variant and its
        // value type is not an array."
        var (outcome, _) = Run([("V", VBVariantType.TypeInfo)], "V = 42", "ReDim V(1 To 3)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void Redim_MoreThanOneTarget_ResizesEachOfThem()
    {
        var (outcome, array) = Run(
            [("A", VBResizableArrayType.TypeInfo), ("B", VBResizableArrayType.TypeInfo)],
            "ReDim A(1 To 2), B(1 To 5)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(2, array("A")!.Length);
        Assert.AreEqual(5, array("B")!.Length);
    }

    [TestMethod]
    public void Erase_AResizableArray_LosesItsDimensions()
    {
        // "removes the dimensions and data of a resizable array (setting it back to its initial state)".
        var (outcome, array) = Run(
            [("Buffer", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Buffer"], 1, 3),
            "Erase Buffer");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(0, array("Buffer")!.Rank);
        Assert.IsFalse(array("Buffer")!.IsInitialized);
    }

    [TestMethod]
    public void Erase_AFixedSizeArray_KeepsItsDimensionsAndResetsItsElements()
    {
        // "reinitializes the elements of a fixed-size array to their default values" - the bounds are part of
        // its declaration, and nothing at run time may change them.
        var (outcome, array) = Run(
            [("Fixed", new VBFixedSizeArrayType(VBVariantType.TypeInfo))], ModuleDirectives.None,
            (session, symbols) => Fill(session, symbols["Fixed"], [1, 3], fixedSize: true),
            "Erase Fixed");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(3, array("Fixed")!.Length, "the dimensions stay");
        AssertIsDefault(array("Fixed")!, 1, "and the elements are back at their default");
    }

    [TestMethod]
    public void Erase_MoreThanOneElement_ErasesEachOfThem()
    {
        var (outcome, array) = Run(
            [("A", VBResizableArrayType.TypeInfo), ("B", VBResizableArrayType.TypeInfo)], ModuleDirectives.None,
            (session, symbols) =>
            {
                Fill(session, symbols["A"], 1, 2);
                Fill(session, symbols["B"], 1, 2);
            },
            "Erase A, B");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.AreEqual(0, array("A")!.Rank);
        Assert.AreEqual(0, array("B")!.Rank);
    }

    [TestMethod]
    public void Erase_SomethingThatIsNotAnArray_IsTypeMismatch()
    {
        var (outcome, _) = Run([("N", VBLongType.TypeInfo)], "Erase N");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch, outcome.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void RedimThenErase_LeaveTheArrayWhereItStarted()
    {
        // the pair, read as each other's opposite: what ReDim gives, Erase takes away.
        var (outcome, array) = Run([("Buffer", VBResizableArrayType.TypeInfo)],
            "ReDim Buffer(1 To 3)",
            "Erase Buffer");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, outcome.ErrorInfo?.Verbose);
        Assert.IsFalse(array("Buffer")!.IsInitialized);
    }

    /// <summary>Gives an array variable dimensions and a value of 10x its subscript in each element.</summary>
    private static void Fill(IRuntimeSession session, Symbol symbol, params int[] bounds)
        => Fill(session, symbol, bounds, fixedSize: false);

    private static void Fill(IRuntimeSession session, Symbol symbol, int[] bounds, bool fixedSize)
    {
        var dimensions = Enumerable.Range(0, bounds.Length / 2)
            .Select(index => (bounds[index * 2], bounds[index * 2 + 1])).ToArray();

        VBArrayValue array = fixedSize
            ? new VBFixedSizeArrayValue(dimensions)
            : new VBResizableArrayValue(dimensions);

        if (dimensions.Length == 1)
        {
            for (var subscript = dimensions[0].Item1; subscript <= dimensions[0].Item2; subscript++)
            {
                array.TrySetElement(new VBLongValue(subscript * 10).Handle, subscript);
            }
        }

        session.Symbols.Resolver.TryAllocate(symbol, array, out _);
    }
}
