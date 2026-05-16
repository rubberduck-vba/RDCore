using RDCore.Parsing.Model.Symbols.Abstract;

namespace RDCore.Runtime;

internal record ScopeContext
{
    public ScopeContext(Symbol scopeSymbol, ScopeContext? parent = null)
    {
        ScopeSymbol = scopeSymbol;
        Parent = parent;
    }

    public Symbol ScopeSymbol { get; }
    public ScopeContext? Parent { get; }
}
