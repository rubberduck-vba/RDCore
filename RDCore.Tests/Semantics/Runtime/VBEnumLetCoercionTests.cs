using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for let-coercion to an Enum type (MS-VBAL §5.2.3.4): an enum name in an as-type clause means
/// the declared type <c>Long</c>, so the coercion to one is the coercion to <c>Long</c> — the same result, the same
/// errors, and any <c>Long</c> accepted, whether a member of the Enum names it or not. Runs through the provider over
/// every real strategy, the way a coercion reaches it at run time.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.2.3.4 Let-coercion to an Enum type")]
public sealed class VBEnumLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    private static readonly Uri Root = TestUri.WorkspaceRoot();

    // Enum Colour: Red (0), Green (1) - the values do not matter to the coercion, only that it is an Enum type.
    private static readonly VBEnumType Colour = EnumType("Colour", "Red", "Green");

    private static VBEnumType EnumType(string name, params string[] members)
    {
        var symbol = new VBEnumMemberSymbol(Root, TestUri.TestModuleUri(), name, ScopeKind.Module, SymbolKindExt.Enum,
            VBUnknownType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBEnumType(symbol, members.Select(member => new VBEnumConstMemberSymbol(
            Root, symbol.Uri, member, ScopeKind.Module, SymbolKindExt.EnumMember, SourceRange.Empty, SourceRange.Empty)));
    }

    private static LetCoercionResult CoerceToColour(VBTypedValue source)
    {
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(Colour));
        return LetCoercionAnalysisHarness.BuildProvider().EvaluateLetCoercionSemantics(null!, ThrowawayExpression, frame);
    }

    [TestMethod]
    public void IntegralSource_IsTheSameLong()
        => AssertCoercedTo<VBLongValue>(CoerceToColour(new VBIntegerValue(1)), 1);

    [TestMethod]
    public void AnUndefinedMember_IsAccepted()
        // no member of Colour is 5: the declared type is Long, and 5 is a Long.
        => AssertCoercedTo<VBLongValue>(CoerceToColour(new VBLongValue(5)), 5);

    [TestMethod]
    public void StringSource_IsParsed()
        => AssertCoercedTo<VBLongValue>(CoerceToColour(new VBStringValue("1")), 1);

    [TestMethod]
    [DataRow(1.7, 2, DisplayName = "rounded to the nearest")]
    [DataRow(2.5, 2, DisplayName = "a tie: rounded to even")]
    public void FractionalSource_IsBankersRounded(double source, int expected)
        => AssertCoercedTo<VBLongValue>(CoerceToColour(new VBDoubleValue(source)), expected);

    [TestMethod]
    public void BooleanSource_IsMinusOne()
        => AssertCoercedTo<VBLongValue>(CoerceToColour(VBBooleanValue.True), -1);

    [TestMethod]
    public void EmptySource_IsZero()
        => AssertCoercedTo<VBLongValue>(CoerceToColour(VBEmptyValue.Empty), 0);

    [TestMethod]
    public void VariantSource_IsUnwrapped()
        => AssertCoercedTo<VBLongValue>(CoerceToColour(new VBVariantValue(new VBIntegerValue(1))), 1);

    [TestMethod]
    public void BeyondLong_IsOverflow()
        => AssertError(CoerceToColour(new VBDoubleValue(3000000000d)), VBRuntimeErrorId.Overflow);

    [TestMethod]
    public void NonNumericString_IsTypeMismatch()
        => AssertError(CoerceToColour(new VBStringValue("abc")), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void NullSource_IsInvalidUseOfNull()
        // the Null's own rule raises it, as it does for a Long (MS-VBAL 5.5.1.2.10).
        => AssertError(CoerceToColour(VBNullValue.Null), VBRuntimeErrorId.InvalidUseOfNull);

    [TestMethod]
    public void Analyze_AFractionalSource_ReportsTheLongCoercionsRounding()
    {
        // the facts of the coercion are those of the coercion to Long, rounding included.
        var builder = Substitute.For<ILetCoercionSemanticContextBuilder>();
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBDoubleValue(1.7), new VBTypeDescValue(Colour));

        LetCoercionAnalysisHarness.BuildProvider().Analyze(null!, builder, ThrowawayExpression, frame);

        builder.Received().AddLetCoercionFlags(
            Arg.Is<ConversionSemanticFlags>(flags => flags.HasFlag(ConversionSemanticFlags.BankersRounding)), InputIndex.CoercionSourceValue);
    }
}
