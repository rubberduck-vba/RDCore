using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Abstract;

namespace RDCore.Parsing.Model.Values.Abstract;

internal abstract record class VBRuntimeEntity(VBType TypeInfo, Symbol? Symbol = null)
{
    /// <summary>
    /// Throws the exception returned by the specified function if <c>Symbol</c> is defined.
    /// </summary>
    /// <remarks>
    /// Does strictly nothing otherwise.
    /// </remarks>
    protected void ThrowWithSymbol(Func<Symbol,VBRuntimeErrorException> getException)
    {
        if (Symbol is not null)
        {
            throw getException(Symbol);
        }
    }
}

