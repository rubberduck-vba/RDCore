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
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Analysis;
using RDCore.SDK.Semantics.Builders;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Integration tests for <see cref="LetCoercionRuntimeSemanticsProvider"/> — strategy dispatch and
/// the recursion guard. The provider is not DI-wired anywhere yet, so the graph is built by hand.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §5.5.1.2 Let-coercion (runtime)")]
public sealed class LetCoercionRuntimeProviderTests : LetCoercionRuntimeSemanticsTests
{
    /// <summary>Breaks the provider ⇄ strategy construction cycle for the test.</summary>
    private sealed class ProviderHandle : ILetCoercionRuntimeSemanticsProvider
    {
        public ILetCoercionRuntimeSemanticsProvider Inner { get; set; } = default!;
        public LetCoercionResult EvaluateLetCoercionSemantics(ISymbolResolver r, ExpressionNode e, LetCoercionStackFrame f)
            => Inner.EvaluateLetCoercionSemantics(r, e, f);
        public LetCoercionAnalysisContext Analyze(ISymbolResolver r, ILetCoercionSemanticContextBuilder b, ExpressionNode e, LetCoercionStackFrame f)
            => Inner.Analyze(r, b, e, f);
    }

    private static LetCoercionRuntimeSemanticsProvider BuildProvider()
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var handle = new ProviderHandle();
        ILetCoercionRuntimeSemantics[] strategies =
        [
            new VBNumericLetCoercionTypeRuntimeSemantics(fmt, handle),
            new VBBooleanLetCoercionRuntimeSemantics(handle, fmt),
            new VBDateLetCoercionRuntimeSemantics(handle, fmt),
            new VBNullTypeLetCoercionRuntimeSemantics(fmt),
        ];
        var provider = new LetCoercionRuntimeSemanticsProvider(strategies, fmt);
        handle.Inner = provider;
        return provider;
    }

    private static LetCoercionResult Coerce(VBTypedValue source, VBType destination)
    {
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination));
        // resolver unused; expression only on error paths — reuse the base throwaway via a public shim
        return BuildProvider().EvaluateLetCoercionSemantics(null!, ThrowawayExpression, frame);
    }

    // the provider over every strategy the runtime implements, where the destination a source is coerced to is the whole
    // point: the one above has only four, and a destination none of them handles is a type mismatch for any source.
    private static LetCoercionResult CoerceWithEveryStrategy(VBTypedValue source, VBType destination)
    {
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination));
        return LetCoercionAnalysisHarness.BuildProvider().EvaluateLetCoercionSemantics(null!, ThrowawayExpression, frame);
    }

    private static VBUserDefinedType Udt(string name)
    {
        var uri = TestUri.TestModuleUri();
        var symbol = new VBUserDefinedTypeMemberSymbol(uri, uri, name, ScopeKind.Module, SourceRange.Empty, SourceRange.Empty, AccessModifier.Public);
        return new VBUserDefinedType(symbol, []);
    }

    [TestMethod]
    public void Dispatch_ANullSource_IsInvalidUseOfNull_NotAnInternalError()
    {
        // MS-VBAL 5.5.1.2.10: the provider dispatches on the DESTINATION, so the Null strategy (keyed on a Null
        // destination) was never reached for a Null source, which fell through as an internal error.
        var result = Coerce(new VBNullValue(), VBLongType.TypeInfo);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual((int)VBRuntimeErrorId.InvalidUseOfNull, result.ErrorInfo?.ErrorId);
    }

    [TestMethod]
    public void Dispatch_ResolvesTheNumericStrategyForAConcreteNumericDestination()
    {
        // regression: the provider keyed on frame.DestinationTypeDesc.GetType() (always VBTypeDescValue),
        // so it never matched a strategy and always returned TypeMismatch.
        var result = Coerce(new VBDoubleValue(2.67), VBIntegerType.TypeInfo);
        Assert.IsTrue(result.IsSuccess, result.ErrorInfo is null ? "" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString());
        Assert.IsInstanceOfType<VBIntegerValue>(result.Result);
        Assert.AreEqual((short)3, ((VBIntegerValue)result.Result!).Value);
    }

    [TestMethod]
    public void Dispatch_AVariantWrappedSource_UnwrapsBeforeTheStrategySeesIt()
        // regression: a Variant source's own TypeInfo mirrors its wrapped value's (so dispatch above
        // still picks VBNumericLetCoercionTypeRuntimeSemantics), but that strategy casts frame.SourceValue
        // directly to VBNumericTypedValue - handed the VBVariantValue box itself, that cast threw
        // InvalidCastException instead of coercing.
    {
        var result = Coerce(new VBVariantValue(new VBDoubleValue(2.67)), VBIntegerType.TypeInfo);
        Assert.IsTrue(result.IsSuccess, result.ErrorInfo is null ? "" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString());
        Assert.IsInstanceOfType<VBIntegerValue>(result.Result);
        Assert.AreEqual((short)3, ((VBIntegerValue)result.Result!).Value);
    }

    [TestMethod]
    public void Dispatch_UnknownDestination_IsTypeMismatch()
        => Assert.AreEqual((int)VBRuntimeErrorId.TypeMismatch,
            Coerce(new VBDoubleValue(1), VBStringType.TypeInfo).ErrorInfo!.ErrorId);

    [TestMethod]
    public void Analyze_OrdinarySuccessfulCoercion_DoesNotThrow()
    {
        // regression: EvaluateLetCoercion's ordinary Success() call doesn't attach a
        // LetCoercionStackFrame (nothing consumed LetCoercionResult.Frame before Analyze existed) -
        // AnalyzeConversionOperation used to read it back off that frame-less result and crash with
        // an IndexOutOfRangeException on every "operand already needs a real coercion" analysis.
        var builder = Substitute.For<ILetCoercionSemanticContextBuilder>();
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBDoubleValue(2.67), new VBTypeDescValue(VBIntegerType.TypeInfo));

        BuildProvider().Analyze(null!, builder, ThrowawayExpression, frame);

        // confirms the flags actually got built from the coercion, not just "nothing threw".
        builder.Received().AddLetCoercionFlags(
            ConversionSemanticFlags.LetCoerced, InputIndex.CoercionSourceValue);
    }

    #region a Null or Error source, whatever it is coerced to

    // MS-VBAL 5.5.1.2.9 and 5.5.1.2.10 key off the source, so the provider hands a Null or an Error source to the strategy
    // of its own type for any destination but a Variant; ANullEmptyOrErrorSource_CoercesByItsOwnRules_WhateverTheDestinationIs
    // pins some of the rows. These pin the destinations it leaves out: the scalar ones, an Object (where 5.5.1.2.13's last row
    // would say Object required), a resizable array of another element type and a UDT, and a fixed-size array, which neither
    // rule covers.

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
    [DataRow("Long")]
    [DataRow("Integer")]
    [DataRow("String")]
    [DataRow("Boolean")]
    [DataRow("Date")]
    [DataRow("Double")]
    [DataRow("Object")] // where 5.5.1.2.13's last row would say Object required (424)
    public void Dispatch_ANullSource_ToADestinationThatCannotHoldIt_IsInvalidUseOfNull(string type)
    {
        VBType destination = type switch
        {
            "Long" => VBLongType.TypeInfo,
            "Integer" => VBIntegerType.TypeInfo,
            "String" => VBStringType.TypeInfo,
            "Boolean" => VBBooleanType.TypeInfo,
            "Date" => VBDateType.TypeInfo,
            "Double" => VBDoubleType.TypeInfo,
            _ => VBObjectType.TypeInfo,
        };

        // "Null -> Any other type except a fixed-size array or Variant: Runtime error 94 (Invalid use of Null) is raised."
        AssertError(CoerceWithEveryStrategy(VBNullValue.Null, destination), VBRuntimeErrorId.InvalidUseOfNull);
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
    public void Dispatch_ANullSource_ToAResizableArrayOrUserDefinedType_IsTypeMismatch()
    {
        AssertError(CoerceWithEveryStrategy(VBNullValue.Null, new VBResizableArrayType(VBLongType.TypeInfo)), VBRuntimeErrorId.TypeMismatch);
        AssertError(CoerceWithEveryStrategy(VBNullValue.Null, new VBResizableByteArrayType()), VBRuntimeErrorId.TypeMismatch);
        AssertError(CoerceWithEveryStrategy(VBNullValue.Null, Udt("Point")), VBRuntimeErrorId.TypeMismatch);
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
    public void Dispatch_ANullSource_ToAFixedSizeArray_IsStillTypeMismatch()
        // neither rule covers a fixed-size array and no strategy is keyed on one, so this stays the provider's own type
        // mismatch rather than the Null strategy's not applicable, which the provider reports as internal error 51.
        => AssertError(CoerceWithEveryStrategy(VBNullValue.Null, new VBFixedSizeArrayType(VBLongType.TypeInfo)), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.9 Let-coercion to and from Error")]
    public void Dispatch_AnErrorSource_ToAFixedSizeArray_IsStillTypeMismatch()
        => AssertError(CoerceWithEveryStrategy(new VBErrorValue(5), new VBFixedSizeArrayType(VBLongType.TypeInfo)), VBRuntimeErrorId.TypeMismatch);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
    public void Dispatch_AVariantHoldingNull_ToAFixedLengthString_IsInvalidUseOfNull()
        => AssertError(CoerceWithEveryStrategy(new VBVariantValue(VBNullValue.Null), new VBFixedStringType(3)), VBRuntimeErrorId.InvalidUseOfNull);

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
    public void Dispatch_ANullSource_ToAVariant_IsStillNull()
    {
        var result = CoerceWithEveryStrategy(VBNullValue.Null, VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo is null ? "" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString());
        Assert.IsInstanceOfType<VBNullValue>(((VBVariantValue)result.Result!).TypedValue);
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.9 Let-coercion to and from Error")]
    public void Dispatch_AnErrorSource_ToAVariant_IsStillAnError()
    {
        var result = CoerceWithEveryStrategy(new VBErrorValue(5), VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo is null ? "" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString());
        Assert.IsInstanceOfType<VBErrorValue>(((VBVariantValue)result.Result!).TypedValue);
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.9 Let-coercion to and from Error")]
    [DataRow("Currency")]
    [DataRow("Boolean")]
    [DataRow("Date")]
    [DataRow("Long()")]
    [DataRow("UDT")]
    [DataRow("Object")] // where 5.5.1.2.13's last row would say Object required (424)
    public void Dispatch_AnErrorSource_ToAnyDestinationButAVariant_IsTypeMismatch(string type)
    {
        VBType destination = type switch
        {
            "Currency" => VBCurrencyType.TypeInfo,
            "Boolean" => VBBooleanType.TypeInfo,
            "Date" => VBDateType.TypeInfo,
            "Long()" => new VBResizableArrayType(VBLongType.TypeInfo),
            "UDT" => Udt("Point"),
            _ => VBObjectType.TypeInfo,
        };

        // "Error -> Any type except a fixed-size array or Variant: Runtime error 13 (Type mismatch) is raised."
        AssertError(CoerceWithEveryStrategy(new VBErrorValue(5), destination), VBRuntimeErrorId.TypeMismatch);
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
    public void Analyze_ANullSource_IsFlaggedAsTheFailedNullOperand()
    {
        // the error is the Null strategy's, but the strategy asked to analyze the coercion is the Long's, which says
        // nothing about a Null: it is the provider that knows the operand whose coercion fails is the Null one.
        var builder = new LetCoercionSemanticContextFlagsBuilder();
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, VBNullValue.Null, new VBTypeDescValue(VBLongType.TypeInfo));

        LetCoercionAnalysisHarness.BuildProvider().Analyze(null!, builder, ThrowawayExpression, frame);

        var flags = builder.LetCoercionFlagsOf(InputIndex.CoercionSourceValue);
        Assert.IsTrue(flags.HasFlag(ConversionSemanticFlags.NullOperand | ConversionSemanticFlags.Failed), flags.ToString());
    }

    [TestMethod]
    [TestCategory("RD-VBAL §5.5.1.2.10 Let-coercion from Null")]
    public void Analyze_ANullSource_ToAVariant_IsNotAFailure()
    {
        var builder = new LetCoercionSemanticContextFlagsBuilder();
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, VBNullValue.Null, new VBTypeDescValue(VBVariantType.TypeInfo));

        LetCoercionAnalysisHarness.BuildProvider().Analyze(null!, builder, ThrowawayExpression, frame);

        Assert.IsFalse(builder.LetCoercionFlagsOf(InputIndex.CoercionSourceValue).HasFlag(ConversionSemanticFlags.Failed));
    }

    #endregion

    [TestMethod]
    public void RecursiveReEntry_WithTheSameFrame_IsOutOfStackSpace()
    {
        // a strategy that (however it got there) calls back into the provider with the exact same
        // frame it was given is the one scenario the recursion guard exists for. No production
        // strategy actually does this by design, so a substitute stands in for one that does.
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var strategy = Substitute.For<ILetCoercionRuntimeSemantics>();
        strategy.LetCoercionSpecification.Returns(typeof(VBLongType));

        var provider = new LetCoercionRuntimeSemanticsProvider([strategy], fmt);
        strategy.EvaluateLetCoercion(default!, default!, default).ReturnsForAnyArgs(call =>
            provider.EvaluateLetCoercionSemantics(call.ArgAt<ISymbolResolver>(0), call.ArgAt<VBOperatorExpression>(1), call.ArgAt<LetCoercionStackFrame>(2)));

        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBLongValue(5), new VBTypeDescValue(VBLongType.TypeInfo));
        var result = provider.EvaluateLetCoercionSemantics(null!, ThrowawayExpression, frame);

        Assert.IsTrue(result.ErrorInfo is not null, "expected a runtime error");
        Assert.AreEqual((int)VBRuntimeErrorId.OutOfStackSpace, result.ErrorInfo!.ErrorId);
    }

    [TestMethod]
    public void AfterARecursiveReEntry_TheStackIsCleared_SoALaterUnrelatedCallStillWorks()
        // the recursion guard clears the whole stack on detection (comment: "no need to dig any
        // deeper") rather than leaving it in a partially-unwound state -- confirm a later, entirely
        // unrelated coercion isn't permanently poisoned by an earlier recursive failure.
    {
        var fmt = Substitute.For<IVerboseMessageBuilder>();
        var strategy = Substitute.For<ILetCoercionRuntimeSemantics>();
        strategy.LetCoercionSpecification.Returns(typeof(VBLongType));

        var provider = new LetCoercionRuntimeSemanticsProvider([strategy], fmt);
        var recursedOnce = false;
        strategy.EvaluateLetCoercion(default!, default!, default).ReturnsForAnyArgs(call =>
        {
            if (recursedOnce)
            {
                return LetCoercionResult.Success(new VBLongValue(9));
            }
            recursedOnce = true;
            return provider.EvaluateLetCoercionSemantics(call.ArgAt<ISymbolResolver>(0), call.ArgAt<VBOperatorExpression>(1), call.ArgAt<LetCoercionStackFrame>(2));
        });

        var recursiveFrame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, new VBLongValue(5), new VBTypeDescValue(VBLongType.TypeInfo));
        provider.EvaluateLetCoercionSemantics(null!, ThrowawayExpression, recursiveFrame);

        // the strategy no longer recurses on this second, later call -- confirms only whether the
        // provider's own stack state was reset, not anything about the strategy's own behavior.
        var result = provider.EvaluateLetCoercionSemantics(null!, ThrowawayExpression, recursiveFrame);

        Assert.IsTrue(result.IsSuccess);
    }

    public static IEnumerable<object[]> SourcesThatCoerceByTheirOwnRules()
    {
        // MS-VBAL 5.5.1.2.10
        yield return [VBNullValue.Null, VBResizableByteArrayType.TypeInfo, VBRuntimeErrorId.TypeMismatch];
        yield return [VBNullValue.Null, VBErrorType.TypeInfo, VBRuntimeErrorId.InvalidUseOfNull];
        yield return [VBNullValue.Null, new VBFixedStringType(3), VBRuntimeErrorId.InvalidUseOfNull];
        // MS-VBAL 5.5.1.2.11
        yield return [VBEmptyValue.Empty, VBResizableByteArrayType.TypeInfo, VBRuntimeErrorId.TypeMismatch];
        yield return [VBEmptyValue.Empty, VBErrorType.TypeInfo, VBRuntimeErrorId.TypeMismatch];
        // MS-VBAL 5.5.1.2.9
        yield return [new VBErrorValue(5), VBResizableByteArrayType.TypeInfo, VBRuntimeErrorId.TypeMismatch];
        yield return [new VBErrorValue(5), VBStringType.TypeInfo, VBRuntimeErrorId.TypeMismatch];
        yield return [new VBErrorValue(5), new VBFixedStringType(3), VBRuntimeErrorId.TypeMismatch];
        yield return [new VBErrorValue(5), VBLongType.TypeInfo, VBRuntimeErrorId.TypeMismatch];
    }

    [TestMethod]
    [DynamicData(nameof(SourcesThatCoerceByTheirOwnRules))]
    public void ANullEmptyOrErrorSource_CoercesByItsOwnRules_WhateverTheDestinationIs(VBTypedValue source, VBType destination, VBRuntimeErrorId expected)
        => AssertError(LetCoercionAnalysisHarness.BuildProvider().EvaluateLetCoercionSemantics(null!, ThrowawayExpression,
            new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination))), expected);

    [TestMethod]
    public void ANullSource_ToAVariant_IsHeldByIt()
    {
        var result = LetCoercionAnalysisHarness.BuildProvider().EvaluateLetCoercionSemantics(null!, ThrowawayExpression,
            new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, VBNullValue.Null, new VBTypeDescValue(VBVariantType.TypeInfo)));

        Assert.IsTrue(result.IsSuccess);
    }
}
