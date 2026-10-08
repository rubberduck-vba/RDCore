using RDCore.Runtime.Execution;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using RDCore.SDK.Semantics.Instructions;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// Taking a module out of a session: what it declared and what it held go, and nothing that is not the module's does.
/// </summary>
[TestClass]
public sealed class ModuleUnloaderTests
{
    private sealed class Provider(params Symbol[] symbols) : ISymbolProvider
    {
        public IEnumerable<Symbol> ProvideSymbols() => symbols;
    }

    private static readonly Uri Workspace = TestUri.WorkspaceRoot();

    private static VBStandardModuleSymbol Module(string name) => new(Workspace, Workspace, name);

    private static VBModuleFieldVariableMemberSymbol Variable(VBStandardModuleSymbol module, string name, VBType? type = null)
        => new(Workspace, module.Uri, name, ScopeKind.Module, type ?? VBLongType.TypeInfo, SourceRange.Empty, SourceRange.Empty, AccessModifier.Implicit);

    private static (IRuntimeSession Session, ProgramImage Image) Compose(params Symbol[] symbols)
        => (RuntimeSessionComposer.Compose(new RuntimeEnvironmentProfile(Is64Bit: true, 0, 1252, false), [], [new Provider(symbols)]), new ProgramImage());

    [TestMethod]
    public void TheVariablesOfAModule_AreUndefined_AndTheirStorageIsFree()
    {
        var program = Module("Program");
        var n = Variable(program, "N");
        var i = Variable(program, "I", VBDoubleType.TypeInfo);
        var (session, image) = Compose(program, n, i);
        var allocated = session.Memory.Info.AllocatedBytes;
        Assert.IsGreaterThan(0, allocated, "the variables were given storage when they were defined");

        var undefined = new ModuleUnloader(session, image).Unload(program.Uri);

        Assert.AreEqual(2, undefined);
        Assert.IsFalse(session.Symbols.Resolver.TryGetAddress(n, out _));
        Assert.IsFalse(session.Symbols.Resolver.TryGetAddress(i, out _));
        Assert.IsEmpty(session.Symbols.DeclaredIn(program.Uri));
        Assert.AreEqual(0, session.Memory.Info.AllocatedBytes);
    }

    [TestMethod]
    public void TheSymbolOfTheModuleItself_StaysDefined()
    {
        var program = Module("Program");
        var (session, image) = Compose(program, Variable(program, "N"));

        new ModuleUnloader(session, image).Unload(program.Uri);

        Assert.IsTrue(session.Symbols.TryResolveValue("Program", GlobalSymbols.UnresolvedSymbol, out var module));
        Assert.AreEqual(program.Uri, module!.Uri);
    }

    [TestMethod]
    public void AModuleDefinedAgainAfterItWasUnloaded_StartsFromNothing()
    {
        var program = Module("Program");
        var n = Variable(program, "N");
        var (session, image) = Compose(program, n);
        session.Symbols.Resolver.GetValue(n).SetValue(session.Symbols.Resolver, new RDCore.SDK.Model.Values.Runtime.VBRuntimeValue<int>(30));
        new ModuleUnloader(session, image).Unload(program.Uri);

        Assert.IsTrue(session.Symbols.TryDefine(n, n.ScopeKind));

        Assert.AreEqual(0, session.Symbols.Resolver.GetValue(n).Value.BoxedValue, "what the variable held is not what the new one starts as");
    }

    [TestMethod]
    public void AnotherModule_IsNotTouched()
    {
        var program = Module("Program");
        var other = Module("Other");
        var kept = Variable(other, "N");
        var (session, image) = Compose(program, Variable(program, "N"), other, kept);

        new ModuleUnloader(session, image).Unload(program.Uri);

        Assert.IsTrue(session.Symbols.Resolver.TryGetAddress(kept, out _));
        Assert.HasCount(1, session.Symbols.DeclaredIn(other.Uri));
    }

    [TestMethod]
    public void AModuleWhoseNameBeginsTheOthers_DoesNotTakeThatModuleWithIt()
    {
        var prog = Module("Prog");
        var program = Module("Program");
        var kept = Variable(program, "N");
        var (session, image) = Compose(prog, Variable(prog, "N"), program, kept);

        new ModuleUnloader(session, image).Unload(prog.Uri);

        Assert.IsTrue(session.Symbols.Resolver.TryGetAddress(kept, out _), "Prog is not Program: a fragment 'Program.N' continues 'Prog' but not 'Prog.'");
    }

    [TestMethod]
    public void TheNamesOfAModule_AreFound_WhateverTheirCase()
    {
        var program = Module("Program");
        var (session, _) = Compose(program, Variable(program, "N"));

        Assert.HasCount(1, session.Symbols.DeclaredIn(new UriBuilder(program.Uri) { Fragment = "PROGRAM" }.Uri));
    }

    [TestMethod]
    public void TheCodeAndTheSemanticModelOfAModule_AreForgotten()
    {
        var program = Module("Program");
        var main = Variable(program, "Main");
        var (session, image) = Compose(program, main);
        image.Load(program.Uri, [new KeyValuePair<SemanticId, InstructionList>(main.SemanticId, new InstructionList(
            [], new Dictionary<string, int>(), new Dictionary<RDCore.SDK.Model.AST.Abstract.SyntaxNodeId, int>()))]);
        image.Semantics.Store(new ModuleSemanticModel(program.Uri, [], []));
        Assert.IsTrue(image.IsLoaded(program.Uri));
        Assert.IsTrue(image.Semantics.TryGet(program.Uri, out _));

        new ModuleUnloader(session, image).Unload(program.Uri);

        Assert.IsFalse(image.IsLoaded(program.Uri));
        Assert.IsFalse(image.ContainsKey(main.SemanticId));
        Assert.IsFalse(image.Semantics.TryGet(program.Uri, out _));
    }

    [TestMethod]
    public void AModuleThatDeclaredNothing_UndefinesNothing()
    {
        var program = Module("Program");
        var (session, image) = Compose(program);

        Assert.AreEqual(0, new ModuleUnloader(session, image).Unload(program.Uri));
    }
}
