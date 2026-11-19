using RDCore.SDK.Runtime.Libraries;
using System.IO.Abstractions.TestingHelpers;
using static RDCore.Tests.Runtime.Libraries.LibraryFixtures;

namespace RDCore.Tests.Runtime.Libraries;

/// <summary>
/// A reference, or a dependency, that gives the identifier and version of a type library is to the description made from it: libraries of one name can be
/// several (DAO 3.6, and the DAO 2.5/3.51 compatibility library VB6.OLB is built against). A project still has one library of a name, and its own reference
/// decides which.
/// </summary>
[TestClass]
[TestCategory("RD-VBAL §2.3.1.2 References")]
public sealed class LibraryIdentityTests
{
    private static readonly Guid Dao = new("00025E01-0000-0000-C000-000000000046");
    private static readonly Guid DaoCompat = new("00025E04-0000-0000-C000-000000000046");
    private static readonly Guid Ace = new("4AC9E1DA-5BAD-4AC7-86E3-24F4CDCECA28");

    private static LibraryDescription Of(string name, Guid guid, string version, params string[] classes) => new()
    {
        Name = name,
        Origin = new LibraryOrigin { Guid = guid.ToString("B").ToUpperInvariant(), Version = version },
        Classes = [.. classes.Select(declared => new ClassDescription { Name = declared })],
    };

    private static readonly LibraryDescription AceDao = Of("DAO", Ace, "12.0", "Recordset", "Recordset2");
    private static readonly LibraryDescription Dao36 = Of("DAO", Dao, "5.0", "Recordset");
    private static readonly LibraryDescription Dao351 = Of("DAO", Dao, "4.0", "Recordset");
    private static readonly LibraryDescription Compat = Of("DAO", DaoCompat, "3.5", "Recordset", "Snapshot");

    // the library a VB6 project gets implicitly: it is built against the compatibility library.
    private static readonly LibraryDescription Vb = new()
    {
        Name = "VB",
        DependsOn = [new LibraryIdentity("DAO", DaoCompat, 3, 5)],
        Classes = [new ClassDescription { Name = "Data" }],
    };

    // the first of a name is the library of the name: DAO.json.
    private static InMemoryLibrarySource Machine() => new(AceDao, Dao36, Dao351, Compat, Vb);

    private static ReferencedLibraries Load(ILibrarySource source, params LibraryIdentity[] references) => ReferencedLibraries.Load(source, references, ["VBA"]);

    [TestMethod]
    [DataRow(4, 0, "4.0", DisplayName = "the version asked for")]
    [DataRow(5, 0, "5.0", DisplayName = "the other major")]
    [DataRow(4, null, "4.0", DisplayName = "a major, and no minor")]
    [DataRow(null, null, "5.0", DisplayName = "no version: the highest")]
    public void AReferenceWithAnIdentifier_FindsTheDescriptionOfThatTypeLibrary_OfItsMajorVersion(int? major, int? minor, string expected)
    {
        ILibrarySource source = Machine();

        Assert.IsTrue(source.TryGet(new LibraryIdentity("DAO", Dao, major, minor), out var description));

        Assert.AreEqual(expected, description.Origin?.Version);
    }

    // LoadRegTypeLib's rule: of the same major, exactly the minor asked for, or else the highest minor above it; another major is none.
    [TestMethod]
    [DataRow(1, 2, "1.2", DisplayName = "the version asked for, though a higher minor is there")]
    [DataRow(1, 3, "1.5", DisplayName = "no such minor: the highest above it")]
    [DataRow(1, 0, "1.5", DisplayName = "a minor below every one: the highest")]
    [DataRow(1, 6, null, DisplayName = "a minor above every one: none")]
    [DataRow(3, 0, null, DisplayName = "another major: none")]
    public void TheVersionAskedFor_IsChosen_OrElseTheHighestMinorAboveIt_OfTheSameMajor(int major, int minor, string? expected)
    {
        LibraryDescription[] candidates = [Of("Widgets", Dao, "1.5"), Of("Widgets", Dao, "1.2"), Of("Widgets", Dao, "2.0")];

        var chosen = new LibraryIdentity("Widgets", Dao, major, minor).Choose(candidates);

        Assert.AreEqual(expected, chosen?.Origin?.Version);
    }

