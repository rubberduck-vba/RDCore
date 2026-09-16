using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Services.VerboseMessages;
using NSubstitute;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for Boolean let-coercion (MS-VBAL §5.5.1.2.2): the "-&gt; Boolean" half of
/// the table, since the coercion provider dispatches by destination type. Numeric, Date and String
/// sources all funnel through this class when the destination is Boolean.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.2 Let-coercion to and from Boolean")]
public sealed class VBBooleanLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    // String -> Boolean falls back to String -> Double -> Boolean for anything that isn't a
    // recognized token, so the real Numeric strategy is wired in via the same cyclic-break pattern
    // used elsewhere for a strategy that recurses through the provider.
    private static VBBooleanLetCoercionRuntimeSemantics Sut()
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        var provider = new LetCoercionRuntimeSemanticsProvider([new VBNumericLetCoercionTypeRuntimeSemantics(fmt, handle)], fmt);
        handle.Inner = provider;
        return new VBBooleanLetCoercionRuntimeSemantics(provider, fmt);
    }

    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver resolver, VBOperatorExpression expression, LetCoercionStackFrame frame)
            => Inner.EvaluateLetCoercionSemantics(resolver, expression, frame);
        public LetCoercionAnalysisContext Analyze(ISymbolResolver resolver, ILetCoercionSemanticContextBuilder builder, VBOperatorExpression expression, LetCoercionStackFrame frame)
            => Inner.Analyze(resolver, builder, expression, frame);
    }

    [TestMethod]
    public void BooleanSource_IsACopy()
        => AssertCoercedTo<VBBooleanValue>(Coerce(Sut(), new VBBooleanValue(true), VBBooleanType.TypeInfo), true);

    [TestMethod]
    [DataRow(0d, false)]
    [DataRow(5d, true)]
    [DataRow(-1d, true)]
    public void NumericSource_IsFalseOnlyWhenZero(double source, bool expected)
        => AssertCoercedTo<VBBooleanValue>(Coerce(Sut(), new VBDoubleValue(source), VBBooleanType.TypeInfo), expected);

    [TestMethod]
    public void DateSource_IsFalseOnlyForTheZeroSerialValue()
    {
        AssertCoercedTo<VBBooleanValue>(Coerce(Sut(), new VBDateValue(0d), VBBooleanType.TypeInfo), false);
        AssertCoercedTo<VBBooleanValue>(Coerce(Sut(), new VBDateValue(1d), VBBooleanType.TypeInfo), true);
    }

    [TestMethod]
    [DataRow("True", true)]
    [DataRow("true", true)]
    [DataRow("TRUE", true)]
    [DataRow("False", false)]
    [DataRow("false", false)]
    public void StringSource_TrueFalseTokens_AreCaseInsensitive(string source, bool expected)
        => AssertCoercedTo<VBBooleanValue>(Coerce(Sut(), new VBStringValue(source), VBBooleanType.TypeInfo), expected);

    [TestMethod]
    [DataRow("#TRUE#", true)]
    [DataRow("#FALSE#", false)]
    public void StringSource_HashTokens_AreCaseSensitive(string source, bool expected)
        => AssertCoercedTo<VBBooleanValue>(Coerce(Sut(), new VBStringValue(source), VBBooleanType.TypeInfo), expected);

    [TestMethod]
    public void StringSource_HashTokenWrongCase_FallsThroughToNumericParse_IsTypeMismatch()
        // "#true#" is not a recognized token (case-sensitive) and isn't a numeric-coercion-string either.
        => AssertError(Coerce(Sut(), new VBStringValue("#true#"), VBBooleanType.TypeInfo), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    [DataRow("5", true)]
    [DataRow("0", false)]
    [DataRow("-1.5", true)]
    public void StringSource_NumericString_CoercesThroughDouble(string source, bool expected)
        => AssertCoercedTo<VBBooleanValue>(Coerce(Sut(), new VBStringValue(source), VBBooleanType.TypeInfo), expected);

    [TestMethod]
    public void StringSource_Garbage_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBStringValue("not a boolean"), VBBooleanType.TypeInfo), VBRuntimeErrorId.TypeMismatch);
}
