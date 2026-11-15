using RDCore.Runtime.LanguageCore.Static.Abstract;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Operators;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.2.3.2: the constant binding of a constant is the data value of its constant expression, Let-coerced to its declared type. It is known here where
// the expression is a literal, a negated or parenthesized one, or the name of another constant whose binding is known; and where the Let-coercion is to
// Boolean (MS-VBAL §5.5.1.2.2), to a Byte, an Integer, a Long, a LongLong, a LongPtr, a Single or a Double from a number that fits it (§5.5.1.2.1,
// rounding to even), or to the value's own type or a Variant.
internal static class ConstantBindings
{
    // the most constants a binding is looked for through, each naming the next: past them, the binding is not known.
    private const int Depth = 8;

    public static VBTypedValue? Of(StaticEvaluationContext context, Symbol? constant) => Of(context, constant, 0);

    private static VBTypedValue? Of(StaticEvaluationContext context, Symbol? constant, int depth) => constant switch
    {
        VBLocalConstantSymbol { Value: { } value } local => LetCoerced(ValueOf(context, value, depth), local.ResolvedType),
        VBConstantMemberSymbol { Value: { } value } member => LetCoerced(ValueOf(context, value, depth), member.ResolvedType),
        _ => null,
    };

    private static VBTypedValue? ValueOf(StaticEvaluationContext context, ExpressionNode expression, int depth) => expression switch
    {
        LiteralExpressionNode literal => literal.StaticValue,
        VBUnaryOperatorExpressionNode { Token: OperatorSymbolNames.UnaryArithmeticPrecedenceOp or OperatorSymbolNames.UnaryLetCoerceOp } parentheses
            => ValueOf(context, parentheses.Operand, depth),
        VBUnaryOperatorExpressionNode { Token: Tokens.NegationOp } negation => Negated(ValueOf(context, negation.Operand, depth)),
        SimpleNameExpressionNode name when depth < Depth
            => Of(context, context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol, depth + 1),
        _ => null,
    };

    // the negation of a number is of its type, where the type holds it.
    private static VBTypedValue? Negated(VBTypedValue? value) => value is VBNumericTypedValue number
        ? LetCoerced(-number.AsDouble, number.TypeInfo)
        : null;

    // a constant declared with no As clause and no type character has the declared type of its constant expression, the type of the value (MS-VBAL §5.2.3.2).
    private static VBTypedValue? LetCoerced(VBTypedValue? value, VBType? type) => (value, type) switch
    {
        (null, _) => null,
        (_, null or VBUnknownType or VBVariantType) => value,
        (VBBooleanValue, VBBooleanType) => value,
        (VBNumericTypedValue number, VBBooleanType) => new VBBooleanValue(number.AsDouble != 0),
        (VBNumericTypedValue number, _) => LetCoerced(number.AsDouble, type),
        _ when value.TypeInfo.Name == type.Name => value,
        _ => null,
    };

    private static VBTypedValue? LetCoerced(double number, VBType type)
    {
        var integral = Math.Round(number, MidpointRounding.ToEven);
        return type switch
        {
            VBByteType when integral is >= byte.MinValue and <= byte.MaxValue => new VBByteValue((byte)integral),
            VBIntegerType when integral is >= short.MinValue and <= short.MaxValue => new VBIntegerValue((short)integral),
            VBLongType when integral is >= int.MinValue and <= int.MaxValue => new VBLongValue((int)integral),
            VBLongLongType or VBLongPtrType_x64 when integral is >= long.MinValue and < 9.2233720368547758E18 => Wide(type, (long)integral),
            VBLongPtrType_x86 when integral is >= int.MinValue and <= int.MaxValue => new VBLongPtrValue((int)integral),
            VBSingleType when Math.Abs(number) <= float.MaxValue => new VBSingleValue((float)number),
            VBDoubleType => new VBDoubleValue(number),
            _ => null,
        };
    }

    private static VBTypedValue Wide(VBType type, long value) => type is VBLongPtrType_x64 ? new VBLongPtrValue(value) : new VBLongLongValue(value);
}
