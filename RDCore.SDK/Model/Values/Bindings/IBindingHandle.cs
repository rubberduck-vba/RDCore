using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
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
    /// Signals a binding's capability to yield a <see cref="IRuntimeValue"/>.
    /// </summary>
    GetValue = 1 << 1,
    /// <summary>
    /// Signals a binding's capability to accept a <see cref="IRuntimeValue"/>.
    /// </summary>
    SetValue = 1 << 2,
    /// <summary>
    /// Signals a binding's capability to invoke a callable entity.
    /// </summary>
    Invoke = 1 << 3,
    /// <summary>
    /// Signals a binding's capability to yield an indexed <see cref="IRuntimeValue"/>.
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
/// <remarks>
/// The value operations take <see cref="ISymbolResolver"/> — the read face over the current
/// execution context — so a handle that is not self-contained (a reference to another address, a
/// property- or member-backed binding) can resolve its value through the same read protocol that
/// produced it. The self-contained literal/value/reference cases ignore it today.
/// </remarks>
public interface IBindingHandle
{
    /// <summary>
    /// Gets the value associated to this handle, resolving through <paramref name="resolver"/> when
    /// the binding is not self-contained.
    /// </summary>
    /// <remarks>
    /// 👉 Verify that the binding supports <see cref="BindingCapabilities.GetValue"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException"></exception>
    IRuntimeValue GetValue(ISymbolResolver resolver);
    /// <summary>
    /// Sets the value associated to this handle.
    /// </summary>
    /// <remarks>
    /// 👉 Verify that the binding supports <see cref="BindingCapabilities.SetValue"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException"></exception>
    void SetValue(ISymbolResolver resolver, IRuntimeValue value);
    /// <summary>
    /// Invokes the callable entity associated to this handle.
    /// </summary>
    /// <remarks>
    /// 👉 Verify that the binding supports <see cref="BindingCapabilities.Invoke"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException"></exception>
    // TODO an invocable binding also needs call-stack access to push a frame — that likely belongs on
    // a dedicated callable abstraction, not on every IBindingHandle.
    IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args);

    /// <summary>
    /// The bound runtime value, read without a resolver.
    /// </summary>
    /// <remarks>
    /// 👉 A direct read for the common literal/value cases. Handles that resolve a value lazily
    /// or via a reference still expose <see cref="GetValue(ISymbolResolver)"/>.
    /// </remarks>
    /// <exception cref="NotSupportedException">The binding has no readable value.</exception>
    IRuntimeValue Value { get; }

    /// <summary>
    /// Indicates the valid members of this binding.
    /// </summary>
    BindingCapabilities BindingCapabilities { get; }
}
