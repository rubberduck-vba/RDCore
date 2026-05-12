using RDCore.Parsing.Model.Symbols;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Types.Complex;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Server;

namespace RDCore.Parsing.Model.Expressions.Operators;

internal static class SymbolOperation
{
    internal delegate VBTypedValue UnaryOperation(
        VBExecutionContext context,
        VBUnaryOperatorExpression operation,
        VBTypedValue value);

    internal delegate VBTypedValue BinaryOperation(
        VBExecutionContext context,
        VBBinaryOperatorExpression operation,
        VBTypedValue lhsValue,
        VBTypedValue rhsValue);

    private static readonly Dictionary<Uri, BinaryOperation> _binaryInstructions =
        GlobalSymbols.Operators.OfType<BinaryOperatorSymbol>()
        .ToDictionary(symbol => symbol.Uri, symbol => symbol.ExecuteBinaryOp);

    private static readonly Dictionary<Uri, UnaryOperation> _unaryInstructions =
        GlobalSymbols.Operators.OfType<UnaryOperatorSymbol>()
        .ToDictionary(symbol => symbol.Uri, symbol => symbol.ExecuteUnaryOp);

    public static BinaryOperation GetBinaryInstruction(Uri uri) => _binaryInstructions[uri];
    public static UnaryOperation GetUnaryInstruction(Uri uri) => _unaryInstructions[uri];

    private static VBType GetPromotedType(VBType lhs, VBType rhs)
    {
        // Type Promotion Hierarchy
        // Double > Single > Long > Integer > Byte
        if (lhs is VBDoubleType || rhs is VBDoubleType)
        {
            return VBDoubleType.TypeInfo;
        }
        if (lhs is VBSingleType || rhs is VBSingleType)
        {
            return VBSingleType.TypeInfo;
        }
        if (lhs is VBLongType || rhs is VBLongType)
        {
            return VBLongType.TypeInfo;
        }
        if (lhs is VBIntegerType || rhs is VBIntegerType)
        {
            return VBIntegerType.TypeInfo;
        }

        if (lhs is VBBooleanType && rhs is VBBooleanType)
        {
            return VBBooleanType.TypeInfo;
        }

        return VBByteType.TypeInfo;
    }

    private static VBTypedValue EvaluateUnaryOp(VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value,
        Func<VBTypedValue, VBTypedValue> op)
    {
        // Null Propagation
        if (value is VBNullValue)
        {
            return VBNullValue.Null;
        }

        // Let-Coercion
        var coercionDepth = 0;
        var effectiveValue = value is VBObjectValue obj ? obj.LetCoerce(ref coercionDepth) : value;

        // Empty Coercion
        if (effectiveValue is VBEmptyValue)
        {
            effectiveValue = VBIntegerValue.Zero;
        }

        if (effectiveValue is not VBNumericTypedValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Symbol?.SelectionRange!);
        }

