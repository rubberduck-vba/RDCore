using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;

namespace RDCore.Tests.Runtime.Execution;

/// <summary>
/// The declared symbols of a session, apart from anything that happens when code runs (<strong>RD-VBAL §2.3.1.2</strong>).
/// </summary>
[TestClass]
public sealed class SymbolTableTests
{
    private static VBStandardModuleSymbol Module(string name)
    {
        var workspace = TestUri.WorkspaceRoot();
        return new VBStandardModuleSymbol(workspace, workspace, name);
    }

    [TestMethod]
    public void ADeclaration_IsInTheTable_AndInItsScopeTree_OnceAdded()
    {
        var table = new SymbolTable();
        var module = Module("Module1");

        Assert.IsTrue(table.TryAdd(module, module.ScopeKind));

        CollectionAssert.AreEqual(new[] { module }, table.All().ToArray());
        Assert.IsTrue(table.ScopeTree.TryGetScope(module.Uri, out _));
    }

    [TestMethod]
    public void ADeclaration_IsGoneFromTheScopeTree_OnceRemoved()
    {
        var table = new SymbolTable();
        var module = Module("Module1");
        table.TryAdd(module, module.ScopeKind);
        Assert.IsTrue(table.ScopeTree.TryGetScope(module.Uri, out _), "the tree is built, and so memoized, before the change");

        Assert.IsTrue(table.Remove(module, module.ScopeKind));

        Assert.IsEmpty(table.All());
        Assert.IsFalse(table.ScopeTree.TryGetScope(module.Uri, out _));
    }

    [TestMethod]
    public void ItsNames_ResolveAgainstTheTableAsItIs_WhenTheNameIsLookedUp()
    {
        // the resolver keeps the one it made for the tree, and makes another when a change builds another tree.
        var table = new SymbolTable();
        var names = table.Names;
        var first = Module("Module1");
        table.TryAdd(first, first.ScopeKind);
        Assert.AreSame(first, names.ResolveValue("Module1", ScopeKind.Global, first.Uri).Symbol);
        Assert.IsTrue(names.ResolveValue("Module2", ScopeKind.Global, first.Uri).IsUnbound);

        var second = Module("Module2");
        table.TryAdd(second, second.ScopeKind);

        Assert.AreSame(second, names.ResolveValue("Module2", ScopeKind.Global, first.Uri).Symbol);
        table.Remove(first, first.ScopeKind);
        Assert.IsTrue(names.ResolveValue("Module1", ScopeKind.Global, second.Uri).IsUnbound);
    }

    [TestMethod]
    public void TheFirstDefinitionOfADeclaration_Wins()
    {
        var table = new SymbolTable();
        var module = Module("Module1");

        Assert.IsTrue(table.TryAdd(module, module.ScopeKind));
        Assert.IsFalse(table.TryAdd(module with { }, module.ScopeKind));

        Assert.HasCount(1, table.All());
    }

    [TestMethod]
    public void RemovingADeclarationThatIsNotThere_IsNotAnError_ButItIsNotRemoved()
    {
        var module = Module("Module1");

        Assert.IsFalse(new SymbolTable().Remove(module, module.ScopeKind));
    }

    [TestMethod]
    public void ANewerDefinitionOfADeclaration_ReplacesItInPlace()
    {
        var table = new SymbolTable();
        var module = Module("Module1");
        table.TryAdd(module, module.ScopeKind);
        var newer = module with { };

        Assert.IsTrue(table.TryGet(newer, newer.ScopeKind, out var existing));
        Assert.AreSame(module, existing);

        table.Replace(newer, newer.ScopeKind);

        Assert.AreSame(newer, table.All().Single());
    }

    [TestMethod]
    public void TwoTables_DoNotShareTheirDeclarations()
    {
        var first = new SymbolTable();
        var second = new SymbolTable();
        var module = Module("Module1");

        first.TryAdd(module, module.ScopeKind);

        Assert.IsEmpty(second.All());
    }
}
