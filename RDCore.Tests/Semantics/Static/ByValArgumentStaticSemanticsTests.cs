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
/// <strong>RD-VBAL §5.6.13.1</strong>: an argument written <c>ByVal</c> is valid where <strong>MS-VBA</strong> compiles it - for a <c>ByVal</c> parameter, and
/// any parameter of an external procedure - and not only for an external procedure, as <strong>MS-VBAL §5.6.13.1</strong> says. What is invoked, and the
/// parameter the argument is for, are what say which.
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

    // a module with a variable x, an array a, and a procedure Foo that is a Declare when asked to be, its parameter ByRef unless asked otherwise.
    private static StaticEvaluationContext Compose(bool external, bool optionExplicit = false, ParameterKind parameterKind = ParameterKind.ImplicitByRef)
    {
        var module = new VBStandardModuleSymbol(Root, Root, "Main");
        if (optionExplicit)
        {
            module = module with { Directives = new ModuleDirectives(Explicit: true) };
        }

        var x = new VBModuleFieldVariableMemberSymbol(Root, module.Uri, "x", ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);
        var a = new VBModuleFieldVariableMemberSymbol(Root, module.Uri, "a", ScopeKind.Module, new VBResizableArrayType(VBLongType.TypeInfo), R, R, AccessModifier.Implicit);
        // Foo(p As Long): one parameter, which the calls give their argument to.
        VBReturningMemberSymbol foo = external
            ? new VBExternalFunctionMemberSymbol(Root, module.Uri, "Foo", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public, true, "kernel32", null)
            : new VBFunctionMemberSymbol(Root, module.Uri, "Foo", ScopeKind.Module, SymbolKindExt.Function, VBLongType.TypeInfo, R, R, AccessModifier.Public);
        foo = foo with { Parameters = [new VBParameterSymbol(Root, foo.Uri, "p", R, R, parameterKind, VBLongType.TypeInfo)] };

        var tree = ScopeTreeBuilder.Build([module, x, a, foo]);
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
    public void AByValArgument_OfAByValParameter_IsValid()
        => Assert.IsTrue(ExpressionStaticSemanticsEvaluator.Evaluate(
            Compose(external: false, parameterKind: ParameterKind.ExplicitByVal), CallOf(NameOf("Foo"), ByVal(NameOf("x")))).IsSuccess);

    [TestMethod]
    public void AByValArgument_OfAByRefParameterOfTheWorkspace_IsATypeMismatch()
    {
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: false), CallOf(NameOf("Foo"), ByVal(NameOf("x"))));

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
    }

    [TestMethod]
    public void AByValNamedArgument_OfAByRefParameterOfTheWorkspace_IsATypeMismatch()
    {
        var result = ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: false), CallOf(NameOf("Foo"), Named("p", ByVal(NameOf("x")))));

        Assert.AreEqual(VBCompileErrorId.TypeMismatch, result.ErrorInfo!.VBCompileErrorId);
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
    public void ByVal_InAnIndexOfAnArray_IsASyntaxError()
    {
        // a is an array: `a(ByVal x)` indexes it (MS-VBA: "Syntax error", recorded in Excel).
        Assert.AreEqual(VBCompileErrorId.SyntaxError,
            ExpressionStaticSemanticsEvaluator.Evaluate(Compose(external: true), CallOf(NameOf("a"), ByVal(NameOf("x")))).ErrorInfo?.VBCompileErrorId);
    }
}
