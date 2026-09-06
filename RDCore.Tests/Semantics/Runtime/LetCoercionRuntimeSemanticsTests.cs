﻿using NSubstitute;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics;
using RDCore.SDK.Services.VerboseMessages;

namespace RDCore.Tests.Semantics.Runtime;

/// <summary>
/// Base for the let-coercion runtime-semantics characterization matrix. Mirrors the static-semantics
/// harness (<see cref="Abstract.StaticSemanticsTests"/>): drive a strategy over a
/// (source value, destination type) grid and assert the coerced result's type and managed value, or
/// the runtime error id.
/// </summary>
public abstract class LetCoercionRuntimeSemanticsTests
{
    protected static readonly RDCore.SDK.Model.AST.Abstract.SyntaxNodeId NodeId = new(TestUri.TestModuleUri().AbsolutePath, [42]);

    protected static IVerboseMessageBuilder Formatter() => Substitute.For<IVerboseMessageBuilder>();
    protected static ILetCoercionRuntimeSemanticsProvider FakeProvider() => Substitute.For<ILetCoercionRuntimeSemanticsProvider>();

    // resolver is never dereferenced by EvaluateLetCoercion; expression is only used on error paths
    // (for expression.Location + the substituted formatter), so a throwaway node is enough.
    protected static readonly VBOperatorExpression ThrowawayExpression = new VBBinaryOperatorExpressionNode(
        "+", default, TestLocations.TestLocation,
        [
            new LiteralExpressionNode(default, TestLocations.TestLocationLHS, new VBIntegerValue((short)0)),
            new LiteralExpressionNode(default, TestLocations.TestLocationRHS, new VBIntegerValue((short)0)),
        ]);

    protected static LetCoercionResult Coerce(ILetCoercionRuntimeSemantics strategy, VBTypedValue source, VBType destination)
    {
        var frame = new LetCoercionStackFrame(NodeId, InputIndex.CoercionSourceValue, source, new VBTypeDescValue(destination));
        return strategy.EvaluateLetCoercion(null!, ThrowawayExpression, frame);
    }

    protected static void AssertCoercedTo<TValue>(LetCoercionResult result, object expectedManaged)
        where TValue : VBTypedValue
    {
        Assert.IsTrue(result.IsApplicable, "strategy reported NotApplicable");
        Assert.IsNull(result.ErrorInfo, $"unexpected {(result.ErrorInfo is null ? "" : ((VBRuntimeErrorId)result.ErrorInfo.ErrorId).ToString())}");
        Assert.IsInstanceOfType<TValue>(result.Result);
        Assert.AreEqual(expectedManaged, ManagedOf(result.Result!));
    }

    protected static void AssertError(LetCoercionResult result, VBRuntimeErrorId expected)
    {
        Assert.IsNotNull(result.ErrorInfo, "expected a runtime error");
        Assert.AreEqual(expected, (VBRuntimeErrorId)result.ErrorInfo!.ErrorId);
    }

    protected static object ManagedOf(VBTypedValue value) => value switch
    {
        VBBooleanValue v => (bool)v.Value,
        VBByteValue v => v.Value,
        VBIntegerValue v => v.Value,
        VBLongValue v => v.Value,
        VBLongLongValue v => v.Value,
        VBSingleValue v => v.Value,
        VBDoubleValue v => v.Value,
        VBCurrencyValue v => v.Value.Value,
        VBDecimalValue v => v.Value,
        VBStringValue v => v.Value,
        VBDateValue v => v.SerialValue,
        _ => value.RuntimeValue.BoxedValue,
    };
}