        // Unary + and - on Empty results in 0 (Integer)
        // Note: Parentheses on Empty stays Empty until an operator touches it.
        return op(effectiveValue);
    }

    private static VBTypedValue EvaluateNumericBinaryOp(VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs,
        Func<double, double, double> op,
        out VBNumericTypedValue lhsNumeric,
        out VBNumericTypedValue rhsNumeric,
        out VBType targetType
        )
    {
        lhsNumeric = default!;
        rhsNumeric = default!;

        // MS-VBAL 5.5.1.2.10 Let-coercion from Null
        // if either operand is Null and the other is a UDT or resizable array value, throw a type mismatch.
        // MS-VBAL 5.6.9.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.
        // --- so, Null is irrelevant then - 5.6.9.3 takes precedence in the context of a binary operation.
        if (lhs is VBNullValue && (rhs is VBResizableArrayValue || rhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(rhs.Symbol?.Range!);
        }
        if (rhs is VBNullValue && (lhs is VBResizableArrayValue || lhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(lhs.Symbol!.Range!);
        }

        // ...otherwise if either operand is Null, the result is Null.
        if (lhs is VBNullValue || rhs is VBNullValue)
        {
            targetType = VBNullType.TypeInfo;
            return VBNullValue.Null;
        }

        // MS-VBAL 5.5.1.2.11 Let-coercion from Empty
        // If both operands are Empty, the result is an Integer 0.
        if (lhs is VBEmptyValue && rhs is VBEmptyValue)
        {
            targetType = VBIntegerType.TypeInfo;
            return VBIntegerValue.Zero;
        }

        // MS-VBAL 5.6.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.

        if (lhs is VBErrorValue && rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
        if (lhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }

        // Numeric String Conversions
        // If one operand is a String and the other is a numeric, the String operand is converted to a Double.
        // RDC00109 is issued for each such implicit coercion, twice if both sides require numeric coercion.
        var lhsCoercionDepth = 0;
        lhsNumeric = lhs.TypeInfo is INumericType ? (VBNumericTypedValue)lhs : ((INumericCoercion)lhs).AsCoercedNumeric(ref lhsCoercionDepth)!;

        var rhsCoercionDepth = 0;
        rhsNumeric = rhs.TypeInfo is INumericType ? (VBNumericTypedValue)rhs : ((INumericCoercion)rhs).AsCoercedNumeric(ref rhsCoercionDepth)!;

        if (lhs is VBStringValue)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Left.Location.Range, lhs.TypeInfo, VBDoubleType.TypeInfo));
        }

        if (rhs is VBStringValue)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(expression.Right.Location.Range, rhs.TypeInfo, VBDoubleType.TypeInfo));
        }

        // determine the target type
        if (lhs is VBBooleanValue && rhs is VBBooleanValue)
        {
            targetType = VBBooleanType.TypeInfo;
        }
        else
        {
            targetType = GetPromotedType(lhsNumeric.TypeInfo, rhsNumeric.TypeInfo);
        }

        // calculate the numeric result
        var resultValue = op(lhsNumeric.NumericValue, rhsNumeric.NumericValue);

        // Overflow checks [VBR0006]
        if (targetType is INumericType)
        {
            return (VBTypedValue)((VBNumericTypedValue)targetType.CreateValue(expression.Symbol))
                .WithValue(resultValue);
        }
        else
        {
            return ((VBBooleanValue)targetType.CreateValue(expression.Symbol))
                .WithValue(resultValue);
        }
    }


    public static VBTypedValue EvaluateBinaryAddition(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        VBNumericTypedValue lhsNumeric;
        VBNumericTypedValue rhsNumeric;

        if (lhs is VBStringValue && rhs is VBStringValue)
        {
            // if both operands are strings, then this is a concatenation, not an addition.
            // a diagnostic is issued for the ambiguous concatenation operator usage.
            context.AddDiagnostic(RDCoreDiagnostic.AmbiguousConcatenation(expression.Symbol.Range!));
        }

        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => left + right,
            out lhsNumeric,
            out rhsNumeric,
            out var targetType);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType)
        {
            // Date math is effectively Double math re-wrapped
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));
            return new VBDateValue(expression.Symbol).WithValue(((VBNumericTypedValue)resultValue).NumericValue);
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinarySubtraction(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => left - right,
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType)
        {
            // Date math is effectively Double math re-wrapped
            var diff = lhsNumeric.NumericValue - rhsNumeric.NumericValue;
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));

            // If both are dates, return Double. If only one is a date, return Date.
            return (lhs.TypeInfo is VBDateType && rhs.TypeInfo is VBDateType)
                ? new VBDoubleValue(expression.Symbol).WithValue(diff)
                : new VBDateValue(expression.Symbol).WithValue(diff);
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryMultiplication(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => left * right,
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType)
        {
            // Date math is effectively Double math re-wrapped
            var diff = lhsNumeric.NumericValue - rhsNumeric.NumericValue;
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));

            // If both are dates, return Double. If only one is a date, return Date.
            return new VBDoubleValue(expression.Symbol).WithValue(diff);
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryExponentiation(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        return EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => Math.Pow(left, right),
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);
    }

    public static VBTypedValue EvaluateBinaryDivision(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        // MS-VBAL 5.5.1.2.10 Let-coercion from Null
        // if either operand is Null and the other is a UDT or resizable array value, throw a type mismatch.
        // MS-VBAL 5.6.9.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.
        // --- so, Null is irrelevant then - 5.6.9.3 takes precedence in the context of a binary operation.
        if (lhs is VBNullValue && (rhs is VBResizableArrayValue || rhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(rhs.Symbol?.Range!);
        }
        if (rhs is VBNullValue && (lhs is VBResizableArrayValue || lhs is VBUserDefinedTypeValue))
        {
            throw VBRuntimeErrorException.TypeMismatch(lhs.Symbol!.Range!);
        }

        // ...otherwise if either operand is Null, the result is Null.
        if (lhs is VBNullValue || rhs is VBNullValue)
        {
            return VBNullValue.Null;
        }

        // MS-VBAL 5.5.1.2.11 Let-coercion from Empty
        // If both operands are Empty, the result is an Integer 0.
        if (lhs is VBEmptyValue && rhs is VBEmptyValue)
        {
            return VBIntegerValue.Zero;
        }

        // MS-VBAL 5.6.3 runtime semantics
        // if the value type of any operand is an array, UDT, or Error, raise error 13 type mismatch.

        if (lhs is VBErrorValue && rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }
        if (lhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Left.Location.Range);
        }
        if (rhs is VBErrorValue)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Right.Location.Range);
        }

        var lhsCoercionDepth = 0;
        var lhsNumeric = lhs.TypeInfo is INumericType ? (VBNumericTypedValue)lhs : ((INumericCoercion)lhs).AsCoercedNumeric(ref lhsCoercionDepth)!;

        var rhsCoercionDepth = 0;
        var rhsNumeric = rhs.TypeInfo is INumericType ? (VBNumericTypedValue)rhs : ((INumericCoercion)rhs).AsCoercedNumeric(ref rhsCoercionDepth)!;

        if (rhsNumeric.NumericValue == 0)
        {
            throw VBRuntimeErrorException.DivisionByZero(expression.Location.Range);
        }

        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
             (left, right) => left / right,
             out lhsNumeric,
             out rhsNumeric,
             out var targetType);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType)
        {
            // Date math is effectively Double math re-wrapped
            var diff = lhsNumeric.NumericValue - rhsNumeric.NumericValue;
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));

            // If both are dates, return Double. If only one is a date, return Date.
            return new VBDoubleValue(expression.Symbol).WithValue(diff);
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryIntegerDivision(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        var lhsCoercionDepth = 0;
        var lhsNumeric = lhs.TypeInfo is INumericType ? (VBNumericTypedValue)lhs : ((INumericCoercion)lhs).AsCoercedNumeric(ref lhsCoercionDepth)!;

        var rhsCoercionDepth = 0;
        var rhsNumeric = rhs.TypeInfo is INumericType ? (VBNumericTypedValue)rhs : ((INumericCoercion)rhs).AsCoercedNumeric(ref rhsCoercionDepth)!;

        if (rhsNumeric.NumericValue == 0)
        {
            throw VBRuntimeErrorException.DivisionByZero(expression.Location.Range);
        }

        var resultValue = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => (int)Math.Round(left, 0, MidpointRounding.ToEven) / (int)Math.Round(right, 0, MidpointRounding.ToEven),
            out lhsNumeric,
            out rhsNumeric,
            out var targetType);

        // Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType)
        {
            // Date math is effectively Double math re-wrapped
            var diff = lhsNumeric.NumericValue - rhsNumeric.NumericValue;
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));

            // If both are dates, return Double. If only one is a date, return Date.
            return new VBDoubleValue(expression.Symbol).WithValue(diff);
        }

        return resultValue;
    }

    public static VBTypedValue EvaluateBinaryModulo(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        var result = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => Math.DivRem((int)left, (int)right).Remainder,
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

        if (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitDateSerialConversion(expression.Symbol?.Range!));
        }

        return result;
    }

    public static VBTypedValue EvaluateBinaryIsRefEquality(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs
        )
    {
        if (lhs is not VBObjectValue lObj || rhs is not VBObjectValue rObj)
        {
            throw VBRuntimeErrorException.TypeMismatch(expression.Location.Range);
        }

        var result = lObj.RawAddress == rObj.RawAddress;
        return new VBBooleanValue(expression.Symbol).WithValue(result);
    }

    public static VBTypedValue EvaluateUnaryParentheses(
        VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value) =>
    EvaluateUnaryOp(context, expression, value, value => value);

    // NOTE: no-op on the value, but forces null/empty propagation and let coercions.
    public static VBTypedValue EvaluateUnaryPlus(
        VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value) => EvaluateUnaryOp(context, expression, value, value => value);

    public static VBTypedValue EvaluateUnaryMinus(
        VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value)
    {
        var validValue = (VBNumericTypedValue)EvaluateUnaryOp(context, expression, value, value => value);
        return (VBTypedValue)validValue.WithValue(-validValue.NumericValue);
    }

    public static VBTypedValue EvaluateUnaryBitwiseNot(
        VBExecutionContext context,
        VBUnaryOperatorExpression expression,
        VBTypedValue value) =>
    EvaluateUnaryOp(context, expression, value, v =>
    {
        var depth = 0;
        var num = ((VBNumericTypedValue)v).AsCoercedNumeric(ref depth);
        return num.WithValue(~(long)num.NumericValue);
    });

    public static VBTypedValue EvaluateBinaryBitwiseAnd(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs)
    {
        var result = EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => (long)left & (long)right,
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

        var numericResult = result as VBNumericTypedValue;
        var depth = 0;
        var coercedResult = (result as INumericCoercion)?.AsCoercedNumeric(ref depth);
        {
            if (numericResult is null && coercedResult != null)
            {
                var diagnostic = RDCoreDiagnostic.ImplicitNumericCoercion(expression.Symbol.SelectionRange!, result.TypeInfo, coercedResult.TypeInfo);
                context.AddDiagnostic(diagnostic);
            }
        }

        return targetType switch
        {
            VBBooleanType => new VBBooleanValue(expression.Symbol).WithValue(coercedResult!.AsBoolean().Value),
            INumericType numericType => (VBTypedValue)targetType.CreateNumericValue(expression.Symbol).WithValue(numericResult!.NumericValue),
            _ => throw new InvalidOperationException($"Unexpected bitwise result type: {targetType.Name}")
        };
    }

    public static VBTypedValue EvaluateBinaryBitwiseOr(
    VBExecutionContext context,
    VBBinaryOperatorExpression expression,
    VBTypedValue lhs,
    VBTypedValue rhs) => EvaluateNumericBinaryOp(context, expression, lhs, rhs,
        (left, right) => (long)left | (long)right,
        out var lhsNumeric,
        out var rhsNumeric,
        out var targetType);

    public static VBTypedValue EvaluateBinaryBitwiseXOr(
    VBExecutionContext context,
    VBBinaryOperatorExpression expression,
    VBTypedValue lhs,
    VBTypedValue rhs) => EvaluateNumericBinaryOp(context, expression, lhs, rhs,
        (left, right) => (long)left ^ (long)right,
        out var lhsNumeric,
        out var rhsNumeric,
        out var targetType);

    public static VBTypedValue EvaluateBinaryBitwiseImp(
    VBExecutionContext context,
    VBBinaryOperatorExpression expression,
    VBTypedValue lhs,
    VBTypedValue rhs) => EvaluateNumericBinaryOp(context, expression, lhs, rhs,
        (left, right) => ~(long)left | ~(long)right,
        out var lhsNumeric,
        out var rhsNumeric,
        out var targetType);

    public static VBTypedValue EvaluateBinaryBitwiseEqv(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs) => EvaluateNumericBinaryOp(context, expression, lhs, rhs,
            (left, right) => ~((long)left ^ (long)right),
            out var lhsNumeric,
            out var rhsNumeric,
            out var targetType);

    public static VBTypedValue EvaluateBinaryMemberAccess(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBTypedValue rhs)
    {
        if (lhs.TypeInfo is VBVariantType)
        {
            context.AddDiagnostic(RDCoreDiagnostic.LateBoundMemberAccess(lhs.Symbol?.Range!));
        }

        if (lhs is IVBMemberOwnerType lhsOwner)
        {
            if (rhs.Symbol is not null)
            {
                var members = lhsOwner.Members.ToLookup(e => e.Name);
                var candidates = members[rhs.Symbol.Name];
                if (candidates.Any())
                {
                    // TODO make this statically deterministic, not based on where we found it in the source document.
                    var member = candidates.OrderBy(e => e.Uri).First();
                    var value = context.Memory.GetValue(member);

                    return value;
                }
                else
                {
                    // NOTE: LHS member owner could be a class, a stdmodule, an enum, or a UDT.
                    if (lhsOwner is not VBClassType lhsClassType)
                    {
                        throw VBCompileErrorException.MethodOrDataMemberNotFound(rhs.Symbol.SelectionRange!);
                    }

                    // if LHS is a class type, let's be nice and work with a deferred member instead:
                    return new VBDeferredMemberValue(expression.Symbol)
                           .WithContext(lhs)
                           .WithName(rhs.Symbol.Name)
                           .WithDiagnostic(RDCoreDiagnostic.UnresolvedLateBoundMemberAccess(rhs.Symbol.SelectionRange!));
                }
            }
            else
            {
                // user has typed the dot, but not the member name yet.
                // the members should be returned to the client to populate a completion list.
                // we're using VBVoidValue here to signal this:
                return VBVoidValue.Void;
            }
        }
        else
        {
            // Given a `NonExistingModule.NonExistingMember` member call where neither is defined:
            if (context.CurrentScope.ScopeSymbol is VBTypeMemberSymbol)
            {
                // VBA throws a compile error (variable not defined) if the code is inside the editor (scoped context)
                throw VBCompileErrorException.VariableNotDefined(lhs.Symbol?.SelectionRange!);
            }
            else
            {
                // VBA throws a runtime error (VBR00424 object required) if the same code is inside the immediate pane (default context)
                throw VBRuntimeErrorException.ObjectRequired(lhs.Symbol?.SelectionRange!);
            }
        }

        throw VBCompileErrorException.SyntaxError(expression.Location.Range, "An identifier is expected");
    }

    public static VBTypedValue EvaluateBinaryDictionaryAccess(
        VBExecutionContext context,
        VBBinaryOperatorExpression expression,
        VBTypedValue lhs,
        VBStringValue rhs)
    {
        context.AddDiagnostic(RDCoreDiagnostic.LateBoundMemberAccess(expression.Symbol.SelectionRange!));

        if (lhs is not IVBMemberOwnerType lhsOwner ||
            lhsOwner.Members.FirstOrDefault(member => member.Get(SymbolProperties.UserMemId) == 0) is not VBTypeMemberSymbol defaultMember)
        {
            throw VBRuntimeErrorException.ObjectDoesntSupportPropertyOrMethod(lhs.Symbol?.SelectionRange!);
        }

        if (rhs.Symbol is null)
        {
            // user has typed the bang, but not the member name yet (NOTE: it may have been parsed as a type hint)
            // the members could be returned to the client to populate a completion list.
            // we're using VBVoidValue here to signal this:
            return VBVoidValue.Void;
        }

        return new VBDeferredMemberValue(expression.Symbol)
               .WithContext(lhs)
               .WithName(rhs.Symbol.Name)
               .WithDiagnostic(RDCoreDiagnostic.UnresolvedLateBoundMemberAccess(rhs.Symbol.SelectionRange!));
    }
}