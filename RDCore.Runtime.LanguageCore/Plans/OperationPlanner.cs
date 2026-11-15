using RDCore.SDK.Model;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Concurrent;

namespace RDCore.Runtime.LanguageCore.Plans;

// MS-VBAL §5.6.9, §5.6.9.5, §5.6.9.8
// the relational operators' static semantics must not consult this table: a comparison's declared type says nothing of the type it compares in.
// two rules depend on values, not types, and stay the evaluation's: a Variant number against a String (§5.6.9.5), Null in a logical operation (§5.6.9.8).
public static class OperationPlanner
{
    private static readonly ConcurrentDictionary<(string Operator, VBType Left, VBType Right), OperationPlan> _binary = new();
    private static readonly ConcurrentDictionary<(string Operator, VBType Operand), OperationPlan> _unary = new();

    private static readonly RecentPlans<OperationPlan> _recentBinary = new();
    private static readonly RecentPlans<OperationPlan> _recentUnary = new();

    public static OperationPlan ForValues(string @operator, VBType left, VBType right)
    {
        if (_recentBinary.TryGet(@operator, left, right, out var plan))
        {
            return plan;
        }

        plan = _binary.GetOrAdd((@operator, left, right), key => new OperationPlan(key.Operator, [key.Left, key.Right], BinaryEffectiveType(key.Operator, AliasedTypeOf(key.Left), AliasedTypeOf(key.Right))));
        _recentBinary.Add(@operator, left, right, plan);
        return plan;
    }

    public static OperationPlan ForValue(string @operator, VBType operand)
    {
        if (_recentUnary.TryGet(@operator, operand, null, out var plan))
        {
            return plan;
        }

        plan = _unary.GetOrAdd((@operator, operand), key => new OperationPlan(key.Operator, [key.Operand], UnaryEffectiveType(key.Operator, AliasedTypeOf(key.Operand))));
        _recentUnary.Add(@operator, operand, null, plan);
        return plan;
    }

    public static OperationPlan ForDeclared(string @operator, VBType left, VBType right)
        => IsDeferredType(left) || IsDeferredType(right)
            ? new OperationPlan(@operator, [left, right], null, IsDeferred: true)
            : ForValues(@operator, ValueTypeOf(left), ValueTypeOf(right));

    public static OperationPlan ForDeclared(string @operator, VBType operand)
        => IsDeferredType(operand)
            ? new OperationPlan(@operator, [operand], null, IsDeferred: true)
            : ForValue(@operator, ValueTypeOf(operand));

    // a Variant and an object are what their value is; a type that is not known statically - late-bound - is known when the operation is evaluated.
    private static bool IsDeferredType(VBType type) => type is VBVariantType or VBObjectType or VBUnknownType;

    private static VBType ValueTypeOf(VBType declared) => declared is VBFixedStringType ? VBStringType.TypeInfo : declared;

    // MS-VBAL §2.2: LongPtr is an alias, of LongLong in a 64-bit environment and of Long in a 32-bit one, and is operated on as what it is an alias of.
    private static VBType AliasedTypeOf(VBType type) => type switch
    {
        VBLongPtrType_x64 => VBLongLongType.TypeInfo,
        VBLongPtrType_x86 => VBLongType.TypeInfo,
        _ => type,
    };

    private static VBType? BinaryEffectiveType(string @operator, VBType left, VBType right) => @operator switch
    {
        Tokens.AdditionOp => AdditionEffectiveType(left, right) ?? ArithmeticEffectiveType(left, right),
        Tokens.SubtractionOp => SubtractionEffectiveType(left, right) ?? ArithmeticEffectiveType(left, right),
        Tokens.MultiplicationOp => MultiplicationEffectiveType(left, right) ?? ArithmeticEffectiveType(left, right),
        Tokens.DivisionOp => DivisionEffectiveType(left, right) ?? ArithmeticEffectiveType(left, right),
        Tokens.IntegerDivisionOp or Tokens.ModuloOp => IntegerDivisionEffectiveType(left, right) ?? ArithmeticEffectiveType(left, right),
        Tokens.PowerOp => ExponentEffectiveType(left, right) ?? ArithmeticEffectiveType(left, right),
        Tokens.ConcatOp => ConcatEffectiveType(left, right),
        Tokens.CompareEqualOp or Tokens.CompareNotEqualOp or Tokens.CompareLessThanOp or Tokens.CompareLessThanOrEqualOp
            or Tokens.CompareGreaterThanOp or Tokens.CompareGreaterThanOrEqualOp => RelationalEffectiveType(left, right),
        Tokens.CompareLikeOp => LikeEffectiveType(left, right),
        // MS-VBAL 5.6.9.7: two object references are compared as they are, whatever they are; an operand that is not one is the evaluation's error 424.
        Tokens.CompareIsOp => VBBooleanType.TypeInfo,
        Tokens.LogicalAndOp or Tokens.LogicalOrOp or Tokens.LogicalXOrOp or Tokens.LogicalEqvOp or Tokens.LogicalImpOp => LogicalEffectiveType(left, right),
        _ => throw new NotSupportedException($"The binary operator '{@operator}' is not planned."),
    };

