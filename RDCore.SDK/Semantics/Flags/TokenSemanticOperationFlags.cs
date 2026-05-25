namespace RDCore.SDK.Semantics.Flags;

[Flags]
/// <summary>
/// The semantic flags associated with a <c>TokenSemanticOperation</c>.
/// </summary>
public enum TokenSemanticOperationFlags
{
    /// <summary>
    /// A complete and valid abstract syntax tree (AST) was successfully generated for the symbol at the specified <c>Uri</c>.
    /// </summary>
    Completed = 1 << 0,
    /// <summary>
    /// The abstract syntax tree (AST) at the specified <c>Uri</c> contains invalid symbols and error nodes.
    /// </summary>
    SyntaxError = 1 << 1,

    /// <summary>
    /// Combines all values.
    /// </summary>
    All = Completed | SyntaxError
}
