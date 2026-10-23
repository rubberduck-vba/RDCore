using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using System.Runtime.CompilerServices;

namespace RDCore.SDK.Semantics.Binding;

/// <summary>
/// The slots of an activation of a procedure (IR lowering §3.1, D2): one for each of its parameters and of the variables it declares, which a bound name of
/// the procedure is bound to by number rather than looked up by its symbol each time it is read.
/// </summary>
/// <remarks>
/// <para>
/// A slot is a position, which each activation fills with what it holds there - the runtime decides what that is. Two activations of a procedure share its
/// layout, and the layout of a procedure is the same whatever its activations hold: the numbering is the order its parameters, then its variables, are
/// declared in.
/// </para>
/// <para>
/// A <c>Static</c> variable has no slot: it lives as long as the procedure's module does, not as long as an activation, and is not the activation's to hold.
/// Neither has a constant, which has a value rather than storage.
/// </para>
/// <para>
/// A layout is made for one definition of a procedure: a procedure that is defined again has a layout of its own. An activation that holds the slots of
/// one layout is no activation of another, so a slot is only read where its layout is the activation's (<see cref="FrameSlot.Layout"/>).
/// </para>
/// </remarks>
public sealed class FrameLayout
{
    private readonly Dictionary<SemanticId, int> _slots = [];

    private FrameLayout(Symbol procedure)
    {
        Procedure = procedure;
        var (parameters, locals) = procedure switch
        {
            VBReturningMemberSymbol returning => (returning.Parameters, returning.Locals),
            VBProcedureMemberSymbol sub => (sub.Parameters, sub.Locals),
            _ => ([], []),
        };

        foreach (var variable in parameters.Cast<Symbol>().Concat(locals).OfType<VBLocalVariableSymbol>().Where(variable => !variable.IsStatic))
        {
            _slots.TryAdd(variable.SemanticId, _slots.Count);
        }
    }

    /// <summary>
    /// The layout of an activation of <paramref name="procedure"/>.
    /// </summary>
    /// <param name="procedure">The procedure: a <c>Sub</c>, a <c>Function</c> or a property accessor. Anything else has no slots.</param>
    public static FrameLayout Of(Symbol procedure) => new(procedure);

    private static readonly ConditionalWeakTable<Symbol, FrameLayout> _layouts = [];

    /// <summary>
    /// The layout of an activation of <paramref name="procedure"/>, the same instance every time it is asked for that very symbol: what binds a name of the
    /// procedure to a slot and what lays out its activations agree on it by identity (<see cref="FrameSlot.Layout"/>).
    /// </summary>
    /// <param name="procedure">The procedure: a <c>Sub</c>, a <c>Function</c> or a property accessor. Anything else has no slots.</param>
    public static FrameLayout For(Symbol procedure) => _layouts.GetValue(procedure, Of);

    /// <summary>
    /// The procedure this is the layout of.
    /// </summary>
    public Symbol Procedure { get; }

    /// <summary>
    /// How many slots an activation of the procedure has.
    /// </summary>
    public int Count => _slots.Count;

    /// <summary>
    /// The slot of a parameter or a variable of the procedure, if it has one.
    /// </summary>
    /// <param name="symbol">The symbol a name of the procedure resolves to.</param>
    /// <param name="slot">The slot.</param>
    /// <returns><see langword="false"/> for anything that is no parameter or variable of this procedure, or is <c>Static</c>.</returns>
    public bool TryGetSlot(Symbol symbol, out FrameSlot slot)
    {
        var found = _slots.TryGetValue(symbol.SemanticId, out var index);
        slot = found ? new FrameSlot(this, index) : default;
        return found;
    }
}

/// <summary>
/// A slot of a <see cref="FrameLayout"/>.
/// </summary>
/// <param name="Layout">The layout the slot is in, which is the activation's only when the activation is of that very definition of the procedure.</param>
/// <param name="Index">The number of the slot.</param>
public readonly record struct FrameSlot(FrameLayout Layout, int Index);
