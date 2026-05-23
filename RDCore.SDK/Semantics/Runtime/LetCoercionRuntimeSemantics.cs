using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime;

/// <summary>
/// MS-VBAL 5.5.1.2 Let-coercion (runtime semantics)
/// </summary>
public record class LetCoercionRuntimeSemantics : RuntimeSemantics
{
    private static readonly Lazy<LetCoercionRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static RuntimeSemantics Instance => _instance.Value;

    private static LetCoercionRuntimeSemantics? GetSemantics(VBTypedValue value) => 
        value.TypeInfo switch
        {
            INumericType => LetCoercionVBNumericTypeRuntimeSemantics.Instance,
            VBBooleanType => LetCoercionVBBooleanRuntimeSemantics.Instance,
            VBDateType => LetCoercionVBDateRuntimeSemantics.Instance,
            VBFixedStringType => LetCoercionVBFixedStringRuntimeSemantics.Instance,
            VBStringType => LetCoercionVBStringRuntimeSemantics.Instance,
            VBResizableByteArrayType => LetCoercionVBResizableByteArrayRuntimeSemantics.Instance,
            VBResizableArrayType => LetCoercionVBResizableArrayRuntimeSemantics.Instance,
            VBUserDefinedType => LetCoercionVBUserDefinedTypeRuntimeSemantics.Instance,
            VBErrorType => LetCoercionVBErrorTypeRuntimeSemantics.Instance,
            VBNullType => LetCoercionVBNullTypeRuntimeSemantics.Instance,
            VBVariantType => LetCoercionVBVariantTypeRuntimeSemantics.Instance,
            VBObjectType => LetCoercionVBObjectRuntimeSemantics.Instance,

            _ => default
        };

    /// <summary>
    /// Evaluates the let-coerced <c>VBTypedValue</c> for the specified <c>effectiveType</c> in the context of the specified <c>expression</c>.
    /// </summary>
    /// <param name="context">An execution context and memory space to operate in.</param>
    /// <param name="expression">The <c>BoundExpression</c> that is being evaluated.</param>
    /// <param name="effectiveType">The semantically determined <em>effective type</em> of the operation.</param>
    /// <param name="value">The value to let-coerce into the <em>effective type</em>.</param>
    /// <returns><c>null</c> if no value could be determined, but exceptions are specified where appropriate.</returns>
    /// <exception cref="VBRuntimeErrorException"></exception>
    public static VBTypedValue? Evaluate(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        var semantics = GetSemantics(value);
        return semantics?.EvaluateLetCoercion(context, expression, effectiveType, value);
    }

    /// <summary>
    /// Evaluates the let-coerced <c>VBTypedValue</c> for the specified <c>effectiveType</c> in the context of the specified <c>expression</c>.
    /// </summary>
    /// <param name="context">An execution context and memory space to operate in.</param>
    /// <param name="expression">The <c>BoundExpression</c> that is being evaluated.</param>
    /// <param name="effectiveType">The semantically determined <em>effective type</em> of the operation.</param>
    /// <param name="value">The value to let-coerce into the <em>effective type</em>.</param>
    /// <returns><c>null</c> if no value could be determined, but exceptions are specified where appropriate.</returns>
    /// <exception cref="VBRuntimeErrorException"></exception>
    protected virtual VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value) => default;

    public override VBType? DetermineEffectiveType(IVBExecutionContext context, params VBType[] operandDeclaredTypes) => operandDeclaredTypes[0];

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue[] operands) => operands[0];
}

