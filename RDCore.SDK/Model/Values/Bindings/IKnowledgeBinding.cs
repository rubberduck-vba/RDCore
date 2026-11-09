using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// A writable binding that remembers whether the value it holds is <em>known</em>.
/// </summary>
/// <remarks>
/// <para>
/// Whether a value is known is a property of the value (<see cref="Abstract.VBTypedValue.IsIndeterminate"/>), and a value read
/// from a variable is bound to the variable's own cell. For an analysis that runs assignments, the cell is where the answer has
/// to be kept: a variable that holds a value the analysis does not know is still assigned a known one later, and the other way
/// around. A cell that is not known holds the value its type assumes in its place; see <see cref="IndeterminateBindingHandle"/>.
/// </para>
/// <para>
/// A cell is known unless it is told otherwise, so a session that runs code rather than analyzes it is unchanged by any of this.
/// Writing through <see cref="IBindingHandle.SetValue"/> states a known value; a write that may be of an unknown one has to say so
/// (<see cref="BindingKnowledge.Store(IBindingHandle, RDCore.SDK.Runtime.Abstract.Execution.IRuntimeSymbolResolver, Abstract.VBTypedValue)"/>).
/// </para>
/// </remarks>
public interface IKnowledgeBinding : IBindingHandle
{
    /// <summary>
    /// Whether the value held is known. When it is not, the value held is the one its type assumes.
    /// </summary>
    bool IsKnown { get; }

    /// <summary>
    /// Replaces the value held, and says whether the new value is known.
    /// </summary>
    /// <param name="value">The new value, or the value assumed in place of one that is not known.</param>
    /// <param name="isKnown">Whether <paramref name="value"/> is known.</param>
    void Store(IRuntimeValue value, bool isKnown);

    /// <summary>
    /// Makes the value held not known, keeping it as the value assumed in its place.
    /// </summary>
    void Forget();
}
