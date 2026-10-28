using RDCore.SDK.Model.Symbols;

namespace RDCore.Tests.Model.Symbols;

/// <summary>
/// The one rule symbols are addressed by: a module under the root of its project, by its name as the fragment; a member under its parent, its name appended
/// after a dot. What the language server and the environment host build by hand has to be what a symbol is.
/// </summary>
[TestClass]
public sealed class SymbolUriTests
{
    private static readonly Uri Root = new(Path.Combine(Path.GetTempPath(), "rdcore-symboluri-ws"));

    [TestMethod]
    public void AModule_IsTheRootOfItsProject_ByItsName()
    {
        var module = SymbolUri.Module(Root, "GameBoard");

        Assert.AreEqual("GameBoard", module.Fragment.TrimStart('#'));
        Assert.AreEqual(Root.GetLeftPart(UriPartial.Path), module.GetLeftPart(UriPartial.Path));
    }

    [TestMethod]
    public void AModule_IsWhatItsSymbolSaysItIs()
        => Assert.AreEqual(new VBStandardModuleSymbol(Root, Root, "GameBoard").Uri, SymbolUri.Module(Root, "GameBoard"));

    [TestMethod]
    public void TheRootAsAPath_AndAsAUri_AddressTheSameModule()
        => Assert.AreEqual(SymbolUri.Module(Root, "GameBoard"), SymbolUri.Module(Root.LocalPath, "GameBoard"));

    [TestMethod]
    public void AMember_IsUnderItsParent_AfterADot()
    {
        var module = SymbolUri.Module(Root, "GameBoard");

        var member = SymbolUri.Member(module, "Reset");

        Assert.AreEqual("GameBoard.Reset", member.Fragment.TrimStart('#'));
        Assert.AreEqual("GameBoard.Reset.Count", SymbolUri.Member(member, "Count").Fragment.TrimStart('#'));
    }

    [TestMethod]
    public void TheSameModuleOfTwoProjects_IsTwoAddresses()
        => Assert.AreNotEqual(
            SymbolUri.Module(new Uri(Path.Combine(Path.GetTempPath(), "rdcore-symboluri-a")), "Module1"),
            SymbolUri.Module(new Uri(Path.Combine(Path.GetTempPath(), "rdcore-symboluri-b")), "Module1"));

    // LSP 3.17 §URI: a client may escape what need not be (file:///c%3A/...), and the folder is the same.
    [TestMethod]
    public void TheRootAsAUriThatEscapesItsPath_IsTheRootItsPathIs()
    {
        var root = new Uri(Path.Combine(Path.GetTempPath(), "rdcore-symboluri-(ws)"));
        var escaped = root.AbsoluteUri.Replace("(ws)", "%28ws%29", StringComparison.Ordinal);
        Assert.AreNotEqual(root.AbsoluteUri, new Uri(escaped).AbsoluteUri, "a Uri keeps what it is written with");

        Assert.AreEqual(SymbolUri.Module(root.LocalPath, "GameBoard").AbsoluteUri, SymbolUri.Module(escaped, "GameBoard").AbsoluteUri);
    }

    [TestMethod]
    public void ARootOfAnotherScheme_IsTheRootAsItIs()
        => Assert.AreEqual("untitled:Untitled-1#Module1", SymbolUri.Module("untitled:Untitled-1", "Module1").AbsoluteUri);

    [TestMethod]
    [DataRow("src", DisplayName = "a relative path")]
    [DataRow("../Work", DisplayName = "a path that climbs")]
    public void ARootThatIsNeitherAnAbsolutePathNorAnAbsoluteUri_IsRefusedSaying(string root)
        => StringAssert.Contains(Assert.Throws<ArgumentException>(() => SymbolUri.Module(root, "Module1")).Message, root);

    [TestMethod]
    public void AFileOfTheWorkspace_IsAddressedUnderTheWorkspace_WhereverItIs()
    {
        var elsewhere = new Uri(Path.Combine(Path.GetTempPath(), "rdcore-elsewhere", "Program1.rdc"));

        Assert.AreEqual(Root, SymbolUri.RootOf(elsewhere, Root.LocalPath));
    }

    [TestMethod]
    public void ADocumentThatIsNoFile_IsAProjectOfItsOwn()
        => Assert.AreEqual("untitled:Untitled-1", SymbolUri.RootOf(new Uri("untitled:Untitled-1"), Root.LocalPath).AbsoluteUri);

    [TestMethod]
    public void ADocumentOfAServerWithNoWorkspace_IsAProjectOfItsOwn()
    {
        var document = new Uri(Path.Combine(Path.GetTempPath(), "rdcore-alone", "Module1.bas"));

        Assert.AreEqual(document, SymbolUri.RootOf(document, workspaceRoot: string.Empty));
    }
}
