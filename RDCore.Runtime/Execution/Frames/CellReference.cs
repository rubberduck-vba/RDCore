using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Execution.Frames;

/// <summary>
/// The argument of a <c>ByRef</c> parameter that is a variable held in a cell of the caller's activation: the cell itself, which the
/// parameter's slot holds too, so that the parameter is a second name for the variable (<strong>MS-VBAL §5.3.1.11</strong>) without the variable being
/// given an address.
/// </summary>
/// <remarks>
/// What a <see cref="VBRuntimeReference"/> is with an address, this is with a cell: only the procedure invoker reads it, to lay the parameter out.
/// </remarks>
/// <param name="Cell">The cell of the caller's variable.</param>
/// <param name="ReferencedType">
/// The declared type of the caller's variable when the parameter, a <c>Variant</c>, does not have it (<see cref="CallStackFrame.TryGetReferencedType"/>);
/// <see langword="null"/> when it does.
/// </param>
internal sealed record class CellReference(IBindingHandle Cell, SDK.Model.Types.Abstract.VBType? ReferencedType = null) : IRuntimeValue
{
    /// <inheritdoc/>
    public object BoxedValue => Cell.Value.BoxedValue;
}
