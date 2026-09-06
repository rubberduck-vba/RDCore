using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
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

    public IRuntimeValue Value => throw new NotSupportedException("The binding is not valid.");

    public IRuntimeValue GetValue(IVBExecutionContext context) => throw new NotSupportedException();

    public IRuntimeValue Invoke(IVBExecutionContext context, IRuntimeValue[] args) => throw new NotSupportedException();

    public void SetValue(IVBExecutionContext context, IRuntimeValue value) => throw new NotSupportedException();
}