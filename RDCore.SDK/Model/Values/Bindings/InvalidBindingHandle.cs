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

    // the record's own printing would read Value: a value that holds this handle must still be printable (a debugger, a test name, a log).
    protected virtual bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Invalid");
        return true;
    }

    public IRuntimeValue GetValue(ISymbolResolver resolver) => throw new NotSupportedException();

    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();

    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => throw new NotSupportedException();
}