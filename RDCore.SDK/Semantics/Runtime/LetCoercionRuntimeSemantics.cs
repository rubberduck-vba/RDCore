using RDCore.SDK.Model.Expressions;
using RDCore.SDK.Model.Expressions.Operators;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types.Intrinsic;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Semantics.Runtime.Abstract;

namespace RDCore.SDK.Semantics.Runtime;

/// <summary>
/// MS-VBAL 5.5.1.2 Let-coercion (runtime semantics)
/// </summary>
public abstract record class LetCoercionRuntimeSemantics : RuntimeSemantics
{
    public static LetCoercionRuntimeSemantics? GetSemantics(VBType sourceType)
    {
        return sourceType switch
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
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.1 Let-coercion between numeric types
/// </summary>
public record class LetCoercionVBNumericTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBNumericTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBNumericTypeRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.2 Let-coercion to and from <c>VBBooleanType</c>
/// </summary>
public record class LetCoercionVBBooleanRuntimeSemantics : LetCoercionRuntimeSemantics
{
    private static readonly Lazy<LetCoercionVBBooleanRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBBooleanRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.3 Let-coercion to and from <c>VBDateType</c>
/// </summary>
public record class LetCoercionVBDateRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBDateRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBDateRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.4 Let-coercion to and from <c>VBStringType</c>
/// </summary>
public record class LetCoercionVBStringRuntimeSemantics : LetCoercionRuntimeSemantics
{
    private static readonly Lazy<LetCoercionVBStringRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBStringRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.5 Let-coercion to and from <c>VBFixedStringType</c>
/// </summary>
public record class LetCoercionVBFixedStringRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBFixedStringRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBFixedStringRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.6 Let-coercion to and from <c>VBResizableByteArray</c>
/// </summary>
public record class LetCoercionVBResizableByteArrayRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBResizableByteArrayRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBResizableByteArrayRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.7 Let-coercion to and from <c>VBResizableArray</c>
/// </summary>
public record class LetCoercionVBResizableArrayRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBResizableArrayRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBResizableArrayRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.8 Let-coercion to and from <c>VBUserDefinedType</c>
/// </summary>
public record class LetCoercionVBUserDefinedTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBUserDefinedTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBUserDefinedTypeRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.9 Let-coercion to and from <c>VBErrorType</c>
/// </summary>
public record class LetCoercionVBErrorTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBErrorTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBErrorTypeRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.10 Let-coercion to and from <c>VBNullType</c>
/// </summary>
public record class LetCoercionVBNullTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBNullTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBNullTypeRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.11 Let-coercion to and from <c>VBEmptyType</c>
/// </summary>
public record class LetCoercionVBEmptyTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBEmptyTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBEmptyTypeRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.12 Let-coercion to and from <c>VBVariantType</c>
/// </summary>
public record class LetCoercionVBVariantTypeRuntimeSemantics : LetCoercionRuntimeSemantics 
{
    private static readonly Lazy<LetCoercionVBVariantTypeRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBVariantTypeRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// MS-VBAL 5.5.1.2.13 Let-coercion to and from <c>VBObjectValue</c>
/// </summary>
public record class LetCoercionVBObjectRuntimeSemantics : LetCoercionRuntimeSemantics
{
    private static readonly Lazy<LetCoercionVBObjectRuntimeSemantics> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    public static LetCoercionVBObjectRuntimeSemantics Instance => _instance.Value;

    public override VBType? DetermineEffectiveType(params VBType[] operandDeclaredTypes)
    {
        throw new NotImplementedException();
    }

    protected override VBTypedValue? EvaluateExpressionResult(IVBExecutionContext context, ValuedExpression expression, VBType effectiveType, VBTypedValue[] operands)
    {
        throw new NotImplementedException();
    }
}

