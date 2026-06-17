namespace RDCore.SDK.Semantics.Flags;

[Flags]
/// <summary>
/// The semantic flags associated with <c>IdentifierTokenParsingSemanticOperation</c>.
/// </summary>
public enum IdentifierTokenSemanticFlags
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
    /// The <em>identifier token</em> has a representation in the source code that is in a different casing than the corresponding symbol name in the global static context.
    /// </summary>
    CaseMismatch = 1 << 2,
    /// <summary>
    /// The <em>identifier token</em> has a representation in the source code that uses non-Latin identifier semantics (<strong>MS-VBAL 3.3.5.1</strong> Non-Latin Identifiers)
    /// </summary>
    NonLatinIdentifier = 1 << 3,
    /// <summary>
    /// The <em>identifier token</em> has a representation in the source code that uses Japanese identifier semantics (<strong>MS-VBAL 3.3.5.1.1</strong> Japanese Identifiers)
    /// </summary>
    JapaneseIdentifier = 1 << 4,
    /// <summary>
    /// The <em>identifier token</em> has a representation in the source code that uses Korean identifier semantics (<strong>MS-VBAL 3.3.5.1.2</strong> Korean Identifiers)
    /// </summary>
    KoreanIdentifier = 1 << 5,
    /// <summary>
    /// The <em>identifier token</em> has a representation in the source code that uses Simplified Chinese identifier semantics (<strong>MS-VBAL 3.3.5.1.3</strong> Simplified Chinese Identifiers)
    /// </summary>
    SimplifiedChineseIdentifier = 1 << 6,
    /// <summary>
    /// The <em>identifier token</em> has a representation in the source code that uses Traditional Chinese identifier semantics (<strong>MS-VBAL 3.3.5.1.4</strong> Traditional Chinese Identifiers)
    /// </summary>
    TraditionalChineseIdentifier = 1 << 7,
    /// <summary>
    /// The <em>identifier token</em> uses a reserved <c>name-value</c> (<strong>MS-VBAL 3.3.5.2</strong> Reserved Identifiers).
    /// </summary>
    ReservedIdentifier = 1 << 8,
    /// <summary>
    /// The <em>identifier token</em> is a <c>foreign-name</c> token  (<strong>MS-VBAL 3.3.5.3</strong> Special Identifier Forms).
    /// </summary>
    /// <remarks>
    /// Foreign identifiers are only legal when surrounded with [square brackets].
    /// </remarks>
    ForeignIdentifier = 1 << 9,
    /// <summary>
    /// The <em>identifier token</em> is suffixed with a <c>type-suffix</c>.
    /// </summary>
    TypeSuffix = 1 << 10,
    /// <summary>
    /// The <em>identifier token</em> is referring to an intrinsic data type ("built-in") for which a <c>type-suffix</c> is legal.
    /// </summary>
    IsIntrinsicType = 1 << 11,

    /// <summary>
    /// Combines all values.
    /// </summary>
    All = Completed | SyntaxError | CaseMismatch | NonLatinIdentifier | JapaneseIdentifier | KoreanIdentifier | SimplifiedChineseIdentifier | TraditionalChineseIdentifier
        | ReservedIdentifier | ForeignIdentifier | TypeSuffix | IsIntrinsicType
}
