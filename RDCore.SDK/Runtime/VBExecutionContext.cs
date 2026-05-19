using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Runtime;

/// <summary>
/// Represents 
/// </summary>
public interface IVBExecutionContext
{
    /// <summary>
    /// <c>true</c> if the execution context describes a 64-bit environment; <c>false</c> otherwise.
    /// </summary>
    /// <remarks>
    /// This value determines the size of a <c>VBLongPtrValue</c> and is used anywhere needed, e.g. when evaluating precompiler directives.
    /// </remarks>
    bool Is64Bit { get; }

    /// <summary>
    /// Gets the memory space for this context.
    /// </summary>
    IVirtualHeap Memory { get; }

    /// <summary>
    /// Enters the execution scope of the specified scope symbol.
    /// </summary>
    /// <param name="scopeSymbol">A symbol representing an execution scope, e.g. a <c>VBTypeMemberSymbol</c></param>
    /// <returns></returns>
    IDisposable EnterScope(Symbol scopeSymbol);
}

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
