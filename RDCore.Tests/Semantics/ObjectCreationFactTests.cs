using RDCore.SDK.Runtime.Libraries;
using RDCore.SDK.Semantics;
using RDCore.Tests.Cli;
using RDCore.Tests.Runtime.Libraries;

namespace RDCore.Tests.Semantics;

/// <summary>
/// A <c>CreateObject</c> call given a constant programmatic identifier is known, by tooling, to create an object of the class a library describes with it -
/// referenced or not - and the fact travels with the model to the language server. It is never a declaration: nothing of it is a compile error, and the object
/// stays late-bound.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 6.1.2.8.1.4 CreateObject")]
public sealed class ObjectCreationFactTests
{
    private static readonly LibraryDescription Lab = new()
    {
        Name = "Lab",
        Classes =
        [
            new ClassDescription
            {
                Name = "Shelf",
                ProgId = "Lab.Shelf",
                IsCreatable = true,
                Members = [new MemberDescription { Name = "Label", Kind = MemberKind.PropertyGet, Type = "String" }],
            },
            new ClassDescription { Name = "BunsenBurner", ProgId = "Lab.Burner.2", IsCreatable = true },
        ],
    };

    // what the facts of Main say the CreateObject calls of its body create, in order.
    private static async Task<ObjectCreationFact?[]> CreatesAsync(string[] references, params string[] lines)
    {
        var source = $"Attribute VB_Name = \"Program\"\r\nPrivate Const Id As String = \"Lab.Shelf\"\r\nPublic Sub Main()\r\nDim o As Object\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n";
        var payload = await ModuleWorkspace.SemanticsAsync([], source, libraries: new ModuleWorkspace.WorkspaceLibraries(references, new InMemoryLibrarySource(Lab)));
        var procedure = payload.Modules.Single().Procedures.Single();

        Assert.IsEmpty(procedure.CompileErrors, "a fact for tooling is never a compile error");
        // the call is the widest expression that starts where it does: its callee starts there too.
        return [.. procedure.Expressions
            .Where(fact => fact.Location.Range.Start.Character == "Set o = ".Length)
            .GroupBy(fact => fact.Location.Range.Start.Line)
            .OrderBy(line => line.Key)
            .Select(line => line.MaxBy(fact => fact.Location.Range.End.Character)!.Creates)];
    }

    [TestMethod]
    public async Task AClassOfAReferencedLibrary_IsTheClassTheObjectIsKnownToBe()
        => Assert.AreEqual(
            new ObjectCreationFact("Lab.Shelf", "Lab", "Shelf", ObjectClassSource.ReferencedLibrary),
            (await CreatesAsync(["Lab"], "Set o = CreateObject(\"Lab.Shelf\")")).Single());

    [TestMethod]
    public async Task AClassOfALibraryTheProjectDoesNotReference_IsKnownFromItsDescription()
        => Assert.AreEqual(
            new ObjectCreationFact("lab.shelf", "Lab", "Shelf", ObjectClassSource.UnreferencedLibrary),
            (await CreatesAsync([], "Set o = CreateObject(\"lab.shelf\")")).Single());

    // DAO.DBEngine is DAO.DBEngine.120: which version the machine has is its own to say.
    [TestMethod]
    [DataRow(new[] { "Lab" }, ObjectClassSource.ReferencedLibrary, DisplayName = "referenced")]
    [DataRow(new string[0], ObjectClassSource.UnreferencedLibrary, DisplayName = "not referenced")]
    public async Task AnIdentifierThatNamesNoVersion_IsTheClassWhoseIdentifierNamesOne(string[] references, ObjectClassSource source)
        => Assert.AreEqual(
            new ObjectCreationFact("Lab.Burner", "Lab", "BunsenBurner", source),
            (await CreatesAsync(references, "Set o = CreateObject(\"Lab.Burner\")")).Single());

    [TestMethod]
    public async Task AnIdentifierThatIsAConstant_IsTheConstantsValue()
        => Assert.AreEqual(
            new ObjectCreationFact("Lab.Shelf", "Lab", "Shelf", ObjectClassSource.ReferencedLibrary),
            (await CreatesAsync(["Lab"], "Set o = CreateObject(Id)")).Single());

    [TestMethod]
    public async Task AnObjectOnAnotherMachine_OrOfAnIdentifierTheCodeDoesNotSay_OrThatNothingDescribes_IsNotKnown()
        => CollectionAssert.AreEqual(new ObjectCreationFact?[] { null, null, null, null, null }, await CreatesAsync(["Lab"],
            "Set o = CreateObject(\"Lab.Shelf\", \"faraway\")",
            "Set o = CreateObject(\"Lab.\" & \"Shelf\")",
            "Set o = CreateObject(\"Lab.Nothing\")",
            "Set o = CreateObject(\"Shelf\")",
            "Set o = CreateObject(\"Elsewhere.Shelf\")"));

    [TestMethod]
    public async Task AnEmptyServerName_IsThisMachine()
        => Assert.AreEqual(
            new ObjectCreationFact("Lab.Shelf", "Lab", "Shelf", ObjectClassSource.ReferencedLibrary),
            (await CreatesAsync(["Lab"], "Set o = CreateObject(\"Lab.Shelf\", \"\")")).Single());

    // what the platform implements itself is found first, as CreateObject finds it.
    [TestMethod]
    public async Task AClassThePlatformImplements_IsAClassOfTheStandardLibrary()
        => Assert.AreEqual(
            new ObjectCreationFact("VBScript.RegExp", "VBA", "RegExp", ObjectClassSource.StandardLibrary),
            (await CreatesAsync([], "Set o = CreateObject(\"VBScript.RegExp\")")).Single());
}
