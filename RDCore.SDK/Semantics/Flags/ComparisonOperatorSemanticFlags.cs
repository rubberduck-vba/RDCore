namespace RDCore.SDK.Semantics.Flags;

/// <summary>
/// The semantic flags of a <em>relational operator</em> operation (MS-VBAL 5.6.9.5).
/// </summary>
[Flags]
public enum ComparisonOperatorSemanticFlags
{
    /// <summary>
    /// The semantic <em>effective type</em> of the comparison operation is an <em>integral numeric data type</em>.
    /// </summary>
    IntegralNumericEffectiveType = 1 << 0,
    /// <summary>
    /// The semantic <em>effective type</em> of the comparison operation is <c>VBCurrencyType</c> or <c>VBDecimalType</c>.
    /// </summary>
    FixedPointNumericEffectiveType = 1 << 1,
    /// <summary>
    /// The semantic <em>effective type</em> of the comparison operation is <c>VBSingleType</c> or <c>VBDoubleType</c>.
    /// </summary>
    FloatingPointNumericEffectiveType = 1 << 2,
    /// <summary>
    /// The operation involves one or more <c>NaN</c> floating-point values.
    /// </summary>
    HasNaNOperand = 1 << 3,
    /// <summary>
    /// The semantic <em>effective type</em> of the comparison operation is <c>VBBooleanType</c>.
    /// </summary>
    BooleanEffectiveType = 1 << 4,
    /// <summary>
    /// The semantic <em>effective type</em> of the comparison operation is <c>VBByteType</c>.
    /// </summary>
    ByteEffectiveType = 1 << 5,
    IntegerEffectiveType = 1 << 6,
    LongEffectiveType = 1 << 7,
    LongLongEffectiveType = 1 << 8,
    SingleEffectiveType = 1 << 9,
    DoubleEffectiveType = 1 << 10,
    CurrencyEffectiveType = 1 << 11,
    DecimalEffectiveType = 1 << 12,
    /// <summary>
    /// The semantic <em>effective type</em> of the comparison operation is <c>VBStringType</c>.
    /// </summary>
    StringEffectiveType = 1 << 13,
    /// <summary>
    /// A string comparison is made in a case-sensitive manner by comparing the byte values of each string.
    /// </summary>
    StringComparisonBinary = 1 << 14,
    /// <summary>
    /// A string comparison is made in a case-insentive manner according to the platform's host-defined regional settings.
    /// </summary>
    StringComparisonText = 1 << 15,
    /// <summary>
    /// The semantic <em>effective type</em> of the comparison operation is <c>VBNullType</c>.
    /// </summary>
    NullEffectiveType = 1 << 16,
    /// <summary>
    /// The semantic <em>effective type</em> of the comparison operation is <c>VBErrorType</c>.
    /// </summary>
    ErrorEffectiveType = 1 << 17,
    /// <summary>
    /// <c>true</c> if both <c>Error</c> values are between <c>0</c> and <c>65535</c> (<em>standard error codes</em> range).
    /// </summary>
    /// <remarks>
    /// 👉 The result of a comparison operation where the effective type is <c>VBErrorType</c> is otherwise undefined.
    /// </remarks>
    HasStandardErrorCodes = 1 << 18,
    /// <summary>
    /// <c>true</c> if both operands are <c>VBVariant</c> with one having a <c>VBStringType</c> subtype and the other any <c>VBNumericType</c>.
    /// </summary>
    /// <remarks>
    /// 👉 In such cases the <em>numeric</em> <c>VBVariant</c> operand <strong>is always considered <em>less than</em></strong> the <em>string</em> <c>VBVariant</c> operand.
    /// </remarks>
    IsVariantStringNumericException = 1 << 19,
    /// <summary>
    /// <c>true</c> if one operand is a <c>VBVariant</c> holding a number, a <c>Date</c> or a <c>Boolean</c>, and the other a <c>String</c> that is no
    /// <c>VBVariant</c> - a variable declared <c>As String</c>, a literal: the two are compared as text, as MS-VBA compares them.
    /// </summary>
    /// <remarks>
    /// 👉 <strong>MS-VBAL §5.6.9.5</strong> makes the effective type the number's, which makes a <c>String</c> that is no number a type mismatch (13);
    /// MS-VBA compares the text of the <c>Variant</c>'s value with the <c>String</c>, as Microsoft Learn documents it (RD-VBAL §5.6.9.5).
    /// </remarks>
    IsVariantComparedWithStringAsText = 1 << 20,
    /// <summary>
    /// <c>true</c> if the pattern of a <c>Like</c> operation is written in the code, and has a <c>[]</c> or a <c>[!]</c>.
    /// </summary>
    /// <remarks>
    /// 👉 <strong>MS-VBAL §5.6.9.6</strong> makes either a list of no character; MS-VBA matches <c>[]</c> with no character at all, and <c>[!]</c>
    /// with a <c>!</c> (RD-VBAL §5.6.9.6).
    /// </remarks>
    LikePatternHasEmptyCharList = 1 << 21,
    /// <summary>
    /// <c>true</c> if the pattern of a <c>Like</c> operation is written in the code, and is not a valid <c>like-pattern</c> in the comparison mode of
    /// the operation: it has an unterminated list, or a range whose first character is above its last.
    /// </summary>
    /// <remarks>
    /// 👉 <strong>MS-VBAL §5.6.9.6</strong> raises error 93 (<em>Invalid pattern string</em>) for such a pattern; MS-VBA raises it only where a match
    /// reaches the invalid element (RD-VBAL §5.6.9.6).
    /// </remarks>
    LikePatternIsInvalid = 1 << 22,

    /// <summary>
    /// Combines all values.
    /// </summary>
    All = IntegralNumericEffectiveType | FixedPointNumericEffectiveType
        | FloatingPointNumericEffectiveType | HasNaNOperand
        | BooleanEffectiveType | ByteEffectiveType | IntegerEffectiveType | LongEffectiveType | LongLongEffectiveType
        | SingleEffectiveType | DoubleEffectiveType | CurrencyEffectiveType | DecimalEffectiveType
        | StringEffectiveType | StringComparisonBinary | StringComparisonText 
        | NullEffectiveType 
        | ErrorEffectiveType | HasStandardErrorCodes 
        | IsVariantStringNumericException | IsVariantComparedWithStringAsText
        | LikePatternHasEmptyCharList | LikePatternIsInvalid
}
