using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract;
using RDCore.SDK.Semantics.Runtime.Abstract;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace RDCore.Runtime.Model;

/// <summary>
/// Represents a single <em>call stack</em> frame that allocates locally-scoped symbols.
/// </summary>
public record class CallStackFrame : IndexedStackFrame
{
    private readonly Stack<Symbol> _localSymbols = [];
    private readonly Dictionary<Uri, Symbol> _localSymbolTable = [];
    private readonly Dictionary<Symbol, VBTypedValue> _localHeap = [];

    /// <summary>
    /// Represents a single <em>call</em> stack frame.
    /// </summary>
    /// <param name="nodeUri">The <c>SemanticId</c> of the executable node.</param>
    /// <param name="staticSymbol">The <see cref="StaticSymbol"/> associated with this executable operation.</param>
    /// <param name="inputs">Optional <see cref="VBTypedValue"/> inputs to initialize the stack with.</param>
    public CallStackFrame(Uri nodeUri, StaticSymbol staticSymbol, params VBTypedValue[] inputs)
        : base(nodeUri, staticSymbol, inputs.Select((input, index) => ((InputIndex)index, input)))
    {
    }

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
