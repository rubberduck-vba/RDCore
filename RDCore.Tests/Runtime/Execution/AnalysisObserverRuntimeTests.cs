using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Semantics.Runtime.Operators;
using RDCore.Tests.Semantics.Runtime;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The facts the runtime semantics state about real source: parsed, lowered and executed through the whole pipeline, with an observer
/// attached. Each construct that coerces a value says where it does, which is what these pin.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.0.3 Semantic Analysis")]
public sealed class AnalysisObserverRuntimeTests
{
    private static (RecordingAnalysisObserver Observer, RuntimeExecutionOutcome Outcome, IReadOnlyList<string> Output) Observe(params string[] body)
    {
        var observer = new RecordingAnalysisObserver();
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], output, standardLibrary: false, arrange: null, ModuleDirectives.None, observer, body);
        return (observer, outcome, output.Lines);
    }

    [TestMethod]
    public void TheAssignmentOfADoubleToALong_IsAnImplicitLossyNarrowingConversion_AtTheAssignmentSite()
    {
        var (observer, _, _) = Observe(
            "Dim d As Double",
            "Dim n As Long",
            "d = 2.5",
            "n = d");

        var fact = observer.Conversions.Single(conversion =>
            conversion.Site == ConversionSite.Assignment && conversion.Source.Equals(VBDoubleType.TypeInfo) && conversion.Destination.Equals(VBLongType.TypeInfo));
        Assert.IsTrue(fact.Flags.HasFlag(ConversionSemanticFlags.Implicit | ConversionSemanticFlags.Narrowing | ConversionSemanticFlags.Lossy));
        Assert.IsTrue(fact.IsValueKnown);
        Assert.IsNull(fact.Error);
        Assert.IsEmpty(observer.Operations, "an assignment states its conversion, and no operation");
    }

    [TestMethod]
    public void AnArithmeticExpression_StatesItsOperation_AndTheConversionOfItsOperand()
    {
        var (observer, _, _) = Observe(
            "Dim d As Double",
            "d = 2.5",
            "d = d + 1");

        var operation = (ArithmeticOperatorFact)observer.Operations.Single();
        Assert.AreEqual("+", operation.Operator);
        Assert.AreEqual(VBDoubleType.TypeInfo, operation.EffectiveType);
        Assert.IsTrue(operation.Flags.HasFlag(ArithmeticOperatorSemanticFlags.VBNumericEffectiveType));

        var operand = observer.Conversions.Single(conversion => conversion.Site == ConversionSite.OperatorOperand);
        Assert.AreEqual(VBIntegerType.TypeInfo, operand.Source);
        Assert.AreEqual(VBDoubleType.TypeInfo, operand.Destination);
        Assert.IsTrue(operand.Flags.HasFlag(ConversionSemanticFlags.Widening));
    }

    [TestMethod]
    public void ADivisionByZeroOfKnownOperands_IsStatedAsACertainError_AndIsTheError_TheCodeRaises()
    {
        var (observer, outcome, _) = Observe(
            "Dim n As Long",
            "Dim z As Long",
            "n = 10",
            "z = 0",
            "n = n / z");

        var division = observer.Operations.Single(operation => operation.Operator == "/");
        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, division.Error?.ErrorId);
        Assert.IsTrue(division.IsValueKnown);
        Assert.AreEqual((int)VBRuntimeErrorId.DivisionByZero, outcome.ErrorInfo?.ErrorId);
    }

    [TestMethod]
    public void TheConditionOfAnIf_IsAConversionToBoolean_AtTheConditionSite()
    {
        var (observer, _, _) = Observe(
            "Dim n As Long",
            "n = 1",
            "If n Then",
            "n = 2",
            "End If");

        var condition = observer.Conversions.Single(conversion => conversion.Site == ConversionSite.Condition);
        Assert.AreEqual(VBLongType.TypeInfo, condition.Source);
        Assert.AreEqual(VBBooleanType.TypeInfo, condition.Destination);
    }

    [TestMethod]
    public void EveryConstructThatCoerces_StatesItsSite_AndNoFactIsLeftUnspecified()
    {
        var (observer, outcome, _) = Observe(
            "Dim a As Long",
            "Dim d As Double",
            "Dim s As String",
            "Dim arr() As Long",
            "a = 1",
            "d = 2.5",
            "s = \"xyz\"",
            "If a Then",
            "a = 2",
            "End If",
            "Do While a < 3",
            "a = a + 1",
            "Loop",
            "Select Case a",
            "Case 1",
            "a = 5",
            "Case 3 To 9",
            "a = 6",
            "End Select",
            "For a = 1 To 3",
            "Next a",
            "ReDim arr(1 To 3)",
            "arr(2) = d",
            "Debug.Print a, d",
            "Mid(s, 1, 1) = \"y\"");

        Assert.AreEqual(RuntimeExecutionOutcomeKind.ExitProcedure, outcome.Kind, "the body was expected to run to completion");

        var unspecified = observer.Conversions.Where(conversion => conversion.Site == ConversionSite.Unspecified).ToArray();
        Assert.IsEmpty(unspecified, string.Join(", ", unspecified.Select(conversion => $"{conversion.Source.Name}->{conversion.Destination.Name} at {conversion.Location}")));

        var sites = observer.Conversions.Select(conversion => conversion.Site).ToHashSet();
        foreach (var expected in new[]
        {
            ConversionSite.Assignment, ConversionSite.Condition, ConversionSite.OperatorOperand, ConversionSite.CaseTest,
            ConversionSite.LoopBound, ConversionSite.Subscript, ConversionSite.PrintItem, ConversionSite.StringStatement,
        })
        {
            Assert.Contains(expected, sites, $"no conversion was stated at the {expected} site");
        }
    }

    [TestMethod]
    public void TheSelectorOfAJump_AndTheNumberOfAnError_AreConversionsAtTheirOwnSites()
    {
        var (observer, _, _) = Observe(
            "Dim a As Long",
            "a = 2",
            "On Error Resume Next",
            "On a GoTo L1, L2",
            "L1:",
            "L2:",
            "Error a");

        var sites = observer.Conversions.Select(conversion => conversion.Site).ToHashSet();
        Assert.Contains(ConversionSite.JumpSelector, sites, observer.Describe());
        Assert.Contains(ConversionSite.ErrorNumber, sites, observer.Describe());
        Assert.DoesNotContain(ConversionSite.Unspecified, sites, observer.Describe());
    }

    [TestMethod]
    public void TheArgumentOfACall_IsAConversionToTheTypeOfItsParameter_AtTheArgumentSite()
    {
        var observer = new RecordingAnalysisObserver();
        RuntimeSourceHarness.Run(null, [], new RuntimeOutputBuffer(), standardLibrary: true, arrange: null, ModuleDirectives.None, observer,
            "Dim a As Long",
            "a = 5",
            "a = Len(a)");

        Assert.IsTrue(observer.Conversions.Any(conversion => conversion.Site == ConversionSite.Argument), observer.Describe());
        Assert.IsFalse(observer.Conversions.Any(conversion => conversion.Site == ConversionSite.Unspecified), observer.Describe());
    }

    [TestMethod]
    public void ACodeRunWithAnObserver_RunsExactlyAsWithout()
    {
        string[] body =
        [
            "Dim a As Long",
            "Dim d As Double",
            "a = 7",
            "d = a / 2",
            "Debug.Print a, d, a Mod 4, d > a",
            "For a = 1 To 3",
            "d = d + a",
            "Next a",
            "Debug.Print a, d",
        ];

        var observed = Observe(body);
        var output = new RuntimeOutputBuffer();
        var (_, outcome) = RuntimeSourceHarness.Run(null, [], output, standardLibrary: false, body);

        Assert.AreEqual(outcome.Kind, observed.Outcome.Kind);
        CollectionAssert.AreEqual(output.Lines.ToArray(), observed.Output.ToArray());
        Assert.IsNotEmpty(observed.Observer.Conversions);
        Assert.IsNotEmpty(observed.Observer.Operations);
    }
}
