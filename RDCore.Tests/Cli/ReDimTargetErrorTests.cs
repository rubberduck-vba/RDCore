using RDCore.Runtime.LanguageCore;

namespace RDCore.Tests.Cli;

/// <summary>
/// <strong>MS-VBAL §5.4.3.3</strong> the target of a <c>ReDim</c> is a variable, and MS-VBA does not re-dimension a member of an object, whatever the member
/// is: a member of an object of a declared class is "Method or data member not found" (<c>VBC09316</c>), and of an <c>Object</c> "Invalid ReDim"
/// (<c>VBC09306</c>), at compile time. A member of a <c>Variant</c> compiles, and re-dimensions a value of the member when it runs. Each case is what MS-VBA does,
/// recorded in Excel with a compile of the whole project.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.4.3.3 ReDim Statement")]
public sealed class ReDimTargetErrorTests
{
    private static readonly (string, string) Box = ("Box", ModuleWorkspace.ClassModule("Box",
        "Public Loose As Variant", "Public Property Get Prop() As Variant", "End Property"));

    private static string Program(params string[] body)
        => "Attribute VB_Name = \"Program\"\r\nPrivate Type TRecord\r\nItems() As Long\r\nEnd Type\r\nPublic Items() As Long\r\nPublic Sub Main()\r\n"
            + string.Join("\r\n", body) + "\r\nEnd Sub\r\n";

    private static void AssertTheError(string message, string[] errors)
        => StringAssert.Contains(Assert.ContainsSingle(errors), message);

    [TestMethod]
    [DataRow("Dim b As New Box", "ReDim b.Loose(1 To 3)", DisplayName = "A public variable of the class")]
    [DataRow("Dim b As New Box", "ReDim Preserve b.Loose(1 To 3)", DisplayName = "With Preserve")]
    [DataRow("Dim b As New Box", "ReDim b.Prop(1 To 3)", DisplayName = "A property of the class")]
    [DataRow("Dim b As New Box", "With b\r\nReDim .Loose(1 To 3)\r\nEnd With", DisplayName = "The member of a With block's object")]
    public async Task ReDim_OfAMemberOfAnObjectOfADeclaredClass_IsMethodOrDataMemberNotFound(string declaration, string statement)
        => AssertTheError(Exceptions.VBCompileError_MethodOrDataMemberNotFound, await ModuleWorkspace.LoadErrorsAsync([Box], Program(declaration, statement)));

    [TestMethod]
    [DataRow("Dim o As Object", "ReDim o.Loose(1 To 3)", DisplayName = "A member of an Object")]
    [DataRow("Dim o As Object", "With o\r\nReDim .Loose(1 To 3)\r\nEnd With", DisplayName = "The member of a With block's Object")]
    public async Task ReDim_OfAMemberOfAnObject_IsInvalidReDim(string declaration, string statement)
        => AssertTheError(Exceptions.VBCompileError_InvalidReDim, await ModuleWorkspace.LoadErrorsAsync([Box], Program(declaration, statement)));

    [TestMethod]
    [DataRow("Dim r As TRecord", "ReDim r.Items(1 To 3)", DisplayName = "A field of a record")]
    [DataRow("Dim r As TRecord", "With r\r\nReDim .Items(1 To 3)\r\nEnd With", DisplayName = "A field of a With block's record")]
    [DataRow("", "ReDim Program.Items(1 To 3)", DisplayName = "A variable of a module named through it")]
    [DataRow("Dim v As Variant", "ReDim v.Loose(1 To 3)", DisplayName = "A member of a Variant, which is not known to be an object's")]
    public async Task ReDim_OfWhatIsNotKnownToBeAMemberOfAnObject_IsNoError(string declaration, string statement)
        => CollectionAssert.AreEqual(Array.Empty<string>(), await ModuleWorkspace.LoadErrorsAsync([Box], Program(declaration, statement)));

    // the member of the object the Variant holds is read as a value, and what is made of it is not written back.
    [TestMethod]
    public async Task ReDim_OfAMemberOfTheObjectAVariantHolds_ReDimensionsAValue_AndLeavesTheMemberAsItWas()
        => CollectionAssert.AreEqual(new[] { "0 Empty" }, await ModuleWorkspace.RunAsync([Box], Program(
            "On Error Resume Next", "Dim v As Variant", "Set v = New Box", "ReDim v.Loose(1 To 3)", "Debug.Print Err.Number & \" \" & TypeName(v.Loose)")));

    [TestMethod]
    public async Task ReDim_OfAMemberOfAVariantThatHoldsNoObject_IsObjectRequired()
        => CollectionAssert.AreEqual(new[] { "424" }, await ModuleWorkspace.RunAsync([Box], Program(
            "On Error Resume Next", "Dim v As Variant", "v = 5", "ReDim v.Loose(1 To 3)", "Debug.Print Err.Number")));
}
