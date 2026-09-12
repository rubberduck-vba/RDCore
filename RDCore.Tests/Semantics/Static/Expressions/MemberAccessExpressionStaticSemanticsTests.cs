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
public sealed class MemberAccessExpressionStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly StaticEvaluationContext Context = new(
        NSubstitute.Substitute.For<ISymbolResolver>(),
        new LexicalScope(StaticSymbol.GlobalUri, LexicalScopeKind.Global, parent: null, []));

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [42]), TestLocations.TestLocation, identifier);

    private static MemberAccessExpressionNode Access(string ownerName, string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [43]), TestLocations.TestLocation, NameOf(ownerName), NameOf(memberName));

    private static MemberAccessExpressionNode WithRelativeAccess(string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [43]), TestLocations.TestLocation, Owner: null, NameOf(memberName));

    private static VBUserDefinedType Udt(string name, params VBTypeMemberSymbol[] fields)
    {
        var symbol = new VBUserDefinedTypeMemberSymbol(Root, Root, name, ScopeKind.Module, R, R, AccessModifier.Public);
        return new VBUserDefinedType(symbol, [.. fields]);
    }

    private static VBUserDefinedTypeFieldSymbol Field(string name, VBType type)
        => new(Root, Root, name, type, R, R, AccessModifier.Public);

    private static VBEnumType Enum(string name, params string[] members)
    {
        var symbol = new VBStandardModuleSymbol(Root, Root, name);
        return new VBEnumType(symbol, members.Select(member => new VBEnumConstMemberSymbol(Root, Root, member, ScopeKind.Module, SymbolKindExt.EnumMember, R, R)));
    }

    private static VBClassType Class(string name, params VBTypeMemberSymbol[] members)
        => new(new VBClassModuleSymbol(Root, Root, name), [.. members]);

    private static VBFunctionMemberSymbol Method(string name, VBType returnType)
        => new(Root, Root, name, ScopeKind.Instance, SymbolKindExt.Function, returnType, R, R, AccessModifier.Public);

    [TestMethod]
    public void ResolvesAUdtField_ReturnsItsDeclaredType()
    {
        var udt = Udt("TPoint", Field("X", VBLongType.TypeInfo), Field("Y", VBLongType.TypeInfo));

        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("point", "X"), udt);

        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ResolvesAUdtField_CaseInsensitively()
    {
        var udt = Udt("TPoint", Field("X", VBLongType.TypeInfo));

        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("point", "x"), udt);

        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AUdtFieldNotDeclared_IsAMethodOrDataMemberNotFoundError()
    {
        var udt = Udt("TPoint", Field("X", VBLongType.TypeInfo));

        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("point", "Z"), udt);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.MethodOrDataMemberNotFound, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ResolvesAnEnumMember_ReturnsTheEnumsUnderlyingType()
    {
        var colour = Enum("Colour", "Red", "Green");

        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("colour", "Red"), colour);

        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnEnumMemberNotDeclared_IsAMethodOrDataMemberNotFoundError()
    {
        var colour = Enum("Colour", "Red");

        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("colour", "Blue"), colour);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.MethodOrDataMemberNotFound, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ResolvesAClassMethod_ReturnsItsReturnType()
    {
        var widget = Class("Widget", Method("Refresh", VBBooleanType.TypeInfo));

        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("obj", "Refresh"), widget);

        Assert.AreEqual(VBBooleanType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AClassMemberNotDeclared_IsDeferredNotAnError()
    {
        var widget = Class("Widget");

        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("obj", "Whatever"), widget);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AVariantLeftHandSide_IsLateBound_SucceedsAsVariant()
    {
        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("v", "Anything"), VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnObjectLeftHandSide_IsLateBound_SucceedsAsVariant()
    {
        var result = MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("o", "Anything"), VBObjectType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ANonMemberAccessExpression_Throws()
    {
        var notMemberAccess = NameOf("bareName");

        Assert.ThrowsExactly<ArgumentException>(() => MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, notMemberAccess, VBLongType.TypeInfo));
    }

    [TestMethod]
    public void AWithRelativeAccess_OwnerIsImplicit_IsNotYetSupported()
    {
        Assert.ThrowsExactly<NotSupportedException>(
            () => MemberAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, WithRelativeAccess("Whatever")));
    }
}
