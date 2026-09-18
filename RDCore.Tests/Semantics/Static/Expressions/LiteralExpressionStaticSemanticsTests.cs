using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;

namespace RDCore.Tests.Semantics.Static.Expressions;

/// <summary>
/// MS-VBAL 5.6.5: the declared type of a literal expression is that of its own token.
/// </summary>
[TestClass]
public sealed class LiteralExpressionStaticSemanticsTests
{
    private static readonly StaticEvaluationContext Context = new(
        NSubstitute.Substitute.For<ISymbolResolver>(),
        new LexicalScope(StaticSymbol.GlobalUri, LexicalScopeKind.Global, parent: null, []));

    private static LiteralExpressionNode LiteralOf(VBTypedValue value)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [80]), TestLocations.TestLocation, value);

    private static void AssertResolvesTo(VBTypedValue value, VBType expected)
    {
        var result = LiteralExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, LiteralOf(value));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(expected, result.Result);
    }

    [TestMethod]
    public void AnIntegerLiteral_ResolvesToInteger()
        => AssertResolvesTo(new VBIntegerValue((short)7), VBIntegerType.TypeInfo);

    [TestMethod]
    public void ALongLiteral_ResolvesToLong()
        => AssertResolvesTo(new VBLongValue(7), VBLongType.TypeInfo);

    [TestMethod]
    public void ALongLongLiteral_ResolvesToLongLong()
        => AssertResolvesTo(new VBLongLongValue(7L), VBLongLongType.TypeInfo);

    [TestMethod]
    public void ABooleanLiteral_ResolvesToBoolean()
        => AssertResolvesTo(new VBBooleanValue(true), VBBooleanType.TypeInfo);

    [TestMethod]
    public void AnExpressionThatIsNotALiteral_IsRejected()
    {
        var name = new SimpleNameExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [81]), TestLocations.TestLocation, "x");

        Assert.ThrowsExactly<ArgumentException>(() => LiteralExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, name));
    }
}
