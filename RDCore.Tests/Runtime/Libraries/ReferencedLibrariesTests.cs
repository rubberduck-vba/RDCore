using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions.TestingHelpers;
using static RDCore.Tests.Runtime.Libraries.LibraryFixtures;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// The libraries a project references are found by name and loaded with the libraries they depend on; a library that cannot be loaded is a problem, and so is
/// every library that depends on it. Libraries that depend on one another are rejected, not resolved.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
public sealed class ReferencedLibrariesTests
{
    private static readonly string[] Standard = ["VBA"];

    private static ReferencedLibraries Load(ILibrarySource source, params string[] references) => ReferencedLibraries.Load(source, references, Standard);

    private static string[] Names(ReferencedLibraries libraries) => [.. libraries.Libraries.Select(library => library.Name)];

    [TestMethod]
    public void ALibraryIsLoaded_AfterTheLibrariesItDependsOn_AndOnce()
    {
        var libraries = Load(new InMemoryLibrarySource(Widgets, Panels), "Panels", "Widgets");

        CollectionAssert.AreEqual(new[] { "Widgets", "Panels" }, Names(libraries));
        Assert.IsEmpty(libraries.Problems);
    }

    [TestMethod]
    public void ThePrecedenceOfALibrary_IsItsPositionInTheReferences_AndOneOnlyLoadedForAnotherComesAfterThemAll()
    {
        // the earlier reference has the higher reference precedence, the lower number (MS-VBAL §4.1); the standard library, before them all, has none.
        var libraries = Load(new InMemoryLibrarySource(Widgets, Panels, Named("Other")), "VBA", "Panels", "Other");

        Assert.AreEqual(1, libraries.Priorities["Panels"]);
        Assert.AreEqual(2, libraries.Priorities["Other"]);
        Assert.AreEqual(3, libraries.Priorities["Widgets"]);
        Assert.IsFalse(libraries.Priorities.ContainsKey("VBA"));
    }

    [TestMethod]
    public void ALibraryIsFoundByItsName_WhateverTheCase_AndTheStandardLibraryIsNotAskedOfTheSource()
    {
        var source = new InMemoryLibrarySource(Widgets);

        var libraries = Load(source, "VBA", "wIdGeTs");

        CollectionAssert.AreEqual(new[] { "Widgets" }, Names(libraries));
        CollectionAssert.AreEqual(new[] { "wIdGeTs" }, source.Asked, "the standard library is the platform's own");
    }

    [TestMethod]
    public void ALibraryTheSourceHasNot_IsAProblem()
    {
        var libraries = Load(new InMemoryLibrarySource(Widgets), "Widgets", "Excel");

        CollectionAssert.AreEqual(new[] { "Widgets" }, Names(libraries));
        var problem = libraries.Problems.Single();
        Assert.AreEqual("Excel", problem.Name);
        Assert.AreEqual(LibraryProblemKind.NotFound, problem.Kind);
    }

    [TestMethod]
    public void ALibraryTheSourceCannotRead_IsAProblem_AndSaysWhy()
    {
        var source = new InMemoryLibrarySource(Widgets);
        source.Unreadable.Add("Widgets");

        var problem = Load(source, "Widgets").Problems.Single();

        Assert.AreEqual(LibraryProblemKind.Unreadable, problem.Kind);
        StringAssert.Contains(problem.Related.Single(), "cannot be read");
    }

    [TestMethod]
    public void ALibraryWhoseDependencyIsMissing_IsNotLoaded_AndSaysWhichOne()
    {
        var libraries = Load(new InMemoryLibrarySource(Panels), "Panels");

        Assert.IsEmpty(libraries.Libraries);
        CollectionAssert.AreEquivalent(
            new[] { ("Widgets", LibraryProblemKind.NotFound), ("Panels", LibraryProblemKind.DependencyFailed) },
            libraries.Problems.Select(problem => (problem.Name, problem.Kind)).ToArray());
        CollectionAssert.AreEqual(new[] { "Widgets" }, libraries.Problems.Single(problem => problem.Name == "Panels").Related.ToArray());
    }

