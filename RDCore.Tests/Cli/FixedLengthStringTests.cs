using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// A variable declared <c>String * n</c> is a fixed-length string of <em>n</em> characters (<strong>MS-VBAL §5.2.3.1.4</strong>), wherever it is declared - a
/// local, a module variable, a field of a user-defined type, the elements of an array - and whether <em>n</em> is a number or the name of a constant.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3.1.4 Variable Type Declarations")]
public sealed class FixedLengthStringTests
{
    private static Task<string[]> Run(string declarations, params string[] lines)
        => RunAsync([], $"Attribute VB_Name = \"Program\"\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", lines)}\r\nEnd Sub\r\n");

    [TestMethod]
    public async Task ALocal_IsAsLongAsItIsDeclared_AndWhatIsAssignedIsPaddedOrCut()
        => CollectionAssert.AreEqual(new[] { "4", "[ab  ]", "[abcd]" }, await Run(
            string.Empty,
            "Dim code As String * 4",
            "Debug.Print Len(code)",
            "code = \"ab\"",
            "Debug.Print \"[\" & code & \"]\"",
            "code = \"abcdefgh\"",
            "Debug.Print \"[\" & code & \"]\""));

    [TestMethod]
    public async Task TheLength_MayBeTheNameOfAConstant()
        => CollectionAssert.AreEqual(new[] { "16", "16" }, await Run(
            "Private Const Size = 16\r\nPrivate Label As String * Size",
            "Dim name As String * Size",
            "Debug.Print Len(name)",
            "Debug.Print Len(Label)"));

    [TestMethod]
    public async Task AFieldOfAUserDefinedType_IsAsLongAsItIsDeclared()
        => CollectionAssert.AreEqual(new[] { "8", "12", "[duck    ]" }, await Run(
            "Private Type Tag\r\n    Id As Long\r\n    Name As String * 8\r\nEnd Type",
            "Dim t As Tag",
            "Debug.Print Len(t.Name)",
            "Debug.Print Len(t)",
            "t.Name = \"duck\"",
            "Debug.Print \"[\" & t.Name & \"]\""));

    [TestMethod]
    public async Task TheElementsOfAnArray_AreEachAsLongAsTheyAreDeclared()
        => CollectionAssert.AreEqual(new[] { "[abc]", "3" }, await Run(
            string.Empty,
            "Dim names(1) As String * 3",
            "names(0) = \"abcdef\"",
            "Debug.Print \"[\" & names(0) & \"]\"",
            "Debug.Print Len(names(1))"));
}
