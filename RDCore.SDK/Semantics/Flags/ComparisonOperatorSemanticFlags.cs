namespace RDCore.SDK.Semantics.Flags
{
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

        All = IntegralNumericEffectiveType | FixedPointNumericEffectiveType 
            | FloatingPointNumericEffectiveType | HasNaNOperand 
            | BooleanEffectiveType
            | StringEffectiveType | StringComparisonBinary | StringComparisonText 
            | NullEffectiveType 
            | ErrorEffectiveType | HasStandardErrorCodes 
            | IsVariantStringNumericException
    }
}
