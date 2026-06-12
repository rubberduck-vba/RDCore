namespace RDCore.SDK.Model.AST.Directives
{
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
        /// </remarks>
        public bool IsMatch(string identifierName) 
            => Enumerable.Range(FromCharValue, (ToCharValue ?? FromCharValue) - FromCharValue + 1).Any(ascii => identifierName[0] == ascii);
    }
}
