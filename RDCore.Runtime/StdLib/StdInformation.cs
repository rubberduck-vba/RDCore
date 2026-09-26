using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdInformationModule"/>
/// <remarks>
/// 🚧 Only <see cref="Erl"/> is implemented. Every other member returns the error a member nothing
/// implements returns, which is what VBA itself says about a call it has no answer for — the symbol
/// resolves, the call is well-formed, and the platform has not got the code. The declarations are what say
/// the shape each one has to satisfy, so filling them in adds no plumbing.
/// </remarks>
/// <param name="session">The session whose state these members report on.</param>
public sealed class StdInformation(IRuntimeSession session) : IStdInformationModule
{
    /// <summary>
    /// The error every member that is declared but not written yet returns.
    /// </summary>
    /// <remarks>
    /// TODO one per member as they land. Deliberately an error rather than a plausible value: a
    /// <c>TypeName</c> that answered <c>"Empty"</c> for everything would be worse than one that says it
    /// cannot answer.
    /// </remarks>
    private static RuntimeSemanticsEvaluationResult<TValue> NotImplemented<TValue>(string member)
        where TValue : VBTypedValue
        => RuntimeSemanticsEvaluationResult<TValue>.Error(VBRuntimeErrorInfo.For(
            VBRuntimeErrorId.ApplicationDefinedOrObjectDefinedError, default,
            $"'{member}' is declared but not implemented yet."));

    public RuntimeSemanticsEvaluationResult<VBObjectValue> Err() => NotImplemented<VBObjectValue>(nameof(Err));

    /// <summary>
    /// The line the session's most recent run-time error was raised at, counted the way the environment says
    /// to count it (<c>IRuntimeEnvironmentProfile.ErlLineNumbering</c>).
    /// </summary>
    /// <remarks>
    /// Reads the session's error state, which captured it when the error was raised — by the time source
    /// asks, inside a handler, the activation it happened in has been unwound.
    /// </remarks>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Erl()
        => RuntimeSemanticsEvaluationResult<VBLongValue>.Success(
            new VBLongValue(checked((int)session.Errors.LineNumber)));

    public RuntimeSemanticsEvaluationResult<VBLongValue> IMEStatus() => NotImplemented<VBLongValue>(nameof(IMEStatus));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsArray(VBVariantValue arg) => NotImplemented<VBBooleanValue>(nameof(IsArray));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsDate(VBVariantValue arg) => NotImplemented<VBBooleanValue>(nameof(IsDate));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsEmpty(VBVariantValue arg) => NotImplemented<VBBooleanValue>(nameof(IsEmpty));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsError(VBVariantValue arg) => NotImplemented<VBBooleanValue>(nameof(IsError));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsMissing(VBVariantValue arg) => NotImplemented<VBBooleanValue>(nameof(IsMissing));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsNull(VBVariantValue arg) => NotImplemented<VBBooleanValue>(nameof(IsNull));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsNumeric(VBVariantValue arg) => NotImplemented<VBBooleanValue>(nameof(IsNumeric));

    public RuntimeSemanticsEvaluationResult<VBBooleanValue> IsObject(VBVariantValue arg) => NotImplemented<VBBooleanValue>(nameof(IsObject));

    public RuntimeSemanticsEvaluationResult<VBLongValue> QBColor(VBIntegerValue color) => NotImplemented<VBLongValue>(nameof(QBColor));

    public RuntimeSemanticsEvaluationResult<VBLongValue> RGB(VBIntegerValue red, VBIntegerValue green, VBIntegerValue blue)
        => NotImplemented<VBLongValue>(nameof(RGB));

    public RuntimeSemanticsEvaluationResult<VBStringValue> TypeName(VBVariantValue arg) => NotImplemented<VBStringValue>(nameof(TypeName));

    public RuntimeSemanticsEvaluationResult<VBLongValue> VarType(VBVariantValue varName) => NotImplemented<VBLongValue>(nameof(VarType));
}
