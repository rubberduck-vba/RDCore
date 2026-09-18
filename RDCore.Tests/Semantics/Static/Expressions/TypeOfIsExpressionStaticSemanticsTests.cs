using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;

namespace RDCore.Tests.Semantics.Static.Expressions;

[TestClass]
public sealed class TypeOfIsExpressionStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly StaticEvaluationContext Context = new(
        NSubstitute.Substitute.For<ISymbolResolver>(),
        new LexicalScope(StaticSymbol.GlobalUri, LexicalScopeKind.Global, parent: null, []));

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [70]), TestLocations.TestLocation, identifier);

    private static TypeOfIsExpressionNode TypeOfIsOf(string operandName, string typeName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [71]), TestLocations.TestLocation, NameOf(operandName), NameOf(typeName));

    private static VBClassType Class(string name)
        => new(new VBClassModuleSymbol(Root, Root, name), []);

    private static VBUserDefinedType Udt(string name)
        => new(new VBUserDefinedTypeMemberSymbol(Root, Root, name, ScopeKind.Module, R, R, AccessModifier.Public), []);

    [TestMethod]
    public void AClassOperand_ResolvesToBoolean()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("obj", "Widget"), Class("Widget"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AUdtOperand_ResolvesToBoolean()
        // MS-VBAL 5.6.7 explicitly allows "a specific UDT", unlike dictionary access.
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("point", "TPoint"), Udt("TPoint"));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnObjectOperand_ResolvesToBoolean()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("o", "Widget"), VBObjectType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AVariantOperand_ResolvesToBoolean()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("v", "Widget"), VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnUnknownOperand_ResolvesToBoolean_DeferredNotAnError()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("whatever", "Widget"), VBUnknownType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnIntrinsicValueTypeOperand_IsATypeMismatchError()
    {
        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("n", "Widget"), VBLongType.TypeInfo);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AnArrayOperand_IsATypeMismatchError()
    {
        var array = new VBFixedSizeArrayType(VBLongType.TypeInfo);

        var result = TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, TypeOfIsOf("data", "Widget"), array);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ANonTypeOfIsExpression_Throws()
    {
        var notTypeOfIs = NameOf("bareName");

        Assert.ThrowsExactly<ArgumentException>(() => TypeOfIsExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, notTypeOfIs, VBLongType.TypeInfo));
    }
}
