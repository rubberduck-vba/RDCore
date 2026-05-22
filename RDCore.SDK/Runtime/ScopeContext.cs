using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Runtime;

public sealed record class ScopeContext(Symbol ScopeSymbol, ScopeContext? Parent = null)
{
    public Symbol ScopeSymbol { get; } = ScopeSymbol;
    public ScopeContext? Parent { get; } = Parent;
}
