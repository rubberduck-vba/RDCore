namespace RDCore.SDK.Model.Symbols.Abstract;

/// <summary>
/// Uniquely identifies a <see cref="Symbol"/>, safe for use as a dictionary key or set member.
/// </summary>
/// <remarks>
/// Wraps a symbol's declaring <see cref="Symbol.Uri"/>, comparing <see cref="System.Uri.AbsoluteUri"/>
/// ordinally instead of deferring to <see cref="System.Uri"/>'s own <c>Equals</c>/<c>GetHashCode</c>:
/// those deliberately ignore <see cref="System.Uri.Fragment"/>, but a symbol's discriminating name and
/// location (module, member, nesting) is encoded entirely in the fragment — two distinct symbols
/// sharing a workspace root would otherwise compare equal.
/// </remarks>
/// <param name="Uri">The identified symbol's <see cref="Symbol.Uri"/>.</param>
public readonly record struct SemanticId(Uri Uri) : IEquatable<SemanticId>
{
    public bool Equals(SemanticId other) => string.Equals(Uri.AbsoluteUri, other.Uri.AbsoluteUri, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Uri.AbsoluteUri);
    public override string ToString() => Uri.AbsoluteUri;
}
