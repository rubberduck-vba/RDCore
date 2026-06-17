namespace RDCore.SDK.Semantics.Flags;

[Flags]
/// <summary>
/// The semantic flags associated with <c>NumberTokenParsingSemanticOperation</c>.
/// </summary>
public enum NumberTokenSemanticFlags
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
    /// The <em>number token</em> expresses a value that cannot be represented with a valid integer <c>VBTypedValue</c> type.
    /// </summary>
    InvalidInteger = 1 << 2,
    /// <summary>
    /// The <em>number token</em> is expressed in <em>decimal notation</em>.
    /// </summary>
    DecimalNotation = 1 << 3,
    /// <summary>
    /// The <em>number token</em> is expressed in <em>octal notation</em>.
    /// </summary>
    OctalNotation = 1 << 4,
    /// <summary>
    /// The <em>number token</em> is expressed in <em>octal notation</em>.
    /// </summary>
    HexadecimalNotation = 1 << 5,
    /// <summary>
    /// The data type of the <em>number token</em> is semantically determined by a <c>type-suffix</c> token.
    /// </summary>
    TypeSuffix = 1 << 6,
    /// <summary>
    /// The data type of the <em>number token</em> is statically invalid in an environment that does not support 64-bit arithmetics.
    /// </summary>
    Unsupported64Bit = 1<< 7,
    /// <summary>
    /// The <em>number token</em> expresses a value that cannot be represented with a valid floating-point <c>VBTypedValue</c> type.
    /// </summary>
    InvalidFloat = 1 << 8,
    /// <summary>
    /// A <em>number token</em> with a <c>Currency</c> declared type was rounded to 4 significant digits using the <em>Banker's Rounding</em> algorithm.
    /// </summary>
    /// <remarks>
    /// See <strong>MS-VBAL 5.5.1.2.1.1</strong> Banker's Rounding.
    /// </remarks>
    BankersRounding = 1 << 9,

    /// <summary>
    /// Combines all values.
    /// </summary>
    All = Completed | SyntaxError | InvalidInteger | DecimalNotation | OctalNotation | HexadecimalNotation | TypeSuffix 
        | Unsupported64Bit | InvalidFloat | BankersRounding
}
