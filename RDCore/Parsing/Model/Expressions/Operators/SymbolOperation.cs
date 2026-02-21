using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Server;

namespace RDCore.Parsing.Model.Expressions.Operators;

internal static class SymbolOperation
{
    internal delegate VBTypedValue BinaryOperation(
        VBExecutionContext context,
        VBBinaryOperatorExpression operation,
        VBTypedValue lhsValue,
        VBTypedValue rhsValue);

    private static readonly Dictionary<Uri, BinaryOperation> _binaryInstructions = new()
    {
        { GlobalSymbols.Addition.Uri, EvaluateAddition }
    };

    public static VBTypedValue EvaluateAddition(
        VBExecutionContext context,
        VBBinaryOperatorExpression operation,
        VBTypedValue lhs,
        VBTypedValue rhs)
    {
        // MS-VBAL 5.6.9.4: If either operand is Null, the result is Null.
        if (lhs is VBNullValue || rhs is VBNullValue) { return VBNullValue.Null; }

        // MS-VBAL 5.6.9.4: If both operands are Empty, the result is an Integer 0.
        if (lhs is VBEmptyValue && rhs is VBEmptyValue) { return VBIntegerValue.Zero; }

        // MS-VBAL 5.4.1.2: Numeric String Conversions
        // If one operand is a String and the other is a numeric, the String operand is converted to a Double.
        // RDC00109 is issued for each such implicit coercion, twice if both sides require numeric coercion.
        var lhsNumeric = lhs.TypeInfo is INumericType ? (VBNumericTypedValue)lhs : VBDoubleValue.Zero;
        var rhsNumeric = rhs.TypeInfo is INumericType ? (VBNumericTypedValue)rhs : VBDoubleValue.Zero;

        if (lhs is VBStringValue lhsString)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(operation.Left.Location.Range, lhs.TypeInfo, VBDoubleType.TypeInfo));
            lhsNumeric = lhsString.AsCoercedNumeric();
        }

        if (rhs is VBStringValue rhsString)
        {
            context.AddDiagnostic(RDCoreDiagnostic.ImplicitNumericCoercion(operation.Right.Location.Range, rhs.TypeInfo, VBDoubleType.TypeInfo));
            rhsNumeric = rhsString.AsCoercedNumeric();
        }

        if (lhs is VBStringValue && rhs is VBStringValue)
        {
            // if both operands are strings, then this is a concatenation, not an addition.
            // RDC11006 is issued for the ambiguous concatenation operator usage.
            context.AddDiagnostic(RDCoreDiagnostic.AmbiguousConcatenation(operation.Symbol.Range!));
        }

        // MS-VBAL 5.6.9.4: Addition Operator Promotion Table
        // "The result type is determined by the types of the operands..."
        var targetType = GetPromotedType(lhsNumeric.TypeInfo, rhsNumeric.TypeInfo);

        // MS-VBAL 5.6.9.4: Date Math Rule
        // "If one operand is a Date and the other is numeric, the result is a Date."
        if (lhs.TypeInfo is VBDateType || rhs.TypeInfo is VBDateType)
        {
            // Date math is effectively Double math re-wrapped
            return new VBDateValue(operation.Symbol).WithValue(lhsNumeric.NumericValue + rhsNumeric.NumericValue);
        }

        // MS-VBAL 5.6.9.4: Overflow Check [VBR0006]
        // If the result is too large for the value range of the result type, a run-time error is raised.
        var resultValue = lhsNumeric.NumericValue + rhsNumeric.NumericValue;

        return (VBTypedValue)((VBNumericTypedValue)targetType.CreateValue(operation.Symbol)).WithValue(resultValue);
    }

    private static VBType GetPromotedType(VBType lhs, VBType rhs)
    {
        // MS-VBAL 5.6.9.4: Type Promotion Hierarchy
        // Double > Single > Long > Integer > Byte
        if (lhs is VBDoubleType || rhs is VBDoubleType) return VBDoubleType.TypeInfo;
        if (lhs is VBSingleType || rhs is VBSingleType) return VBSingleType.TypeInfo;
        if (lhs is VBLongType || rhs is VBLongType) return VBLongType.TypeInfo;

        return VBIntegerType.TypeInfo;
    }
}