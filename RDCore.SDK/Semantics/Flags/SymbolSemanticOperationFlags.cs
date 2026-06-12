namespace RDCore.SDK.Semantics.Flags
{
    [Flags]
    /// <summary>
    /// The semantic flags associated with a <c>SymbolSemanticOperation</c>.
    /// </summary>
    public enum SymbolSemanticOperationFlags
    {
        /// <summary>
        /// A new symbol was successfully defined.
        /// </summary>
        Completed = 1 << 0,
        /// <summary>
        /// The new symbol was created, but failed a case-sensitive name table lookup.
        /// </summary>
        /// <remarks>
        /// In MS-VBA, this causes a diff across the entire project space, recasing the source code to match the name table identifier.
        /// </remarks>
        NameTableMismatch = 1 << 1,
    }
}