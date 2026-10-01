namespace RDCore.SDK.Model.Values.Runtime;

/// <summary>
/// Represents a <em>runtime object</em>, an <em>instance</em> of a class definition.
/// </summary>
public readonly record struct VBRuntimeObjectId() : IEquatable<VBRuntimeObjectId>
{
    private readonly Guid _guid = Guid.NewGuid();

    public bool Equals(VBRuntimeObjectId other) => _guid.Equals(other._guid);
    public override int GetHashCode() => _guid.GetHashCode();
    public override string ToString() => _guid.ToString();

    /// <summary>
    /// The identity as a number: <c>0</c> for <c>Nothing</c>, and for any object one that no other has - a pointer's worth of the
    /// identifier, which is as unique as the identifier is unlikely to repeat in it.
    /// </summary>
    public long Identity => BitConverter.ToInt64(_guid.ToByteArray(), 0);
}
