namespace RDCore.SDK.Semantics;

/// <summary>
/// A <em>Builder</em> representing any specific <em>semantic operation</em>.
/// </summary>
/// <param name="IntValue">Any semantic flags attached to this operation, expressed as an <c>int</c>.</param>
public abstract record class SemanticFlagsBuilder()
{
    private readonly Dictionary<Type, int> _state = [];

    public SemanticFlagsBuilder WithFlags<TFlags>(TFlags values) where TFlags : struct, Enum
    {
        var key = typeof(TFlags);
        var intValues = (int)(object)values;
        if (!_state.TryGetValue(key, out var flags))
        {
            flags = intValues;
        }
        var value = flags | intValues;
        _state[key] = value;

        return this;
    }

    /// <summary>
    /// Represents the semantic flags as a typed <c>Enum</c> value.
    /// </summary>
    public TFlags Build<TFlags>() where TFlags : struct, Enum => (TFlags)(object)_state;
}

/// <summary>
/// A <em>Builder</em> representing any specific <em>semantic operation</em>.
/// </summary>
/// <param name="SemanticFlags">Any semantic flags attached to this operation, expressed as an <c>Enum</c>.</param>
/// <typeparam name="TFlags">The <c>Enum</c> type that this builder intends to work with.</typeparam>
public record class SemanticOperationBuilder<TFlags>(TFlags SemanticFlags = default) : SemanticFlagsBuilder()
    where TFlags : struct, Enum
{
    /// <summary>
    /// Appends the specified value to the current semantic flags.
    /// </summary>
    /// <returns>
    /// A new semantic operation builder of the same type, holding the bitwise-or new value.
    /// </returns>
    public SemanticOperationBuilder<TFlags> WithFlags(TFlags flags) => new(AsFlags(AsInt32(SemanticFlags) | AsInt32(flags)));
    /// <summary>
    /// Represents the semantic flags as a typed <c>Enum</c> value.
    /// </summary>
    public TFlags Build() => Build<TFlags>();

    private static int AsInt32(TFlags value) => (int)(object)value;
    private static TFlags AsFlags(int value) => (TFlags)(object)value;
}


[Flags]
/// <summary>
/// The semantic flags associated with <c>DocumentParsingSemanticOperation</c>.
/// </summary>
public enum ParsingSemanticOperationFlags
{
    /// <summary>
    /// A complete abstract syntax tree (AST) was successfully generated for the symbol at the specified <c>Uri</c>.
    /// </summary>
    Completed = 1 << 0,
    /// <summary>
    /// The abstract syntax tree (AST) at the specified <c>Uri</c> contains invalid symbols and error nodes.
    /// </summary>
    SyntaxError = 1 << 1,
}

/// <summary>
/// A <em>semantic operation</em> consisting in the <em>lexing and parsing</em> of the source code for a given <c>SymbolUri</c>, 
/// resulting in an <em>concrete syntax tree</em> (CST) comprised of grammar-specific nodes; this CST is then traversed by the parser to produce an
/// <em>abstract syntax tree</em> (AST) comprised of <c>RDCore.SDK</c> symbol nodes.
/// </summary>
/// <param name="Flags">Any semantic flags associated with the operation.</param>
public sealed record class ParsingSemanticOperation(ParsingSemanticOperationFlags Flags = 0) : SemanticOperationBuilder<ParsingSemanticOperationFlags>(Flags) { }


[Flags]
/// <summary>
/// The semantic flags associated with <c>NumberTokenParsingSemanticOperation</c>.
/// </summary>
public enum NumberTokenSemanticOperationFlags
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
}

/// <summary>
/// A <em>semantic operation</em> consisting in the semantic interpretation of an integer <em>number token</em> as defined in <strong>MS-VBAL 3.3.2 Number Tokens</strong>.
/// </summary>
/// <param name="Flags">Any semantic flags associated with the operation.</param>
public sealed record class IntegerNumberTokenParsingSemanticOperation(NumberTokenSemanticOperationFlags Flags) : SemanticOperationBuilder<NumberTokenSemanticOperationFlags>(Flags) { }

/// <summary>
/// A <em>semantic operation</em> consisting in the semantic interpretation of an floating-point <em>number token</em> as defined in <strong>MS-VBAL 3.3.2 Number Tokens</strong>.
/// </summary>
/// <param name="Flags">Any semantic flags associated with the operation.</param>
public sealed record class FloatNumberTokenParsingSemanticOperation(NumberTokenSemanticOperationFlags Flags) : SemanticOperationBuilder<NumberTokenSemanticOperationFlags>(Flags) { }

