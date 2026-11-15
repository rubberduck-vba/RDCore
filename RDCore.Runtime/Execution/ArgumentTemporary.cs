using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.Execution;

/// <summary>
/// The temporary a value is passed in to a library member's <c>As Any</c> parameter (<c>VarPtr(1)</c>), held as a temporary of the statement that passes
/// it (<see cref="ISessionObjects.PushTemporary"/>): the address is freed when the statement is over (<see cref="ObjectReferences.ReleaseTemporaries"/>).
/// </summary>
/// <remarks>
/// As in <strong>MS-VBA</strong>, two temporaries of one statement are at two addresses: <c>VarPtr(1) = VarPtr(1)</c> is <c>False</c> (observed in Excel).
/// It holds no value of its own - what is at the address does - so a statement that lets go of it lets go of no object.
/// </remarks>
/// <param name="Address">The address of the temporary.</param>
internal sealed record class ArgumentTemporary(MemoryAddress Address) : IBindingHandle
{
    /// <inheritdoc/>
    public IRuntimeValue Value => VBEmptyValue.Empty.RuntimeValue;

    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => BindingCapabilities.None;

    /// <inheritdoc/>
    public IRuntimeValue GetValue(IRuntimeSymbolResolver resolver) => Value;

    /// <inheritdoc/>
    public void SetValue(IRuntimeSymbolResolver resolver, IRuntimeValue value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public IRuntimeValue Invoke(IRuntimeSymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
