using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Facts;
using RDCore.Tests.Semantics.Runtime;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// What an array, and an assignment to one of its elements, leaves known: <c>A(i) = x</c> with a subscript that is not known, an array that
/// is not known, and the statements that make an array known (or not) whatever it held, <c>ReDim</c> and <c>Erase</c>.
/// </summary>
/// <remarks>
/// <c>A</c> is a fixed-size array of Long, declared here: its elements are the declared default, 0, and known. <c>I</c> and <c>U</c> are
/// variables whose values are not known on entry. What is known is read back through the facts of the conversions of the code
/// (<see cref="ConversionFact.IsValueKnown"/>), in source order, as in <see cref="KnowledgeBindingRuntimeTests"/>.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class KnowledgeContainersRuntimeTests
{
    private static readonly VBModuleFieldVariableMemberSymbol A = Variable("A", new VBFixedSizeArrayType(VBLongType.TypeInfo));
    private static readonly VBModuleFieldVariableMemberSymbol R = Variable("R", new VBResizableArrayType(VBLongType.TypeInfo));
    private static readonly VBModuleFieldVariableMemberSymbol I = Variable("I", VBLongType.TypeInfo);
    private static readonly VBModuleFieldVariableMemberSymbol U = Variable("U", VBLongType.TypeInfo);
    private static readonly VBModuleFieldVariableMemberSymbol X = Variable("X", VBLongType.TypeInfo);

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static void Hold(IRuntimeSession session, VBModuleFieldVariableMemberSymbol variable, VBArrayValue array, bool isKnown = true)
    {
        var cell = (IKnowledgeBinding)session.Symbols.Resolver.GetValue(variable);
        cell.Store(new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array)), isKnown);
    }

    private static (RuntimeExecutionOutcome Outcome, bool[] Assigned) Run(bool arrayIsKnown, bool resizableIsKnown, params string[] body)
    {
        var observer = new RecordingAnalysisObserver();
        var (_, outcome) = RuntimeSourceHarness.Run(
            null, [A, R, I, U, X], new RuntimeOutputBuffer(), standardLibrary: false,
            session =>
            {
                Hold(session, A, new VBFixedSizeArrayValue([(1, 10)], VBLongType.TypeInfo), arrayIsKnown);
                Hold(session, R, new VBResizableArrayValue([(1, 3)], VBLongType.TypeInfo), resizableIsKnown);
                ((IKnowledgeBinding)session.Symbols.Resolver.GetValue(I)).Forget();
                ((IKnowledgeBinding)session.Symbols.Resolver.GetValue(U)).Forget();
            },
            ModuleDirectives.None, observer, body);

        // what each assignment was told of the value it assigns, in source order.
        return (outcome, [.. observer.Conversions.Where(conversion => conversion.Site == ConversionSite.Assignment).Select(conversion => conversion.IsValueKnown)]);
    }

    private static bool[] Assigned(params string[] body) => Run(arrayIsKnown: true, resizableIsKnown: true, body).Assigned;

    [TestMethod]
    public void AnElementSelectedByASubscriptThatIsNotKnown_IsNotKnown_ThoughtEveryElementIsKnown()
        => CollectionAssert.AreEqual(new[] { false }, Assigned("X = A(I)"));

    [TestMethod]
    public void AnElementKeptKnownByAnAssignmentOfTheValueItHeld_IsNoLongerKnown_OnceADifferentValueIsAssigned()
        // after A(I) = 0 each element is 0 whichever was assigned; after A(I) = 5 each is 0 or 5, and which is not known.
        => CollectionAssert.AreEqual(new[] { true, true, true, false }, Assigned("A(I) = 0", "X = A(3)", "A(I) = 5", "X = A(4)"));

    [TestMethod]
    public void AnAssignmentToAnUnknownElement_OfTheValueEveryElementHolds_LeavesEveryElementKnown()
        => CollectionAssert.AreEqual(new[] { true, true }, Assigned("A(I) = 0", "X = A(3)"));

    [TestMethod]
    public void AnAssignmentToAnUnknownElement_OfAKnownValueThatDiffers_LeavesNoElementKnown()
        => CollectionAssert.AreEqual(new[] { true, false }, Assigned("A(I) = 5", "X = A(3)"));

    [TestMethod]
    public void AnAssignmentToAnUnknownElement_OfAValueThatIsNotKnown_LeavesNoElementKnown()
        => CollectionAssert.AreEqual(new[] { false, false }, Assigned("A(I) = U", "X = A(3)"));

    [TestMethod]
    public void AnAssignmentToAKnownElement_OfAValueThatIsNotKnown_LeavesOnlyThatElementNotKnown()
        => CollectionAssert.AreEqual(new[] { false, false, true }, Assigned("A(2) = U", "X = A(2)", "X = A(3)"));

    [TestMethod]
    public void AnElementOfAnArrayThatIsNotKnown_IsNotKnown_Whatever_ItWasAssigned()
        => CollectionAssert.AreEqual(new[] { true, false }, Run(arrayIsKnown: false, resizableIsKnown: true, "A(3) = 7", "X = A(3)").Assigned);

    [TestMethod]
    public void ASubscriptOutOfTheBounds_IsAnError_OfAnArrayThatIsKnown_AndNotKnownToBeOneOfAnArrayThatIsNot()
    {
        var known = Run(arrayIsKnown: true, resizableIsKnown: true, "X = R(99)");
        var unknown = Run(arrayIsKnown: true, resizableIsKnown: false, "X = R(99)", "X = 1");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, known.Outcome.Kind);
        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, unknown.Outcome.Kind);
        CollectionAssert.AreEqual(new[] { false, true }, unknown.Assigned);
    }

    // a fixed-size array's bounds are its declaration's, whatever it holds (MS-VBAL §5.2.3.1.3): a subscript outside them is error 9, read or assigned.
    [TestMethod]
    [DataRow("X = A(99)")]
    [DataRow("A(99) = 1")]
    public void ASubscriptOutOfTheBounds_IsAnError_OfAFixedSizeArrayThatIsNotKnown(string statement)
        => Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, Run(arrayIsKnown: false, resizableIsKnown: true, statement).Outcome.Kind);

    [TestMethod]
    public void Erase_MakesAFixedSizeArrayKnown_WhateverItHeld()
        => CollectionAssert.AreEqual(new[] { true }, Run(arrayIsKnown: false, resizableIsKnown: true, "Erase A", "X = A(3)").Assigned);

    [TestMethod]
    public void ReDim_MakesAnArrayKnown_WhateverItHeld()
        => CollectionAssert.AreEqual(new[] { true }, Run(arrayIsKnown: true, resizableIsKnown: false, "ReDim R(1 To 3)", "X = R(2)").Assigned);

    [TestMethod]
    public void ReDim_WithABoundThatIsNotKnown_MakesAnArrayThatIsNotKnown_AndRaisesNothing()
    {
        var run = Run(arrayIsKnown: true, resizableIsKnown: true, "ReDim R(1 To I)", "X = R(2)", "ReDim R(1 To 3)", "X = R(2)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, run.Outcome.Kind);
        CollectionAssert.AreEqual(new[] { false, true }, run.Assigned);
    }

    [TestMethod]
    public void ReDimPreserve_OfAnArrayThatIsNotKnown_IsNotKnown_AndRaisesNothing()
    {
        // the shape it is preserved from is only assumed: a Preserve that would change a bound it may not is not known to be an error.
        var run = Run(arrayIsKnown: true, resizableIsKnown: false, "ReDim Preserve R(1 To 5)", "X = R(2)");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, run.Outcome.Kind);
        CollectionAssert.AreEqual(new[] { false }, run.Assigned);
    }

    [TestMethod]
    public void ReDimPreserve_OfAnArrayThatIsKnown_KeepsWhatItHeld_Known()
    {
        var run = Run(arrayIsKnown: true, resizableIsKnown: true, "R(1) = U", "ReDim Preserve R(1 To 5)", "X = R(1)", "X = R(2)", "X = R(5)");

        // R(1) was assigned a value that is not known and is preserved as that; the rest are the declared default.
        CollectionAssert.AreEqual(new[] { false, false, true, true }, run.Assigned);
    }
}
