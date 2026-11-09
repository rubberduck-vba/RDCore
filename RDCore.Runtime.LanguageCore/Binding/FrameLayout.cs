using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.LanguageCore.Binding;

// a Static variable has no slot: it lives as long as its module, not as long as an activation.
// a slot is read only where its layout is the activation's: a procedure defined again has a layout of its own.
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

        ParameterSlots = [.. parameters.Select(SlotNumberOf)];
        LocalSlots = [.. locals.Select(SlotNumberOf)];
    }

    private int SlotNumberOf(Symbol symbol) => _slots.TryGetValue(symbol.SemanticId, out var index) ? index : -1;

    public ImmutableArray<int> ParameterSlots { get; }

    public ImmutableArray<int> LocalSlots { get; }

    public static FrameLayout Of(Symbol procedure) => new(procedure);

    private static readonly ConditionalWeakTable<Symbol, FrameLayout> _layouts = [];

    public static FrameLayout For(Symbol procedure) => _layouts.GetValue(procedure, Of);

    public Symbol Procedure { get; }

    public int Count => _slots.Count;

    public bool TryGetSlot(Symbol symbol, out FrameSlot slot)
    {
        var found = _slots.TryGetValue(symbol.SemanticId, out var index);
        slot = found ? new FrameSlot(this, index) : default;
        return found;
    }
}

public readonly record struct FrameSlot(FrameLayout Layout, int Index);