    [TestMethod]
    public void AReferenceWithAMinorVersionAndNoMajor_IsNotValid_AndIsAProblem()
    {
        var libraries = Load(Machine(), new LibraryIdentity("DAO", Dao, Minor: 0));

        Assert.IsEmpty(libraries.Libraries);
        Assert.AreEqual(LibraryProblemKind.InvalidIdentity, libraries.Problems.Single().Kind);
    }

    // a name is one library of a project: the first reference is loaded, and the second, which means another library, is a problem.
    [TestMethod]
    public void TwoReferencesOfOneName_ThatMeanTwoLibraries_AreAProblem_AndTheFirstIsLoaded()
    {
        var libraries = Load(Machine(), "DAO", new LibraryIdentity("DAO", Dao, 5, 0));

        Assert.AreSame(AceDao, libraries.Libraries.Single());
        var problem = libraries.Problems.Single();
        Assert.AreEqual(LibraryProblemKind.DuplicateName, problem.Kind);
        CollectionAssert.AreEqual(new[] { "DAO", "DAO {00025E01-0000-0000-C000-000000000046} 5.0" }, problem.Related.ToArray());
    }

    // what decides is the description each finds: the same identity, or two that the source finds one description for.
    [TestMethod]
    [DataRow("by name twice")]
    [DataRow("by the same identity twice")]
    [DataRow("by name, and by the identity of the library of the name")]
    [DataRow("by a major version, and by that major version and the minor it has")]
    public void TwoReferencesOfOneName_ThatMeanOneLibrary_AreOne(string how)
    {
        (LibraryIdentity First, LibraryIdentity Second) references = how switch
        {
            "by name twice" => ("DAO", "dao"),
            "by the same identity twice" => (new LibraryIdentity("DAO", Dao, 5, 0), new LibraryIdentity("dao", Dao, 5, 0)),
            "by name, and by the identity of the library of the name" => ("DAO", new LibraryIdentity("DAO", Ace, 12, 0)),
            _ => (new LibraryIdentity("DAO", Dao, 5), new LibraryIdentity("DAO", Dao, 5, 0)),
        };

        var libraries = Load(Machine(), references.First, references.Second);

        Assert.IsEmpty(libraries.Problems, string.Join("; ", libraries.Problems.Select(problem => string.Join(", ", problem.Related))));
        Assert.HasCount(1, libraries.Libraries);
    }

    [TestMethod]
    [DataRow(null, null, "DAO {00025E01-0000-0000-C000-000000000046}")]
    [DataRow(5, null, "DAO {00025E01-0000-0000-C000-000000000046} 5")]
    [DataRow(5, 0, "DAO {00025E01-0000-0000-C000-000000000046} 5.0")]
    public void AnIdentity_IsStatedWithTheVersionItGives_AsItGivesIt(int? major, int? minor, string expected)
        => Assert.AreEqual(expected, new LibraryIdentity("DAO", Dao, major, minor).ToString());

    [TestMethod]
    public void AReferenceOfNoName_HasAHash()
    {
        var unnamed = new RDCore.SDK.Workspace.RDCoreReference { Name = null! };

        Assert.AreEqual(unnamed.GetHashCode(), new RDCore.SDK.Workspace.RDCoreReference { Name = null! }.GetHashCode());
    }

