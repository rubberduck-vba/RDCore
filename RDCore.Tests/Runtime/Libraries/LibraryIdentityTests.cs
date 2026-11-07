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

    // COM's rule, as LoadRegTypeLib has it: the same major, and a minor that is at least the one asked for, the highest such.
    [TestMethod]
    public void AMinorVersion_IsTheLeastOneAccepted_AndAnotherMajorIsNotOne()
    {
        var older = Of("Widgets", Dao, "1.2");
        var newer = Of("Widgets", Dao, "1.5");
        var next = Of("Widgets", Dao, "2.0");

        Assert.AreSame(newer, new LibraryIdentity("Widgets", Dao, 1, 3).Choose([older, newer, next]));
        Assert.AreSame(newer, new LibraryIdentity("Widgets", Dao, 1, 0).Choose([older, newer, next]));
        Assert.IsNull(new LibraryIdentity("Widgets", Dao, 1, 6).Choose([older, newer, next]));
        Assert.IsNull(new LibraryIdentity("Widgets", Dao, 3, 0).Choose([older, newer, next]));
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

    [TestMethod]
    public void AFolder_HasTheLibraryOfAName_AndOtherTypeLibrariesOfIt_EachInAFileOfItsOwn()
    {
        var files = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [@"c:\symbols\DAO.json"] = new(LibraryJson.Write(AceDao)),
            [@"c:\symbols\DAO.dao2535.json"] = new(LibraryJson.Write(Compat)),
            [@"c:\symbols\DAO.dao360.json"] = new(LibraryJson.Write(Dao36)),
            [@"c:\symbols\VB.json"] = new(LibraryJson.Write(Vb)),
        });
        ILibrarySource source = new DirectoryLibrarySource(files, @"c:\symbols");

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
        var files = new MockFileSystem(new Dictionary<string, MockFileData> { [@"c:\symbols\DAO.dao2535.json"] = new(LibraryJson.Write(Vb)) });
        ILibrarySource source = new DirectoryLibrarySource(files, @"c:\symbols");

        var exception = Assert.Throws<InvalidDataException>(() => source.TryGet(new LibraryIdentity("DAO", DaoCompat, 3, 5), out _));

        StringAssert.Contains(exception.Message, "DAO.dao2535.json");
    }

    [TestMethod]
    public void ADependencyWithAnIdentity_IsWrittenWithIt_AndOneWithANameAlone_AsTheName()
    {
        var text = LibraryJson.Write(new LibraryDescription { Name = "VB", DependsOn = ["stdole", new LibraryIdentity("DAO", DaoCompat, 3, 5)] });

        StringAssert.Contains(text, "\"formatVersion\": 2");
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
