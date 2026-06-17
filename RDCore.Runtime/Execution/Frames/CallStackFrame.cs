using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// Represents a single <em>call stack</em> frame that allocates locally-scoped symbols.
/// </summary>
public record class CallStackFrame(Uri NodeUri, StaticSymbol StaticSymbol, ImmutableArray<VBTypedValue> Inputs) : ICallStackFrame
{
    private readonly Stack<Symbol> _localSymbols = [];
    private readonly Dictionary<Uri, Symbol> _localSymbolTable = [];
    private readonly Dictionary<Symbol, VBTypedValue> _localHeap = [];

    public VBTypedValue this[Symbol symbol] => _localHeap[symbol];

    /// <summary>
    /// Pushes a value onto this <em>stack frame</em>.
    /// </summary>
    /// <param name="value">A <see cref="VBTypedValue"/> to be allocated locally in this frame.</param>
    public void Push(VBTypedValue value)
    {
        if (_localSymbols.Contains(value.ResolvedSymbol))
        {
            // something went very wrong.
            throw new InvalidOperationException();
        }
        _localSymbols.Push(value.ResolvedSymbol);

        _localHeap[value.ResolvedSymbol] = value;
        _localSymbolTable[value.ResolvedSymbol.Uri] = value.ResolvedSymbol;
    }

    /// <summary>
    /// Gets the <see cref="VBTypedValue"/> associated with the specified local <c>Symbol</c>.
    /// </summary>
    /// <param name="symbol">A <see cref="Symbol"/> that is allocated on this stack frame.</param>
    /// <param name="value"></param>
    /// <returns></returns>
    public bool TryResolve(Symbol symbol, [NotNullWhen(true)][MaybeNullWhen(false)] out VBTypedValue? value) 
        => _localHeap.TryGetValue(symbol, out value);
}
