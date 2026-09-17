using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;

namespace RDCore.Tests.Semantics.Static.Expressions;

/// <summary>
/// Characterization matrix for <see cref="InstanceExpressionStaticSemantics"/> — MS-VBAL 5.6.11: the
/// declared type of Me is the type defined by the class module containing the enclosing procedure;
/// invalid outside a class module.
/// </summary>
[TestClass]
public sealed class InstanceExpressionStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBClassModuleSymbol ClassModule(string name) => new(Root, Root, name);
    private static VBStandardModuleSymbol StdModule(string name) => new(Root, Root, name);

    private static VBProcedureMemberSymbol Procedure(Uri moduleUri, string name, ScopeKind scope)
        => new(Root, moduleUri, name, scope, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Implicit);

    private static VBInstanceFieldVariableMemberSymbol Field(Uri moduleUri, string name)
        => new(Root, moduleUri, name, R, R, VBLongType.TypeInfo, AccessModifier.Implicit);

    private static InstanceExpressionNode MeOf()
        => new(new(TestUri.TestModuleUri().AbsolutePath, [42]), TestLocations.TestLocation);

    private static StaticEvaluationContext ContextAt(Uri scopeUri, params Symbol[] symbols)
    {
        var tree = ScopeTreeBuilder.Build(symbols);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(scopeUri));
    }

    [TestMethod]
    public void WithinAClassModulesProcedure_ReturnsTheClasssOwnVBClassType()
    {
        var classModule = ClassModule("Widget");
        var procedure = Procedure(classModule.Uri, "DoWork", ScopeKind.Instance);
        var context = ContextAt(procedure.Uri, classModule, procedure);

        var result = InstanceExpressionStaticSemantics.Instance.DetermineDeclaredType(context, MeOf());

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        var classType = Assert.IsInstanceOfType<VBClassType>(result.Result);
        Assert.AreEqual("Widget", classType.Name);
    }

    [TestMethod]
    public void CarriesTheEnclosingClasssMemberList()
        // proves the class's Members - populated by WorkspaceSymbolResolver.Compose's second pass,
        // not built here - flow straight through into the resulting VBClassType, same as New's own.
    {
        var classModule = ClassModule("Widget");
        var procedure = Procedure(classModule.Uri, "DoWork", ScopeKind.Instance);
        var field = Field(classModule.Uri, "State");
        var populated = classModule with { Members = [field] };
        var context = ContextAt(procedure.Uri, populated, procedure);

        var result = InstanceExpressionStaticSemantics.Instance.DetermineDeclaredType(context, MeOf());

        var classType = Assert.IsInstanceOfType<VBClassType>(result.Result);
        Assert.HasCount(1, classType.Members);
        Assert.AreEqual("State", classType.Members[0].Name);
    }

    [TestMethod]
    public void WithinAStandardModulesProcedure_IsAnError()
    {
        var module = StdModule("Module1");
        var procedure = Procedure(module.Uri, "DoWork", ScopeKind.Module);
        var context = ContextAt(procedure.Uri, module, procedure);

        var result = InstanceExpressionStaticSemantics.Instance.DetermineDeclaredType(context, MeOf());

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.InvalidUseOfMe, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ANonInstanceExpression_Throws()
    {
        var module = StdModule("Module1");
        var context = ContextAt(module.Uri, module);
        var notAnInstanceExpression = new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, VBUnknownType.TypeInfo.DefaultValue);

        Assert.ThrowsExactly<ArgumentException>(() => InstanceExpressionStaticSemantics.Instance.DetermineDeclaredType(context, notAnInstanceExpression));
    }
}
