using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

namespace RDCore.SDK.Model.Types.Complex;

/// <summary>
/// The candidate-type merge and resolution rules an <see cref="IVBInferableType"/> follows, per its
/// own static-semantics note (<see cref="IVBInferableType"/> xmldoc remarks).
/// </summary>
public static class IVBInferableTypeExtensions
{
    /// <summary>
    /// Resolves the single data type <paramref name="inferable"/>'s candidates currently agree on, or
    /// <c>null</c> when they don't agree on one yet.
    /// </summary>
    /// <remarks>
    /// A lone candidate resolves to itself. Two or more resolve to <c>Variant</c> when every candidate
    /// is a plain value type (numeric, <c>String</c>, <c>Boolean</c>, …) — never when one of them is
    /// <c>Object</c> or a class-ish type (<see cref="IVBMemberOwnerType"/>): those don't blend into a
    /// <c>Variant</c> guess, and what they <em>do</em> resolve to isn't decided yet (the algorithm's
    /// own doc trails into "TODO continue this" at exactly this point) — such a set currently resolves
    /// to <c>null</c>, same as no candidates at all.
    /// </remarks>
    public static VBType? Resolve(this IVBInferableType inferable) => inferable.CandidateTypes.Count switch
    {
        0 => null,
        1 => inferable.CandidateTypes.Single(),
        _ => inferable.CandidateTypes.All(IsPlainValueType) ? VBVariantType.TypeInfo : null,
    };

    /// <summary>
    /// Adds <paramref name="vbType"/> as a new candidate, applying the widening / merge rules a newly
    /// found candidate goes through before joining the set.
    /// </summary>
    /// <remarks>
    /// An integral candidate (<see cref="IIntegralNumericType"/> — <c>Byte</c>/<c>Integer</c>/
    /// <c>Long</c>/<c>LongLong</c>) widens to <see cref="VBLongType"/>; a floating-point candidate
    /// (<see cref="IFloatingPointNumericType"/>) widens to <see cref="VBDoubleType"/>; a fixed-point
    /// candidate (<see cref="IFixedPointNumericType"/>) widens to <see cref="VBCurrencyType"/> — so
    /// differently-sized numeric candidates bucket together instead of manufacturing spurious
    /// conflicts. A candidate that is itself inferable contributes its own candidate set (already
    /// widened) rather than joining as one opaque entry.
    /// </remarks>
    public static ImmutableHashSet<VBType> MergeCandidate(this ImmutableHashSet<VBType> candidates, VBType vbType) => vbType switch
    {
        IVBInferableType inferable => candidates.Union(inferable.CandidateTypes),
        IIntegralNumericType => candidates.Add(VBLongType.TypeInfo),
        IFloatingPointNumericType => candidates.Add(VBDoubleType.TypeInfo),
        IFixedPointNumericType => candidates.Add(VBCurrencyType.TypeInfo),
        _ => candidates.Add(vbType),
    };

    private static bool IsPlainValueType(VBType type) => type is not (VBObjectType or IVBMemberOwnerType or IVBInferableType);
}
