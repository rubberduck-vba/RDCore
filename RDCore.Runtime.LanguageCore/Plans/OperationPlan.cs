using RDCore.SDK.Model;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Runtime.LanguageCore.Plans;

public sealed record class OperationPlan(string Operator, IReadOnlyList<VBType> Operands, VBType? EffectiveType, bool IsDeferred = false)
{
    public bool IsTypeMismatch => EffectiveType is null && !IsDeferred;

    // MS-VBAL §5.6.9.3, §5.6.9.7
    public VBType? OperandDestination { get; } = DestinationOf(Operator, EffectiveType);

    private static VBType? DestinationOf(string @operator, VBType? effectiveType) => effectiveType switch
    {
        _ when @operator == Tokens.CompareIsOp => null,
        VBDateType => VBDoubleType.TypeInfo,
        VBNullType or null => null,
        var effective => effective,
    };

    public bool Equals(OperationPlan? other)
        => other is not null && Operator == other.Operator && Operands.SequenceEqual(other.Operands)
        && Equals(EffectiveType, other.EffectiveType) && IsDeferred == other.IsDeferred;

    public override int GetHashCode()
        => Operands.Aggregate(HashCode.Combine(Operator, EffectiveType, IsDeferred), HashCode.Combine);
}
