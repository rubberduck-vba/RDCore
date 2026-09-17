using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;

namespace RDCore.Tests.Semantics.Static.Expressions;

/// <summary>
/// Characterization matrix for <see cref="NewExpressionStaticSemantics"/> — MS-VBAL 5.6.8: a New
/// expression is invalid if the type referenced by its TypeExpression is not instantiable.
/// </summary>
[TestClass]
public sealed class NewExpressionStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);
    private static VBClassModuleSymbol ClassModule(string name) => new(Root, Root, name);

    private static VBModuleFieldVariableMemberSymbol Field(Uri moduleUri, string name, VBType type)
        => new(Root, moduleUri, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [42]), TestLocations.TestLocation, identifier);

    private static NewExpressionNode NewOf(ExpressionNode typeExpression)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [42]), TestLocations.TestLocation, typeExpression);

    private static StaticEvaluationContext ContextAt(Uri scopeUri, params Symbol[] symbols)
    {
        var tree = ScopeTreeBuilder.Build(symbols);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(scopeUri));
    }

    [TestMethod]
    public void ResolvesToAClassModule_ReturnsVBObjectType()
    {
        var module = Module("Caller");
        var classModule = ClassModule("Collection1");
        var context = ContextAt(module.Uri, module, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Collection1")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBObjectType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ResolvesToANonClassSymbol_IsATypeMismatchError()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total", VBLongType.TypeInfo);
        var context = ContextAt(module.Uri, module, field);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Total")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AnUnresolvedName_IsAnError()
    {
        var module = Module("Mod1");
        var context = ContextAt(module.Uri, module);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Nope")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.UserDefinedTypeNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AQualifiedTypeExpression_SucceedsAsUnknown()
        // New Project.ClassName isn't modeled yet (TypeExpression isn't a SimpleNameExpressionNode) -
        // defer rather than misreport an instantiability error for a shape this rule doesn't understand.
    {
        var module = Module("Mod1");
        var context = ContextAt(module.Uri, module);
        var notASimpleName = new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, VBUnknownType.TypeInfo.DefaultValue);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(notASimpleName));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ANonNewExpression_Throws()
    {
        var module = Module("Mod1");
        var context = ContextAt(module.Uri, module);

        Assert.ThrowsExactly<ArgumentException>(() => NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Foo")));
    }
}
