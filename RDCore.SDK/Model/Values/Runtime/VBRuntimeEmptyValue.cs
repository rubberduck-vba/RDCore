namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// The runtime value of <c>Empty</c>: a <c>Variant</c> of the <c>VT_EMPTY</c> subtype - it has not been initialized, and holds nothing.
/// </summary>
/// <remarks>
/// 👉 That <c>Empty</c> behaves as <c>0</c> where a number is expected and as a zero-length string where a string is expected is a
/// let-coercion rule of the semantic layer (<see cref="VBEmptyValue"/>); the runtime does not turn it into either.
/// </remarks>
public readonly struct VBRuntimeEmptyValue : IRuntimeValue, IEquatable<VBRuntimeEmptyValue>
{
    /// <inheritdoc/>
    public object BoxedValue => this;

    /// <summary>
    /// Every value of this type is the same one: there is nothing in it to differ by.
    /// </summary>
    public bool Equals(VBRuntimeEmptyValue other) => true;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is VBRuntimeEmptyValue;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    /// <inheritdoc/>
    public override string ToString() => "Empty";
}