    private static VBType? UnaryEffectiveType(string @operator, VBType operand) => @operator switch
    {
        Tokens.NegationOp => UnaryArithmeticEffectiveType(operand),
        Tokens.LogicalNotOp => NotEffectiveType(operand),
        _ => throw new NotSupportedException($"The unary operator '{@operator}' is not planned."),
    };

    // MS-VBAL 5.6.9.3: the effective type of an arithmetic operation, by the value types of its operands - the table every arithmetic operator starts from.
    private static VBType? ArithmeticEffectiveType(VBType left, VBType right) => left switch
    {
        VBByteType when right is VBByteType or VBEmptyType => VBByteType.TypeInfo,
        VBByteType or VBEmptyType when right is VBByteType => VBByteType.TypeInfo,

        VBBooleanType or VBIntegerType when right is VBByteType or VBBooleanType or VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
        VBByteType or VBBooleanType or VBIntegerType or VBEmptyType when right is VBBooleanType or VBIntegerType => VBIntegerType.TypeInfo,
        VBEmptyType when right is VBEmptyType => VBIntegerType.TypeInfo,

        VBLongType when right is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBEmptyType => VBLongType.TypeInfo,
        VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBEmptyType when right is VBLongType => VBLongType.TypeInfo,

        VBLongLongType when right is IIntegralNumericType or VBBooleanType or VBEmptyType => VBLongLongType.TypeInfo,
        IIntegralNumericType or VBBooleanType or VBEmptyType when right is VBLongLongType => VBLongLongType.TypeInfo,

        VBSingleType when right is VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType => VBSingleType.TypeInfo,
        VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBEmptyType when right is VBSingleType => VBSingleType.TypeInfo,

        VBSingleType when right is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
        VBLongType or VBLongLongType when right is VBSingleType => VBDoubleType.TypeInfo,
        VBDoubleType or VBStringType when right is IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBStringType or VBEmptyType => VBDoubleType.TypeInfo,
        IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBStringType or VBEmptyType when right is VBDoubleType or VBStringType => VBDoubleType.TypeInfo,

        VBCurrencyType when right is IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
        IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBStringType or VBEmptyType when right is VBCurrencyType => VBCurrencyType.TypeInfo,

        // a Date operation is evaluated in Double (OperationPlan.OperandDestination), and its result is a Date.
        VBDateType when right is IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBDateType or VBEmptyType => VBDateType.TypeInfo,
        IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBDateType or VBEmptyType when right is VBDateType => VBDateType.TypeInfo,

        VBDecimalType when right is INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType => VBDecimalType.TypeInfo,
        INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType when right is VBDecimalType => VBDecimalType.TypeInfo,

        VBNullType when right is INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType or VBNullType => VBNullType.TypeInfo,
        INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType or VBNullType when right is VBNullType => VBNullType.TypeInfo,

        // the operation of two Error values is planned in Error, which the evaluation of its operands refuses; one Error is a type mismatch.
        VBErrorType when right is VBErrorType => VBErrorType.TypeInfo,

        _ => null,
    };

    // MS-VBAL 5.6.9.3.2: two strings are concatenated.
    private static VBType? AdditionEffectiveType(VBType left, VBType right)
        => left is VBStringType && right is VBStringType ? VBStringType.TypeInfo : null;

    // MS-VBAL 5.6.9.3.3: the difference of two dates is the number of days between them, a Double - not a date.
    private static VBType? SubtractionEffectiveType(VBType left, VBType right)
        => left is VBDateType && right is VBDateType ? VBDoubleType.TypeInfo : null;

