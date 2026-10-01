using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Semantics.Static;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.Tests.Semantics.Static;

/// <summary>
/// <strong>MS-VBAL §5.6.13.1</strong>: "It is invalid for an argument list to contain a ByVal argument unless it is the
/// argument list for an invocation of an external procedure." What is invoked is what says whether it is.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.13.1 Argument Lists")]
public sealed class ByValArgumentStaticSemanticsTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static SimpleNameExpressionNode NameOf(string identifier)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [1]), TestLocations.TestLocation, identifier);

    private static ByValArgumentExpressionNode ByVal(ExpressionNode operand)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [2]), TestLocations.TestLocation, operand);

    private static IndexExpressionNode CallOf(ExpressionNode callee, params ExpressionNode[] arguments)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [3]), TestLocations.TestLocation, callee, [.. arguments]);

    private static NamedArgumentNode Named(string name, ExpressionNode value)
        => new(new(TestUri.TestModuleUri().AbsolutePath, [4]), TestLocations.TestLocation, name, value);

    // a module with a variable x, and a procedure Foo that is a Declare when asked to be.
    private static StaticEvaluationContext Compose(bool external, bool optionExplicit = false)
    {
        var module = new VBStandardModuleSymbol(Root, Root, "Main");
        if (optionExplicit)
        {
            module = module with { Directives = new ModuleDirectives(Explicit: true) };
        }

        var x = new VBModuleFieldVariableMemberSymbol(Root, module.Uri, "x", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        Symbol foo = external
            ? new VBExternalFunctionMemberSymbol(Root, module.Uri, "Foo", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public, true, "kernel32", null)
            : new VBFunctionMemberSymbol(Root, module.Uri, "Foo", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);

        var tree = ScopeTreeBuilder.Build([module, x, foo]);
        return new StaticEvaluationContext(new ScopeTreeSymbolResolver(tree), tree.ScopeFor(module.Uri));
    }

    [TestMethod]
    public void AByValArgument_OfAnExternalProcedure_IsValid()
    {
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: true), CallOf(NameOf("Foo"), ByVal(NameOf("x"))));

        Assert.IsTrue(result.IsSuccess, result.ErrorInfo?.Description);
        Assert.AreEqual(VBLongType.TypeInfo, result.Result);
    }

    [TestMethod]
    public void AByValArgument_OfAnyOtherProcedure_IsInvalid()
    {
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: false), CallOf(NameOf("Foo"), ByVal(NameOf("x"))));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.ByValArgumentNotAllowed, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AByValNamedArgument_OfAnyOtherProcedure_IsInvalid()
    {
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: false), CallOf(NameOf("Foo"), Named("p", ByVal(NameOf("x")))));

        Assert.AreEqual(VBCompileErrorId.ByValArgumentNotAllowed, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AByValNamedArgument_OfAnExternalProcedure_IsValid()
        => Assert.IsTrue(ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: true), CallOf(NameOf("Foo"), Named("p", ByVal(NameOf("x"))))).IsSuccess);

    [TestMethod]
    public void AnArgumentWithoutByVal_IsValid_WhateverTheProcedure()
        => Assert.IsTrue(ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: false), CallOf(NameOf("Foo"), NameOf("x"))).IsSuccess);

    [TestMethod]
    public void TheOperandOfAByValArgument_IsAnExpressionLikeAnyOther()
    {
        // an external call, so ByVal is valid: what is wrong is the name in it.
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(
            Compose(external: true, optionExplicit: true), CallOf(NameOf("Foo"), ByVal(NameOf("Missing"))));

        Assert.AreEqual(VBCompileErrorId.VariableNotDefined, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void ByVal_InAnArgumentListOfSomethingThatIsNotAProcedure_IsInvalid()
        // x is a variable: `x(ByVal 1)` indexes it, and nothing external is invoked.
        => Assert.AreEqual(VBCompileErrorId.ByValArgumentNotAllowed,
            ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: true), CallOf(NameOf("x"), ByVal(NameOf("x")))).ErrorInfo!.VBCompileErrorId);
}
