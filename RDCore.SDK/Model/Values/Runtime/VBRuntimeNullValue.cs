namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// The runtime value of <c>Null</c>: a <c>Variant</c> of the <c>VT_NULL</c> subtype - it holds no valid data, and says so.
/// </summary>
/// <remarks>
/// 👉 What <c>Null</c> <em>means</em> - that it propagates through an expression, that it is not an error - is the semantic layer's
/// (<see cref="VBNullValue"/>); this is what the runtime has in memory for it, which is nothing but the tag.
/// </remarks>
public readonly struct VBRuntimeNullValue : IRuntimeValue, IEquatable<VBRuntimeNullValue>
{
    /// <inheritdoc/>
    public object BoxedValue => this;

    /// <summary>
    /// Every value of this type is the same one: there is nothing in it to differ by.
    /// </summary>
    public bool Equals(VBRuntimeNullValue other) => true;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is VBRuntimeNullValue;

    /// <inheritdoc/>
    public override int GetHashCode() => 0;

    /// <inheritdoc/>
    public override string ToString() => "Null";
}