    // MS-VBAL 5.6.9.3.4: a Currency multiplied by a floating-point number or a string, and a Date by anything numeric, are multiplied in Double.
    private static VBType? MultiplicationEffectiveType(VBType left, VBType right) => left switch
    {
        VBCurrencyType when right is VBSingleType or VBDoubleType or VBFixedStringType or VBStringType => VBDoubleType.TypeInfo,
        VBSingleType or VBDoubleType or VBFixedStringType or VBStringType when right is VBCurrencyType => VBDoubleType.TypeInfo,
        VBDateType when right is VBNumericType or VBBooleanType or VBFixedStringType or VBStringType or VBDateType or VBEmptyType => VBDoubleType.TypeInfo,
        VBNumericType or VBBooleanType or VBFixedStringType or VBStringType or VBDateType or VBEmptyType when right is VBDateType => VBDoubleType.TypeInfo,
        _ => null,
    };

    // MS-VBAL 5.6.9.3.5: a division is in Double, but for one of Single and an integral or Single value, which the table above makes Single.
    private static VBType? DivisionEffectiveType(VBType left, VBType right) => left switch
    {
        VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType
            when right is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBLongLongType or VBEmptyType => VBDoubleType.TypeInfo,
        VBDoubleType or VBStringType or VBCurrencyType or VBDateType when right is INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType => VBDoubleType.TypeInfo,
        INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType when right is VBDoubleType or VBStringType or VBCurrencyType or VBDateType => VBDoubleType.TypeInfo,
        _ => null,
    };

    // MS-VBAL 5.6.9.3.6 and 5.6.9.3.7: '\' and 'Mod' are integral, and share their table.
    private static VBType? IntegerDivisionEffectiveType(VBType left, VBType right) => left switch
    {
        VBByteType when right is VBEmptyType => VBIntegerType.TypeInfo,
        VBEmptyType when right is VBByteType => VBIntegerType.TypeInfo,

        // a Boolean is a numeric operand here, the way the table above treats it as an Integer.
        IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBDateType
            when right is VBNumericType and not VBLongLongType or VBBooleanType or VBStringType or VBDateType or VBEmptyType => VBLongType.TypeInfo,
        VBNumericType and not VBLongLongType or VBBooleanType or VBStringType or VBDateType or VBEmptyType
            when right is IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBDateType => VBLongType.TypeInfo,
        VBLongLongType when right is VBNumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType => VBLongLongType.TypeInfo,
        VBNumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType when right is VBLongLongType => VBLongLongType.TypeInfo,
        _ => null,
    };

    // MS-VBAL 5.6.9.3.8: '^' is always Double. A Boolean is a numeric operand here, as it is an Integer one in the table above.
    private static VBType? ExponentEffectiveType(VBType left, VBType right)
        => left is VBNumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType
            && right is VBNumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType
            ? VBDoubleType.TypeInfo
            : null;

    // MS-VBAL 5.6.9.3.1: the negation of a Byte is an Integer, as a Byte has no negative values; anything else is its own type, as the table of the unary
    // arithmetic operators has it.
    private static VBType? UnaryArithmeticEffectiveType(VBType operand) => operand switch
    {
        VBByteType => VBIntegerType.TypeInfo,
        VBBooleanType or VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
        VBLongType => VBLongType.TypeInfo,
        VBLongLongType => VBLongLongType.TypeInfo,
        VBSingleType => VBSingleType.TypeInfo,
        VBDoubleType or VBStringType => VBDoubleType.TypeInfo,
        VBCurrencyType => VBCurrencyType.TypeInfo,
        // a Date operation is evaluated in Double (OperationPlan.OperandDestination), and its result is a Date.
        VBDateType => VBDateType.TypeInfo,
        VBDecimalType => VBDecimalType.TypeInfo,
        VBNullType => VBNullType.TypeInfo,
        _ => null,
    };

    // MS-VBAL 5.6.9.4: anything that is a string, or converts to one, is concatenated as a String; Null is only the result of Null and Null. The table of the
    // specification has no Boolean row, which would make `"Done: " & True` a type mismatch, and MS-VBA concatenates "Done: True".
    private static VBType? ConcatEffectiveType(VBType left, VBType right) => left switch
    {
        VBNumericType or VBBooleanType or VBStringType or VBDateType or VBNullType or VBEmptyType
            when right is VBNumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType => VBStringType.TypeInfo,
        VBNumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType
            when right is VBNumericType or VBBooleanType or VBStringType or VBDateType or VBNullType or VBEmptyType => VBStringType.TypeInfo,
        // a Byte() is the data of a String (MS-VBAL 5.5.1.2.6).
        VBArrayType { ItemType: VBByteType } when right is VBArrayType { ItemType: VBByteType } => VBStringType.TypeInfo,
        VBNullType when right is VBNullType => VBNullType.TypeInfo,
        _ => null,
    };

