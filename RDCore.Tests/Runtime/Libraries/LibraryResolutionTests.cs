using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;
using static RDCore.Tests.Runtime.Libraries.LibraryFixtures;
using RDCore.Runtime.LanguageCore.Services;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// A type of a library is named by its own name, as the standard library's are, and qualified by the name of the library: <c>Widgets.Gadget</c> is the
/// library's class and nothing else of the name (<strong>MS-VBAL §5.6.12</strong>).
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.6.4 Type Binding Context")]
public sealed class LibraryResolutionTests
{
    private static readonly Uri Root = new("file:///c:/ws/");
    private static readonly VBStandardModuleSymbol Program = new(Root, Root, "Program");

    private static ScopeTreeSymbolResolver ResolverOf(params LibraryDescription[] libraries)
    {
        List<Symbol> symbols =
        [
            Program,
            new VBClassModuleSymbol(Root, Root, "Local"),
            .. new StdLibSymbolProvider(Root).ProvideSymbols(),
            .. new LibrarySymbolReader(Root).Read(libraries),
        ];

        return new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build(symbols));
    }

    [TestMethod]
    [DataRow("Widgets", "Gadget", true, DisplayName = "a class, qualified by its library")]
    [DataRow(null, "Gadget", true, DisplayName = "a class, by its name")]
    [DataRow("Widgets", "WidgetMode", true, DisplayName = "an enumeration, qualified")]
    [DataRow(null, "WidgetMode", true, DisplayName = "an enumeration, by its name")]
    [DataRow("Panels", "Panel", true, DisplayName = "a class of a library that depends on another")]
    [DataRow("Panels", "Gadget", false, DisplayName = "a class of the library depended on is not the other's")]
    [DataRow("VBA", "Gadget", false, DisplayName = "and not the standard library's")]
    [DataRow("Widgets", "Collection", false, DisplayName = "the standard library's classes are not a library's")]
    [DataRow("VBA", "Collection", true, DisplayName = "the standard library's own is")]
    [DataRow("Widgets", "Local", false, DisplayName = "a class of the workspace is not a library's")]
    [DataRow(null, "Local", true, DisplayName = "the workspace's own is found")]
    [DataRow("Widgets", "Missing", false, DisplayName = "a type the library does not declare")]
    [DataRow("Excel", "Gadget", false, DisplayName = "a library that is not referenced")]
    public void ATypeIsResolvedQualifiedOrNot_ToWhatTheQualifierSays(string? qualifier, string name, bool resolves)
    {
        var resolved = VBProjectSymbol.ResolveQualifiedType(ResolverOf(Widgets, Panels), qualifier, name, Program.Uri);

        Assert.AreEqual(resolves, resolved.IsResolved, $"{qualifier}.{name}");
    }

    [TestMethod]
    public void AQualifiedType_IsTheSymbolOfTheLibrary()
    {
        var gadget = VBProjectSymbol.ResolveQualifiedType(ResolverOf(Widgets), "Widgets", "Gadget", Program.Uri).Symbol;

        Assert.IsInstanceOfType<VBClassModuleSymbol>(gadget);
        Assert.AreEqual("Widgets", gadget.GetProperty(SymbolProperties.Library));
    }

    [TestMethod]
    public void TheMembersOfAnEnumeration_AreNamesOfTheProject_AsTheStandardLibrarysAre()
    {
        var constant = ResolverOf(Widgets).ResolveValue("wgOn", ScopeKind.Local, Program.Uri).Symbol;

        Assert.IsInstanceOfType<VBEnumConstMemberSymbol>(constant);
    }

    // two libraries that each declare a class `Application`, as a host library and the library of its editor do; `Zulu` also declares a class of its own.
    private static LibraryDescription Hosting(string name, params string[] others)
        => new()
        {
            Name = name,
            Classes = [new ClassDescription { Name = "Application" }, .. others.Select(other => new ClassDescription { Name = other })],
        };

    private static ScopeTreeSymbolResolver ResolverOfReferences(string[] references, params LibraryDescription[] available)
    {
        var libraries = ReferencedLibraries.Load(new Runtime.Libraries.InMemoryLibrarySource(available), references.Select(name => new LibraryIdentity(name)), ["VBA"]);
        Assert.IsEmpty(libraries.Problems);

        List<Symbol> symbols =
        [
            Program,
            new VBClassModuleSymbol(Root, Root, "Local"),
            new VBClassModuleSymbol(Root, Root, "Mine"),
            .. new StdLibSymbolProvider(Root).ProvideSymbols(),
            .. new LibrarySymbolProvider(Root, libraries).ProvideSymbols(),
        ];

        return new ScopeTreeSymbolResolver(ScopeTreeBuilder.Build(symbols));
    }

    private static string? LibraryOf(SymbolResolutionResult result) => result.Symbol?.GetProperty(SymbolProperties.Library);

    [TestMethod]
    public void TheTypeOfAProject_IsOneOfThatProject_WhateverOtherProjectsDeclareTheName()
    {
        var resolver = ResolverOfReferences(["Excel", "Word"], Hosting("Excel", "Range"), Hosting("Word", "Document"));

        Assert.AreEqual("Excel", LibraryOf(VBProjectSymbol.ResolveQualifiedType(resolver, "Excel", "Application", Program.Uri)));
        Assert.AreEqual("Word", LibraryOf(VBProjectSymbol.ResolveQualifiedType(resolver, "Word", "Application", Program.Uri)));
        Assert.IsTrue(VBProjectSymbol.ResolveQualifiedType(resolver, "Excel", "Document", Program.Uri).IsUnbound, "Document is Word's");
    }

    [TestMethod]
    public void OfTwoLibrariesThatDeclareAName_TheOneReferencedFirstIsWhatTheNameMeans()
    {
        // the earlier reference has the higher reference precedence (MS-VBAL §4.1), and of the matches of one tier, those of the project of the highest
        // reference precedence are kept (MS-VBAL §5.6.10).
        var excelFirst = ResolverOfReferences(["Excel", "Word"], Hosting("Excel"), Hosting("Word"));
        var wordFirst = ResolverOfReferences(["Word", "Excel"], Hosting("Excel"), Hosting("Word"));

        Assert.AreEqual("Excel", LibraryOf(VBProjectSymbol.ResolveQualifiedType(excelFirst, null, "Application", Program.Uri)));
        Assert.AreEqual("Word", LibraryOf(VBProjectSymbol.ResolveQualifiedType(wordFirst, null, "Application", Program.Uri)));
    }

    [TestMethod]
    public void ALibraryThatDeclaresANameOfTheStandardLibrary_IsShadowedByIt()
    {
        // the standard library comes before every reference a project lists, so its names are what the names mean, and the library's own are qualified.
        var shadowing = new LibraryDescription
        {
            Name = "Shadows",
            Enums = [new EnumDescription { Name = "Choices", Members = [new EnumMemberDescription { Name = "vbCancel", Value = 99 }] }],
            Classes = [new ClassDescription { Name = "Collection" }],
        };
        var resolver = ResolverOfReferences(["Shadows"], shadowing);

        Assert.AreEqual("VBA", LibraryOf(VBProjectSymbol.ResolveQualifiedType(resolver, null, "Collection", Program.Uri)));
        Assert.AreEqual("VBA", LibraryOf(resolver.ResolveValue("vbCancel", ScopeKind.Local, Program.Uri)));
        Assert.AreEqual("Shadows", LibraryOf(VBProjectSymbol.ResolveQualifiedType(resolver, "Shadows", "Collection", Program.Uri)));
    }

    [TestMethod]
    public void ALibraryOnlyLoadedForAnother_IsShadowedByTheOnesTheProjectReferences()
    {
        // Excel depends on Editor, and both declare Application: the project referenced Excel, so it is Excel's.
        var excel = Hosting("Excel") with { DependsOn = ["Editor"] };
        var resolver = ResolverOfReferences(["Excel"], excel, Hosting("Editor"));

        Assert.AreEqual("Excel", LibraryOf(VBProjectSymbol.ResolveQualifiedType(resolver, null, "Application", Program.Uri)));
        Assert.AreEqual("Editor", LibraryOf(VBProjectSymbol.ResolveQualifiedType(resolver, "Editor", "Application", Program.Uri)));
    }

    [TestMethod]
    public void AClassOfTheWorkspace_ShadowsTheClassOfAnyLibraryOfItsName_AndTheLibraryQualifiesItsOwn()
    {
        var resolver = ResolverOfReferences(["Widgets"], new LibraryDescription { Name = "Widgets", Classes = [new ClassDescription { Name = "Mine" }] });

        Assert.IsNull(LibraryOf(VBProjectSymbol.ResolveQualifiedType(resolver, null, "Mine", Program.Uri)), "the workspace's own");
        Assert.AreEqual("Widgets", LibraryOf(VBProjectSymbol.ResolveQualifiedType(resolver, "Widgets", "Mine", Program.Uri)));
    }

    [TestMethod]
    public void TheNameOfALibrary_IsAQualifier_AndNothingElse()
    {
        var resolver = ResolverOf(Widgets);

        Assert.IsInstanceOfType<VBProjectSymbol>(resolver.ResolveQualifier("Widgets", ScopeKind.Global, Program.Uri).Symbol);
        Assert.IsNull(resolver.ResolveValue("Parts", ScopeKind.Local, Program.Uri).Symbol, "the members of a class are the members of an object, not names of the project");
    }
}
