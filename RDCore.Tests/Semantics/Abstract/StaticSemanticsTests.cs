using NSubstitute;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
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
        var scope = new LexicalScope(StaticSymbol.GlobalUri, LexicalScopeKind.Global, parent: null, []);
        var context = new StaticEvaluationContext(resolver, scope);
        var expression = new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [42]), TestLocations.TestLocation, VBUnknownType.TypeInfo.DefaultValue);
        var result = semantics.DetermineDeclaredType(context, expression, operandDeclaredTypes);
        Assert.AreEqual(expected, result.Result);
    }
}