    // MS-VBAL §5.6.9.5; RD-VBAL §5.6.9.5
    // kept for an analysis that states where code departs from the comparison the specification states.
    public static VBType? SpecifiedRelationalEffectiveType(VBType left, VBType right) => TableRelationalEffectiveType(AliasedTypeOf(left), AliasedTypeOf(right));

    // MS-VBA compares a String with a Byte, an Integer or a Long as Doubles, where MS-VBAL makes the effective type the number's: "2.5" = 2 is False and
    // "2.5" > 2 True, a Variant String or a declared one (observed in Excel; RD-VBAL 5.6.9.5). A declared String and a
    // Single are the table's; a Variant String and a Single or a LongLong are Doubles too, by the value it holds (BinaryRelationalOperatorRuntimeSemantics).
    private static VBType? RelationalEffectiveType(VBType left, VBType right)
        => left is VBByteType or VBIntegerType or VBLongType && right is VBStringType || left is VBStringType && right is VBByteType or VBIntegerType or VBLongType
            ? VBDoubleType.TypeInfo
            : TableRelationalEffectiveType(left, right);

    // MS-VBAL 5.6.9.5: the effective type of a comparison, by the value types of its operands. A comparison yields a Boolean, or Null for a Null effective
    // type; a Date comparison compares the operands as Doubles (OperationPlan.OperandDestination).
    private static VBType? TableRelationalEffectiveType(VBType left, VBType right) => left switch
    {
        VBByteType when right is VBByteType or VBStringType or VBEmptyType => VBByteType.TypeInfo,
        VBByteType or VBStringType or VBEmptyType when right is VBByteType => VBByteType.TypeInfo,

        VBBooleanType when right is VBBooleanType or VBStringType => VBBooleanType.TypeInfo,
        VBBooleanType or VBStringType when right is VBBooleanType => VBBooleanType.TypeInfo,

        VBIntegerType when right is VBByteType or VBBooleanType or VBIntegerType or VBStringType or VBEmptyType => VBIntegerType.TypeInfo,
        VBByteType or VBBooleanType or VBIntegerType or VBStringType or VBEmptyType when right is VBIntegerType => VBIntegerType.TypeInfo,
        VBBooleanType when right is VBByteType or VBEmptyType => VBIntegerType.TypeInfo,
        VBByteType or VBEmptyType when right is VBBooleanType => VBIntegerType.TypeInfo,
        VBEmptyType when right is VBEmptyType => VBIntegerType.TypeInfo,

        VBLongType when right is VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBStringType or VBEmptyType => VBLongType.TypeInfo,
        VBByteType or VBBooleanType or VBIntegerType or VBLongType or VBStringType or VBEmptyType when right is VBLongType => VBLongType.TypeInfo,

        VBLongLongType when right is IIntegralNumericType or VBBooleanType or VBStringType or VBEmptyType => VBLongLongType.TypeInfo,
        IIntegralNumericType or VBBooleanType or VBStringType or VBEmptyType when right is VBLongLongType => VBLongLongType.TypeInfo,

        VBSingleType when right is VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBDoubleType or VBStringType or VBEmptyType => VBSingleType.TypeInfo,
        VBByteType or VBBooleanType or VBIntegerType or VBSingleType or VBDoubleType or VBStringType or VBEmptyType when right is VBSingleType => VBSingleType.TypeInfo,

        // a Single holds neither every Long nor every LongLong: the two are compared as Doubles.
        VBSingleType when right is VBLongType or VBLongLongType => VBDoubleType.TypeInfo,
        VBLongType or VBLongLongType when right is VBSingleType => VBDoubleType.TypeInfo,
        VBDoubleType when right is IIntegralNumericType or VBBooleanType or VBDoubleType or VBStringType or VBEmptyType => VBDoubleType.TypeInfo,
        IIntegralNumericType or VBBooleanType or VBDoubleType or VBStringType or VBEmptyType when right is VBDoubleType => VBDoubleType.TypeInfo,

        VBStringType when right is VBStringType or VBEmptyType => VBStringType.TypeInfo,
        VBStringType or VBEmptyType when right is VBStringType => VBStringType.TypeInfo,

        VBCurrencyType when right is IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType => VBCurrencyType.TypeInfo,
        IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBEmptyType when right is VBCurrencyType => VBCurrencyType.TypeInfo,

        VBDateType when right is IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBDateType or VBEmptyType => VBDateType.TypeInfo,
        IIntegralNumericType or VBBooleanType or IFloatingPointNumericType or VBCurrencyType or VBStringType or VBDateType or VBEmptyType when right is VBDateType => VBDateType.TypeInfo,

        VBDecimalType when right is INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType => VBDecimalType.TypeInfo,
        INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType when right is VBDecimalType => VBDecimalType.TypeInfo,

        VBNullType when right is INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType or VBNullType => VBNullType.TypeInfo,
        INumericType or VBBooleanType or VBStringType or VBDateType or VBEmptyType or VBNullType when right is VBNullType => VBNullType.TypeInfo,

        // MS-VBAL 5.6.9.5: two Error values compare by their numeric value.
        VBErrorType when right is VBErrorType => VBErrorType.TypeInfo,

        _ => null,
    };

