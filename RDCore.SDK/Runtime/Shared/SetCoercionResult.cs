using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;

namespace RDCore.SDK.Runtime.Shared;

/// <summary>
/// The result of a <strong>MS-VBAL 5.5.2.2</strong> Set-coercion (run-time semantics) evaluation.
/// </summary>
/// <param name="Result">The coerced object reference, if the coercion succeeded.</param>
/// <param name="ErrorInfo">The run-time error metadata, if the coercion failed.</param>
public readonly record struct SetCoercionResult(VBTypedValue? Result, VBRuntimeErrorInfo? ErrorInfo)
{
    /// <summary>
    /// <c>true</c> if the coercion succeeded.
    /// </summary>
    public bool IsSuccess => ErrorInfo is null;

    /// <summary>
    /// Creates a new (successful) <see cref="SetCoercionResult"/> with the specified coerced value.
    /// </summary>
    public static SetCoercionResult Success(VBTypedValue result) => new(result, null);

    /// <summary>
    /// Creates a new (failed) <see cref="SetCoercionResult"/> with the specified error metadata.
    /// </summary>
    public static SetCoercionResult Error(VBRuntimeErrorInfo error) => new(null, error);
}
