using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Semantics.Runtime.Abstract.Operators
{
    public readonly record struct DetermineOperatorEffectiveTypeResult(bool IsApplicable, VBType? Result, VBRuntimeErrorInfo? ErrorInfo)
    {
        public static DetermineOperatorEffectiveTypeResult Success(VBType result) => new(true, result, null);
        public static DetermineOperatorEffectiveTypeResult Error(VBRuntimeErrorInfo error) => new(true, null, error);
        public static DetermineOperatorEffectiveTypeResult NotApplicable() => new(false, null, null);
    }
}
