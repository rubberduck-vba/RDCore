using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime;

namespace RDCore.Tests.Semantics.Runtime;

public abstract class BinaryOperatorOperationTests : SymbolOperationTests
{
    protected abstract BinaryOperatorSymbol Symbol { get; }

    private static VBLiteralExpression CreateOperand(object value, Location location) => new(location, WrapVBTypedValue(value, location));
    private static VBBinaryOperatorExpression CreateOpExpression(BinaryOperatorSymbol operatorSymbol, VBTypedValue lhs, VBTypedValue rhs)
        => new(operatorSymbol, TestLocation, CreateOperand(lhs, TestLocationLHS), CreateOperand(rhs, TestLocationRHS));

    protected VBTypedValue EvaluateBinaryOp(IVBExecutionContext context, object lhs, object rhs)
    {
        var lhsValue = WrapVBTypedValue(lhs, TestLocationLHS);
        var rhsValue = WrapVBTypedValue(rhs, TestLocationRHS);
        var expression = CreateOpExpression(Symbol, lhsValue, rhsValue);

        if (lhsValue?.TypeInfo is null)
        {
            Assert.Inconclusive("LHS is unexpectedly null");
        }
        if (rhsValue?.TypeInfo is null)
        {
            Assert.Inconclusive("RHS is unexpectedly null");
        }

        return Semantics.Evaluate(context, expression, lhsValue, rhsValue)!;
    }

}
