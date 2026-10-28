using NSubstitute;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;
using System.Collections.Immutable;

namespace RDCore.Tests.Semantics.Static.Expressions;

[TestClass]
public sealed class IndexExpressionStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly StaticEvaluationContext Context = new(
        NSubstitute.Substitute.For<ISymbolResolver>(),
        new LexicalScope(StaticSymbol.GlobalUri, LexicalScopeKind.Global, parent: null, []));

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [50]), TestLocations.TestLocation, identifier);

    private static IndexExpressionNode IndexOf(string calleeName, params ExpressionNode[] arguments)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [51]), TestLocations.TestLocation, NameOf(calleeName), [.. arguments]);

    private static VBClassType Class(string name, VBTypeMemberSymbol? defaultMember = null)
        => new(new VBClassModuleSymbol(Root, Root, name), []) { DefaultMember = defaultMember };

    private static VBFunctionMemberSymbol Method(string name, VBType returnType)
        => new(Root, Root, name, ScopeKind.Instance, SymbolKindExt.Function, returnType, R, R, AccessModifier.Public);

    [TestMethod]
    public void ArrayCallee_ResolvesToTheElementType()
    {
        var array = new VBFixedSizeArrayType(VBLongType.TypeInfo);

        var result = IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, IndexOf("data", NameOf("i")), array);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void VariantCallee_IsLateBound_SucceedsAsVariant()
    {
        var result = IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, IndexOf("v", NameOf("i")), VBVariantType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ObjectCallee_IsLateBound_SucceedsAsVariant()
    {
        var result = IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, IndexOf("o", NameOf("i")), VBObjectType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ClassCalleeWithADefaultMember_ResolvesToTheDefaultMembersReturnType()
    {
        var collection = Class("Collection", defaultMember: Method("Item", VBVariantType.TypeInfo));

        var result = IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, IndexOf("items", NameOf("i")), collection);

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBVariantType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ClassCalleeWithoutADefaultMember_IsAMethodOrDataMemberNotFoundError()
    {
        var widget = Class("Widget");

        var result = IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, IndexOf("obj", NameOf("i")), widget);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.MethodOrDataMemberNotFound, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void FunctionReturnTypeCallee_PassesThroughUnchanged()
        // the callee's declared type already IS a function/property-get's return type (see
        // SimpleNameExpressionStaticSemantics/MemberAccessExpressionStaticSemantics) - the index
        // expression just takes on that same classification and type, unchanged.
    {
        var result = IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, IndexOf("GetValue", NameOf("i")), VBLongType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void UnknownCallee_PassesThroughUnchanged()
    {
        // a name that is declared, of a type not known yet: a variable whose type is inferred.
        var resolver = NSubstitute.Substitute.For<ISymbolResolver>();
        resolver.ResolveValue("whatever", Arg.Any<ScopeKind>(), Arg.Any<Uri>())
            .Returns(SymbolResolutionResult.Resolved(new VBModuleFieldVariableMemberSymbol(Root, Root, "whatever", ScopeKind.Module, VBUnknownType.TypeInfo, R, R, AccessModifier.Private)));
        var result = IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context with { Resolver = resolver }, IndexOf("whatever", NameOf("i")), VBUnknownType.TypeInfo);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ANameNothingDeclares_CalledWithArguments_IsNotASubOrFunction()
    {
        // the language server declares an implicit variable for every name nothing declares: one that is still not declared was declared by what is not
        // compiled, a procedure written only in a dead #If branch.
        var result = IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, IndexOf("whatever", NameOf("i")), VBUnknownType.TypeInfo);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(VBCompileErrorId.SubOrFunctionNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ANonIndexExpression_Throws()
    {
        var notIndexExpression = NameOf("bareName");

        Assert.ThrowsExactly<ArgumentException>(() => IndexExpressionStaticSemantics.Instance.DetermineDeclaredType(Context, notIndexExpression, VBLongType.TypeInfo));
    }
}
