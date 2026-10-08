namespace RDCore.SDK.Model.AST.Directives;

/// <summary>
/// The <c>A-Z</c> letter span of a <c>Def&lt;Type&gt;</c> directive (MS-VBAL 5.2.2): the one that covers every name of the module, whatever its first character.
/// </summary>
public record class DefTypeUniversalPrefixMapping() : DefTypePrefixMapping('A', 'Z')
{
    /// <inheritdoc/>
    public override bool IsMatch(string identifierName) => !string.IsNullOrEmpty(identifierName);
}

/// <summary>
/// Maps a range of prefix characters to a <c>Def&lt;Type&gt;</c> directive.
/// </summary>
/// <param name="FromCharValue">The first character in the prefix mapping range.</param>
/// <param name="ToCharValue">The last character in the prefix mapping range. Matches the <c>FromCharValue</c> if unspecified.</param>
public record class DefTypePrefixMapping(char FromCharValue, char? ToCharValue = default) 
{
    /// <summary>
    /// <c>true</c> if the specified <c>identifierName</c> matches this prefix mapping rule.
    /// </summary>
    /// <param name="identifierName">The <em>identifier</em> name to match.</param>
    /// <remarks>
    /// If this method returns <c>true</c>, the associated symbol has the implicit data type defined by the corresponding <c>Def&lt;Type&gt;</c> directive.
    /// <para>
    /// The span is of letters, whatever their case, and it can be ascending or descending (MS-VBAL 5.2.2): <c>DefInt N-I</c> covers the same names as <c>DefInt I-N</c>.
    /// </para>
    /// </remarks>
    public virtual bool IsMatch(string identifierName)
    {
        if (string.IsNullOrEmpty(identifierName))
        {
            return false;
        }

        var first = char.ToUpperInvariant(identifierName[0]);
        var (from, to) = (char.ToUpperInvariant(FromCharValue), char.ToUpperInvariant(ToCharValue ?? FromCharValue));
        return first >= Math.Min(from, to) && first <= Math.Max(from, to);
    }
}
