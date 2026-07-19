using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Interop;
using RDCore.SDK.Model.Values.Meta;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Signals the capabilities of a given binding handle.
/// </summary>
[Flags]
public enum BindingCapabilities
{
    /// <summary>
    /// Signals a binding that supports no valid operations.
    /// </summary>
    None = 0,
    /// <summary>
    /// Signals a binding's capability to yield a <see cref="VBMemberDescValue"/>.
    /// </summary>
    GetMember = 1 << 0,
    /// <summary>
    /// Signals a binding's capability to yield a <see cref="IManagedInteropValue"/>.
    /// </summary>
    GetValue = 1 << 1,
    /// <summary>
    /// Signals a binding's capability to accept a <see cref="IManagedInteropValue"/>.
    /// </summary>
    SetValue = 1 << 2,
    /// <summary>
    /// Signals a binding's capability to invoke a callable entity.
    /// </summary>
    Invoke = 1 << 3,
    /// <summary>
    /// Signals a binding's capability to yield an indexed <see cref="IManagedInteropValue"/>.
    /// </summary>
    GetIndex = 1 << 4,
    /// <summary>
    /// Signals a binding's capability to yield an enumerator.
    /// </summary>
    GetEnumerator = 1 << 5
}

/// <summary>
/// A handle to a binding to a runtime entity.
/// </summary>
public interface IBindingHandle
{
    /// <summary>
    /// Gets the value associated to this handle.
    /// </summary>
    /// <remarks>
    /// 👉 Verify that the binding supports <see cref="BindingCapabilities.GetValue"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException"></exception>
    IManagedInteropValue GetValue(IVBExecutionContext context);
    /// <summary>
    /// Sets the value associated to this handle.
    /// </summary>
    /// <remarks>
    /// 👉 Verify that the binding supports <see cref="BindingCapabilities.SetValue"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException"></exception>
    void SetValue(IVBExecutionContext context, IManagedInteropValue value);
    /// <summary>
    /// Invokes the callable entity associated to this handle.
    /// </summary>
    /// <remarks>
    /// 👉 Verify that the binding supports <see cref="BindingCapabilities.SetValue"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException"></exception>
    IManagedInteropValue Invoke(IVBExecutionContext context, IManagedInteropValue[] args);

    /// <summary>
    /// Indicates the valid members of this binding.
    /// </summary>
    BindingCapabilities BindingCapabilities { get; }
}
