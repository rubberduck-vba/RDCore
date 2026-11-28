using RDCore.SDK.Runtime.Libraries;
using RDCore.Tests.Runtime.Libraries;
using System.IO.Abstractions;

namespace RDCore.Tests.Cli;

/// <summary>
/// The <c>RDJson</c> library, which a project references to convert its values to JSON text and back: <c>JsonValue.Serialize</c>,
/// <c>JsonValue.Deserialize</c>, and the <c>JsonSerializationOptions</c> that say what <c>Deserialize</c> raises for a key or a member that does not match.
/// </summary>
[TestClass]
public sealed class JsonLibraryTests
{
    private static readonly (string Name, string Source)[] Classes =
    [
        ("Person", ModuleWorkspace.ClassModule("Person",
            "Public Name As String",
            "Public Age As Long",
            "Private Hidden As Long",
            "Public Property Get Badge() As String",
            "Badge = \"B-\" & Name",
            "End Property",
            "Friend Property Let Code(ByVal value As Long)",
            "End Property",
            "Public Property Let Slot(ByVal index As Long, ByVal value As Long)",
            "End Property",
            "Public Sub Greet()",
            "End Sub")),
        ("Team", ModuleWorkspace.ClassModule("Team",
            "Public Name As String",
            "Public Lead As Person",
            "Public Tags As Variant")),
    ];

    private static ModuleWorkspace.WorkspaceLibraries RDJson => new(["RDJson"], new DirectoryLibrarySource(new FileSystem(), ShippedLibrariesTests.Symbols));

    private static Task<string[]> RunAsync(params string[] body)
        => ModuleWorkspace.RunAsync(Classes, ModuleWorkspace.StandardModule("Program", ["Option Explicit", "Public Sub Main()", .. body, "End Sub"]), RDJson);

