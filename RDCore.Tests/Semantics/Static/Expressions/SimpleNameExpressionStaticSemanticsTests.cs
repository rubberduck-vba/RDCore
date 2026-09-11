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
using RDCore.SDK.Semantics.Static.Abstract;
using RDCore.SDK.Semantics.Static.Expressions;

namespace RDCore.Tests.Semantics.Static.Expressions;

[TestClass]
public sealed class SimpleNameExpressionStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name, bool explicitOption = false)
        => new(Root, Root, name) { Directives = new ModuleDirectives(Explicit: explicitOption) };

    private static VBProcedureMemberSymbol Procedure(Uri moduleUri, string name, AccessModifier access = AccessModifier.Implicit)
        => new(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);

    private static VBModuleFieldVariableMemberSymbol Field(Uri moduleUri, string name, VBType type)
        => new(Root, moduleUri, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

    private static VBLocalVariableSymbol Local(Uri procedureUri, string name)
        => new(Root, procedureUri, name, ScopeKind.Local, R, R);

    private static VBParameterSymbol Parameter(Uri procedureUri, string name, VBType type)
        => new(Root, procedureUri, name, R, R, ParameterKind.ImplicitByRef, type);

    private static VBConstantMemberSymbol ModuleConst(Uri moduleUri, string name, VBType type)
        => new(Root, moduleUri, name, ScopeKind.Module, type, R, R, AccessModifier.Implicit);

    private static VBLocalConstantSymbol LocalConst(Uri procedureUri, string name, VBType type)
        => new(Root, procedureUri, name, R, R, type);

    private static VBFunctionMemberSymbol Function(Uri moduleUri, string name, VBType returnType)
        => new(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Function, returnType, R, R, AccessModifier.Implicit);

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [42]), TestLocations.TestLocation, identifier);

    private static StaticEvaluationContext ContextAt(Uri scopeUri, params Symbol[] symbols)
    {
        var tree = ScopeTreeBuilder.Build(symbols);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(scopeUri));
    }

    [TestMethod]
    public void ResolvesToALocalVariable_ReturnsItsDeclaredType()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "total") with { ResolvedType = VBLongType.TypeInfo };
        var context = ContextAt(procedure.Uri, module, procedure, local);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("total"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ResolvesToAParameter_ReturnsItsDeclaredType()
    {
        var module = Module("Mod1");
        var declared = Procedure(module.Uri, "DoWork");
        var parameter = Parameter(declared.Uri, "value", VBStringType.TypeInfo);
        var procedure = declared with { Parameters = [parameter] };
        var context = ContextAt(procedure.Uri, module, procedure);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("value"));

        Assert.AreEqual(VBStringType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ResolvesToAModuleField_ReturnsItsDeclaredType()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total", VBDoubleType.TypeInfo);
        var context = ContextAt(module.Uri, module, field);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Total"));

        Assert.AreEqual(VBDoubleType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ResolvesToAModuleConstant_ReturnsItsDeclaredType()
    {
        var module = Module("Mod1");
        var @const = ModuleConst(module.Uri, "Pi", VBDoubleType.TypeInfo);
        var context = ContextAt(module.Uri, module, @const);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Pi"));

        Assert.AreEqual(VBDoubleType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ResolvesToALocalConstant_ReturnsItsDeclaredType()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var @const = LocalConst(procedure.Uri, "Max", VBIntegerType.TypeInfo);
        var context = ContextAt(procedure.Uri, module, procedure, @const);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Max"));

        Assert.AreEqual(VBIntegerType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ResolvesToAFunctionReferencedBare_ReturnsItsReturnType()
    {
        var module = Module("Mod1");
        var function = Function(module.Uri, "Compute", VBLongType.TypeInfo);
        var context = ContextAt(module.Uri, module, function);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Compute"));

        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void ResolvesTheInnermostDeclaration_ALocalShadowsAModuleFieldOfTheSameName()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "State") with { ResolvedType = VBIntegerType.TypeInfo };
        var field = Field(module.Uri, "State", VBStringType.TypeInfo);
        var symbols = new Symbol[] { module, procedure, local, field };

        var fromProcedure = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(ContextAt(procedure.Uri, symbols), NameOf("State"));
        var fromModule = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(ContextAt(module.Uri, symbols), NameOf("State"));

        Assert.AreEqual(VBIntegerType.TypeInfo, fromProcedure.Result, "the local shadows the field from within the procedure");
        Assert.AreEqual(VBStringType.TypeInfo, fromModule.Result, "the field itself resolves from the module scope");
    }

    [TestMethod]
    public void AnUnresolvedName_WithoutOptionExplicit_SucceedsAsUnknown()
    {
        var module = Module("Mod1", explicitOption: false);
        var context = ContextAt(module.Uri, module);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Nope"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(VBUnknownType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AnUnresolvedName_UnderOptionExplicit_IsAVariableNotDefinedError()
    {
        var module = Module("Mod1", explicitOption: true);
        var context = ContextAt(module.Uri, module);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Nope"));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ADuplicateDeclarationInScope_IsAnError()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Value", VBLongType.TypeInfo);
        var procedure = Procedure(module.Uri, "Value");
        var context = ContextAt(module.Uri, module, field, procedure);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Value"));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AnAmbiguousName_AcrossTwoModules_IsAnError()
    {
        var alpha = Module("Alpha");
        var alphaCompute = Procedure(alpha.Uri, "Compute", AccessModifier.Public);
        var beta = Module("Beta");
        var betaCompute = Procedure(beta.Uri, "Compute", AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");
        var context = ContextAt(run.Uri, alpha, alphaCompute, beta, betaCompute, caller, run);

        var result = SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, NameOf("Compute"));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.AmbiguousName, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ANonSimpleNameExpression_Throws()
    {
        var module = Module("Mod1");
        var context = ContextAt(module.Uri, module);
        var notASimpleName = new LiteralExpressionNode(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, VBUnknownType.TypeInfo.DefaultValue);

        Assert.ThrowsExactly<ArgumentException>(() => SimpleNameExpressionStaticSemantics.Instance.DetermineDeclaredType(context, notASimpleName));
    }
}
