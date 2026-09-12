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
/// Characterization matrix for Date let-coercion (MS-VBAL §5.5.1.2.3): the "-&gt; Date" half of the
/// table (Date, Numeric, Boolean and String sources), since the coercion provider dispatches by
/// destination type. "Date -&gt;" is owned by whichever class registers for the actual destination
/// (Numeric or Boolean), not by this class.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.3 Let-coercion to and from Date")]
public sealed class VBDateLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    // String -> Date falls back to String -> Double for anything DateTime.TryParse can't read, so the
    // real Numeric strategy is wired in via the same cyclic-break pattern used elsewhere.
    private static VBDateLetCoercionRuntimeSemantics Sut()
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        var provider = new LetCoercionRuntimeSemanticsProvider([new VBNumericLetCoercionTypeRuntimeSemantics(fmt, handle)], fmt);
        handle.Inner = provider;
        return new VBDateLetCoercionRuntimeSemantics(provider, fmt);
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
    public void DateSource_IsACopy()
        => AssertCoercedTo<VBDateValue>(Coerce(Sut(), new VBDateValue(12345d), VBDateType.TypeInfo), 12345d);

    [TestMethod]
    public void NumericSource_IsInterpretedAsSerialValue()
        => AssertCoercedTo<VBDateValue>(Coerce(Sut(), new VBDoubleValue(2d), VBDateType.TypeInfo), 2d);

    [TestMethod]
    public void BooleanSource_IsInterpretedAsSerialValue()
    {
        AssertCoercedTo<VBDateValue>(Coerce(Sut(), new VBBooleanValue(false), VBDateType.TypeInfo), 0d);
        AssertCoercedTo<VBDateValue>(Coerce(Sut(), new VBBooleanValue(true), VBDateType.TypeInfo), -1d);
    }

    [TestMethod]
    public void StringSource_RecognizableDateTime_Parses()
        => AssertCoercedTo<VBDateValue>(Coerce(Sut(), new VBStringValue("2020-01-01"), VBDateType.TypeInfo), new DateTime(2020, 1, 1).ToOADate());

    [TestMethod]
    public void StringSource_NumericFallback_CoercesThroughDouble()
        // not parseable as a date/time, but is a valid numeric-coercion-string within Date's range.
        => AssertCoercedTo<VBDateValue>(Coerce(Sut(), new VBStringValue("5"), VBDateType.TypeInfo), 5d);

    [TestMethod]
    public void StringSource_NumericFallbackOutOfDateRange_IsTypeMismatchNotOverflow()
        // MS-VBAL 5.5.1.2.4: a Double-conversion overflow during the string fallback is reported as
        // Type mismatch (13), not the Overflow (6) that the Double coercion itself would raise; the
        // same applies here since VBDoubleType's own range is far wider than VBDateType's.
        => AssertError(Coerce(Sut(), new VBStringValue("99999999"), VBDateType.TypeInfo), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void StringSource_Garbage_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBStringValue("not a date"), VBDateType.TypeInfo), VBRuntimeErrorId.TypeMismatch);
}