    // a name is one reference of a project, whatever its case: two references that are equal have one hash, as a set of them needs.
    [TestMethod]
    public void TwoReferencesOfOneName_AreEqual_AndHaveOneHash_WhateverTheirIdentityAndCase()
    {
        var first = new RDCore.SDK.Workspace.RDCoreReference { Name = "DAO", Guid = Dao, Major = 5, Minor = 0 };
        var second = new RDCore.SDK.Workspace.RDCoreReference { Name = "dao", Guid = DaoCompat, Major = 3, Minor = 5 };

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        Assert.HasCount(1, new HashSet<RDCore.SDK.Workspace.RDCoreReference> { first, second });
    }

    [TestMethod]
    public void AReferenceWithNoIdentifier_IsTheLibraryOfItsName()
    {
        var libraries = Load(Machine(), "DAO");

        Assert.AreSame(AceDao, libraries.Libraries.Single());
    }

    // the name is not a fallback for an identity the source does not have: it would be another library than the one the project references.
    [TestMethod]
    public void AReferenceWhoseIdentifierHasNoDescription_IsNotFound_NotTheLibraryOfItsName()
    {
        var libraries = Load(Machine(), new LibraryIdentity("DAO", Dao, 6, 0));

        Assert.IsEmpty(libraries.Libraries);
        var problem = libraries.Problems.Single();
        Assert.AreEqual(LibraryProblemKind.NotFound, problem.Kind);
        CollectionAssert.AreEqual(new[] { "DAO {00025E01-0000-0000-C000-000000000046} 6.0" }, problem.Related.ToArray());
    }

    [TestMethod]
    public void AReferenceThatNamesOneLibraryAndIdentifiesAnother_IsNotFound_WithBoth()
    {
        var libraries = Load(Machine(), new LibraryIdentity("Widgets", Dao, 5, 0));

        var problem = libraries.Problems.Single();
        Assert.AreEqual(LibraryProblemKind.NotFound, problem.Kind);
        CollectionAssert.AreEqual(new[] { "Widgets {00025E01-0000-0000-C000-000000000046} 5.0", "DAO" }, problem.Related.ToArray());
    }

    [TestMethod]
    public void ADependency_IsTheTypeLibraryItWasBuiltAgainst_WhenTheProjectDoesNotReferenceItsName()
    {
        var libraries = Load(Machine(), "VB");

        CollectionAssert.AreEqual(new[] { Compat, Vb }, libraries.Libraries.ToArray());
    }

