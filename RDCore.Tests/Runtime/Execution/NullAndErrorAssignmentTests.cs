using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Facts;
using RDCore.SDK.Semantics.Flags;
using RDCore.Tests.Semantics.Runtime;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// <strong>MS-VBAL §5.5.1.2.10</strong> and <strong>§5.5.1.2.9</strong>, end to end: a Null or an Error value that
/// reaches a destination other than a Variant raises the error its own coercion rules say, whatever the destination is.
/// </summary>
/// <remarks>
/// The rules of both key off the source, so a coercion of either is handed to the strategy of the source's own type
/// whatever the destination is. These run them from source, through the statements that reach them.
/// </remarks>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
public sealed class NullAndErrorAssignmentTests
{
    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBModuleFieldVariableMemberSymbol Variable(string name, VBType type)
        => new(Workspace, RuntimeSourceHarness.ModuleUri, name, ScopeKind.Module, type,
            SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static void AssertError(RuntimeExecutionOutcome outcome, VBRuntimeErrorId expected)
    {
        Assert.AreEqual(RuntimeExecutionOutcomeKind.Error, outcome.Kind);
        Assert.AreEqual((int)expected, outcome.ErrorInfo!.ErrorId, outcome.ErrorInfo.Verbose);
    }

    // the Variant is given its Error value before the body runs, so that the one coercion the body makes is the one
    // under test.
    private static RuntimeExecutionOutcome RunWithAnError(VBModuleFieldVariableMemberSymbol destination, string body)
    {
        var variant = Variable("V", VBVariantType.TypeInfo);

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [variant, destination], output: null, standardLibrary: false,
            session => session.Symbols.Resolver.GetValue(variant).SetValue(
                session.Symbols.Resolver, new VBVariantValue(new VBErrorValue(5)).RuntimeValue),
            body);
        return outcome;
    }

    [TestMethod]
    public void AssigningNull_ToALongVariable_IsInvalidUseOfNull()
    {
        // "Null -> Any other type except a fixed-size array or Variant: Runtime error 94 (Invalid use of Null) is raised."
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [Variable("L", VBLongType.TypeInfo)], "L = Null");

        AssertError(outcome, VBRuntimeErrorId.InvalidUseOfNull);
    }

    [TestMethod]
    public void AssigningNull_ToAStringVariable_IsInvalidUseOfNull()
    {
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [Variable("S", VBStringType.TypeInfo)], "S = Null");

        AssertError(outcome, VBRuntimeErrorId.InvalidUseOfNull);
    }

    [TestMethod]
    public void AssigningNull_ToAFixedLengthStringVariable_IsInvalidUseOfNull()
    {
        var (_, outcome) = RuntimeSourceHarness.Run(fileSystem: null, [Variable("S", new VBFixedStringType(3))], "S = Null");

        AssertError(outcome, VBRuntimeErrorId.InvalidUseOfNull);
    }

    [TestMethod]
    public void AssigningNull_ToADynamicArrayVariable_IsTypeMismatch()
    {
        // a Null into a resizable array is the one Null row that raises 13 rather than 94.
        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [Variable("A", new VBResizableArrayType(VBLongType.TypeInfo))], "A = Null");

        AssertError(outcome, VBRuntimeErrorId.TypeMismatch);
    }

    [TestMethod]
    public void AssigningAVariantHoldingNull_ToALong_IsStatedAsAFailedCoercion()
    {
        // what an analysis states about the coercion is what it does: the Null the Variant holds raises 94 by its own rule,
        // and it is the operand that fails, as a Null written as such is.
        var variant = Variable("V", VBVariantType.TypeInfo);
        var observer = new RecordingAnalysisObserver();

        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [variant, Variable("L", VBLongType.TypeInfo)], output: null, standardLibrary: false,
            session => session.Symbols.Resolver.GetValue(variant).SetValue(
                session.Symbols.Resolver, new VBVariantValue(VBNullValue.Null).RuntimeValue),
            new RuntimeSourceHarness.Surroundings { Observer = observer },
            "L = V");

        AssertError(outcome, VBRuntimeErrorId.InvalidUseOfNull);
        var fact = observer.Conversions.Single(conversion => conversion.Site == ConversionSite.Assignment);
        Assert.IsTrue(fact.Flags.HasFlag(ConversionSemanticFlags.Failed), observer.Describe());
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.2.3.4 Let-coercion to an Enum type")]
    public void PassingNull_AsInStrsCompare_IsInvalidUseOfNull()
    {
        // a ByVal argument is Let-coerced to its parameter's declared type like any other destination, and an Enum type
        // means Long: the Null raises 94, as it would for a Long, before InStr is ever called.
        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [], output: null, standardLibrary: true, "Debug.Print InStr(1, \"a\", \"A\", Null)");

        AssertError(outcome, VBRuntimeErrorId.InvalidUseOfNull);
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.9 Let-coercion to and from Error")]
    public void AssigningAVariantHoldingAnError_ToALong_IsTypeMismatch()
        // "Error -> Any type except a fixed-size array or Variant: Runtime error 13 (Type mismatch) is raised."
        => AssertError(RunWithAnError(Variable("L", VBLongType.TypeInfo), "L = V"), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.9 Let-coercion to and from Error")]
    public void AssigningAVariantHoldingAnError_ToAString_IsTypeMismatch()
        => AssertError(RunWithAnError(Variable("S", VBStringType.TypeInfo), "S = V"), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.9 Let-coercion to and from Error")]
    public void AssigningAVariantHoldingAnError_ToAFixedLengthString_IsTypeMismatch()
        => AssertError(RunWithAnError(Variable("S", new VBFixedStringType(3)), "S = V"), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.9 Let-coercion to and from Error")]
    public void TheConditionOfAnIf_AnErrorValue_IsTypeMismatch()
    {
        // a condition is Let-coerced to a Boolean, and an Error value is one no more than it is any other type.
        var (_, outcome) = RuntimeSourceHarness.Run(
            fileSystem: null, [], output: null, standardLibrary: true,
            "If CVErr(5) Then",
            "End If");

        AssertError(outcome, VBRuntimeErrorId.TypeMismatch);
    }
}
