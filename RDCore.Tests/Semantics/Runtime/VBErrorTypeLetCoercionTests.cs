using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Characterization matrix for Error let-coercion (MS-VBAL §5.5.1.2.9): an Error source only coerces
/// to a Variant or fixed-size array (never a "real" declared type); any other source coerces to Error
/// via a Long representation, valid only in the standard error code range (0..65535).
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2.9 Let-coercion to and from Error")]
public sealed class VBErrorTypeLetCoercionTests : LetCoercionRuntimeSemanticsTests
{
    // The recursive Long-coercion sub-step needs the real Numeric strategy; wired via the same
    // provider ⇄ strategy cyclic-break pattern used across tonight's operator coverage.
    private static VBErrorTypeLetCoercionRuntimeSemantics Sut()
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        var provider = new LetCoercionRuntimeSemanticsProvider([new VBNumericLetCoercionTypeRuntimeSemantics(fmt, handle)], fmt);
        handle.Inner = provider;
        return new VBErrorTypeLetCoercionRuntimeSemantics(provider, fmt);
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
    public void ErrorSource_CoercesToNonVariantNonArrayTarget_IsTypeMismatch()
        => AssertError(Coerce(Sut(), new VBErrorValue(5), VBLongType.TypeInfo), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    public void NumericSource_InStandardErrorCodeRange_CoercesToError()
        => AssertCoercedTo<VBErrorValue>(Coerce(Sut(), new VBLongValue(5), VBErrorType.TypeInfo), 5);

    [TestMethod]
    public void NumericSource_AtRangeBoundaries_CoercesToError()
    {
        AssertCoercedTo<VBErrorValue>(Coerce(Sut(), new VBLongValue(0), VBErrorType.TypeInfo), 0);
        AssertCoercedTo<VBErrorValue>(Coerce(Sut(), new VBLongValue(65535), VBErrorType.TypeInfo), 65535);
    }

    [TestMethod]
    public void NumericSource_OutOfStandardErrorCodeRange_IsInvalidProcedureCallOrArgument()
        => AssertError(Coerce(Sut(), new VBLongValue(65536), VBErrorType.TypeInfo), VBRuntimeErrorId.InvalidProcedureCallOrArgument);
}
