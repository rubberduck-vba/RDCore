using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Runtime;

/// <summary>
/// Represents and encapsulates the execution environment and memory space.
/// </summary>
public sealed class VBExecutionContext(IVirtualHeap memory) : IVBExecutionContext
{
    required public bool Is64Bit { get; init; }

    public IVirtualHeap Memory { get; } = memory;

    public ScopeContext CurrentScope { get; private set; } = default!;

    public IDisposable EnterScope(Symbol scopeSymbol)
    {
        var previous = CurrentScope;
        CurrentScope = new ScopeContext(scopeSymbol, previous);
        return new ScopeReliever(this, previous);
    }

    private record ScopeReliever(VBExecutionContext Context, ScopeContext Previous) : IDisposable
    {
        public void Dispose() => Context.CurrentScope = Previous;
    }
}
