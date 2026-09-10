using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Tests.Model.Symbols;

[TestClass]
public sealed class ScopeTreeSymbolResolverTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);

    private static VBProcedureMemberSymbol Procedure(Uri moduleUri, string name, AccessModifier access = AccessModifier.Implicit)
        => new(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);

    private static VBModuleFieldVariableMemberSymbol Field(Uri moduleUri, string name)
        => new(Root, moduleUri, name, ScopeKind.Module, VBLongType.TypeInfo, R, R, AccessModifier.Implicit);

    private static VBLocalVariableSymbol Local(Uri procedureUri, string name)
        => new(Root, procedureUri, name, ScopeKind.Local, R, R);

    private static ScopeTreeSymbolResolver Resolver(params Symbol[] symbols)
        => new(ScopeTreeBuilder.Build(symbols));

    [TestMethod]
    public void Resolve_BindsALocal_FromItsProcedure()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "temp");

        var result = Resolver(module, procedure, local).Resolve("temp", ScopeKind.Unallocated, procedure.Uri);

        Assert.IsTrue(result.IsResolved);
        Assert.AreSame(local, result.Symbol);
    }

    [TestMethod]
    public void Resolve_BindsASiblingModulesPublicMember_FromThisProcedure()
    {
        var library = Module("Library");
        var api = Procedure(library.Uri, "Compute", AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");

        var result = Resolver(library, api, caller, run).Resolve("Compute", ScopeKind.Unallocated, run.Uri);

        Assert.AreSame(api, result.Symbol);
    }

    [TestMethod]
    public void Resolve_BindsTheInnermostDeclaration()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var local = Local(procedure.Uri, "State");
        var field = Field(module.Uri, "State");
        var resolver = Resolver(module, procedure, local, field);

        Assert.AreSame(local, resolver.Resolve("State", ScopeKind.Unallocated, procedure.Uri).Symbol);
        Assert.AreSame(field, resolver.Resolve("State", ScopeKind.Unallocated, module.Uri).Symbol);
    }

    [TestMethod]
    public void Resolve_SameNameTwiceInOneModule_IsADuplicateDeclaration()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Value");
        var procedure = Procedure(module.Uri, "Value");

        var result = Resolver(module, field, procedure).Resolve("Value", ScopeKind.Unallocated, module.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.DuplicateDeclaration, result.ErrorId);
        Assert.IsNull(result.Symbol);
        CollectionAssert.AreEquivalent(new Symbol[] { field, procedure }, result.Candidates.ToArray());
    }

    [TestMethod]
    public void Resolve_APublicNameInTwoModules_IsAnAmbiguousName_FromAThirdModule()
    {
        var alpha = Module("Alpha");
        var alphaCompute = Procedure(alpha.Uri, "Compute", AccessModifier.Public);
        var beta = Module("Beta");
        var betaCompute = Procedure(beta.Uri, "Compute", AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");

        var result = Resolver(alpha, alphaCompute, beta, betaCompute, caller, run)
            .Resolve("Compute", ScopeKind.Unallocated, run.Uri);

        Assert.IsTrue(result.IsError);
        Assert.AreEqual(VBCompileErrorId.AmbiguousName, result.ErrorId);
        CollectionAssert.AreEquivalent(new Symbol[] { alphaCompute, betaCompute }, result.Candidates.ToArray());
    }

    [TestMethod]
    public void Resolve_APublicNameInTwoModules_StillBindsToItsOwnModule()
    {
        var alpha = Module("Alpha");
        var alphaCompute = Procedure(alpha.Uri, "Compute", AccessModifier.Public);
        var beta = Module("Beta");
        var betaCompute = Procedure(beta.Uri, "Compute", AccessModifier.Public);
        var alphaRun = Procedure(alpha.Uri, "Run");

        var result = Resolver(alpha, alphaCompute, beta, betaCompute, alphaRun)
            .Resolve("Compute", ScopeKind.Unallocated, alphaRun.Uri);

        Assert.AreSame(alphaCompute, result.Symbol, "own module wins before the project scope is reached");
    }

    [TestMethod]
    public void Resolve_AnUndeclaredName_IsUnbound()
    {
        var result = Resolver(Module("Mod1")).Resolve("Nope", ScopeKind.Unallocated, Module("Mod1").Uri);

        Assert.IsTrue(result.IsUnbound);
        Assert.IsFalse(result.IsError);
    }

    [TestMethod]
    public void Resolve_FromAnUnknownScope_FallsBackToTheGlobalScope()
        => Assert.IsInstanceOfType<VBStandardModuleSymbol>(
            Resolver(Module("Mod1")).Resolve("Mod1", ScopeKind.Unallocated, new Uri("file://rdcore-test#Ghost")).Symbol);

    [TestMethod]
    public void GetValue_Throws_ItBindsNamesOnly()
        => Assert.ThrowsExactly<NotSupportedException>(
            () => Resolver(Module("Mod1")).GetValue(GlobalSymbols.UnresolvedSymbol));

    [TestMethod]
    public void TryRead_ReturnsFalse_ItHoldsNoRuntimeBindings()
    {
        Assert.IsFalse(Resolver(Module("Mod1")).TryRead(new MemoryAddress(0), out var value));
        Assert.IsNull(value);
    }
}