    // one library of a name: the project's reference decides it, and the library that depends on the name is typed with it, whatever identity it states.
    [TestMethod]
    [DataRow(false, DisplayName = "the reference first")]
    [DataRow(true, DisplayName = "the reference after the library that depends on it")]
    public void ADependencyOfANameTheProjectReferences_IsTheLibraryTheProjectReferences(bool referenceLast)
    {
        var dao = new LibraryIdentity("DAO", Dao, 5, 0);

        var libraries = referenceLast ? Load(Machine(), "VB", dao) : Load(Machine(), dao, "VB");

        Assert.IsEmpty(libraries.Problems);
        CollectionAssert.AreEqual(new[] { Dao36, Vb }, libraries.Libraries.ToArray());
    }

    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "platform", "Symbols");

    [TestMethod]
    public void AFolder_HasTheLibraryOfAName_AndOtherTypeLibrariesOfIt_EachInAFileOfItsOwn()
    {
        var files = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Path.Combine(Folder, "DAO.json")] = new(LibraryJson.Write(AceDao)),
            [Path.Combine(Folder, "DAO.dao2535.json")] = new(LibraryJson.Write(Compat)),
            [Path.Combine(Folder, "DAO.dao360.json")] = new(LibraryJson.Write(Dao36)),
            [Path.Combine(Folder, "VB.json")] = new(LibraryJson.Write(Vb)),
        });
        ILibrarySource source = new DirectoryLibrarySource(files, Folder);

        CollectionAssert.AreEquivalent(new[] { "DAO", "VB" }, source.Names.ToArray());
        Assert.IsTrue(source.TryGet("DAO", out var byName));
        Assert.AreEqual(Ace.ToString("B").ToUpperInvariant(), byName.Origin?.Guid);
        Assert.IsTrue(source.TryGet(new LibraryIdentity("DAO", DaoCompat, 3, 5), out var compat));
        Assert.AreEqual("3.5", compat.Origin?.Version);
        Assert.IsTrue(source.TryGetByGuid(Dao, out var dao));
        Assert.AreEqual("5.0", dao.Origin?.Version);
        Assert.IsFalse(source.TryGet("DAO.dao2535", out _), "a qualifier is not part of the library's name");
    }

    [TestMethod]
    public void AFileOfAName_ThatDescribesAnotherLibrary_IsRefused()
    {
        var files = new MockFileSystem(new Dictionary<string, MockFileData> { [Path.Combine(Folder, "DAO.dao2535.json")] = new(LibraryJson.Write(Vb)) });
        ILibrarySource source = new DirectoryLibrarySource(files, Folder);

        var exception = Assert.Throws<InvalidDataException>(() => source.TryGet(new LibraryIdentity("DAO", DaoCompat, 3, 5), out _));

        StringAssert.Contains(exception.Message, "DAO.dao2535.json");
    }

    [TestMethod]
    public void ADependencyWithAnIdentity_IsWrittenWithIt_AndOneWithANameAlone_AsTheName()
    {
        var text = LibraryJson.Write(new LibraryDescription { Name = "VB", DependsOn = ["stdole", new LibraryIdentity("DAO", DaoCompat, 3, 5)] });

        StringAssert.Contains(text, $"\"formatVersion\": {LibraryJson.CurrentFormatVersion}");
        StringAssert.Contains(text, "\"stdole\"");
        StringAssert.Contains(text, "\"guid\": \"{00025E04-0000-0000-C000-000000000046}\"");
        CollectionAssert.AreEqual(
            new[] { new LibraryIdentity("stdole"), new LibraryIdentity("DAO", DaoCompat, 3, 5) },
            LibraryJson.Read(text).DependsOn.ToArray());
    }

    [TestMethod]
    public void AFileOfTheFirstFormat_StatesItsDependenciesByName_AndIsRead()
    {
        var description = LibraryJson.Read("{ \"formatVersion\": 1, \"name\": \"Excel\", \"dependsOn\": [\"Office\", \"VBIDE\"] }");

        CollectionAssert.AreEqual(new[] { new LibraryIdentity("Office"), new LibraryIdentity("VBIDE") }, description.DependsOn.ToArray());
    }

    [TestMethod]
    public void AFileOfTheFirstFormat_ThatStatesAnIdentity_IsRefused()
    {
        var exception = Assert.Throws<InvalidDataException>(() => LibraryJson.Read(
            "{ \"formatVersion\": 1, \"name\": \"VB\", \"dependsOn\": [{ \"name\": \"DAO\", \"guid\": \"{00025E04-0000-0000-C000-000000000046}\", \"version\": \"3.5\" }] }"));

        StringAssert.Contains(exception.Message, "format version 1");
    }

    [TestMethod]
    [DataRow("[{ \"guid\": \"{00025E04-0000-0000-C000-000000000046}\" }]", DisplayName = "no name")]
    [DataRow("[{ \"name\": \"DAO\", \"guid\": \"DAO\" }]", DisplayName = "an identifier that is not one")]
    [DataRow("[{ \"name\": \"DAO\", \"version\": \"three\" }]", DisplayName = "a version that is not one")]
    [DataRow("[3]", DisplayName = "neither a name nor an object")]
    public void ADependencyThatIsNotOne_IsRefused(string dependsOn)
    {
        Assert.Throws<InvalidDataException>(() => LibraryJson.Read($"{{ \"formatVersion\": 2, \"name\": \"VB\", \"dependsOn\": {dependsOn} }}"));
    }
}