    // a VBA string literal of the text.
    private static string Literal(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

    private static string Joined(string[] printed) => string.Join(" | ", printed);

    [TestMethod]
    public async Task Serialize_IsTheJsonTextOfAValue_AnObjectOfAClassOfItsPublicMembers()
    {
        var printed = await RunAsync(
            "Dim p As Person", "Set p = New Person", "p.Name = \"Ann\"", "p.Age = 7",
            "Debug.Print JsonValue.Serialize(p)",
            "Debug.Print JsonValue.Serialize(Array(1, \"a\", True, Null))");

        CollectionAssert.AreEqual(new[] { """{"Name":"Ann","Age":7,"Badge":"B-Ann"}""", """[1,"a",true,null]""" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task Serialize_OfAValueThatHasNoJson_Is13()
    {
        var printed = await RunAsync(
            "On Error Resume Next", "Dim written As String",
            "written = JsonValue.Serialize(New JsonSerializationOptions)",
            "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "13" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task Deserialize_WithoutInto_IsTheValueAVariantHoldsOfTheText()
    {
        var printed = await RunAsync(
            "Dim v As Variant",
            $"Set v = JsonValue.Deserialize({Literal("""{"a":[1,2]}""")})",
            "Debug.Print TypeName(v)", "Debug.Print TypeName(v(\"a\"))", "Debug.Print v(\"a\")(1)",
            "v = JsonValue.Deserialize(\"12\")", "Debug.Print TypeName(v)", "Debug.Print v");

        CollectionAssert.AreEqual(new[] { "Dictionary", "Variant()", "2", "Integer", "12" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task Deserialize_IntoANewObject_FillsIt_AndReturnsIt()
    {
        var printed = await RunAsync(
            "Dim p As Person",
            $"Set p = JsonValue.Deserialize({Literal("""{"name":"Bo","AGE":3}""")}, New Person)",
            "Debug.Print p.Name", "Debug.Print p.Age");

        CollectionAssert.AreEqual(new[] { "Bo", "3" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task Deserialize_IntoAnObjectTheProgramHas_PatchesIt_AndReturnsIt()
    {
        var printed = await RunAsync(
            "Dim p As Person", "Set p = New Person", "p.Name = \"Ann\"", "p.Age = 7",
            "Dim q As Person",
            $"Set q = JsonValue.Deserialize({Literal("""{"Age":8}""")}, p)",
            "Debug.Print q Is p", "Debug.Print p.Name", "Debug.Print p.Age");

        CollectionAssert.AreEqual(new[] { "True", "Ann", "8" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task ByDefault_AKeyThatNamesNoMemberToGiveAValueTo_IsIgnored_AndAMemberNoKeyNames_KeepsItsValue()
    {
        var printed = await RunAsync(
            "Dim p As Person", "Set p = New Person", "p.Age = 7",
            $"Set p = JsonValue.Deserialize({Literal("""{"Name":"Bo","Nope":1,"Hidden":1,"Code":1,"Greet":1,"Slot":1,"Badge":"x"}""")}, p)",
            "Debug.Print p.Name", "Debug.Print p.Age", "Debug.Print p.Badge");

        CollectionAssert.AreEqual(new[] { "Bo", "7", "B-Bo" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task ANewOptionsObject_IgnoresEveryCase_AndTakesOnlyAJsonMemberHandling()
    {
        var printed = await RunAsync(
            "Dim o As JsonSerializationOptions", "Set o = New JsonSerializationOptions",
            "Debug.Print o.UnknownMembers", "Debug.Print o.MissingMembers", "Debug.Print o.ReadOnlyMembers",
            "o.ReadOnlyMembers = jsonRaise", "Debug.Print o.ReadOnlyMembers",
            "On Error Resume Next", "o.MissingMembers = 2", "Debug.Print Err.Number", "Debug.Print o.MissingMembers");

        CollectionAssert.AreEqual(new[] { "0", "0", "0", "1", "380", "0" }, printed, Joined(printed));
    }

    // the JSON names Name before the key that is refused: the object keeps its own Name, so nothing was given before the error.
    [TestMethod]
    [DataRow("UnknownMembers", """{"Name":"Bo","Nope":1}""", 438, DisplayName = "a key that names no member")]
    [DataRow("UnknownMembers", """{"Name":"Bo","Code":1}""", 438, DisplayName = "a key that names a Friend member")]
    [DataRow("UnknownMembers", """{"Name":"Bo","Hidden":1}""", 438, DisplayName = "a key that names a Private member")]
    [DataRow("UnknownMembers", """{"Name":"Bo","Greet":1}""", 450, DisplayName = "a key that names a method")]
    [DataRow("UnknownMembers", """{"Name":"Bo","Slot":1}""", 450, DisplayName = "a key that names a Property Let that takes an index")]
    [DataRow("ReadOnlyMembers", """{"Name":"Bo","Badge":"x"}""", 451, DisplayName = "a key that names a read-only member")]
    [DataRow("MissingMembers", """{"Name":"Bo"}""", 449, DisplayName = "a member no key names")]
    public async Task AnOptionThatRaises_RaisesItsError_BeforeAnyMemberIsGivenAValue(string option, string json, int error)
    {
        var printed = await RunAsync(
            "Dim o As JsonSerializationOptions", "Set o = New JsonSerializationOptions", $"o.{option} = jsonRaise",
            "Dim p As Person", "Set p = New Person", "p.Name = \"Ann\"",
            "On Error Resume Next",
            $"Set p = JsonValue.Deserialize({Literal(json)}, p, o)",
            "Debug.Print Err.Number", "Debug.Print p.Name");

        CollectionAssert.AreEqual(new[] { error.ToString(System.Globalization.CultureInfo.InvariantCulture), "Ann" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task AnOptionLeftAtIgnore_IgnoresItsCase_WhileTheOthersRaise()
    {
        var printed = await RunAsync(
            "Dim o As JsonSerializationOptions", "Set o = New JsonSerializationOptions",
            "o.UnknownMembers = jsonRaise", "o.MissingMembers = jsonRaise",
            "Dim p As Person",
            $"Set p = JsonValue.Deserialize({Literal("""{"Name":"Bo","Age":1,"Badge":"x"}""")}, New Person, o)",
            "Debug.Print p.Name");

        CollectionAssert.AreEqual(new[] { "Bo" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task TheOptions_HoldForTheObjectOfAMemberToo()
    {
        var printed = await RunAsync(
            "Dim o As JsonSerializationOptions", "Set o = New JsonSerializationOptions", "o.UnknownMembers = jsonRaise",
            "On Error Resume Next", "Dim t As Team",
            $"Set t = JsonValue.Deserialize({Literal("""{"Lead":{"Nope":1}}""")}, New Team, o)",
            "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "438" }, printed, Joined(printed));
    }

    [TestMethod]
    public async Task TextThatIsNotJson_Is5()
    {
        var printed = await RunAsync(
            "On Error Resume Next", "Dim v As Variant", "v = JsonValue.Deserialize(\"{\")", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "5" }, printed, Joined(printed));
    }

    [TestMethod]
    [DataRow("""{"Name":"Bo"}""", "New Collection", DisplayName = "an object of a library")]
    [DataRow("[1]", "New Person", DisplayName = "a JSON array")]
    public async Task Deserialize_IntoWhatAJsonObjectCannotFill_Is13(string json, string into)
    {
        var printed = await RunAsync(
            "On Error Resume Next", "Dim v As Variant", $"Set v = JsonValue.Deserialize({Literal(json)}, {into})", "Debug.Print Err.Number");

        CollectionAssert.AreEqual(new[] { "13" }, printed, Joined(printed));
    }

    // what a client echoes back is what it was given: the read-only Badge is in the text, and skipped on the way back.
    [TestMethod]
    public async Task AnObjectGraph_RoundTrips()
    {
        var printed = await RunAsync(
            "Dim t As Team", "Set t = New Team", "t.Name = \"Core\"", "Set t.Lead = New Person", "t.Lead.Name = \"Ann\"", "t.Lead.Age = 7",
            "t.Tags = Array(\"x\", \"y\")",
            "Dim written As String", "written = JsonValue.Serialize(t)", "Debug.Print written",
            "Dim back As Team", "Set back = JsonValue.Deserialize(written, New Team)",
            "Dim tags As Variant", "tags = back.Tags",
            "Debug.Print back.Lead.Name", "Debug.Print tags(1)", "Debug.Print JsonValue.Serialize(back) = written");

        CollectionAssert.AreEqual(
            new[] { """{"Name":"Core","Lead":{"Name":"Ann","Age":7,"Badge":"B-Ann"},"Tags":["x","y"]}""", "Ann", "y", "True" }, printed, Joined(printed));
    }
}
