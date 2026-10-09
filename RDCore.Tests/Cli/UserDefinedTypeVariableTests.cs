namespace RDCore.Tests.Cli;

/// <summary>
/// A variable declared as a user-defined type of its module is that type in the host, with the fields of the type, whether it is a variable of the module or a local, and
/// whatever order the types are declared in.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.3 Type Declarations")]
public sealed class UserDefinedTypeVariableTests
{
    private static string Program(params string[] lines)
        => "Attribute VB_Name = \"Program\"\r\n" + string.Join("\r\n", lines) + "\r\n";

    private static Task<string[]> RunAsync(params string[] lines) => ModuleWorkspace.RunAsync([], Program(lines));

    [TestMethod]
    public async Task ALocal_OfAUserDefinedType_HasItsFields()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunAsync(
            "Private Type TPoint", "X As Long", "Y As Long", "End Type",
            "Public Sub Main()", "Dim p As TPoint", "p.X = 4", "p.Y = 5", "Debug.Print p.X + p.Y", "End Sub"));

    [TestMethod]
    public async Task AVariableOfTheModule_OfAUserDefinedType_HasItsFields()
        => CollectionAssert.AreEqual(new[] { "5" }, await RunAsync(
            "Private Type TPoint", "X As Long", "Y As Long", "End Type",
            "Public Pt As TPoint",
            "Public Sub Main()", "Pt.Y = 5", "Debug.Print Pt.Y", "End Sub"));

    [TestMethod]
    public async Task ATypeDeclaredAfterTheVariableThatUsesIt_IsStillThatType()
        => CollectionAssert.AreEqual(new[] { "7" }, await RunAsync(
            "Public Pt As TPoint",
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Pt.X = 7", "Debug.Print Pt.X", "End Sub"));

    [TestMethod]
    public async Task AFieldOfAUserDefinedType_ThatIsAUserDefinedType_HasItsOwnFields()
        => CollectionAssert.AreEqual(new[] { "9" }, await RunAsync(
            "Private Type TPoint", "X As Long", "End Type",
            "Private Type TLine", "A As TPoint", "B As TPoint", "End Type",
            "Public Sub Main()", "Dim l As TLine", "l.B.X = 9", "Debug.Print l.B.X", "End Sub"));

    [TestMethod]
    public async Task AParameter_OfAUserDefinedType_IsPassedWithItsFields()
        => CollectionAssert.AreEqual(new[] { "4" }, await RunAsync(
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Dim p As TPoint", "p.X = 4", "Show p", "End Sub",
            "Private Sub Show(ByRef q As TPoint)", "Debug.Print q.X", "End Sub"));

    [TestMethod]
    public async Task AFieldThatIsNotOneOfTheType_IsStillAnError()
    {
        var errors = await ModuleWorkspace.LoadErrorsAsync([], Program(
            "Private Type TPoint", "X As Long", "End Type",
            "Public Sub Main()", "Dim p As TPoint", "p.Z = 1", "End Sub"));

        Assert.IsNotEmpty(errors);
    }
}
