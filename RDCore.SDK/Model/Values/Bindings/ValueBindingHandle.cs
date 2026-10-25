using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Values.Bindings;

/// <summary>
/// Represents a handle to an internally addressed, writable <see cref="IRuntimeValue"/>.
/// </summary>
public record class ValueBindingHandle : IKnowledgeBinding
{
    private IRuntimeValue _value;
    private bool _isKnown = true;

    public ValueBindingHandle(IRuntimeValue value)
    {
        _value = value;
    }

    public IRuntimeValue Value => _value;

    /// <inheritdoc/>
    public bool IsKnown => _isKnown;

    public BindingCapabilities BindingCapabilities => BindingCapabilities.GetValue | BindingCapabilities.SetValue;

    public IRuntimeValue GetValue(ISymbolResolver resolver) => _value;

    /// <remarks>
    /// Stores a value that is known: a write that may be of one that is not says so with <see cref="Store"/>.
    /// </remarks>
    public void SetValue(ISymbolResolver resolver, IRuntimeValue value) => Store(value, isKnown: true);

    /// <inheritdoc/>
    /// <remarks>
    /// A <c>String</c> stored where it already is, <c>s = s</c>, is stored as a copy: a <c>String</c> assigned to a variable is one of its own, whose
    /// characters are elsewhere than the ones it held (<strong>MS-VBA</strong> makes another <c>BSTR</c> for any assignment, recorded in Excel), and what holds
    /// a <c>String</c> is told from what holds another by the very value.
    /// </remarks>
    public void Store(IRuntimeValue value, bool isKnown)
    {
        // vbNullString has no characters to be elsewhere: it is stored as it is.
        _value = ReferenceEquals(value, _value) && value is IRuntimeValue<string> { BoxedValue: not null }
            ? VBRuntimeByteStringValue.FromBytes(VBRuntimeByteStringValue.BytesOf(value))
            : value;
        _isKnown = isKnown;
    }

    /// <inheritdoc/>
    public void Forget() => _isKnown = false;

    public IRuntimeValue Invoke(ISymbolResolver resolver, IRuntimeValue[] args) => throw new NotSupportedException();
}
