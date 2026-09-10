using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Tests.Runtime;

/// <summary>
/// The environment-host session resolves an identifier by walking the <c>ScopeTree</c> outward from
/// the scope the lookup originates in — procedure, then module, then global (RD-VBAL §2.3.1.2).
/// </summary>
[TestClass]
public sealed class SessionSymbolsResolutionTests
{
    private static readonly Uri Root = new("file://rdcore-test");
    private static readonly SourceRange R = SourceRange.Empty;
    private static readonly Symbol GlobalScope = GlobalSymbols.UnresolvedSymbol;

    private sealed class Provider(Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static ISessionSymbols Compose(params Symbol[] symbols)
        => RuntimeSessionComposer.Compose(
            new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), new Provider(symbols)).Symbols;

    private static VBStandardModuleSymbol Module(string name) => new(Root, Root, name);

    private static VBModuleFieldVariableMemberSymbol Field(Uri moduleUri, string name, AccessModifier access = AccessModifier.Implicit)
        => new(Root, moduleUri, name, ScopeKind.Module, VBLongType.TypeInfo, R, R, access);

    private static VBProcedureMemberSymbol Procedure(Uri moduleUri, string name, params VBParameterSymbol[] parameters)
    {
        var declared = new VBProcedureMemberSymbol(
            Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, AccessModifier.Implicit);
        return declared with { Parameters = [.. parameters] };
    }

    private static VBProcedureMemberSymbol Procedure(Uri moduleUri, string name, AccessModifier access)
        => new(Root, moduleUri, name, ScopeKind.Module, SymbolKindExt.Procedure, VBVoidType.TypeInfo, R, R, access);

    private static VBParameterSymbol Parameter(Uri procedureUri, string name)
        => new(Root, procedureUri, name, R, R, ParameterKind.ImplicitByRef, VBLongType.TypeInfo);

    private static VBLocalVariableSymbol Local(Uri procedureUri, string name, LocalDeclarationKind declaredBy = LocalDeclarationKind.Dim)
        => new(Root, procedureUri, name, ScopeKind.Local, R, R, DeclaredBy: declaredBy);

    [TestMethod]
    public void ModuleName_ResolvesFromTheGlobalScope()
    {
        var symbols = Compose(Module("Mod1"));

        Assert.IsTrue(symbols.TryResolve("Mod1", GlobalScope, out var resolved));
        Assert.IsInstanceOfType<VBStandardModuleSymbol>(resolved);
    }

    [TestMethod]
    public void ModuleField_ResolvesFromItsModuleScope()
    {
        var module = Module("Mod1");
        var field = Field(module.Uri, "Total");
        var symbols = Compose(module, field);

        Assert.IsTrue(symbols.TryResolve("Total", module, out var resolved));
        Assert.AreSame(field, resolved);
    }

    [TestMethod]
    public void Parameter_ResolvesFromItsProcedureScope()
    {
        var module = Module("Mod1");
        var declared = Procedure(module.Uri, "DoWork");
        var parameter = Parameter(declared.Uri, "value");
        var procedure = Procedure(module.Uri, "DoWork", parameter);
        var symbols = Compose(module, procedure);

        Assert.IsTrue(symbols.TryResolve("value", procedure, out var resolved));
        Assert.AreSame(parameter, resolved);
    }

    [TestMethod]
    public void ProcedureLocal_ShadowsAModuleField_TheInnerDeclarationWins()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var field = Field(module.Uri, "State");
        var local = Local(procedure.Uri, "State");
        var symbols = Compose(module, procedure, field, local);

        Assert.IsTrue(symbols.TryResolve("State", procedure, out var fromProcedure));
        Assert.AreSame(local, fromProcedure, "the procedure-local shadows the module field");

        Assert.IsTrue(symbols.TryResolve("State", module, out var fromModule));
        Assert.AreSame(field, fromModule, "the field is still what resolves at module scope");
    }

    [TestMethod]
    public void ARedimIntroducedLocal_Resolves()
    {
        var module = Module("Mod1");
        var procedure = Procedure(module.Uri, "DoWork");
        var buffer = Local(procedure.Uri, "buffer", LocalDeclarationKind.ReDim);
        var symbols = Compose(module, procedure, buffer);

        Assert.IsTrue(symbols.TryResolve("buffer", procedure, out var resolved));
        Assert.AreSame(buffer, resolved);
    }

    [TestMethod]
    public void ANameDeclaredTwiceInOneScope_IsUnresolved_AndDoesNotThrow()
    {
        var module = Module("Mod1");
        // a field and a procedure that collide on one name — MS-VBAL "ambiguous name".
        var field = Field(module.Uri, "Value");
        var procedure = Procedure(module.Uri, "Value");
        var symbols = Compose(module, field, procedure);

        Assert.IsFalse(symbols.TryResolve("Value", module, out var resolved));
        Assert.IsNull(resolved);
    }

    [TestMethod]
    public void APublicProcedureInAnotherModule_ResolvesFromThisModule()
    {
        var library = Module("Library");
        var api = Procedure(library.Uri, "Compute", AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");
        var symbols = Compose(library, api, caller, run);

        Assert.IsTrue(symbols.TryResolve("Compute", run, out var resolved));
        Assert.AreSame(api, resolved);
    }

    [TestMethod]
    public void APrivateFieldInAnotherModule_DoesNotResolveFromThisModule()
    {
        var library = Module("Library");
        var cache = Field(library.Uri, "Cache", AccessModifier.Private);
        var caller = Module("Caller");
        var symbols = Compose(library, cache, caller);

        Assert.IsFalse(symbols.TryResolve("Cache", caller, out var resolved));
        Assert.IsNull(resolved);
    }

    [TestMethod]
    public void AProcedureLocal_ShadowsASiblingModulesPublicMember()
    {
        var library = Module("Library");
        var api = Procedure(library.Uri, "Value", AccessModifier.Public);
        var caller = Module("Caller");
        var run = Procedure(caller.Uri, "Run");
        var local = Local(run.Uri, "Value");
        var symbols = Compose(library, api, caller, run, local);

        Assert.IsTrue(symbols.TryResolve("Value", run, out var resolved));
        Assert.AreSame(local, resolved);
    }

    [TestMethod]
    public void AnUndeclaredName_DoesNotResolve()
    {
        var symbols = Compose(Module("Mod1"));

        Assert.IsFalse(symbols.TryResolve("Nope", GlobalScope, out var resolved));
        Assert.IsNull(resolved);
    }
}
