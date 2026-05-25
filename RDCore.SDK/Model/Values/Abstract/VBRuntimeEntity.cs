using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// Represents a typed entity that can be associated with a <c>Symbol</c>.
/// </summary>
/// <param name="TypeInfo">The <c>VBType</c> of the entity.</param>
/// <param name="Symbol">The <c>Symbol</c> associated to the entity.</param>
public abstract record class VBRuntimeEntity(VBType TypeInfo, Symbol Symbol)
{
    /// <summary>
    /// Throws the exception returned by the specified function if <c>Symbol</c> is defined.
    /// </summary>
    /// <remarks>
    /// Does strictly nothing otherwise.
    /// </remarks>
    protected void ThrowWithSymbol(Func<Symbol?, VBRuntimeErrorException> getException)
    {
        if (Symbol is not null)
        {
            throw getException(Symbol);
        }
    }
}

