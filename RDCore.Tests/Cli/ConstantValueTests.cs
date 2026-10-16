using static RDCore.Tests.Cli.ModuleWorkspace;

namespace RDCore.Tests.Cli;

/// <summary>
/// The values of a workspace <c>Enum</c>'s members (<strong>MS-VBAL §5.2.3.4</strong>) and of a <c>Const</c> declared with a type
/// (<strong>MS-VBAL §5.2.3.2</strong>), run through <see cref="ModuleWorkspace"/> so that the symbols the host reads are the ones the code runs
/// against.
/// </summary>
[TestClass]
[TestCategory("MS-VBAL 5.2.3 Module Declarations")]
public sealed class ConstantValueTests
{
    private static string Program(string declarations, params string[] body)
        => $"Attribute VB_Name = \"Program\"\r\nOption Explicit\r\n{declarations}\r\nPublic Sub Main()\r\n{string.Join("\r\n", body)}\r\nEnd Sub\r\n";

    [TestMethod]
    public async Task AnEnumMemberWithoutAnExpression_IsZeroWhenFirst_AndOneMoreThanTheMemberBeforeItOtherwise()
        => CollectionAssert.AreEqual(new[] { "0 1 10 11 6 7" }, await RunAsync([], Program(
            "Private Enum Steps\r\nA\r\nB\r\nC = 10\r\nD\r\nE = B + 5\r\nF\r\nEnd Enum",
            "Debug.Print A & \" \" & B & \" \" & C & \" \" & D & \" \" & E & \" \" & F")));

    [TestMethod]
    public async Task AnEnumMembersExpression_IsLetCoercedToLong()
        // 2.5 rounds to the even 2, and a String constant holding a number is that number.
        => CollectionAssert.AreEqual(new[] { "2 3", "Long Long" }, await RunAsync([], Program(
            "Private Enum Coerced\r\nHalf = 2.5\r\nText = \"3\"\r\nEnd Enum",
            "Debug.Print Half & \" \" & Text",
            "Debug.Print TypeName(Half) & \" \" & TypeName(Text)")));

    [TestMethod]
    public async Task AnEnumMembersExpression_NamesAConstantOfItsModule()
        => CollectionAssert.AreEqual(new[] { "8" }, await RunAsync([], Program(
            "Private Const K = 4\r\nPrivate Enum Scaled\r\nTwice = K * 2\r\nEnd Enum",
            "Debug.Print Twice")));

    [TestMethod]
    public async Task AnEnumMember_QualifiedByItsEnum_OrOfAnotherModule_IsItsValue()
    {
        var mod2 = ("Mod2", StandardModule("Mod2", "Option Explicit", "Public Enum Shade", "Light", "Dark = 5", "Darker", "End Enum"));

        CollectionAssert.AreEqual(new[] { "0 6", "6" }, await RunAsync([], [mod2], Program(string.Empty,
            "Debug.Print Shade.Light & \" \" & Shade.Darker",
            "Debug.Print Mod2.Darker")));
    }

    [TestMethod]
    [DataRow("Private Const C As Long = 1", "1 Long", DisplayName = "An Integer literal, As Long")]
    [DataRow("Private Const C As Double = 1", "1 Double", DisplayName = "An Integer literal, As Double")]
    [DataRow("Private Const C As Integer = 2.5", "2 Integer", DisplayName = "A Double literal, As Integer: Banker's rounding")]
    [DataRow("Private Const C As String = 5", "5 String", DisplayName = "An Integer literal, As String")]
    [DataRow("Private Const C& = 1", "1 Long", DisplayName = "A type character")]
    public async Task AConstantDeclaredWithAType_IsItsValueLetCoercedToThatType(string declaration, string expected)
        => CollectionAssert.AreEqual(new[] { expected }, await RunAsync([], Program(declaration, "Debug.Print C & \" \" & TypeName(C)")));

    [TestMethod]
    public async Task ALocalConstantDeclaredWithAType_IsItsValueLetCoercedToThatType()
        => CollectionAssert.AreEqual(new[] { "2 Long" }, await RunAsync([], Program(string.Empty,
            "Const c As Long = 1.5",
            "Debug.Print c & \" \" & TypeName(c)")));
}
