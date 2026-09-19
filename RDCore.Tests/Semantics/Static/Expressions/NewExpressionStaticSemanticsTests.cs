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
    public void ResolvesToAClassModule_CarriesItsDefaultInterface()
        // proves the class's DefaultInterfaceMembers - populated once by WorkspaceSymbolResolver.
        // Compose's second pass, not built here - flow straight through into the resulting VBClassType.
    {
        var module = Module("Caller");
        var classModule = ClassModule("Collection1");
        var field = Field(classModule.Uri, "Count", VBLongType.TypeInfo);
        var populated = classModule with { Members = [field], DefaultInterfaceMembers = [field] };
        var context = ContextAt(module.Uri, module, populated);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Collection1")));

        var classType = Assert.IsInstanceOfType<VBClassType>(result.Result);
        Assert.HasCount(1, classType.Members);
        Assert.AreEqual("Count", classType.Members[0].Name);
    }

    [TestMethod]
    public void DoesNotRecomputeTheDefaultInterfaceFromMembers()
        // guards against re-introducing VBClassType.FromClassModule at resolution time: Members here
        // holds only a field that Compose would have excluded from the default interface, yet
        // DefaultInterfaceMembers (the precomputed value) is what must come back - proving this rule
        // reads it directly rather than rebuilding it from Members on every New.
    {
        var module = Module("Caller");
        var classModule = ClassModule("Collection1");
        var excludedFromInterface = Field(classModule.Uri, "Internal", VBLongType.TypeInfo);
        var precomputed = Field(classModule.Uri, "Count", VBLongType.TypeInfo);
        var populated = classModule with { Members = [excludedFromInterface], DefaultInterfaceMembers = [precomputed] };
        var context = ContextAt(module.Uri, module, populated);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Collection1")));

        var classType = Assert.IsInstanceOfType<VBClassType>(result.Result);
        Assert.HasCount(1, classType.Members);
        Assert.AreEqual("Count", classType.Members[0].Name);
    }

    [TestMethod]
    public void ResolvesToAModuleThatIsNotAClass_IsATypeMismatchError()
    {
        var module = Module("Mod1");
        var other = Module("Mod2");
        var context = ContextAt(module.Uri, module, other);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Mod2")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ANameThatIsOnlyAUserDefinedType_IsNotFound_BecauseNewNeverLooksForOne()
        // New instantiates classes: a user-defined type is not a candidate for its operand, so this is an
        // undefined type rather than a type that fails to be a class.
    {
        var module = Module("Mod1");
        var udt = new VBUserDefinedTypeMemberSymbol(Root, module.Uri, "Point", ScopeKind.Module, R, R, AccessModifier.Public);
        var context = ContextAt(module.Uri, module, udt);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Point")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.UserDefinedTypeNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AUserDefinedTypeOfTheClassesName_DoesNotHideTheClass()
        // the enclosing module's own Type is the first tier of the type binding context, so it would win the name
        // there; New never looks for a Type, so the class binds.
    {
        var module = Module("Caller");
        var udt = new VBUserDefinedTypeMemberSymbol(Root, module.Uri, "Collection1", ScopeKind.Module, R, R, AccessModifier.Private);
        var classModule = ClassModule("Collection1");
        var context = ContextAt(module.Uri, module, udt, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Collection1")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual("Collection1", Assert.IsInstanceOfType<VBClassType>(result.Result).Name);
    }

    [TestMethod]
    public void NamesAVariable_IsNotAType_UserDefinedTypeNotDefined()
        // the operand of New binds in the type binding context (MS-VBAL 5.6.4): a variable is never a
        // candidate there, so it is not a type that could fail to be a class - it is not a type at all.
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total", VBLongType.TypeInfo);
        var context = ContextAt(module.Uri, module, field);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Total")));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.UserDefinedTypeNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AVariableNamedLikeTheClass_DoesNotHideTheClass()
        // `Dim Widget As New Widget`-style shadowing: the class binds in the type binding context however
        // the variable is named.
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Collection1", VBLongType.TypeInfo);
        var classModule = ClassModule("Collection1");
        var context = ContextAt(module.Uri, module, field, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(NameOf("Collection1")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual("Collection1", Assert.IsInstanceOfType<VBClassType>(result.Result).Name);
    }

    [TestMethod]
    public void QualifiedByTheProjectsName_IgnoresAVariableOfThatName()
        // the legacy Rubberduck bug (issue #973, comment 3): `New MyProject.Class` bound MyProject to a
        // local variable. A variable is never a candidate for the qualifier in the type binding context.
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var module = Module("Caller");
        var procedure = new VBProcedureMemberSymbol(Root, module.Uri, "Run", ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Implicit);
        var local = new VBLocalVariableSymbol(Root, procedure.Uri, "MyProject", ScopeKind.Local, R, R);
        var classModule = ClassModule("Collection1");
        var context = ContextAt(procedure.Uri, project, module, procedure, local, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(MemberOf(NameOf("MyProject"), "Collection1")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual("Collection1", Assert.IsInstanceOfType<VBClassType>(result.Result).Name);
    }

    [TestMethod]
    public void QualifiedByTheProjectsName_WhenTheModuleDeclaresATypeOfThatName_IsStillTheProject()
        // New never looks for a user-defined type, so the Type declared at module level - which would win the
        // name in the type binding context - is not a candidate for the qualifier, and `MyProject` binds the
        // project. This is the shape of `New MyProject.Class` in legacy Rubberduck issue #973.
    {
        var project = new VBProjectSymbol(Root, "MyProject");
        var module = Module("Caller");
        var udt = new VBUserDefinedTypeMemberSymbol(Root, module.Uri, "MyProject", ScopeKind.Module, R, R, AccessModifier.Private);
        var classModule = ClassModule("Collection1");
        var context = ContextAt(module.Uri, project, module, udt, classModule);

        var result = NewExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NewOf(MemberOf(NameOf("MyProject"), "Collection1")));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual("Collection1", Assert.IsInstanceOfType<VBClassType>(result.Result).Name);
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
