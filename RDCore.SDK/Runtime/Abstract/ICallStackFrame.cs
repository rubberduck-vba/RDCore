using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.SDK.Runtime.Abstract
{
    public interface ICallStackFrame
    {
        /// <summary>
        /// Gets the <see cref="VBTypedValue"/> currently held in this frame for the specified <em>locally-scoped</em> <see cref="Symbol"/>.
        /// </summary>
        /// <param name="symbol"></param>
        /// <remarks>
        /// ⚠️ <see cref="CallStackFrame"/> frames are <strong>not immutable</strong>: the value retrieved for a given symbol may be different at a subsequent retrieval.
        /// </remarks>
        VBTypedValue this[Symbol symbol] => default!;
    }
}
