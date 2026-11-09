using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
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
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Facts;
using RDCore.Tests.Semantics.Runtime;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// A variable remembers whether it holds a value the analysis knows (<see cref="IKnowledgeBinding"/>): an assignment of a value that is
/// not known leaves it not known, and an assignment of one that is known makes it known again.
/// </summary>
/// <remarks>
/// The variables are module-level ones, the state the code starts from: <c>U</c> is a variable whose value is not known on entry,
/// the way a parameter or a module variable that other procedures write is. What is known is read back through the facts the
/// conversions of the code state (<see cref="ConversionFact.IsValueKnown"/>), which is what an analyzer would see.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class KnowledgeBindingRuntimeTests
{
    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(TestUri.WorkspaceRoot(), RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static void Forget(IRuntimeSession session, VBModuleFieldVariableMemberSymbol variable)
        => ((IKnowledgeBinding)session.Symbols.Resolver.GetValue(variable)).Forget();

    private static bool IsKnown(IRuntimeSession session, VBModuleFieldVariableMemberSymbol variable)
        => ((IKnowledgeBinding)session.Symbols.Resolver.GetValue(variable)).IsKnown;

    private static (IRuntimeSession Session, RuntimeExecutionOutcome Outcome, RecordingAnalysisObserver Observer) Run(
        VBModuleFieldVariableMemberSymbol[] variables, VBModuleFieldVariableMemberSymbol[] unknown, Action<IRuntimeSession>? arrange, params string[] body)
    {
        var observer = new RecordingAnalysisObserver();
        var (session, outcome) = RuntimeSourceHarness.Run(
            null, variables, new RuntimeOutputBuffer(), standardLibrary: false,
            current =>
            {
                foreach (var variable in unknown)
                {
                    Forget(current, variable);
                }

                arrange?.Invoke(current);
            },
            ModuleDirectives.None, observer, body);
        return (session, outcome, observer);
    }

    // what each assignment was told of the value it assigns, in source order.
    private static bool[] Assigned(RecordingAnalysisObserver observer)
        => [.. observer.Conversions.Where(conversion => conversion.Site == ConversionSite.Assignment).Select(conversion => conversion.IsValueKnown)];

    [TestMethod]
    public void AVariableAssignedAKnownValue_IsKnown_WhateverItWasBefore()
    {
        var u = Variable("U", VBLongType.TypeInfo);

        var (session, _, _) = Run([u], [u], null, "U = 5");

        Assert.IsTrue(IsKnown(session, u));
    }

    [TestMethod]
    public void AVariableAssignedAValueThatIsNotKnown_IsNotKnown()
    {
        var u = Variable("U", VBLongType.TypeInfo);
        var x = Variable("X", VBLongType.TypeInfo);

        var (session, _, _) = Run([u, x], [u], null, "X = U");

        Assert.IsFalse(IsKnown(session, x));
    }

    [TestMethod]
    public void AVariableAssignedSomethingDerivedFromAValueThatIsNotKnown_IsNotKnown()
    {
        var u = Variable("U", VBLongType.TypeInfo);
        var x = Variable("X", VBLongType.TypeInfo);

        var (session, _, _) = Run([u, x], [u], null, "X = U + 1");

        Assert.IsFalse(IsKnown(session, x));
    }

    [TestMethod]
    public void AVariableThatIsNotKnown_IsKnownAgain_OnceItIsAssignedAKnownValue()
    {
        var u = Variable("U", VBLongType.TypeInfo);
        var x = Variable("X", VBLongType.TypeInfo);

        var (session, _, observer) = Run([u, x], [u], null, "X = U", "X = 3", "U = X");

        Assert.IsTrue(IsKnown(session, x));
        Assert.IsTrue(IsKnown(session, u), "U was assigned the known value X holds");
        CollectionAssert.AreEqual(new[] { false, true, true }, Assigned(observer));
    }

    [TestMethod]
    public void AValueReadFromAVariableThatIsNotKnown_StaysNotKnown_WhenTheVariableIsAssignedAfterwards()
    {
        var cell = new ValueBindingHandle(new VBRuntimeValue<int>(0));
        cell.Forget();

        var read = VBLongType.TypeInfo.CreateValue(cell.ForReading());
        cell.Store(new VBRuntimeValue<int>(5), isKnown: true);

        Assert.IsTrue(read.IsIndeterminate, "what was read before the assignment is what it was");
        Assert.IsFalse(VBLongType.TypeInfo.CreateValue(cell.ForReading()).IsIndeterminate);
    }

    [TestMethod]
    public void ACertainErrorIsOnlyStated_OfAValueThatIsKnown_NotOfOneAVariableOnlyAssumes()
    {
        var u = Variable("U", VBLongType.TypeInfo);
        var x = Variable("X", VBLongType.TypeInfo);
        var y = Variable("Y", VBLongType.TypeInfo);

        var (_, outcome, observer) = Run([u, x, y], [u], null, "X = U", "Y = 1 / X");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, "an assumed zero is not a division by zero");
        var division = observer.Operations.Single(operation => operation.Operator == "/");
        Assert.IsNull(division.Error);
        Assert.IsFalse(division.IsValueKnown);
    }

    [TestMethod]
    public void ACertainError_OfAValueTheCodeAssignedItself_IsStated()
    {
        var x = Variable("X", VBLongType.TypeInfo);
        var y = Variable("Y", VBLongType.TypeInfo);

        var (_, outcome, observer) = Run([x, y], [], null, "X = 0", "Y = 1 / X");

        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, outcome.ErrorInfo?.ErrorId);
        var division = observer.Operations.Single(operation => operation.Operator == "/");
        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, division.Error?.ErrorId);
        Assert.IsTrue(division.IsValueKnown);
    }

    [TestMethod]
    public void AnElementOfAnArrayDeclaredHere_IsKnown_ItsDeclaredDefault()
    {
        var a = Variable("A", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var x = Variable("X", VBLongType.TypeInfo);
        var array = new VBFixedSizeArrayValue([(1, 10)], VBLongType.TypeInfo);

        var (_, _, observer) = Run([a, x], [], session => session.Symbols.Resolver.GetValue(a).SetValue(
            session.Symbols.Resolver, new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))), "X = A(3)");

        CollectionAssert.AreEqual(new[] { true }, Assigned(observer));
    }

    [TestMethod]
    public void AnArrayElementAssignedAValueThatIsNotKnown_IsNotKnown_AndItsNeighboursAre()
    {
        var u = Variable("U", VBLongType.TypeInfo);
        var a = Variable("A", new VBFixedSizeArrayType(VBLongType.TypeInfo));
        var x = Variable("X", VBLongType.TypeInfo);
        var array = new VBFixedSizeArrayValue([(1, 10)], VBLongType.TypeInfo);

        var (_, _, observer) = Run([u, a, x], [u], session => session.Symbols.Resolver.GetValue(a).SetValue(
            session.Symbols.Resolver, new VBRuntimeValue<VBRuntimeArrayValue>(new VBRuntimeArrayValue(array))),
            "A(1) = U", "X = A(1)", "X = A(2)", "A(1) = 7", "X = A(1)");

        // the element is assigned U; read back not known; its neighbour known; assigned a known value, known.
        CollectionAssert.AreEqual(new[] { false, false, true, true, true }, Assigned(observer));
    }

    [TestMethod]
    public void AVariableAllocatedWithAValueThatIsNotKnown_IsACellThatCanBeAssigned()
    {
        var x = Variable("X", VBLongType.TypeInfo);

        var (session, _, _) = Run([x], [], current =>
        {
            Assert.IsTrue(current.Symbols.Resolver.TryAllocate(x, VBLongType.TypeInfo.CreateIndeterminateValue(), out _));
            Assert.IsFalse(IsKnown(current, x));
        }, "X = 5");

        Assert.IsTrue(IsKnown(session, x));
    }
}