[Flags]
/// <summary>
/// The semantic flags associated with <c>DateTokenParsingSemanticOperation</c>.
/// </summary>
public enum DateTokenSemanticFlags
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
    /// The <em>date token</em> expresses a value that cannot be represented with a valid <c>VBDateValue</c>.
    /// </summary>
    InvalidDate = 1 << 2,
    /// <summary>
    /// The <em>date token</em> specifies a <em>time value</em>.
    /// </summary>
    TimeValueSpecified = 1 << 3,
    /// <summary>
    /// The <em>date token</em> specifies a <em>date value</em>.
    /// </summary>
    /// <remarks>
    /// The implicit <em>date value</em> is then interpreted as "1899/12/30", or <c>VBDateType.Zero</c>
    /// </remarks>
    DateValueSpecified = 1 << 4,
    /// <summary>
    /// The <em>date token</em> specifies a <em>month name</em> in its <c>left-date-value</c>.
    /// </summary>
    MonthNameLeft = 1 << 5,
    /// <summary>
    /// The <em>date token</em> specifies a <em>month name</em> in its <c>middle-date-value</c>.
    /// </summary>
    MonthNameMiddle = 1 << 6,
    /// <summary>
    /// The <em>date token</em> specifies a <em>month name</em> in its <c>right-date-value</c>.
    /// </summary>
    MonthNameRight = 1 << 7,
    /// <summary>
    /// The <em>date token</em> specifies the year in its <c>left-date-value</c>.
    /// </summary>
    YearLeft = 1 << 8,
    /// <summary>
    /// The <em>date token</em> specifies the year in its <c>right-date-value</c>.
    /// </summary>
    YearRight = 1 << 9,
    /// <summary>
    /// The <em>date token</em> specifies the month in its <c>left-date-value</c>.
    /// </summary>
    MonthLeft = 1 << 10,
    /// <summary>
    /// The <em>date token</em> specifies the month in its <c>middle-date-value</c>.
    /// </summary>
    MonthMiddle = 1 << 11,
    /// <summary>
    /// The <em>date token</em> specifies the month in its <c>right-date-value</c>.
    /// </summary>
    MonthRight = 1 << 12,
    /// <summary>
    /// The <em>date token</em> specifies the day in its <c>left-date-value</c>.
    /// </summary>
    DayLeft = 1 << 13,
    /// <summary>
    /// The <em>date token</em> specifies the day in its <c>middle-date-value</c>.
    /// </summary>
    DayMiddle = 1 << 14,
    /// <summary>
    /// The <em>date token</em> specifies the day in its <c>right-date-value</c>.
    /// </summary>
    DayRight = 1 << 15,
    /// <summary>
    /// The <em>date token</em> specifies an <c>ampm</c> (AM/PM) element in long form "am/pm".
    /// </summary>
    AmPmLong = 1 << 16,
    /// <summary>
    /// The <em>date token</em> specifies an <c>ampm</c> (AM/PM) element in short form "a/p".
    /// </summary>
    AmPmShort = 1 << 17,
    /// <summary>
    /// The <em>date token</em> specifies a <em>minutes-value</em> in its <c>time-value</c> element.
    /// </summary>
    MinutesValue = 1 << 18,
    /// <summary>
    /// The <em>date token</em> specifies a <em>seconds-value</em> in its <c>time-value</c> element.
    /// </summary>
    SecondsValue = 1 << 19,
}

/// <summary>
/// A <em>semantic operation</em> consisting in the semantic interpretation of <em>date token</em> as defined in <strong>MS-VBAL 3.3.3 Date Tokens</strong>.
/// </summary>
/// <param name="Flags">Any semantic flags associated with the operation.</param>
public sealed record class DateTokenParsingSemanticOperation(DateTokenSemanticFlags Flags) : SemanticOperationBuilder<DateTokenSemanticFlags>(Flags) { }

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
}

/// <summary>
/// A <em>semantic operation</em> consisting in the semantic interpretation of <em>string token</em> as defined in <strong>MS-VBAL 3.3.4 String Tokens</strong>.
/// </summary>
/// <param name="Flags">Any semantic flags associated with the operation.</param>
public sealed record class StringTokenParsingSemanticOperation(StringTokenSemanticFlags Flags) : SemanticOperationBuilder<StringTokenSemanticFlags>(Flags) { }

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
}
/// <summary>
/// A <em>semantic operation</em> consisting in the semantic interpretation of <em>identifier token</em> as defined in <strong>MS-VBAL 3.3.5 Identifier Tokens</strong>.
/// </summary>
/// <param name="Flags">Any semantic flags associated with the operation.</param>
public sealed record class IdentifierTokenParsingSemanticOperation(IdentifierTokenSemanticFlags Flags) : SemanticOperationBuilder<IdentifierTokenSemanticFlags>(Flags) { }