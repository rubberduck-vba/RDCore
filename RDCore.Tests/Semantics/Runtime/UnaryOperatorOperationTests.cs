using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.Tests.Semantics.Runtime;

public abstract class UnaryOperatorOperationTests : SymbolOperationTests
{
    protected virtual UnaryOperatorSymbol Symbol => GlobalSymbols.OperatorSymbols.UnaryAdditionOp;

    private static VBLiteralExpression CreateOperand(object value, Location location) => new(location, WrapVBTypedValue(value, location));
    private static VBUnaryOperatorExpression CreateOpExpression(UnaryOperatorSymbol operatorSymbol, VBTypedValue operand)
        => new(operatorSymbol, TestLocation, CreateOperand(operand, TestLocationLHS));

    protected VBTypedValue EvaluateUnaryOp(IVBExecutionContext context, object operand)
    {
        var operandValue = WrapVBTypedValue(operand, TestLocationLHS);
        var expression = CreateOpExpression(Symbol, operandValue);

        if (operandValue?.TypeInfo is null)
        {
            Assert.Inconclusive("Operand is unexpectedly null");
        }

        return Semantics.Evaluate(context, expression, operandValue)!;
    }
}
