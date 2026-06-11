using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using System.Collections.Immutable;
using System.Diagnostics;

namespace RDCore.SDK.Semantics.Runtime.Abstract;

/// <summary>
/// Defines the representation of a single <em>stack frame</em>.
/// </summary>
public interface IStackFrame : IEquatable<IStackFrame>
{
    /// <summary>
    /// Gets the <em>globally unique identifier</em> for this frame.
    /// </summary>
    /// <remarks>
    /// 👉 Each frame gets a different unique identifier, even given identical inputs.
    /// </remarks>
    Guid Id { get; }
    /// <summary>
    /// The <c>SemanticId</c> of the <see cref="BoundNode"/> being evaluated.
    /// </summary>
    Uri NodeUri { get; }
    /// <summary>
    /// The <see cref="StaticSymbol"/> representing the <strong>unallocated</strong>, <em>language-level</em> symbol associated with this operation.
    /// </summary>
    StaticSymbol StaticSymbol { get; }
    /// <summary>
    /// Gets an immutable array containing the ordered <see cref="VBTypedValue"/> inputs of the operation.
    /// </summary>
    /// <remarks>
    /// Operations requiring <see cref="VBType"/> inputs wrap them with a <see cref="VBTypeDescValue"/>.
    /// </remarks>
    ImmutableArray<VBTypedValue> Inputs { get; }

    /// <summary>
    /// Equates a given <see cref="IStackFrame"/> references if it presents the same <c>Id</c>.
    /// </summary>
    /// <param name="other">Possibly an <see cref="IStackFrame"/> reference to be equated with.</param>
    /// <returns><c>true</c> if the two instances have the same <c>Id</c>, <c>false</c> otherwise.</returns>
    bool IEquatable<IStackFrame>.Equals(IStackFrame? other) => Id.Equals(other?.Id);
}

/// <summary>
/// An <see cref="IStackFrame"/> that exposes an <em>indexer</em> to retrieve its inputs using a <see cref="InputIndex"/> named constant.
/// </summary>
public interface IIndexedStackFrame : IStackFrame
{
    /// <summary>
    /// Gets the input at the specified <see cref="InputIndex"/>.
    /// </summary>
    /// <param name="value"></param>
    /// <remarks>⚠️ An interface-level <em>default implementation</em> returns a <c>null</c> reference.</remarks>
    /// <returns>The <see cref="VBTypedValue"/> that was provided as an input at the specified index, or <c>null</c> if no such value exists.</returns>
    VBTypedValue? this[InputIndex value] => default;
}

/// <summary>
/// A base (<c>abstract</c>) implementation of an <see cref="IIndexedStackFrame"/>.
/// </summary>
public abstract record class IndexedStackFrame : IIndexedStackFrame
{
    private readonly Dictionary<InputIndex, VBTypedValue> _map;

    public IndexedStackFrame(Uri nodeUri, StaticSymbol staticSymbol, IEnumerable<(InputIndex Index, VBTypedValue Input)> inputs)
    {
        NodeUri = nodeUri;
        StaticSymbol = staticSymbol;
        Inputs = [.. inputs.Select(input => input.Input)];

        if (Inputs.Length > 0)
        {
            Debug.Assert(inputs.Select(input => (int)input.Index).Order().SequenceEqual(Enumerable.Sequence(0, Inputs.Length - 1, 1)));
        }
        _map = inputs.ToDictionary(e => e.Index, e => e.Input);
    }

    public Guid Id { get; } = Guid.NewGuid();
    public Uri NodeUri { get; }
    public StaticSymbol StaticSymbol { get; }
    public ImmutableArray<VBTypedValue> Inputs { get; }

    /// <summary>
    /// Gets the input at the specified <see cref="InputIndex"/>.
    /// </summary>
    /// <returns>The <see cref="VBTypedValue"/> that was provided as an input at the specified index, or <c>null</c> if no such value exists.</returns>
    public VBTypedValue? this[InputIndex index] => _map.TryGetValue(index, out var input) ? input : null;
}
