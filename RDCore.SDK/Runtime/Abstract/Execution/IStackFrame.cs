using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Meta;
using System.Collections.Immutable;

namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// Defines the representation of a single generic <em>stack frame</em>.
/// </summary>
public interface IStackFrame
{
    /// <summary>
    /// The <c>SemanticId</c> of the <see cref="BoundNode"/> being evaluated.
    /// </summary>
    Uri NodeUri { get; }
    /// <summary>
    /// The <see cref="Model.Symbols.Abstract.StaticSymbol"/> representing the <strong>unallocated</strong>, <em>language-level</em> symbol associated with this operation.
    /// </summary>
    StaticSymbol StaticSymbol { get; }
    /// <summary>
    /// Gets an immutable array containing the ordered <see cref="VBTypedValue"/> inputs of the operation.
    /// </summary>
    /// <remarks>
    /// Operations requiring <see cref="VBType"/> inputs wrap them with a <see cref="VBTypeDescValue"/>.
    /// </remarks>
    ImmutableArray<VBTypedValue> Inputs { get; }
}

/// <summary>
/// Defines the representation of a single generic, indexed <em>stack frame</em>.
/// </summary>
public interface IStackFrame<TInputIndex> : IStackFrame
    where TInputIndex : struct, Enum
{
    VBTypedValue this[TInputIndex value] => Inputs[Convert.ToInt32(value)];
}
