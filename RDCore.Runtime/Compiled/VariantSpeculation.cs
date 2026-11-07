using RDCore.SDK.Model;
using RDCore.SDK.Semantics.Plans;

namespace RDCore.Runtime.Compiled;

/// <summary>
/// The subtypes compiled code speculates a <c>Variant</c> holds: where every Variant an expression reads holds the subtype the analysis
/// speculates it holds (<see cref="ProcedureAnalysis.SpeculatedKinds"/>), the expression's operations are chosen when it is compiled (the plan
/// <c>TryOperateDeferred</c> makes for those subtypes) and done on the interpreter's own payload operations (<see cref="PayloadOperations"/>). A Variant
/// that holds another subtype, or what is no payload, is evaluated as any is (<see cref="VariantOperations.Binary"/>): a speculation that does not hold is
/// slower, and its result is the same.
/// </summary>
internal static class VariantSpeculation
{
    /// <summary>
    /// The subtype of what <paramref name="node"/> evaluates to, when each Variant it reads holds the subtype speculated for its slot and each of its
    /// operations is one compiled code plans; <see langword="null"/> otherwise.
    /// </summary>
    /// <param name="node">The expression.</param>
    /// <param name="speculated">The subtype speculated for each Variant slot, by slot.</param>
    /// <returns>The subtype; <see langword="null"/> when the expression has no speculation.</returns>
    public static ScalarKind? KindOf(VariantNode node, IReadOnlyDictionary<int, ScalarKind> speculated) => node switch
    {
        VariantSlotNode slot => speculated.TryGetValue(slot.Slot, out var kind) ? kind : null,
        VariantOfNode of when IsPlanned(of.Value.Kind) => of.Value.Kind,
        VariantBinaryNode binary when PlanOf(binary.Token, KindOf(binary.Left, speculated), KindOf(binary.Right, speculated)) is { } plan => plan,
        _ => null,
    };

    /// <summary>
    /// The subtype an arithmetic operation of payloads of <paramref name="left"/> and <paramref name="right"/> is done in, and its result is of
    /// (<c>RuntimeExpressionEvaluator.TryOperate</c>): both operands are converted to it, and the operation is the payload operation of it.
    /// </summary>
    /// <param name="token">The operator's token.</param>
    /// <param name="left">The subtype of the left operand; <see langword="null"/> when it has none.</param>
    /// <param name="right">The subtype of the right operand; <see langword="null"/> when it has none.</param>
    /// <returns>The subtype; <see langword="null"/> when the operation is not one compiled code plans.</returns>
    public static ScalarKind? PlanOf(string token, ScalarKind? left, ScalarKind? right)
    {
        if (left is not { } l || right is not { } r || token is not (Tokens.AdditionOp or Tokens.SubtractionOp or Tokens.MultiplicationOp))
        {
            return null;
        }

        var plan = OperationPlanner.ForValues(token, ScalarKinds.TypeOf(l), ScalarKinds.TypeOf(r));
        return ScalarKinds.Of(plan.OperandDestination) is { } destination && ScalarKinds.Of(plan.EffectiveType) == destination && IsPlanned(destination)
            ? destination
            : null;
    }

    /// <summary>
    /// The subtype both operands of a comparison of payloads of <paramref name="left"/> and <paramref name="right"/> are compared in (<c>TryCompare</c>).
    /// </summary>
    /// <param name="token">The comparison operator's token.</param>
    /// <param name="left">The subtype of the left operand; <see langword="null"/> when it has none.</param>
    /// <param name="right">The subtype of the right operand; <see langword="null"/> when it has none.</param>
    /// <returns>The subtype; <see langword="null"/> when the comparison is not one compiled code plans.</returns>
    public static ScalarKind? ComparisonOf(string token, ScalarKind? left, ScalarKind? right)
    {
        if (left is not { } l || right is not { } r || OperatorTokens.ComparisonOf(token) is null)
        {
            return null;
        }

        var plan = OperationPlanner.ForValues(token, ScalarKinds.TypeOf(l), ScalarKinds.TypeOf(r));
        return ScalarKinds.Of(plan.OperandDestination) is { } destination && IsPlanned(destination) ? destination : null;
    }

    /// <summary>
    /// The subtype two speculations of one Variant make: the one an addition of both is done in (the wider), so that a Variant assigned an
    /// <c>Integer</c> and then a <c>Double</c> is speculated to hold the <c>Double</c> it holds from then on.
    /// </summary>
    /// <param name="held">The subtype speculated so far.</param>
    /// <param name="assigned">The subtype of what the Variant is assigned.</param>
    /// <returns>The subtype speculated from then on.</returns>
    public static ScalarKind Join(ScalarKind held, ScalarKind assigned) => held == assigned ? held : PlanOf(Tokens.AdditionOp, held, assigned) ?? assigned;

    /// <summary>
    /// The subtype a loop of a Variant counter counts in, from the subtypes of its start, end and step: the widest of them, ranked as
    /// <c>ForLoopEvaluator</c> ranks them.
    /// </summary>
    /// <param name="start">The subtype of the start; <see langword="null"/> when it has none.</param>
    /// <param name="end">The subtype of the end; <see langword="null"/> when it has none.</param>
    /// <param name="step">The subtype of the step; <see langword="null"/> when it has none.</param>
    /// <returns>The subtype; <see langword="null"/> when the loop is not one compiled code plans.</returns>
    public static ScalarKind? CountingKindOf(ScalarKind? start, ScalarKind? end, ScalarKind? step)
    {
        if (start is not { } s || end is not { } e || step is not { } p)
        {
            return null;
        }

        ScalarKind? widest = null;
        var rank = 0;
        foreach (var kind in new[] { s, e, p })
        {
            if (RankOf(kind) is not { } ranked)
            {
                return null;
            }

            if (ranked.Rank > rank)
            {
                (widest, rank) = (ranked.Kind, ranked.Rank);
            }
        }

        return widest is { } counting && IsPlanned(counting) ? counting : null;
    }

    // ForLoopEvaluator.RankOf, of the scalars: a Boolean counts in Integer, a Single in Double.
    private static (int Rank, ScalarKind Kind)? RankOf(ScalarKind kind) => kind switch
    {
        ScalarKind.Byte => (1, ScalarKind.Byte),
        ScalarKind.Boolean or ScalarKind.Integer => (2, ScalarKind.Integer),
        ScalarKind.Long => (3, ScalarKind.Long),
        ScalarKind.LongLong => (4, ScalarKind.LongLong),
        ScalarKind.Single or ScalarKind.Double => (5, ScalarKind.Double),
        ScalarKind.Currency => (6, ScalarKind.Currency),
        _ => null,
    };

    /// <summary>
    /// Whether the interpreter's operations on payloads of <paramref name="kind"/> are the payload operations compiled code calls: those of an integral
    /// kind but <c>Boolean</c> (<c>OperatorTokens.OperateIntegral</c>), and a <c>Double</c>'s, the sum of two Doubles checked as finite.
    /// </summary>
    /// <param name="kind">The subtype.</param>
    /// <returns>Whether compiled code plans operations of it.</returns>
    public static bool IsPlanned(ScalarKind kind) => kind is ScalarKind.Double || ScalarKinds.IsIntegral(kind) && kind is not ScalarKind.Boolean;
}
