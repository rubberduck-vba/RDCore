using NSubstitute;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Tests.Semantics.Abstract;

public abstract class StaticSemanticsTests
{
    public static void AssertDeterminedDeclaredType(StaticSemantics semantics, VBType[] operandDeclaredTypes, VBType expected)
    {
        var resolver = Substitute.For<ISymbolResolver>();
        var expression = new VBLiteralExpression(TestUri.TestVariableUri(), TestLocations.TestLocation, VBUnknownType.TypeInfo.DefaultValue);
        var result = semantics.DetermineDeclaredType(resolver, expression, operandDeclaredTypes);
        Assert.AreEqual(expected, result.Result);
    }
}
