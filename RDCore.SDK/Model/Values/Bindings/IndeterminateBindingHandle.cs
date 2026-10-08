using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// A binding to a value that is not known: what an expression yields when it is analyzed rather than run, and its operands are not constant.
/// </summary>
/// <remarks>
/// The binding yields the value it is given to assume, so that a semantics that reads its operands' values evaluates as it would for any value of the
/// same type. The assumed value is a stand-in, not a fact: whatever is derived from an indeterminate value is indeterminate too, and nothing an
/// evaluation raises because of an assumed value is known to happen (<see cref="VBTypedValue.IsIndeterminate"/>).
/// <para>
/// An indeterminate value is never assigned: the binding refuses <see cref="BindingCapabilities.SetValue"/>.
/// </para>
/// </remarks>
public sealed record class IndeterminateBindingHandle : IBindingHandle
{
    /// <summary>
    /// Creates a binding to a value that is not known, which yields the value of <paramref name="assumed"/>.
    /// </summary>
    /// <param name="assumed">The binding to the value assumed in place of the value that is not known.</param>
    public IndeterminateBindingHandle(IBindingHandle assumed)
    {
        Assumed = assumed is IndeterminateBindingHandle indeterminate ? indeterminate.Assumed : assumed;
    }

    /// <summary>
    /// The binding to the value assumed in place of the value that is not known.
    /// </summary>
    public IBindingHandle Assumed { get; }

    /// <inheritdoc/>
    public BindingCapabilities BindingCapabilities => Assumed.BindingCapabilities & ~BindingCapabilities.SetValue;

    /// <inheritdoc/>
    public IRuntimeValue Value => Assumed.Value;

    /// <inheritdoc/>
    public IRuntimeValue GetValue(ISymbolResolver resolver) => Assumed.GetValue(resolver);

    /// <inheritdoc/>
    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => Assumed.Invoke(resolver, args);

    /// <summary>
    /// Always throws: a value that is not known is not assigned.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public void SetValue(ISymbolResolver resolver, IRuntimeValue value)
        => throw new NotSupportedException("An indeterminate value cannot be assigned.");

    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Indeterminate, Assumed = ").Append(Assumed);
        return true;
    }
}
