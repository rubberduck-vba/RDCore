using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Interop;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an invalid binding.
/// </summary>
/// <remarks>
/// 💥 All methods throw <see cref="NotSupportedException"/>.
/// </remarks>
public record class InvalidBindingHandle : IBindingHandle
{
    public static InvalidBindingHandle Default { get; } = new();

    public BindingCapabilities BindingCapabilities => BindingCapabilities.None;

    public IManagedInteropValue GetValue(IVBExecutionContext context) => throw new NotSupportedException();

    public IManagedInteropValue Invoke(IVBExecutionContext context, IManagedInteropValue[] args) => throw new NotSupportedException();

    public void SetValue(IVBExecutionContext context, IManagedInteropValue value) => throw new NotSupportedException();
}