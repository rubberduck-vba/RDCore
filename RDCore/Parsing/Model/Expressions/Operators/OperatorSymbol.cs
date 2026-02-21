using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;
using RDCore.Runtime;
using RDCore.Runtime.Model.Operators;
using RDCore.Server.ProtocolExtensions;
using static RDCore.Parsing.Model.Expressions.Operators.SymbolOperation;

namespace RDCore.Parsing.Model.Expressions.Operators;

internal abstract record class OperatorSymbol : StaticSymbol
{
    protected OperatorSymbol(string name, BinaryOperation operation, VBType? vbType = default)
        : base(name, SymbolKindExt.Operator, vbType)
    {
        Execute = operation;
    }

    public BinaryOperation Execute { get; init; }
}

internal abstract record class BinaryOperatorSymbol : OperatorSymbol
{
    protected BinaryOperatorSymbol(string name, BinaryOperation operation, VBType? vbType = null)
        : base(name, operation, vbType)
    {
    }

    protected static bool CanConvertSafely(VBTypedValue lhsValue, VBTypedValue rhsValue)
        => lhsValue.TypeInfo.ConvertsSafelyToTypes.Contains(rhsValue.TypeInfo);
}

internal abstract record class BitwiseOperatorSymbol : BinaryOperatorSymbol
{
    protected BitwiseOperatorSymbol(string name, VBType? vbType = null)
        : base(name, null!, vbType)
    {
        Execute = ExecuteInternal;
    }

    private VBTypedValue ExecuteInternal(VBExecutionContext context, VBOperatorExpression expression, VBTypedValue lhs, VBTypedValue rhs)
    {
        //var result = SymbolOperation.EvaluateBinaryOpResult(context, this, lhs, rhs);
        return UnresolvedType.VBType.DefaultValue;
    }
}

internal record class AdditionOperatorSymbol(VBType? VBType = default) : BinaryOperatorSymbol(Tokens.AdditionOp, SymbolOperation.EvaluateAddition) { }
//internal record class SubtractionOperationSymbol(VBType? VBType = default) : BinaryOperatorSymbol(Tokens.SubtractionOp) { }
//internal record class LogicalAndOperatorSymbol() : BinaryOperatorSymbol(Tokens.LogicalAndOp, VBBooleanType.TypeInfo) { }
