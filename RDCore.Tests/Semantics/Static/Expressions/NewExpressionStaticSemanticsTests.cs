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
using RDCore.SDK.Model.Types.Complex;
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

    private static VBClassModuleSymbol ClassModule(string name, bool creatable = true)
        => (VBClassModuleSymbol)new VBClassModuleSymbol(Root, Root, name).With(SymbolProperties.Creatable, creatable);

    private static VBModuleFieldVariableMemberSymbol Field(Uri moduleUri, string name, VBType type)
        => new(Root, moduleUri, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [42]), TestLocations.TestLocation, identifier);

    private static MemberAccessExpressionNode MemberOf(ExpressionNode owner, string memberName)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [43]), TestLocations.TestLocation, owner, NameOf(memberName));

    private static NewExpressionNode NewOf(ExpressionNode typeExpression)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [42]), TestLocations.TestLocation, typeExpression);

    private static StaticEvaluationContext ContextAt(Uri scopeUri, params Symbol[] symbols)
    {
        var tree = ScopeTreeBuilder.Build(symbols);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(scopeUri));
    }

    [TestMethod]
    public void ResolvesToAClassModule_ReturnsItsVBClassType()
    {
        var module = Module("Caller");
        var classModule = ClassModule("Collection1");
        var context = ContextAt(module.Uri, module, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Collection1")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        var classType = Assert.IsInstanceOfType<VBClassType>(result.Result);
        Assert.AreEqual("Collection1", classType.Name);
    }

    [TestMethod]
    public void ResolvesToAClassModule_CarriesItsMemberList()
        // proves the class's Members - populated by WorkspaceSymbolResolver.Compose's second pass,
        // not built here - flow straight through into the resulting VBClassType.
    {
        var module = Module("Caller");
        var classModule = ClassModule("Collection1");
        var field = Field(classModule.Uri, "Count", VBLongType.TypeInfo);
        var populated = classModule with { Members = [field] };
        var context = ContextAt(module.Uri, module, populated);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Collection1")));

        var classType = Assert.IsInstanceOfType<VBClassType>(result.Result);
        Assert.HasCount(1, classType.Members);
        Assert.AreEqual("Count", classType.Members[0].Name);
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
    public void AMalformedTypeExpressionShape_SucceedsAsUnknown()
        // a shape this rule doesn't understand (neither a bare name nor an owner.member qualified
        // reference) - defer rather than misreport an instantiability error.
    {
        var module = Module("Mod1");
        var context = ContextAt(module.Uri, module);
        var notAName = new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, VBUnknownType.TypeInfo.DefaultValue);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(notAName));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void QualifiedByTheEnclosingProjectsOwnName_ResolvesTheClass()
        // MS-VBAL 5.6.4's type binding context: New Project.ClassName where Project is the enclosing
        // project's own name resolves ClassName the same as the unqualified form.
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var module = Module("Caller");
        var classModule = ClassModule("Collection1");
        var context = ContextAt(module.Uri, project, module, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(
            context, NewOf(MemberOf(NameOf("MyProject"), "Collection1")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual("Collection1", Assert.IsInstanceOfType<VBClassType>(result.Result).Name);
    }

    [TestMethod]
    public void QualifiedByAnUnknownName_IsAnError()
        // "Foo" doesn't resolve to a VBProjectSymbol at all (no referenced-project namespace is
        // modeled yet) - stays unbound rather than silently falling through to the bare name.
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var module = Module("Caller");
        var classModule = ClassModule("Collection1");
        var context = ContextAt(module.Uri, project, module, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(
            context, NewOf(MemberOf(NameOf("SomeOtherProject"), "Collection1")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.UserDefinedTypeNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ANonCreatableClass_IsATypeMismatchError()
        // Attribute VB_Creatable = False - the workspace's own classes are always creatable in
        // practice; this exercises the check itself ahead of referenced-library classes actually
        // being reachable (no library symbol provider exists yet).
    {
        var module = Module("Caller");
        var classModule = ClassModule("Collection1", creatable: false);
        var context = ContextAt(module.Uri, module, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Collection1")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ANonNewExpression_Throws()
    {
        var module = Module("Mod1");
        var context = ContextAt(module.Uri, module);

        Assert.ThrowsExactly<ArgumentException>(() => NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Foo")));
    }
}
