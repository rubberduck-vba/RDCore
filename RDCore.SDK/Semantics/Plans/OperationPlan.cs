using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Semantics.Plans;

/// <summary>
/// What an operation does, decided from the types of its operands alone - before, and apart from, any value it is evaluated on (IR lowering §3.2).
/// </summary>
/// <remarks>
/// <para>
/// A plan is the value-independent half of an operation: its <em>effective type</em>, and what follows from it - the type each operand is let-coerced to, and
/// whether the operation is a type mismatch. The other half, evaluating the plan on values, is the runtime's.
/// </para>
/// <para>
/// The same planner answers for two kinds of types. Given the <em>value types</em> of the operands - what the interpreter has, a Variant operand being the type of
/// what it holds - a plan is final. Given <em>declared types</em>, an operand declared <c>Variant</c> or <c>Object</c> says nothing about the value it will hold,
/// and the plan is <see cref="IsDeferred"/>: it is made again, at run time, from the value types. The tables of the specification are keyed by value types, so for
/// any other operand the value type is the declared type, and the two plans agree.
/// </para>
/// </remarks>
/// <param name="Operator">The operator: one of the <c>Tokens</c> operators.</param>
/// <param name="Operands">The types the plan was made for, one for each operand, in order.</param>
/// <param name="EffectiveType">
/// The effective type of the operation, or <see langword="null"/> when there is none: a type mismatch, or a plan that is deferred.
/// </param>
/// <param name="IsDeferred">Whether the plan depends on the value an operand declared <c>Variant</c> or <c>Object</c> holds, and is made at run time.</param>
public sealed record class OperationPlan(string Operator, IReadOnlyList<VBType> Operands, VBType? EffectiveType, bool IsDeferred = false)
{
    /// <summary>
    /// Whether the operation has no effective type for these operands: run-time error 13, <em>Type mismatch</em>.
    /// </summary>
    public bool IsTypeMismatch => EffectiveType is null && !IsDeferred;

    /// <summary>
    /// What a non-<c>Null</c> operand is let-coerced to before the operation is evaluated: the effective type, except that a <c>Date</c> operation is evaluated in
    /// <c>Double</c> (<strong>MS-VBAL §5.6.9.3</strong>). <see langword="null"/> when nothing is coerced - a <c>Null</c> effective type, whose result is
    /// <c>Null</c> whatever the other operand is, and a plan with no effective type.
    /// </summary>
    public VBType? OperandDestination => EffectiveType switch
    {
        VBDateType => VBDoubleType.TypeInfo,
        VBNullType or null => null,
        var effective => effective,
    };

    /// <summary>
    /// Whether two plans agree: the same operator, the same operand types, the same outcome. A record of an array compares the arrays by reference, which is not
    /// what makes two plans the same.
    /// </summary>
    public bool Equals(OperationPlan? other)
        => other is not null && Operator == other.Operator && Operands.SequenceEqual(other.Operands)
        && Equals(EffectiveType, other.EffectiveType) && IsDeferred == other.IsDeferred;

    /// <inheritdoc/>
    public override int GetHashCode()
        => Operands.Aggregate(HashCode.Combine(Operator, EffectiveType, IsDeferred), HashCode.Combine);
}
