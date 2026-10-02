using System.Runtime.CompilerServices;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdEnumVariantClass"/>
/// <remarks>
/// An enumerator is a position in a snapshot of the members of what it enumerates, which it is given when it is made
/// (<see cref="Create"/>). The state belongs to the enumerator's own instance, so it lasts exactly as long as the object does.
/// </remarks>
/// <param name="session">The session the enumerators are objects of.</param>
internal sealed class StdEnumVariant(IRuntimeSession session) : IStdEnumVariantClass, IStdLibReceiverBound
{
    private sealed class Position(IReadOnlyList<VBTypedValue> members)
    {
        public IReadOnlyList<VBTypedValue> Members { get; } = members;

        // before the first member.
        public int Index { get; set; } = -1;
    }

    private readonly ConditionalWeakTable<IObjectInstance, Position> _positions = [];

    /// <inheritdoc/>
    public VBRuntimeObjectId? Receiver { get; set; }

    /// <summary>
    /// Makes an enumerator over <paramref name="members"/>: an object of the <c>IEnumVARIANT</c> class, positioned before the first of them.
    /// </summary>
    /// <param name="members">What it enumerates, as it is now: later changes to whatever they came from are not its.</param>
    /// <returns>The enumerator, or <see langword="null"/> when the library has no such class.</returns>
    public VBObjectValue? Create(IReadOnlyList<VBTypedValue> members)
    {
        if (session.Symbols.Resolver.ResolveType("IEnumVARIANT", ScopeKind.Global, StaticSymbol.GlobalUri).Symbol is not VBClassModuleSymbol enumerator)
        {
            return null;
        }

        var objectId = session.Objects.CreateObject();
        var instance = session.Symbols.CreateInstance(objectId, enumerator);
        _positions.Add(instance, new Position(members));
        return new VBObjectValue(objectId);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBBooleanValue> MoveNext()
    {
        if (!TryGetPosition(out var position))
        {
            return RuntimeSemanticsEvaluationResult<VBBooleanValue>.InternalError();
        }

        // once past the last member it stays there, which is what has the loop end, however often it is asked.
        position.Index = Math.Min(position.Index + 1, position.Members.Count);
        return RuntimeSemanticsEvaluationResult<VBBooleanValue>.Success(new VBBooleanValue(position.Index < position.Members.Count));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Current()
    {
        if (!TryGetPosition(out var position))
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.InternalError();
        }

        return position.Index >= 0 && position.Index < position.Members.Count
            ? RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(position.Members[position.Index]))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(VBRuntimeErrorInfo.For(
                VBRuntimeErrorId.InvalidProcedureCallOrArgument, default, "The enumerator is not at a member."));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Reset()
    {
        if (!TryGetPosition(out var position))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        position.Index = -1;
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    private bool TryGetPosition(out Position position)
    {
        position = null!;
        return Receiver is { } receiver
            && session.Symbols.TryGetInstance(receiver, out var instance)
            && _positions.TryGetValue(instance, out position!);
    }
}
