using RDCore.SDK.Model;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Concurrent;

namespace RDCore.SDK.Semantics.Plans;

/// <summary>
/// Makes the <see cref="OperationPlan"/> of an operation from the types of its operands: the one table of the effective types of the operators, consulted when an
/// operation is bound from declared types and when it is evaluated on values (IR lowering §3.2).
/// </summary>
/// <remarks>
/// <para>
/// A plan depends on nothing but the operator and the operand types, so each is made once and kept: the interpreter asks for the plan of every operation it
/// evaluates, and an operation evaluated a million times is planned once. The plans last asked for are also kept by the identity of the operator and the
/// types (<see cref="RecentPlans{TPlan}"/>), which is what an operation evaluated in a loop asks with every time.
/// </para>
/// <para>
/// Every operator of <strong>MS-VBAL §5.6.9</strong> is planned here: the arithmetic operators and unary <c>+</c>, <c>&amp;</c>, the relational operators,
/// <c>Like</c> and <c>Is</c>, and the logical operators. What else evaluates as an operator - a let-assignment, a pair of parentheses - is a conversion, which
/// has no table of effective types, and asking for its plan is a mistake (<see cref="NotSupportedException"/>).
/// </para>
/// <para>
/// A plan is made from the types of the operands, and two rules of the specification depend on more than that, so they are the evaluation's: a comparison of
/// two operands <em>declared</em> <c>Variant</c>, one holding a number and the other a <c>String</c>, ranks the number below the <c>String</c> whatever their
/// types make of it (<strong>§5.6.9.5</strong>); and a logical operation of <c>Null</c> and a number is <c>Null</c> or a number, depending on the number
/// (<strong>§5.6.9.8</strong>).
/// </para>
/// <para>
/// The static semantics of the arithmetic, logical and concatenation operators consult this table. Those of the relational operators do not, and are not to:
/// a comparison's <em>declared</em> type is Boolean, or Variant (<strong>§5.6.9.5</strong>), which says nothing of the effective type its operands are
/// compared in. What the static semantics accept, this table does not reject for the same declared types - but where the specification itself makes a
/// comparison of an <c>Error</c> and anything else statically valid and a type mismatch whenever it runs.
/// </para>
/// </remarks>
public static class OperationPlanner
{
    private static readonly ConcurrentDictionary<(string Operator, VBType Left, VBType Right), OperationPlan> _binary = new();
    private static readonly ConcurrentDictionary<(string Operator, VBType Operand), OperationPlan> _unary = new();

    private static readonly RecentPlans<OperationPlan> _recentBinary = new();
    private static readonly RecentPlans<OperationPlan> _recentUnary = new();

    /// <summary>
    /// The plan of a binary operation on values of these types: final, whatever the types are.
    /// </summary>
    /// <param name="operator">The operator: one of the <c>Tokens</c> binary operators.</param>
    /// <param name="left">The value type of the left operand: the type of what a Variant operand holds.</param>
    /// <param name="right">The value type of the right operand.</param>
    /// <exception cref="NotSupportedException">The operator has no table of effective types: it is a conversion.</exception>
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

    /// <summary>
    /// The plan of a unary operation on a value of this type: final, whatever the type is.
    /// </summary>
    /// <param name="operator">The operator: one of the <c>Tokens</c> unary operators.</param>
    /// <param name="operand">The value type of the operand.</param>
    /// <exception cref="NotSupportedException">The operator has no table of effective types: it is a conversion.</exception>
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

    /// <summary>
    /// The plan of a binary operation on operands of these declared types: <see cref="OperationPlan.IsDeferred"/> when either is declared <c>Variant</c> or
    /// <c>Object</c>, or is late-bound (<see cref="VBUnknownType"/>), whose value type is not known until the operation is evaluated, and the plan of the
    /// values otherwise.
    /// </summary>
    /// <param name="operator">The operator: one of the <c>Tokens</c> binary operators.</param>
    /// <param name="left">The declared type of the left operand.</param>
    /// <param name="right">The declared type of the right operand.</param>
    /// <remarks>A fixed-length string holds a <c>String</c>, which is the value type it is planned as.</remarks>
    public static OperationPlan ForDeclared(string @operator, VBType left, VBType right)
        => IsDeferredType(left) || IsDeferredType(right)
            ? new OperationPlan(@operator, [left, right], null, IsDeferred: true)
            : ForValues(@operator, ValueTypeOf(left), ValueTypeOf(right));

    /// <summary>
    /// The plan of a unary operation on an operand of this declared type: <see cref="OperationPlan.IsDeferred"/> when it is declared <c>Variant</c> or
    /// <c>Object</c>, and the plan of the value otherwise.
    /// </summary>
    /// <param name="operator">The operator: one of the <c>Tokens</c> unary operators.</param>
    /// <param name="operand">The declared type of the operand.</param>
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
        Tokens.NegationOp or Tokens.AdditionOp => UnaryArithmeticEffectiveType(operand),
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
    // arithmetic operators has it. Unary '+' is RD-VBAL's, and has the same table: what it does to its operand is the conversion to it.
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

    // MS-VBAL 5.6.9.5: the effective type of a comparison, by the value types of its operands. A comparison yields a Boolean, or Null for a Null effective
    // type; a Date comparison compares the operands as Doubles (OperationPlan.OperandDestination).
    private static VBType? RelationalEffectiveType(VBType left, VBType right) => left switch
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
