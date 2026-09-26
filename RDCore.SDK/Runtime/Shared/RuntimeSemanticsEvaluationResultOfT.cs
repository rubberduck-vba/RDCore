using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// Represents the result of an evaluation whose result type is known statically: the same outcome as
/// <see cref="RuntimeSemanticsEvaluationResult"/>, with the <see cref="VBTypedValue"/> it produces
/// named by <typeparamref name="TValue"/>.
/// </summary>
/// <remarks>
/// This is how a standard-library member declaration states its VBA return type
/// (<strong>RD-VBAL §6.1</strong>: "the symbols shall carry the appropriate <em>return type</em>
/// metadata"). Naming it in the signature rather than restating it in an attribute means the
/// declaration and the implementation cannot disagree - an implementation of <c>Hex$</c> that returns
/// anything but a <see cref="Model.Values.Intrinsic.VBStringValue"/> does not compile - and a symbol
/// provider reads the declared type back off the signature.
/// <para>
/// A member returning nothing - a VBA <c>Sub</c>, or a <c>Property Let</c>/<c>Set</c> - is declared
/// with the non-generic <see cref="RuntimeSemanticsEvaluationResult"/> instead, so the presence of a
/// return type is itself part of the declaration. Its declared type is
/// <see cref="Model.Types.Complex.VBVoidType"/> and the value it yields is
/// <see cref="Model.Values.VBVoidValue.Void"/> - <em>not</em>
/// <see cref="Model.Values.Intrinsic.VBEmptyValue"/>, which is a real <c>Variant</c> subtype source can
/// see (<c>IsEmpty</c>, <c>VarType</c>, a Let-coercion to <c>0</c>); the result of a <c>Sub</c> is
/// nothing source can see anything about at all.
/// </para>
/// <para>
/// 👉 An enumeration or a class is <em>not</em> expressible as a <typeparamref name="TValue"/>: every
/// value of one is a <see cref="Model.Values.Intrinsic.VBLongValue"/> or a
/// <see cref="Model.Values.Intrinsic.VBObjectValue"/> respectively, which is what an implementation
/// produces and so what this names. Those members state the type VBA source sees with
/// <see cref="Abstract.StdLib.StdLibMemberAttribute.ReturnType"/>.
/// </para>
/// </remarks>
/// <typeparam name="TValue">The <see cref="VBTypedValue"/> a successful evaluation produces.</typeparam>
/// <param name="Result">The result of the evaluation, if one was produced.</param>
/// <param name="ErrorInfo">The error metadata for the <em>run-time</em> error to be reported, if applicable.</param>
public readonly record struct RuntimeSemanticsEvaluationResult<TValue>(
    TValue? Result,
    IVBRaisableError? ErrorInfo)
    : IRuntimeSemanticsEvaluationResult
    where TValue : VBTypedValue
{
    /// <inheritdoc cref="RuntimeSemanticsEvaluationResult.IsSuccess"/>
    public bool IsSuccess => Result is not null && ErrorInfo is null;

    /// <inheritdoc cref="RuntimeSemanticsEvaluationResult.IsError"/>
    public bool IsError => Result is not null && ErrorInfo is not null;

    /// <inheritdoc cref="RuntimeSemanticsEvaluationResult.IsInternalError"/>
    public bool IsInternalError => Result is null && ErrorInfo is null;

    VBTypedValue? IRuntimeSemanticsEvaluationResult.Result => Result;

    /// <inheritdoc cref="RuntimeSemanticsEvaluationResult.Success(VBTypedValue)"/>
    /// <param name="result">The successfully evaluated <em>runtime semantics evaluation</em> result.</param>
    public static RuntimeSemanticsEvaluationResult<TValue> Success(TValue result) => new(result, null);

    /// <inheritdoc cref="RuntimeSemanticsEvaluationResult.Error(IVBRaisableError, VBTypedValue?)"/>
    /// <param name="error">The runtime error metadata describing the evaluation failure.</param>
    /// <param name="result">The result value assigned before the failure, if any.</param>
    public static RuntimeSemanticsEvaluationResult<TValue> Error(IVBRaisableError error, TValue? result = null) => new(result, error);

    /// <inheritdoc cref="RuntimeSemanticsEvaluationResult.InternalError"/>
    public static RuntimeSemanticsEvaluationResult<TValue> InternalError() => new(null, null);

    /// <summary>
    /// Widens the result to one whose result type is not stated, discarding nothing.
    /// </summary>
    /// <param name="result">The result to widen.</param>
    public static implicit operator RuntimeSemanticsEvaluationResult(RuntimeSemanticsEvaluationResult<TValue> result)
        => new(result.Result, result.ErrorInfo);
}