/// <summary>
/// MS-VBAL 5.5.1.2.1 Let-coercion between numeric types
/// </summary>
public sealed record class LetCoercionVBNumericTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBNumericTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBNumericTypeRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
        /* //numeric let-coercion to string
        public virtual VBStringValue AsCoercedString(ref int depth)
        {
            var isNegative = NumericValue < 0;
            var sign = isNegative ? "-" : string.Empty;

            var absoluteValue = Math.Abs(NumericValue);
            var stringValue = absoluteValue.ToString(CultureInfo);

            var dot = CultureInfo.NumberFormat.NumberDecimalSeparator;
            var decimalIndex = stringValue.IndexOf(dot);

            if (decimalIndex >= 0)
            {
                var integerString = stringValue[..(decimalIndex - 1)];
                // MS-VBAL 5.5.1.2.4 Let-coercion to and from String

                // VBSingleValue uses normal notation for values up to 7 integer digits, scientific notation otherwise:
                if (this is VBSingleValue && integerString.Length > VBSingleType.SignificantIntegerDigits)
                {
                    var significantIntegerDigits = VBSingleType.SignificantIntegerDigits;
                    stringValue = ToVBScientificNotation(NumericValue, significantIntegerDigits, dot);

                }
                else if (integerString.Length > VBDoubleType.SignificantIntegerDigits)
                {
                    // Double (or any other numeric type for that matter): truncate to 15 significant digits
                    var significantIntegerDigits = VBDoubleType.SignificantIntegerDigits;
                    stringValue = ToVBScientificNotation(NumericValue, significantIntegerDigits, dot);
                }
                else
                {
                    // we're taking a small shortcut above by presuming of the equality of two constants:
                    Debug.Assert(VBDoubleType.SignificantIntegerDigits == VBNumericTypedValue.SignificantIntegerDigits);
                }
            }
            else
            {
                stringValue = $"{sign}{stringValue}";
            }

            return new VBStringValue(Symbol).WithValue(stringValue);
        }

        private static string ToVBScientificNotation(double value, int significantIntegerDigits, string decimalSeparator)
        {
            var absoluteValue = Math.Abs(value);
            var sign = value < 0 ? "-" : string.Empty;

            var stringValue = absoluteValue.ToString(CultureInfo);
            var decimalIndex = stringValue.IndexOf(decimalSeparator);

            var integerString = stringValue[..(decimalIndex - 1)];
            var decimalString = stringValue[(decimalIndex + 1)..];

            // s * 10^e
            char s;
            int e;
            if (absoluteValue >= 1)
            {
                s = integerString[0];
                e = decimalIndex; // magnitude is just where the decimal separator is at (positive)
            }
            else
            {
                // s is the first non-zero digit
                var nzIndex = decimalString.IndexOfAny(['1', '2', '3', '4', '5', '6', '7', '8', '9']);
                s = decimalString[nzIndex];
                e = -(nzIndex + 1); // magnitude is the (negative) number of decimal positions shifted
            }

            // combined integer+decimal parts cannot exceed a length of 15:
            decimalString = $"{integerString[1..]}{decimalString}";
            var fullValue = $"{integerString}{decimalSeparator}{decimalString}";

            var fullValueLength = fullValue.Length;
            decimalString = fullValueLength > significantIntegerDigits
                ? decimalString[..significantIntegerDigits]
                : decimalString;

            return $"{sign}{s}{decimalSeparator}{decimalString}E{e}";
        }
        */
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.2 Let-coercion to and from <c>VBBooleanType</c>
/// </summary>
public record class LetCoercionVBBooleanRuntimeSemantics : LetCoercionRuntimeSemantics
{
    private static readonly Lazy<LetCoercionVBBooleanRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBBooleanRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.3 Let-coercion to and from <c>VBDateType</c>
/// </summary>
public record class LetCoercionVBDateRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBDateRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBDateRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.4 Let-coercion to and from <c>VBStringType</c>
/// </summary>
public record class LetCoercionVBStringRuntimeSemantics : LetCoercionRuntimeSemantics
{
    private static readonly Lazy<LetCoercionVBStringRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBStringRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.5 Let-coercion to and from <c>VBFixedStringType</c>
/// </summary>
public record class LetCoercionVBFixedStringRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBFixedStringRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBFixedStringRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.6 Let-coercion to and from <c>VBResizableByteArray</c>
/// </summary>
public record class LetCoercionVBResizableByteArrayRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBResizableByteArrayRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBResizableByteArrayRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.7 Let-coercion to and from <c>VBResizableArray</c>
/// </summary>
public record class LetCoercionVBResizableArrayRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBResizableArrayRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBResizableArrayRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.8 Let-coercion to and from <c>VBUserDefinedType</c>
/// </summary>
public record class LetCoercionVBUserDefinedTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBUserDefinedTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBUserDefinedTypeRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.9 Let-coercion to and from <c>VBErrorType</c>
/// </summary>
public record class LetCoercionVBErrorTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBErrorTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBErrorTypeRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.10 Let-coercion to and from <c>VBNullType</c>
/// </summary>
public record class LetCoercionVBNullTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBNullTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBNullTypeRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.11 Let-coercion to and from <c>VBEmptyType</c>
/// </summary>
public record class LetCoercionVBEmptyTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBEmptyTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBEmptyTypeRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.12 Let-coercion to and from <c>VBVariantType</c>
/// </summary>
public record class LetCoercionVBVariantTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBVariantTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBVariantTypeRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.13 Let-coercion to and from <c>VBObjectValue</c>
/// </summary>
public record class LetCoercionVBObjectRuntimeSemantics : LetCoercionRuntimeSemantics
{
    private static readonly Lazy<LetCoercionVBObjectRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public new static LetCoercionVBObjectRuntimeSemantics Instance => _instance.Value;

    protected sealed override VBTypedValue? EvaluateLetCoercion(IVBExecutionContext context, BoundExpression expression, VBType effectiveType, VBTypedValue value)
    {
        return base.EvaluateLetCoercion(context, expression, effectiveType, value);
    }
}