    // MS-VBAL 5.6.9.6: Like is Null when either operand is, and otherwise matches both operands as Strings, whatever they are.
    private static VBType LikeEffectiveType(VBType left, VBType right)
        => left is VBNullType || right is VBNullType ? VBNullType.TypeInfo : VBStringType.TypeInfo;

    // MS-VBAL 5.6.9.8: logical operators are first resolved as simple data operators. The effective type is always Byte, Boolean, Integer, Long, LongLong,
    // Variant or Null: a floating- or fixed-point value, a string and a date are a Long (or a LongLong), never their own type.
    private static VBType? LogicalEffectiveType(VBType left, VBType right) => (left, right) switch
    {
        (VBByteType, VBByteType or VBNullType) or (VBNullType, VBByteType)
            => VBByteType.TypeInfo,

        // a Boolean operation is evaluated over the -1/0 of its operands, and yields a Boolean.
        (VBBooleanType, VBBooleanType or VBNullType) or (VBNullType, VBBooleanType)
            => VBBooleanType.TypeInfo,

        (VBByteType or VBBooleanType or VBIntegerType or VBEmptyType or VBNullType,
            VBByteType or VBBooleanType or VBIntegerType or VBEmptyType)
            or (VBByteType or VBBooleanType or VBIntegerType or VBEmptyType,
                VBByteType or VBBooleanType or VBIntegerType or VBEmptyType or VBNullType)
            => VBIntegerType.TypeInfo,

        // the table of MS-VBAL 5.6.9.8 names no Boolean in these rows, where MS-VBA takes one as the integral value it is: `True And 5^` is the LongLong 5,
        // whichever side the Boolean is on.
        (VBLongLongType, INumericType or VBBooleanType or VBStringType or VBFixedStringType or VBDateType or VBEmptyType or VBNullType)
            or (INumericType or VBBooleanType or VBStringType or VBFixedStringType or VBDateType or VBEmptyType or VBNullType, VBLongLongType)
            => VBLongLongType.TypeInfo,

        (IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBStringType or VBFixedStringType or VBDateType,
            ((INumericType and not VBLongLongType) or VBBooleanType) or VBStringType or VBFixedStringType or VBDateType or VBEmptyType or VBNullType)
            or (((INumericType and not VBLongLongType) or VBBooleanType) or VBStringType or VBFixedStringType or VBDateType or VBEmptyType or VBNullType,
                IFloatingPointNumericType or IFixedPointNumericType or VBLongType or VBStringType or VBFixedStringType or VBDateType)
            => VBLongType.TypeInfo,

        (VBNullType, VBNullType) => VBNullType.TypeInfo,

        (VBVariantType, not (VBArrayType or VBUserDefinedType)) or (not (VBArrayType or VBUserDefinedType), VBVariantType)
            => VBVariantType.TypeInfo,

        _ => null,
    };

    // MS-VBAL 5.6.9.8.1: Not negates the bits of an integral value, in the type of the value - the -1 or 0 of a Boolean in a Boolean, as the binary logical
    // operators have it; a floating- or fixed-point value, a string and a date are a Long, as the binary logical operators take them.
    private static VBType? NotEffectiveType(VBType operand) => operand switch
    {
        VBByteType => VBByteType.TypeInfo,
        VBBooleanType => VBBooleanType.TypeInfo,
        VBIntegerType or VBEmptyType => VBIntegerType.TypeInfo,
        VBLongType => VBLongType.TypeInfo,
        VBLongLongType => VBLongLongType.TypeInfo,
        IFloatingPointNumericType or IFixedPointNumericType or VBStringType or VBDateType => VBLongType.TypeInfo,
        VBNullType => VBNullType.TypeInfo,
        _ => null,
    };
}
