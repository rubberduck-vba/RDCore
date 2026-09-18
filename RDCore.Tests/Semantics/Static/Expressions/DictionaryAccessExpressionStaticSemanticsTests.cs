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
public sealed class DictionaryAccessExpressionStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly StaticEvaluationContext Context = new(
        NSubstitute.Substitute.For<ISymbolResolver>(),
        new LexicalScope(StaticSymbol.GlobalUri, LexicalScopeKind.Global, parent: null, []));

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [60]), TestLocations.TestLocation, identifier);

    private static DictionaryAccessExpressionNode Access(string ownerName, string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [61]), TestLocations.TestLocation, NameOf(ownerName), NameOf(memberName));

    private static DictionaryAccessExpressionNode WithRelativeAccess(string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [61]), TestLocations.TestLocation, Owner: null, NameOf(memberName));

    private static VBClassType Class(string name, VBTypeMemberSymbol? defaultMember = null)
        => new(new VBClassModuleSymbol(Root, Root, name), []) { DefaultMember = defaultMember };

    private static VBFunctionMemberSymbol Method(string name, VBType returnType)
        => new(Root, Root, name, ScopeKind.Instance, SymbolKindExt.Function, returnType, R, R, AccessModifier.Public);

    private static VBUserDefinedType Udt(string name)
        => new(new VBUserDefinedTypeMemberSymbol(Root, Root, name, ScopeKind.Module, R, R, AccessModifier.Public), []);

    [TestMethod]
    public void ClassOwnerWithADefaultMember_ResolvesToTheDefaultMembersReturnType()
    {
        var dictionary = Class("Dictionary", defaultMember: Method("Item", VBVariantType.TypeInfo));

        var result = DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("dict", "key"), dictionary);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ClassOwnerWithoutADefaultMember_IsAMethodOrDataMemberNotFoundError()
    {
        var widget = Class("Widget");

        var result = DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("obj", "key"), widget);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.MethodOrDataMemberNotFound, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void VariantOwner_IsLateBound_SucceedsAsVariant()
    {
        var result = DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("v", "key"), VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ObjectOwner_IsLateBound_SucceedsAsVariant()
    {
        var result = DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("o", "key"), VBObjectType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AUdtOwner_IsATypeMismatchError()
        // MS-VBAL 5.6.14: valid only for a specific class, Object, or Variant - unlike TypeOf...Is, a
        // UDT does not qualify here.
    {
        var udt = Udt("TPoint");

        var result = DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("point", "key"), udt);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AnArrayOwner_IsATypeMismatchError()
    {
        var array = new VBFixedSizeArrayType(VBLongType.TypeInfo);

        var result = DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("data", "key"), array);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AnUnknownOwner_IsDeferredNotAnError()
    {
        var result = DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, Access("whatever", "key"), VBUnknownType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ANonDictionaryAccessExpression_Throws()
    {
        var notDictionaryAccess = NameOf("bareName");

        Assert.ThrowsExactly<ArgumentException>(() => DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, notDictionaryAccess, VBLongType.TypeInfo));
    }

    [TestMethod]
    public void AWithRelativeAccess_ResolvesAgainstTheSubstitutedOwnerType()
        // With-relative resolution (MS-VBAL 5.6.15) is the caller's (ExpressionStaticSemanticsEvaluator's)
        // job to substitute the enclosing With target's type in for the missing Owner - this rule never
        // reads Owner itself, so a null Owner with a real operand type behaves identically to a real one.
    {
        var dictionary = Class("Dictionary", defaultMember: Method("Item", VBVariantType.TypeInfo));

        var result = DictionaryAccessExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, WithRelativeAccess("key"), dictionary);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }
}
