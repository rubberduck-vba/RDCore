namespace RDCore.SDK.Semantics.Flags
{
    [Flags]
    /// <summary>
    /// The semantic flags associated with <c>StringTokenParsingSemanticOperation</c>.
    /// </summary>
    public enum StringTokenSemanticFlags
    {
        /// <summary>
        /// A <em>number token</em> was successfully ingested as a numeric literal expression symbol in the abstract syntax tree (AST).
        /// </summary>
        Completed = 1 << 0,
        /// <summary>
        /// The abstract syntax tree (AST) at the specified <c>Uri</c> contains invalid symbols and error nodes.
        /// </summary>
        SyntaxError = 1 << 1,
        /// <summary>
        /// The <em>string token</em> has a length of zero (empty string).
        /// </summary>
        /// <remarks>
        /// The <em>data value</em> of the string is the <em>zero-length empty string</em>, a statically allocated symbol (<c>GlobalSymbols.StaticSymbols.VBEmptyString</c>).
        /// </remarks>
        ZeroLength = 1 << 2,

        /// <summary>
        /// Combines all values.
        /// </summary>
        All = Completed | SyntaxError | ZeroLength
    }
}