    [TestMethod]
    public void LibrariesThatDependOnOneAnother_AreRejected_BothOfThem_WithTheCycle()
    {
        var libraries = Load(new InMemoryLibrarySource(Named("Alpha", "Beta"), Named("Beta", "Alpha")), "Alpha");

        Assert.IsEmpty(libraries.Libraries, "there is no order to load them in, and none is made up");
        Assert.HasCount(2, libraries.Problems);
        Assert.IsTrue(libraries.Problems.All(problem => problem.Kind == LibraryProblemKind.Cyclic));
        foreach (var problem in libraries.Problems)
        {
            CollectionAssert.AreEqual(new[] { "Alpha", "Beta", "Alpha" }, problem.Related.ToArray(), problem.Name);
        }
    }

    [TestMethod]
    public void ALibraryThatDependsOnItself_IsRejected()
    {
        var libraries = Load(new InMemoryLibrarySource(Named("Loop", "Loop")), "Loop");

        Assert.IsEmpty(libraries.Libraries);
        var problem = libraries.Problems.Single();
        Assert.AreEqual(LibraryProblemKind.Cyclic, problem.Kind);
        CollectionAssert.AreEqual(new[] { "Loop", "Loop" }, problem.Related.ToArray());
    }

    [TestMethod]
    public void ALongerCycle_IsRejected_AndSoIsWhatDependsOnIt_ButNotWhatDoesNot()
    {
        var source = new InMemoryLibrarySource(
            Named("Entry", "A"), Named("A", "B"), Named("B", "C"), Named("C", "A"), Named("Fine", "Leaf"), Named("Leaf"));

        var libraries = Load(source, "Entry", "Fine");

        CollectionAssert.AreEqual(new[] { "Leaf", "Fine" }, Names(libraries));
        var kinds = libraries.Problems.ToDictionary(problem => problem.Name, problem => problem.Kind);
        Assert.AreEqual(LibraryProblemKind.Cyclic, kinds["A"]);
        Assert.AreEqual(LibraryProblemKind.Cyclic, kinds["B"]);
        Assert.AreEqual(LibraryProblemKind.Cyclic, kinds["C"]);
        Assert.AreEqual(LibraryProblemKind.DependencyFailed, kinds["Entry"], "it is not on the cycle, and it cannot be loaded without what is");
        CollectionAssert.AreEqual(new[] { "A", "B", "C", "A" }, libraries.Problems.Single(problem => problem.Name == "B").Related.ToArray());
    }

    [TestMethod]
    public void ARejectedLibrary_IsRejectedOnce_HoweverManyReferenceIt()
    {
        var libraries = Load(new InMemoryLibrarySource(Named("Alpha", "Beta"), Named("Beta", "Alpha")), "Alpha", "Beta", "Alpha");

        Assert.HasCount(2, libraries.Problems);
    }

    [TestMethod]
    public void TheFolderOfDescriptions_IsASource_ByTheNameOfTheFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "platform", "Symbols");
        var files = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(folder, "Widgets.json")] = new(LibraryJson.Write(Widgets)),
            [Path.Combine(folder, "Misnamed.json")] = new(LibraryJson.Write(Widgets)),
            [Path.Combine(folder, "Broken.json")] = new("{ not json"),
        });
        var source = new DirectoryLibrarySource(files, folder);

        Assert.IsTrue(source.TryGet("WIDGETS", out var found));
        Assert.AreEqual("Widgets", found.Name);
        Assert.IsFalse(source.TryGet("Nothing", out _));
        Assert.Throws<InvalidDataException>(() => source.TryGet("Broken", out _));
        var misnamed = Assert.Throws<InvalidDataException>(() => source.TryGet("Misnamed", out _));
        StringAssert.Contains(misnamed.Message, "'Widgets'", "a file is the library its content says it is");
    }

    [TestMethod]
    public void AFolderThatIsNotThere_HasNoLibraries()
        => Assert.IsFalse(new DirectoryLibrarySource(new MockFileSystem(), Path.Combine(Path.GetTempPath(), "platform", "Symbols")).TryGet("Widgets", out _));
}
