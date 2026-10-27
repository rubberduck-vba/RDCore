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
}
